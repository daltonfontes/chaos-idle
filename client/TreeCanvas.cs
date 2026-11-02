using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.App;
using ChaosIdle.Domain.PassiveTree;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// Draws the passive tree: drag to move, pinch / mouse wheel / buttons to zoom, tap a node to
/// select it. State never relies on colour alone: taken nodes are filled, available ones have
/// a thick outline, path nodes are filled with an outline, locked ones are dim, other classes'
/// starts are crossed out, keystones have a double ring.
/// </summary>
public partial class TreeCanvas : Control
{
    private static readonly Color Background = new(0.09f, 0.08f, 0.11f);
    private static readonly Color LinkDim = new(0.32f, 0.31f, 0.36f);
    private static readonly Color Gold = new(0.95f, 0.74f, 0.30f);
    private static readonly Color PathBlue = new(0.45f, 0.75f, 1.00f);
    private static readonly Color Bright = new(0.92f, 0.92f, 0.95f);
    private static readonly Color Dim = new(0.42f, 0.41f, 0.46f);
    private static readonly Color LockedFill = new(0.16f, 0.15f, 0.19f);

    private PassiveTreeView? _view;
    private Dictionary<string, PassiveNodeView> _byId = new();
    private HashSet<string> _path = new();
    private string? _selected;
    private float _zoom = 0.6f;
    private Vector2 _offset;
    private bool _pressed;
    private float _dragDistance;

    public event Action<string>? NodeTapped;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetView(PassiveTreeView view)
    {
        bool first = _view == null;
        _view = view;
        _byId = view.Nodes.ToDictionary(n => n.Id);
        if (first) CenterOnStart();
        QueueRedraw();
    }

    public void ShowSelection(string? selected, IEnumerable<string> path)
    {
        _selected = selected;
        _path = new HashSet<string>(path);
        QueueRedraw();
    }

    public void CenterOnStart()
    {
        var start = _view?.Nodes.FirstOrDefault(n => n.IsOwnStart);
        if (start != null) CenterOn(start.Id);
    }

    public void CenterOn(string id)
    {
        if (!_byId.TryGetValue(id, out var n)) return;
        _offset = -World(n) * _zoom;
        QueueRedraw();
    }

    public void ZoomBy(float factor) => ZoomAround(factor, Size / 2);

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            // Touch is delivered as emulated mouse events (Godot's default), so one path covers both.
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                ZoomAround(1.15f, wheelUp.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                ZoomAround(1 / 1.15f, wheelDown.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    _pressed = true;
                    _dragDistance = 0;
                }
                else
                {
                    if (_pressed && _dragDistance < 12) Tap(click.Position);
                    _pressed = false;
                }

                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _pressed:
                _offset += motion.Relative;
                _dragDistance += motion.Relative.Length();
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMagnifyGesture pinch:
                ZoomAround(pinch.Factor, pinch.Position);
                AcceptEvent();
                break;
            case InputEventPanGesture pan:
                _offset -= pan.Delta * 8;
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_view == null) return;

        foreach (var (a, b) in _view.Links)
        {
            var na = _byId[a];
            var nb = _byId[b];
            bool taken = na.State == PassiveState.Allocated && nb.State == PassiveState.Allocated;
            bool onPath = (_path.Contains(a) || na.State == PassiveState.Allocated) && (_path.Contains(b) || nb.State == PassiveState.Allocated) && (_path.Contains(a) || _path.Contains(b));
            var color = taken ? Gold : onPath ? PathBlue : LinkDim;
            DrawLine(Screen(na), Screen(nb), color, taken || onPath ? 4 : 2, antialiased: true);
        }

        var font = ThemeDB.FallbackFont;
        foreach (var n in _view.Nodes)
        {
            var p = Screen(n);
            if (p.X < -60 || p.Y < -60 || p.X > Size.X + 60 || p.Y > Size.Y + 60) continue;

            float r = Radius(n.Kind);
            bool otherStart = n.Kind == PassiveNodeKind.Start && !n.IsOwnStart;
            if (otherStart)
            {
                DrawCircle(p, r, LockedFill);
                DrawArc(p, r, 0, Mathf.Tau, 32, Dim, 2, true);
                DrawLine(p + new Vector2(-r, -r) * 0.6f, p + new Vector2(r, r) * 0.6f, Dim, 3, true);
                DrawLine(p + new Vector2(-r, r) * 0.6f, p + new Vector2(r, -r) * 0.6f, Dim, 3, true);
            }
            else if (n.State == PassiveState.Allocated)
            {
                DrawCircle(p, r, Gold);
            }
            else if (_path.Contains(n.Id))
            {
                DrawCircle(p, r, PathBlue);
                DrawArc(p, r + 2, 0, Mathf.Tau, 32, Bright, 2, true);
            }
            else if (n.State == PassiveState.Available)
            {
                DrawCircle(p, r, LockedFill);
                DrawArc(p, r, 0, Mathf.Tau, 32, Bright, 4, true);
            }
            else
            {
                DrawCircle(p, r, LockedFill);
                DrawArc(p, r, 0, Mathf.Tau, 32, Dim, 2, true);
            }

            if (n.Kind == PassiveNodeKind.Keystone) DrawArc(p, r + 6, 0, Mathf.Tau, 40, n.State == PassiveState.Allocated ? Gold : Dim, 2, true);
            if (n.Id == _selected) DrawArc(p, r + 10, 0, Mathf.Tau, 40, Bright, 3, true);

            bool named = n.Kind is PassiveNodeKind.Notable or PassiveNodeKind.Keystone or PassiveNodeKind.Start;
            if (named && _zoom >= 0.45f)
            {
                int size = 18;
                float width = font.GetStringSize(n.Name, HorizontalAlignment.Left, -1, size).X;
                DrawString(font, p + new Vector2(-width / 2, r + 22), n.Name, HorizontalAlignment.Left, -1, size, n.State == PassiveState.Allocated ? Gold : Bright);
            }
        }
    }

    private void Tap(Vector2 position)
    {
        if (_view == null) return;
        PassiveNodeView? best = null;
        float bestDistance = float.MaxValue;
        foreach (var n in _view.Nodes)
        {
            float d = Screen(n).DistanceTo(position);
            float hit = Math.Max(28, Radius(n.Kind) + 10);
            if (d < hit && d < bestDistance)
            {
                best = n;
                bestDistance = d;
            }
        }

        if (best != null) NodeTapped?.Invoke(best.Id);
    }

    private void ZoomAround(float factor, Vector2 screenPoint)
    {
        float newZoom = Math.Clamp(_zoom * factor, 0.25f, 2.0f);
        var world = (screenPoint - Size / 2 - _offset) / _zoom;
        _zoom = newZoom;
        _offset = screenPoint - Size / 2 - world * _zoom;
        QueueRedraw();
    }

    private float Radius(PassiveNodeKind kind)
    {
        float baseRadius = kind switch
        {
            PassiveNodeKind.Keystone => 26,
            PassiveNodeKind.Notable => 20,
            PassiveNodeKind.Start => 22,
            _ => 11,
        };
        return Math.Max(6, baseRadius * Math.Clamp(_zoom / 0.6f, 0.6f, 1.6f));
    }

    private static Vector2 World(PassiveNodeView n) => new((float)n.X, (float)n.Y);

    private Vector2 Screen(PassiveNodeView n) => World(n) * _zoom + _offset + Size / 2;
}
