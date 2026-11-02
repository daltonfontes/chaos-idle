using System;
using System.Collections.Generic;
using ChaosIdle.App;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Simulation.Combat;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// Replays the last map run from its recorded beats: the hero on the left, the current enemy on
/// the right, life bars, travelling skill bolts, enemy lunges, and floating numbers. The replay
/// only shows what the simulation already decided; nothing here changes the result.
/// Everything is drawn from simple shapes (original art, no assets).
/// </summary>
public partial class CombatView : Control
{
    private static readonly Color Sky = new(0.07f, 0.08f, 0.11f);
    private static readonly Color Ground = new(0.16f, 0.15f, 0.13f);
    private static readonly Color GroundLine = new(0.32f, 0.28f, 0.22f);
    private static readonly Color Steel = new(0.66f, 0.71f, 0.78f);
    private static readonly Color Cloth = new(0.24f, 0.36f, 0.55f);
    private static readonly Color Skin = new(0.86f, 0.72f, 0.58f);
    private static readonly Color Text = new(0.93f, 0.92f, 0.88f);
    private static readonly Color Muted = new(0.62f, 0.62f, 0.6f);
    private static readonly Color LifeRed = new(0.80f, 0.16f, 0.16f);
    private static readonly Color LifeBack = new(0.22f, 0.08f, 0.08f);
    private static readonly Color EnemyBar = new(0.88f, 0.52f, 0.12f);
    private static readonly Color Crit = new(1f, 0.85f, 0.25f);
    private static readonly Color BlockBlue = new(0.45f, 0.75f, 1f);

    private enum Fx { Bolt, Burst, Number, Lunge, HeroFlash, BlockArc, EnemyFade }
    private enum Side { Hero, Enemy }

    private sealed record Effect(Fx Kind, Side At, double Start, double Duration, string Text, Color Color, float Offset, bool Big);

    private FightReplay? _fight;
    private readonly List<Effect> _effects = new();
    private double _clock;      // replay seconds (simulation time)
    private double _now;        // real seconds, drives effect animation
    private int _next;          // next beat to apply
    private bool _playing;

    // Current state, as of the last applied beat.
    private double _heroLife = 1, _heroShown = 1;
    private string _enemyName = "";
    private bool _enemyBoss;
    private double _enemyLife, _enemyMax = 1, _enemyShown;
    private bool _enemyDead = true;
    private double _enemySpawnedAt = -10;
    private bool _heroDead;
    private int _stage = -1;

    public double Speed { get; set; } = 2;
    public bool IsPlaying => _playing;
    public bool HasFight => _fight != null;
    public event Action? StateChanged;

    public CombatView()
    {
        CustomMinimumSize = new Vector2(0, 430);
        ClipContents = true;
    }

    public void Play(FightReplay fight)
    {
        _fight = fight;
        Seek(0);
        _playing = true;
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (_fight == null) return;
        if (!_playing && _clock >= _fight.DurationSeconds) Seek(0); // finished: play again
        _playing = !_playing;
        StateChanged?.Invoke();
    }

    public void SkipToBoss()
    {
        if (_fight == null) return;
        int boss = _fight.Stages.Count - 1;
        while (boss > 0 && !_fight.Stages[boss].IsBoss) boss--;
        Seek(_fight.Stages[boss].StartTime);
        _playing = true;
        StateChanged?.Invoke();
    }

    /// <summary>Jumps to a moment; the beats just before it get their effects so a still frame reads well.</summary>
    public void Seek(double time)
    {
        if (_fight == null) return;
        _effects.Clear();
        _heroLife = _heroShown = _fight.HeroMaxLife;
        _enemyDead = true;
        _heroDead = false;
        _stage = -1;
        _next = 0;
        _clock = Math.Clamp(time, 0, _fight.DurationSeconds);
        double recent = 0.9 * Speed; // beats this close to the jump still show their effects
        while (_next < _fight.Beats.Count && _fight.Beats[_next].Time <= _clock)
        {
            var beat = _fight.Beats[_next];
            double age = (_clock - beat.Time) / Speed;
            Apply(beat, _clock - beat.Time <= recent ? age : (double?)null);
            _next++;
        }

        _heroShown = _heroLife;
        _enemyShown = _enemyLife;
        UpdateStage();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (_fight != null && _playing)
        {
            _clock += delta * Speed;
            while (_next < _fight.Beats.Count && _fight.Beats[_next].Time <= _clock)
            {
                Apply(_fight.Beats[_next], 0);
                _next++;
            }

            UpdateStage();
            if (_clock >= _fight.DurationSeconds && _next >= _fight.Beats.Count)
            {
                _clock = _fight.DurationSeconds;
                _playing = false;
                StateChanged?.Invoke();
            }
        }

        // Bars slide toward the real value so hits read as hits.
        float k = (float)Math.Min(1, delta * 8);
        _heroShown += (_heroLife - _heroShown) * k;
        _enemyShown += (_enemyLife - _enemyShown) * k;
        _effects.RemoveAll(e => _now - e.Start > e.Duration);
        QueueRedraw();
    }

    private void UpdateStage()
    {
        if (_fight == null) return;
        int s = 0;
        for (int i = 0; i < _fight.Stages.Count; i++)
            if (_fight.Stages[i].StartTime <= _clock) s = i;
        _stage = s;
    }

    /// <param name="age">Real seconds since the beat, for its effects; null applies the state only.</param>
    private void Apply(CombatBeat b, double? age)
    {
        _heroLife = b.PlayerLife;
        _enemyLife = b.EnemyLife;
        _enemyMax = b.EnemyMaxLife;
        if (age is not double a) a = double.NaN;
        bool fx = !double.IsNaN(a);
        double start = _now - (fx ? a : 0);
        float jitter = ((_next * 37) % 9 - 4) * 9f;

        switch (b.Kind)
        {
            case BeatKind.EnemySpawned:
                _enemyName = b.Label;
                _enemyBoss = b.EnemyIsBoss;
                _enemyDead = false;
                _enemySpawnedAt = start;
                _enemyShown = b.EnemyLife;
                break;
            case BeatKind.PlayerHit:
                if (!fx) break;
                Add(Fx.Bolt, Side.Enemy, start, 0.22, "", ElementColor(b.Element), b.IsTriggered ? 1 : 0, false);
                Add(Fx.Burst, Side.Enemy, start + 0.2, 0.3, "", ElementColor(b.Element), 0, b.IsCritical);
                Add(Fx.Number, Side.Enemy, start + 0.2, 1.0, Number(b.Amount) + (b.IsCritical ? "!" : ""),
                    b.IsCritical ? Crit : Text, jitter, b.IsCritical);
                break;
            case BeatKind.PlayerMissed:
                if (!fx) break;
                Add(Fx.Bolt, Side.Enemy, start, 0.22, "", ElementColor(b.Element), 2, false);
                Add(Fx.Number, Side.Enemy, start + 0.2, 0.9, "miss", Muted, jitter, false);
                break;
            case BeatKind.EnemyHit:
                if (!fx) break;
                Add(Fx.Lunge, Side.Enemy, start, 0.3, "", Colors.White, 0, false);
                Add(Fx.HeroFlash, Side.Hero, start + 0.12, 0.25, "", LifeRed, 0, false);
                Add(Fx.Number, Side.Hero, start + 0.12, 1.0, "-" + Number(b.Amount), new Color(1f, 0.45f, 0.4f), jitter, false);
                break;
            case BeatKind.PlayerEvaded:
                if (!fx) break;
                Add(Fx.Lunge, Side.Enemy, start, 0.3, "", Colors.White, 0, false);
                Add(Fx.Number, Side.Hero, start + 0.12, 0.9, "evade", Muted, jitter, false);
                break;
            case BeatKind.PlayerBlocked:
                if (!fx) break;
                Add(Fx.Lunge, Side.Enemy, start, 0.3, "", Colors.White, 0, false);
                Add(Fx.BlockArc, Side.Hero, start + 0.12, 0.35, "", BlockBlue, 0, false);
                Add(Fx.Number, Side.Hero, start + 0.12, 0.9, "block", BlockBlue, jitter, false);
                break;
            case BeatKind.EnemyKilled:
                _enemyDead = true;
                _enemyLife = 0;
                if (fx) Add(Fx.EnemyFade, Side.Enemy, start, 0.6, _enemyName, Colors.White, 0, _enemyBoss);
                break;
            case BeatKind.PlayerDied:
                _heroDead = true;
                break;
            case BeatKind.OutOfMana:
                if (fx) Add(Fx.Number, Side.Hero, start, 1.0, "no mana", new Color(0.5f, 0.6f, 1f), jitter, false);
                break;
        }
    }

    private void Add(Fx kind, Side at, double start, double duration, string text, Color color, float offset, bool big) =>
        _effects.Add(new Effect(kind, at, start, duration, text, color, offset, big));

    // ---- Drawing ----

    private float GroundY => Size.Y - 70;
    private Vector2 HeroAt => new(Size.X * 0.24f, GroundY);
    private Vector2 EnemyAt => new(Size.X * 0.70f, GroundY);

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), Sky);
        DrawRect(new Rect2(0, GroundY, Size.X, Size.Y - GroundY), Ground);
        DrawLine(new Vector2(0, GroundY), new Vector2(Size.X, GroundY), GroundLine, 3);
        for (int i = 0; i < 9; i++) // a few stones so the ground reads as ground
        {
            float x = (i * 97 + 31) % Size.X;
            DrawCircle(new Vector2(x, GroundY + 18 + (i * 13) % 34), 3 + i % 3, GroundLine);
        }

        if (_fight == null)
        {
            Centered(font, "Run a map to watch the fight here.", new Vector2(Size.X / 2, Size.Y / 2), 24, Muted);
            return;
        }

        string stage = _stage >= 0 ? _fight.Stages[_stage].Title : "";
        DrawString(font, new Vector2(14, 32), stage, HorizontalAlignment.Left, Size.X - 150, 22, Text);
        DrawString(font, new Vector2(Size.X - 130, 32), $"{Clock(_clock)}/{Clock(_fight.DurationSeconds)}",
            HorizontalAlignment.Right, 116, 20, Muted);

        DrawHero(font);
        DrawEnemy(font);
        DrawEffects(font);

        if (!_playing && _clock >= _fight.DurationSeconds)
        {
            bool won = _fight.Outcome == "Map cleared";
            var c = won ? Crit : new Color(1f, 0.4f, 0.35f);
            DrawRect(new Rect2(0, Size.Y / 2 - 60, Size.X, 64), new Color(0, 0, 0, 0.6f));
            Centered(font, _fight.Outcome, new Vector2(Size.X / 2, Size.Y / 2 - 16), 40, c);
        }
    }

    private void DrawHero(Font font)
    {
        var p = HeroAt;
        float flash = Strength(Fx.HeroFlash, Side.Hero);
        float fall = _heroDead ? 1 : 0;
        Color tint(Color c) => (_heroDead ? c.Darkened(0.45f) : c).Lerp(LifeRed, flash * 0.7f);

        DrawSetTransform(p, fall * 1.2f, Vector2.One);
        // legs, body, head, sword, shield (a plain kite shape)
        DrawLine(new Vector2(-12, 0), new Vector2(-8, -48), tint(Cloth.Darkened(0.3f)), 9);
        DrawLine(new Vector2(12, 0), new Vector2(8, -48), tint(Cloth.Darkened(0.3f)), 9);
        DrawColoredPolygon(new[] { new Vector2(-22, -48), new Vector2(22, -48), new Vector2(18, -112), new Vector2(-18, -112) }, tint(Cloth));
        DrawColoredPolygon(new[] { new Vector2(-24, -112), new Vector2(24, -112), new Vector2(14, -98), new Vector2(-14, -98) }, tint(Steel));
        DrawCircle(new Vector2(0, -130), 17, tint(Skin));
        DrawArc(new Vector2(0, -132), 18, Mathf.Pi, Mathf.Tau, 16, tint(Steel), 7);
        DrawLine(new Vector2(-30, -80), new Vector2(-46, -150), tint(Steel), 5);
        DrawLine(new Vector2(-38, -92), new Vector2(-24, -86), tint(Steel.Darkened(0.4f)), 5);
        var shield = new[] { new Vector2(20, -112), new Vector2(54, -106), new Vector2(54, -64), new Vector2(37, -36), new Vector2(20, -64) };
        DrawColoredPolygon(shield, tint(Steel.Darkened(0.15f)));
        DrawPolyline(new[] { shield[0], shield[1], shield[2], shield[3], shield[4], shield[0] }, tint(Steel.Lightened(0.3f)), 3);
        DrawLine(new Vector2(37, -104), new Vector2(37, -46), tint(Cloth), 4);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        Bar(font, new Vector2(14, 50), BarWidth, _heroShown / _fight!.HeroMaxLife, LifeRed,
            $"{_fight.HeroName}  {Math.Max(0, Math.Round(_heroLife))}/{Math.Round(_fight.HeroMaxLife)}");
    }

    private void DrawEnemy(Font font)
    {
        if (string.IsNullOrEmpty(_enemyName)) return;
        var fade = Latest(Fx.EnemyFade);
        if (_enemyDead && fade == null) return;

        float alpha = 1, sink = 0;
        if (fade != null)
        {
            float t = (float)((_now - fade.Start) / fade.Duration);
            alpha = 1 - t;
            sink = t * 30;
        }

        float enter = (float)Math.Clamp((_now - _enemySpawnedAt) / 0.35, 0, 1);
        float lunge = Strength(Fx.Lunge, Side.Enemy);
        var p = EnemyAt + new Vector2((1 - enter) * 160 - lunge * 70, sink);
        float s = _enemyBoss ? 1.35f : 1f;
        var body = BodyColor(_enemyName);
        body.A = alpha;
        float hurt = Strength(Fx.Burst, Side.Enemy);
        var shade = body.Lerp(Colors.White, hurt * 0.5f);

        DrawSetTransform(p, 0, new Vector2(s, s));
        // A hunched beast: body, head, legs, eyes; the boss adds horns.
        DrawColoredPolygon(Ellipse(new Vector2(6, -52), 52, 40), shade);
        DrawColoredPolygon(Ellipse(new Vector2(-40, -78), 24, 20), shade.Darkened(0.12f));
        DrawLine(new Vector2(-18, -20), new Vector2(-22, 0), shade.Darkened(0.3f), 9);
        DrawLine(new Vector2(30, -20), new Vector2(34, 0), shade.Darkened(0.3f), 9);
        var eye = _enemyBoss ? new Color(1f, 0.35f, 0.1f, alpha) : new Color(1f, 0.9f, 0.4f, alpha);
        DrawCircle(new Vector2(-50, -82), 4, eye);
        DrawCircle(new Vector2(-36, -84), 4, eye);
        if (_enemyBoss)
        {
            var horn = new Color(0.85f, 0.8f, 0.7f, alpha);
            DrawColoredPolygon(new[] { new Vector2(-50, -94), new Vector2(-42, -96), new Vector2(-58, -124) }, horn);
            DrawColoredPolygon(new[] { new Vector2(-30, -96), new Vector2(-22, -92), new Vector2(-16, -124) }, horn);
            DrawColoredPolygon(new[] { new Vector2(-6, -88), new Vector2(10, -92), new Vector2(18, -104), new Vector2(26, -90), new Vector2(40, -88) }, shade.Darkened(0.3f));
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        Bar(font, new Vector2(Size.X - 14 - BarWidth, 50), BarWidth, _enemyMax > 0 ? _enemyShown / _enemyMax : 0, EnemyBar,
            $"{_enemyName}  {Math.Round(_enemyLife)}/{Math.Round(_enemyMax)}");
    }

    private void DrawEffects(Font font)
    {
        var heroHit = HeroAt + new Vector2(30, -90);
        var enemyHit = EnemyAt + new Vector2(-20, _enemyBoss ? -95 : -60);
        foreach (var e in _effects)
        {
            float t = (float)((_now - e.Start) / e.Duration);
            if (t < 0 || t > 1) continue;
            switch (e.Kind)
            {
                case Fx.Bolt:
                {
                    var from = HeroAt + new Vector2(-40, -140);
                    var to = enemyHit + (e.Offset == 2 ? new Vector2(30, -70) : Vector2.Zero); // a miss sails past
                    var lift = e.Offset == 1 ? new Vector2(0, -90 * (1 - (2 * t - 1) * (2 * t - 1))) : Vector2.Zero; // triggered skills arc
                    var at = from.Lerp(to, t) + lift;
                    var tail = from.Lerp(to, Math.Max(0, t - 0.25f)) + lift * 0.8f;
                    DrawLine(tail, at, e.Color, 7, true);
                    DrawCircle(at, 9, e.Color.Lightened(0.4f));
                    break;
                }
                case Fx.Burst:
                {
                    float r = (e.Big ? 46 : 28) * (0.4f + t);
                    var c = e.Color;
                    c.A = 1 - t;
                    DrawArc(enemyHit, r, 0, Mathf.Tau, 28, c, e.Big ? 7 : 5, true);
                    if (e.Big) DrawCircle(enemyHit, r * 0.4f, new Color(1, 1, 0.8f, 0.6f * (1 - t)));
                    break;
                }
                case Fx.BlockArc:
                {
                    var c = e.Color;
                    c.A = 1 - t;
                    DrawArc(HeroAt + new Vector2(38, -78), 50 + 14 * t, -1.1f, 1.1f, 18, c, 6, true);
                    break;
                }
                case Fx.Number:
                {
                    var anchor = e.At == Side.Hero ? HeroAt + new Vector2(0, -170) : enemyHit + new Vector2(0, -40);
                    var at = anchor + new Vector2(e.Offset, -60 * t);
                    var c = e.Color;
                    c.A = t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
                    int size = e.Big ? 38 : 28;
                    float w = font.GetStringSize(e.Text, HorizontalAlignment.Left, -1, size).X;
                    var outline = new Color(0, 0, 0, c.A);
                    DrawStringOutline(font, at - new Vector2(w / 2, 0), e.Text, HorizontalAlignment.Left, -1, size, 6, outline);
                    DrawString(font, at - new Vector2(w / 2, 0), e.Text, HorizontalAlignment.Left, -1, size, c);
                    break;
                }
                case Fx.EnemyFade:
                {
                    var c = e.Big ? Crit : Text;
                    c.A = 1 - t;
                    Centered(font, e.Big ? "Boss slain!" : "slain", EnemyAt + new Vector2(0, -170 - 30 * t), e.Big ? 34 : 24, c);
                    break;
                }
            }
        }
    }

    private float BarWidth => Size.X * 0.42f;

    /// <summary>Life bars sit in the top band, away from the floating numbers.</summary>
    private void Bar(Font font, Vector2 at, float width, double fraction, Color fill, string caption)
    {
        float f = (float)Math.Clamp(fraction, 0, 1);
        DrawRect(new Rect2(at, new Vector2(width, 16)), LifeBack);
        DrawRect(new Rect2(at, new Vector2(width * f, 16)), fill);
        DrawRect(new Rect2(at, new Vector2(width, 16)), Muted, false, 1);
        DrawString(font, at + new Vector2(0, 40), caption, HorizontalAlignment.Left, width, 19, Text);
    }

    private void Centered(Font font, string text, Vector2 at, int size, Color color)
    {
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawStringOutline(font, at - new Vector2(w / 2, 0), text, HorizontalAlignment.Left, -1, size, 6, new Color(0, 0, 0, color.A));
        DrawString(font, at - new Vector2(w / 2, 0), text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>1 when an effect of this kind has just started, fading to 0 at its end.</summary>
    private float Strength(Fx kind, Side side)
    {
        float best = 0;
        foreach (var e in _effects)
        {
            if (e.Kind != kind || e.At != side) continue;
            float t = (float)((_now - e.Start) / e.Duration);
            if (t >= 0 && t <= 1) best = Math.Max(best, (float)Math.Sin(t * Math.PI));
        }

        return best;
    }

    private Effect? Latest(Fx kind)
    {
        Effect? found = null;
        foreach (var e in _effects)
            if (e.Kind == kind && _now >= e.Start) found = e;
        return found;
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry)
    {
        var points = new Vector2[20];
        for (int i = 0; i < points.Length; i++)
        {
            float a = Mathf.Tau * i / points.Length;
            points[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }

        return points;
    }

    private static Color BodyColor(string name)
    {
        var palette = new[]
        {
            new Color(0.36f, 0.48f, 0.30f), new Color(0.48f, 0.42f, 0.38f), new Color(0.55f, 0.32f, 0.22f),
            new Color(0.34f, 0.40f, 0.52f), new Color(0.46f, 0.30f, 0.44f),
        };
        int h = 0;
        foreach (char ch in name) h = h * 31 + ch;
        return palette[(h & 0x7fffffff) % palette.Length];
    }

    private static Color ElementColor(DamageType type) => type switch
    {
        DamageType.Fire => new Color(1f, 0.5f, 0.15f),
        DamageType.Cold => new Color(0.5f, 0.85f, 1f),
        DamageType.Lightning => new Color(1f, 0.95f, 0.4f),
        _ => new Color(0.9f, 0.88f, 0.8f),
    };

    private static string Number(double v) => v >= 1000 ? $"{v / 1000:0.0}k" : Math.Round(v).ToString();

    private static string Clock(double s) => $"{(int)s / 60}:{(int)s % 60:00}";
}

/// <summary>The replay with its controls: play/pause, speed, skip to the boss.</summary>
public partial class FightPanel : VBoxContainer
{
    private readonly CombatView _view = new();
    private readonly Button _play = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _speed = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _boss = new() { Text = "Skip to boss", SizeFlagsHorizontal = SizeFlags.ExpandFill };

    public FightPanel()
    {
        AddChild(_view);
        var row = new HBoxContainer();
        row.AddChild(_play);
        row.AddChild(_speed);
        row.AddChild(_boss);
        AddChild(row);

        _play.Pressed += _view.TogglePause;
        _speed.Pressed += () =>
        {
            _view.Speed = _view.Speed switch { 1 => 2, 2 => 4, _ => 1 };
            UpdateButtons();
        };
        _boss.Pressed += _view.SkipToBoss;
        _view.StateChanged += UpdateButtons;
        UpdateButtons();
    }

    public void Play(FightReplay fight) => _view.Play(fight);

    /// <summary>For screenshots: a still frame at a given moment of the replay.</summary>
    public void Freeze(FightReplay fight, double time)
    {
        _view.Play(fight);
        _view.TogglePause();
        _view.Seek(time);
    }

    private void UpdateButtons()
    {
        _play.Text = _view.IsPlaying ? "Pause" : "Play";
        _play.Disabled = _boss.Disabled = !_view.HasFight;
        _speed.Text = $"Speed {_view.Speed}×";
    }
}
