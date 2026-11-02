using System;
using System.Collections.Generic;
using System.IO;
using ChaosIdle.App;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// The only screen for now. It builds its controls in code and shows the text that
/// <see cref="GameSession"/> returns; all rules live outside Godot.
/// </summary>
public partial class Main : Control
{
    private GameSession? _session;
    private Label _sheet = null!;
    private HFlowContainer _maps = null!;
    private RichTextLabel _log = null!;
    private ItemList _bag = null!;
    private Label _details = null!;
    private Button _equip = null!;
    private Button _discard = null!;
    private string? _selectedItemId;
    private TabContainer _tabs = null!;
    private PassivesTab _passives = null!;
    private SkillsTab _skillsTab = null!;

    public override void _Ready()
    {
        Theme = new Theme { DefaultFontSize = 26 };
        BuildLayout();
        OpenSession();
    }

    public override void _Notification(int what)
    {
        if (_session == null) return;
        if (what == NotificationApplicationPaused || what == NotificationWMCloseRequest)
        {
            _session.OnPause(DateTime.UtcNow);
        }
        else if (what == NotificationApplicationResumed)
        {
            Show(_session.OnResume(DateTime.UtcNow));
            Refresh();
        }
    }

    private void OpenSession()
    {
        string dir = ProjectSettings.GlobalizePath("user://saves");
        try
        {
            _session = GameSession.LoadOrCreate(dir, DateTime.UtcNow);
            Show(_session.LoadNotices);
            Show(_session.OnResume(DateTime.UtcNow)); // returning after the app was closed
            Refresh();
        }
        catch (Exception e)
        {
            Show(new[] { "Your save could not be loaded.", e.Message, "Tap 'Start over' to keep the damaged file aside and begin a new game." });
            var restart = new Button { Text = "Start over" };
            restart.Pressed += () =>
            {
                foreach (var f in Directory.GetFiles(dir, GameSession.Slot + ".json*"))
                    File.Move(f, f + ".damaged-" + DateTime.UtcNow.Ticks);
                restart.QueueFree();
                OpenSession();
            };
            _maps.AddChild(restart);
        }
    }

    private void BuildLayout()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        AddChild(margin);

        var tabs = _tabs = new TabContainer();
        margin.AddChild(tabs);

        var adventure = new ScrollContainer { Name = "Adventure", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(adventure);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        adventure.AddChild(column);

        // The build tabs share the session; any change there refreshes the sheet and bag here.
        _passives = new PassivesTab(() => _session, Refresh);
        _skillsTab = new SkillsTab(() => _session, Refresh);
        tabs.AddChild(_passives); // not in a ScrollContainer: dragging pans the tree instead of scrolling
        tabs.AddChild(Scrolling(_skillsTab));
        tabs.TabChanged += _ => { _passives.Refresh(); _skillsTab.Refresh(); };

        column.AddChild(new Label { Text = "Chaos Idle", HorizontalAlignment = HorizontalAlignment.Center });
        _sheet = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_sheet);

        _maps = new HFlowContainer();
        column.AddChild(_maps);

        _log = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 360) };
        column.AddChild(_log);

        column.AddChild(new Label { Text = "Bag" });
        _bag = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
        _bag.ItemSelected += OnItemSelected;
        column.AddChild(_bag);

        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_details);

        var actions = new HBoxContainer();
        _equip = new Button { Text = "Equip", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _discard = new Button { Text = "Discard", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _equip.Pressed += () => ItemAction(id => _session!.Equip(id, DateTime.UtcNow));
        _discard.Pressed += () => ItemAction(id => _session!.Discard(id, DateTime.UtcNow));
        actions.AddChild(_equip);
        actions.AddChild(_discard);
        column.AddChild(actions);
    }

    // ---- Hooks used by the Screenshots scene (CI) ----

    public void ShowTab(int index) => _tabs.CurrentTab = index;

    public void PlayFirstUnlocked()
    {
        if (_session == null) return;
        foreach (var e in _session.Expeditions())
        {
            if (!e.Unlocked) continue;
            Show(_session.RunExpedition(e.Id, DateTime.UtcNow));
            Refresh();
            return;
        }
    }

    public void PreviewPassive(string id) => _passives.Preview(id);

    public void PreviewSupport(string skillId, string supportId) => _skillsTab.Preview(skillId, supportId);

    private static ScrollContainer Scrolling(Control content)
    {
        var scroll = new ScrollContainer { Name = content.Name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    private void Refresh()
    {
        if (_session == null) return;
        _sheet.Text = string.Join("\n", _session.CharacterSheet());

        foreach (var child in _maps.GetChildren()) child.QueueFree();
        foreach (var choice in _session.Expeditions())
        {
            var button = new Button
            {
                Text = choice.Unlocked ? $"T{choice.Tier} {choice.Name}" : $"T{choice.Tier} locked",
                Disabled = !choice.Unlocked,
            };
            string id = choice.Id;
            button.Pressed += () =>
            {
                Show(_session.RunExpedition(id, DateTime.UtcNow));
                Refresh();
            };
            _maps.AddChild(button);
        }

        _bag.Clear();
        foreach (var row in _session.Inventory())
        {
            // Words, not only colour, carry the verdict.
            string suffix = row.Verdict.Length > 0 ? $" — {row.Verdict}" : "";
            int index = _bag.AddItem(row.Label + suffix);
            _bag.SetItemMetadata(index, row.ItemId);
        }

        _selectedItemId = null;
        _details.Text = "";
        _equip.Disabled = _discard.Disabled = true;
    }

    private void OnItemSelected(long index)
    {
        _selectedItemId = _bag.GetItemMetadata((int)index).AsString();
        _details.Text = string.Join("\n", _session!.Describe(_selectedItemId));
        _equip.Disabled = _discard.Disabled = false;
    }

    private void ItemAction(Func<string, string> action)
    {
        if (_selectedItemId == null) return;
        Show(new[] { action(_selectedItemId) });
        Refresh();
    }

    private void Show(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        _log.AppendText(string.Join("\n", lines) + "\n\n");
    }
}
