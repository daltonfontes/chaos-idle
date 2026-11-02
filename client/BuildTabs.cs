using System;
using System.Linq;
using ChaosIdle.App;
using ChaosIdle.Domain.PassiveTree;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// The Passives and Skills tabs. Lists with text markers (● taken, ○ available, · locked,
/// ★ keystone) so state never depends on colour alone; every refusal shows its reason.
/// </summary>
public partial class PassivesTab : VBoxContainer
{
    private readonly Func<GameSession?> _session;
    private readonly Action _changed;
    private Label _points = null!;
    private TreeCanvas _canvas = null!;
    private Label _details = null!;
    private Button _take = null!;
    private Button _refund = null!;
    private string? _selected;

    public PassivesTab() : this(() => null, () => { }) { }

    public PassivesTab(Func<GameSession?> session, Action changed)
    {
        _session = session;
        _changed = changed;
        Name = "Passives";
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        _points = new Label();
        AddChild(_points);
        AddChild(new Label
        {
            Text = "Filled: taken · thick ring: available · blue: your chosen path · X: another class · double ring: keystone",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _canvas = new TreeCanvas { CustomMinimumSize = new Vector2(0, 640), SizeFlagsVertical = SizeFlags.ExpandFill };
        _canvas.NodeTapped += Select;
        AddChild(_canvas);

        var view = new HBoxContainer();
        var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(80, 0) };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(80, 0) };
        var center = new Button { Text = "Centre", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        minus.Pressed += () => _canvas.ZoomBy(1 / 1.25f);
        plus.Pressed += () => _canvas.ZoomBy(1.25f);
        center.Pressed += () => _canvas.CenterOnStart();
        view.AddChild(minus);
        view.AddChild(plus);
        view.AddChild(center);
        AddChild(view);

        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_details);

        var buttons = new HBoxContainer();
        _take = new Button { Text = "Take", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _refund = new Button { Text = "Refund", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _take.Pressed += () => Act(s => s.AllocatePath(_selected!, DateTime.UtcNow));
        _refund.Pressed += () => Act(s => s.RefundPassive(_selected!, DateTime.UtcNow));
        buttons.AddChild(_take);
        buttons.AddChild(_refund);
        AddChild(buttons);
    }

    public void Refresh()
    {
        var session = _session();
        if (session == null || _canvas == null) return;
        var tree = session.PassiveTree();
        _points.Text = $"Passive points: {tree.PointsSpent}/{tree.PointsTotal} used, {tree.PointsRemaining} free";
        _canvas.SetView(tree);

        if (_selected != null) Select(_selected);
        else
        {
            _canvas.ShowSelection(null, Array.Empty<string>());
            _details.Text = "Drag to move, pinch or use − / + to zoom. Tap a passive to see the path to it, its cost and what it changes.";
            _take.Disabled = _refund.Disabled = true;
            _take.Text = "Take";
        }
    }

    /// <summary>Selects a node and centres the view on it (used by screenshots).</summary>
    public void Preview(string id)
    {
        Refresh();
        Select(id);
        _canvas.CenterOn(id);
    }

    private void Select(string id)
    {
        var session = _session();
        if (session == null) return;
        _selected = id;
        var path = session.PreviewPath(id);
        var node = session.PassiveTree().Nodes.FirstOrDefault(n => n.Id == id);
        _details.Text = string.Join("\n", path.Lines);
        _canvas.ShowSelection(id, path.NodeIds);

        bool allocated = node?.State == PassiveState.Allocated;
        _take.Disabled = !path.CanTake;
        _take.Text = path.NodeIds.Count > 1 ? $"Take path ({path.NodeIds.Count} points)" : "Take";
        _refund.Disabled = !allocated || node!.IsOwnStart;
    }

    private void Act(Func<GameSession, string> action)
    {
        var session = _session();
        if (session == null || _selected == null) return;
        string message = action(session);
        Refresh();
        _details.Text = message + "\n\n" + _details.Text;
        _changed();
    }
}

public partial class SkillsTab : VBoxContainer
{
    private readonly Func<GameSession?> _session;
    private readonly Action _changed;
    private OptionButton _mainSkill = null!;
    private ItemList _skills = null!;
    private ItemList _supports = null!;
    private Label _details = null!;
    private Button _link = null!;
    private Button _unlink = null!;
    private string? _skillId;
    private string? _supportId;
    private bool _refreshing;

    public SkillsTab() : this(() => null, () => { }) { }

    public SkillsTab(Func<GameSession?> session, Action changed)
    {
        _session = session;
        _changed = changed;
        Name = "Skills";
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10);
        AddChild(new Label { Text = "Main skill" });
        _mainSkill = new OptionButton();
        _mainSkill.ItemSelected += index =>
        {
            if (_refreshing) return;
            var session = _session();
            if (session == null) return;
            _details.Text = session.SetPrimarySkill(_mainSkill.GetItemMetadata((int)index).AsString(), DateTime.UtcNow);
            _skillId = null;
            Refresh();
            _changed();
        };
        AddChild(_mainSkill);

        AddChild(new Label { Text = "Your skills (tap one to manage its supports)" });
        _skills = new ItemList { CustomMinimumSize = new Vector2(0, 150) };
        _skills.ItemSelected += index => SelectSkill(_skills.GetItemMetadata((int)index).AsString());
        AddChild(_skills);

        AddChild(new Label { Text = "Supports" });
        _supports = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 280) };
        _supports.ItemSelected += index => SelectSupport(_supports.GetItemMetadata((int)index).AsString());
        AddChild(_supports);

        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_details);

        var buttons = new HBoxContainer();
        _link = new Button { Text = "Link", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _unlink = new Button { Text = "Unlink", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _link.Pressed += () => Act(s => s.LinkSupport(_skillId!, _supportId!, DateTime.UtcNow));
        _unlink.Pressed += () => Act(s => s.UnlinkSupport(_skillId!, _supportId!, DateTime.UtcNow));
        buttons.AddChild(_link);
        buttons.AddChild(_unlink);
        AddChild(buttons);
    }

    public void Refresh()
    {
        var session = _session();
        if (session == null || _skills == null) return;

        _refreshing = true;
        _mainSkill.Clear();
        foreach (var choice in session.PrimarySkillChoices())
        {
            _mainSkill.AddItem(choice.Name);
            int index = _mainSkill.ItemCount - 1;
            _mainSkill.SetItemMetadata(index, choice.Id);
            if (choice.IsCurrent) _mainSkill.Select(index);
        }

        _refreshing = false;

        var skills = session.SkillsInUse();
        _skills.Clear();
        foreach (var s in skills)
        {
            // Short enough for one line on a phone; the linked supports show in the list below.
            int index = _skills.AddItem($"{s.Name} — {s.Linked.Count} linked, {s.FreeSockets} free");
            _skills.SetItemTooltip(index, $"{s.Name} [{s.Kind}]: " + (s.Linked.Count == 0 ? "no supports" : string.Join(", ", s.Linked.Select(l => l.Name))));
            _skills.SetItemMetadata(index, s.Id);
        }

        _skillId ??= skills.Count > 0 ? skills[0].Id : null;
        if (_skillId != null) SelectSkill(_skillId);
    }

    public void Preview(string skillId, string supportId)
    {
        Refresh();
        SelectSkill(skillId);
        SelectSupport(supportId);
    }

    private void SelectSkill(string skillId)
    {
        var session = _session();
        if (session == null) return;
        _skillId = skillId;
        _supportId = null;
        _supports.Clear();
        foreach (var o in session.SupportOptions(skillId))
        {
            string marker = o.Status switch
            {
                SupportStatus.Linked => "● linked",
                SupportStatus.Available => "○ can link",
                SupportStatus.NoFreeSocket => "· no socket",
                _ => "× does not fit",
            };
            int index = _supports.AddItem($"{o.Name} — {marker}");
            _supports.SetItemMetadata(index, o.Id);
        }

        _details.Text = "Tap a support to see what it would change.";
        _link.Disabled = _unlink.Disabled = true;
    }

    private void SelectSupport(string supportId)
    {
        var session = _session();
        if (session == null || _skillId == null) return;
        _supportId = supportId;
        var option = session.SupportOptions(_skillId).FirstOrDefault(o => o.Id == supportId);
        _details.Text = string.Join("\n", session.PreviewSupport(_skillId, supportId));
        _link.Disabled = option?.Status != SupportStatus.Available;
        _unlink.Disabled = option?.Status != SupportStatus.Linked;
    }

    private void Act(Func<GameSession, string> action)
    {
        var session = _session();
        if (session == null || _skillId == null || _supportId == null) return;
        string message = action(session);
        string skill = _skillId;
        Refresh();
        SelectSkill(skill);
        _details.Text = message;
        _changed();
    }
}
