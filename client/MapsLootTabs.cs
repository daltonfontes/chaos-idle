using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.App;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using Godot;

namespace ChaosIdle.Client;

/// <summary>Pick a map and up to three modifiers, see the risks, rewards and an estimate, then run or farm it offline.</summary>
public partial class MapsTab : VBoxContainer
{
    private readonly Func<GameSession?> _session;
    private readonly Action<IReadOnlyList<string>> _ran;
    private ItemList _maps = null!;
    private VBoxContainer _modifiers = null!;
    private readonly Dictionary<string, CheckBox> _checks = new();
    private Label _preview = null!;
    private Button _run = null!;
    private Button _offline = null!;
    private string? _mapId;

    public MapsTab() : this(() => null, _ => { }) { }

    public MapsTab(Func<GameSession?> session, Action<IReadOnlyList<string>> ran)
    {
        _session = session;
        _ran = ran;
        Name = "Maps";
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(new Label { Text = "Choose a map" });
        _maps = new ItemList { CustomMinimumSize = new Vector2(0, 140) };
        _maps.ItemSelected += index => { _mapId = _maps.GetItemMetadata((int)index).AsString(); UpdatePreview(); };
        AddChild(_maps);

        AddChild(new Label { Text = "Modifiers (up to 3): more danger, more loot" });
        _modifiers = new VBoxContainer();
        AddChild(_modifiers);

        _preview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_preview);

        var buttons = new HBoxContainer();
        _run = new Button { Text = "Run map", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _offline = new Button { Text = "Farm offline here", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _run.Pressed += () =>
        {
            var session = _session();
            if (session == null || _mapId == null) return;
            _ran(session.RunExpedition(_mapId, SelectedModifiers(), DateTime.UtcNow));
            Refresh();
        };
        _offline.Pressed += () =>
        {
            var session = _session();
            if (session == null || _mapId == null) return;
            string message = session.SetOfflineMap(_mapId, SelectedModifiers(), DateTime.UtcNow);
            Refresh();
            _preview.Text = message + "\n\n" + _preview.Text;
        };
        buttons.AddChild(_run);
        buttons.AddChild(_offline);
        AddChild(buttons);
    }

    public void Refresh()
    {
        var session = _session();
        if (session == null || _maps == null) return;

        _maps.Clear();
        foreach (var m in session.Maps())
        {
            string offline = m.IsOfflineMap ? " — offline farm" : "";
            string text = m.Unlocked ? $"T{m.Tier} {m.Name} — boss: {m.BossName}{offline}" : $"T{m.Tier} {m.Name} — locked";
            int i = _maps.AddItem(text);
            _maps.SetItemMetadata(i, m.Id);
        }

        if (_checks.Count == 0)
        {
            foreach (var mod in session.MapModifiers())
            {
                var check = new CheckBox { Text = mod.Name };
                check.Toggled += _ => OnToggled();
                _checks[mod.Id] = check;
                _modifiers.AddChild(check);
                _modifiers.AddChild(new Label { Text = $"   {mod.Description} ({mod.Reward})", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            }
        }

        _mapId ??= session.Maps().First(m => m.Unlocked).Id;
        UpdatePreview();
    }

    /// <summary>Selects a map and modifiers (used by screenshots).</summary>
    public void Choose(string mapId, IEnumerable<string> modifierIds)
    {
        Refresh();
        _mapId = mapId;
        var wanted = new HashSet<string>(modifierIds);
        foreach (var (id, check) in _checks) check.SetPressedNoSignal(wanted.Contains(id));
        UpdatePreview();
    }

    private IReadOnlyList<string> SelectedModifiers() => _checks.Where(kv => kv.Value.ButtonPressed).Select(kv => kv.Key).ToList();

    private void OnToggled()
    {
        // Never more than three: refuse the fourth tick.
        var picked = SelectedModifiers();
        foreach (var (_, check) in _checks) check.Disabled = picked.Count >= 3 && !check.ButtonPressed;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var session = _session();
        if (session == null || _mapId == null) return;
        var preview = session.PreviewMap(_mapId, SelectedModifiers());
        _preview.Text = string.Join("\n", preview.Lines);
        _run.Disabled = _offline.Disabled = !preview.CanRun;
    }
}

/// <summary>Loot filter: preset, minimum rarity, the rules in words, and the bag as the filter sees it.</summary>
public partial class LootTab : VBoxContainer
{
    private static readonly FilterPreset[] Presets = { FilterPreset.ShowEverything, FilterPreset.Focused, FilterPreset.Strict };
    private static readonly string[] PresetLabels = { "Show everything", "Focused on my build", "Strict (full gear)" };
    private static readonly ItemRarity[] Rarities = { ItemRarity.Normal, ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Unique };

    private readonly Func<GameSession?> _session;
    private OptionButton _preset = null!;
    private OptionButton _minimum = null!;
    private Label _rules = null!;
    private ItemList _bag = null!;
    private Label _status = null!;
    private bool _refreshing;

    public LootTab() : this(() => null) { }

    public LootTab(Func<GameSession?> session)
    {
        _session = session;
        Name = "Loot";
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(new Label { Text = "Loot filter preset" });
        _preset = new OptionButton();
        foreach (var label in PresetLabels) _preset.AddItem(label);
        _preset.ItemSelected += _ => Apply();
        AddChild(_preset);

        AddChild(new Label { Text = "Minimum rarity to pick up (uniques always kept)" });
        _minimum = new OptionButton();
        foreach (var r in Rarities) _minimum.AddItem(r.ToString());
        _minimum.ItemSelected += _ => Apply();
        AddChild(_minimum);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);
        AddChild(new Label { Text = "Rules, checked top to bottom (the first match decides):" });
        _rules = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_rules);

        AddChild(new Label { Text = "Your bag with this filter:" });
        _bag = new ItemList { CustomMinimumSize = new Vector2(0, 260) };
        AddChild(_bag);
    }

    public void Refresh()
    {
        var session = _session();
        if (session == null || _rules == null) return;
        var view = session.LootFilter();

        _refreshing = true;
        if (view.Preset is FilterPreset preset) _preset.Select(Array.IndexOf(Presets, preset));
        _minimum.Select(Array.IndexOf(Rarities, view.MinimumRarity));
        _refreshing = false;

        _rules.Text = string.Join("\n", view.Rules.Select((r, i) => $"{i + 1}. {r}"));
        _bag.Clear();
        foreach (var row in session.FilterPreview())
        {
            string action = row.Action switch { FilterAction.Highlight => "★ highlight", FilterAction.Hide => "hide", _ => "show" };
            _bag.AddItem($"{row.Label} — {action}");
        }

        if (_bag.ItemCount == 0) _bag.AddItem("(your bag is empty)", null, selectable: false);
    }

    /// <summary>Sets the filter (used by screenshots too).</summary>
    public void Set(FilterPreset preset, ItemRarity minimum)
    {
        var session = _session();
        if (session == null) return;
        _status.Text = session.SetLootFilter(preset, minimum, DateTime.UtcNow);
        Refresh();
    }

    private void Apply()
    {
        if (_refreshing || _preset.Selected < 0 || _minimum.Selected < 0) return;
        Set(Presets[_preset.Selected], Rarities[_minimum.Selected]);
    }
}
