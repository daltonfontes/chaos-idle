using ChaosIdle.App;
using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Persistence;
using Xunit;

namespace ChaosIdle.Tests;

public class GameSessionMapsLootTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chaos-idle-maps-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 7, 3, 18, 0, 0, DateTimeKind.Utc);
    private ulong _seed = 1;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private GameSession Open() => GameSession.LoadOrCreate(_dir, Now, () => _seed++);

    [Fact]
    public void Map_list_shows_tiers_bosses_locks_and_the_offline_map()
    {
        var maps = Open().Maps();
        Assert.Equal(new[] { 1, 2, 3 }, maps.Select(m => m.Tier));
        Assert.True(maps[0].Unlocked);
        Assert.False(maps[1].Unlocked);
        Assert.Equal("The Bog Tyrant", maps[0].BossName);
        Assert.True(maps[0].IsOfflineMap);
        Assert.All(Open().MapModifiers(), m => Assert.False(string.IsNullOrWhiteSpace(m.Reward)));
    }

    [Fact]
    public void Map_preview_lists_risks_rewards_and_an_estimate()
    {
        var s = Open();
        var preview = s.PreviewMap(Maps.SunkenCauseway.Id, new[] { MapModifiers.Savage.Id, MapModifiers.Hardened.Id });

        Assert.True(preview.CanRun);
        Assert.StartsWith("Sunken Causeway — tier 1", preview.Lines[0]);
        Assert.Contains(preview.Lines, l => l.StartsWith("Boss: The Bog Tyrant"));
        Assert.Contains(preview.Lines, l => l.Contains("Savage Foes") && l.Contains("item quantity"));
        Assert.Contains(preview.Lines, l => l == "Loot bonus: +24% quantity, +10% rarity");
        Assert.Contains(preview.Lines, l => l.StartsWith($"Estimate over {GameSession.EstimateRuns} simulated runs"));
        Assert.Equal(preview.Lines, s.PreviewMap(Maps.SunkenCauseway.Id, new[] { MapModifiers.Savage.Id, MapModifiers.Hardened.Id }).Lines); // stable

        var locked = s.PreviewMap(Maps.AshenTrail.Id, Array.Empty<string>());
        Assert.False(locked.CanRun);
        Assert.Contains(locked.Lines, l => l.StartsWith("Locked"));
    }

    [Fact]
    public void Harder_modifiers_lower_the_estimate()
    {
        var s = Open();
        double Boss(params string[] mods) => double.Parse(
            s.PreviewMap(Maps.SunkenCauseway.Id, mods).Lines.Single(l => l.StartsWith("Estimate")).Split("boss beaten ")[1].TrimEnd('.', '%').Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Boss(MapModifiers.Savage.Id, MapModifiers.Frenzied.Id, MapModifiers.Hardened.Id) < Boss());
    }

    [Fact]
    public void Bad_modifier_choices_are_refused_in_words()
    {
        var s = Open();
        Assert.Contains("once", s.PreviewMap(Maps.SunkenCauseway.Id, new[] { MapModifiers.Savage.Id, MapModifiers.Savage.Id }).Lines[0]);
        Assert.Contains("At most 3", s.RunExpedition(Maps.SunkenCauseway.Id,
            new[] { MapModifiers.Savage.Id, MapModifiers.Hardened.Id, MapModifiers.Frenzied.Id, MapModifiers.Warded.Id }, Now)[0]);
        Assert.Contains("Unknown map modifier", s.PreviewMap(Maps.SunkenCauseway.Id, new[] { "mapmod.nope" }).Lines[0]);
    }

    [Fact]
    public void Runs_use_the_chosen_modifiers()
    {
        var lines = Open().RunExpedition(Maps.SunkenCauseway.Id, new[] { MapModifiers.Withering.Id }, Now);
        Assert.StartsWith("Sunken Causeway (Withering):", lines[0]);
    }

    [Fact]
    public void Offline_farms_the_chosen_map_and_the_choice_is_saved()
    {
        var s = Open();
        Assert.Contains("locked", s.SetOfflineMap(Maps.AshenTrail.Id, Array.Empty<string>(), Now));
        Assert.StartsWith("Offline farming: Sunken Causeway (Frenzied Foes)", s.SetOfflineMap(Maps.SunkenCauseway.Id, new[] { MapModifiers.Frenzied.Id }, Now));

        var reloaded = Open();
        Assert.Equal("Sunken Causeway (Frenzied Foes)", reloaded.EffectiveOfflineMap().Name);
        reloaded.OnPause(Now);
        Assert.Equal(MapModifiers.Frenzied.Id, Assert.Single(reloaded.State.Offline!.Map.Modifiers).Id);
    }

    [Fact]
    public void Old_saves_without_an_offline_map_still_load()
    {
        Open().Save(Now);
        var path = Path.Combine(_dir, GameSession.Slot + ".json");
        var serializer = new SaveSerializer();
        var (data, _) = serializer.Deserialize(File.ReadAllText(path));
        Assert.Null(data.OfflineMap); // never set: same shape as a save from before the field existed
        Assert.Equal("Sunken Causeway", Open().EffectiveOfflineMap().Name);
    }

    [Fact]
    public void Filter_view_names_the_preset_and_reads_the_rules()
    {
        var view = Open().LootFilter();
        Assert.Equal(FilterPreset.Focused, view.Preset);
        Assert.Equal(ItemRarity.Normal, view.MinimumRarity);
        Assert.Contains(view.Rules, r => r.StartsWith("highlight ★: Unique or better"));
        Assert.Contains(view.Rules, r => r.StartsWith("highlight ★: Magic or better, with 2+ of: max life"));
        Assert.Contains(view.Rules, r => r.StartsWith("hide: Magic items, with none of:"));
        Assert.Equal("Anything else: show", view.Rules[^1]);
    }

    [Fact]
    public void Presets_and_minimum_rarity_change_what_the_bag_preview_shows()
    {
        var s = Open();
        s.Character.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap) with { Id = "normal-cap" });
        s.Character.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.life", 20)) with { Id = "magic-cap", Rarity = ItemRarity.Magic });
        s.Character.Inventory.TryAdd(Uniques.StillwaterHelm.Roll("unique-helm", 10, () => 0.5));

        Assert.Equal("Loot filter: Show everything, Rare+.", s.SetLootFilter(FilterPreset.ShowEverything, ItemRarity.Rare, Now));
        var preview = s.FilterPreview().ToDictionary(r => r.ItemId);
        Assert.Equal(FilterAction.Hide, preview["normal-cap"].Action);
        Assert.Equal(FilterAction.Hide, preview["magic-cap"].Action);
        Assert.Equal(FilterAction.Show, preview["unique-helm"].Action); // uniques are never below the minimum
        Assert.Equal(ItemRarity.Rare, s.LootFilter().MinimumRarity);

        s.SetLootFilter(FilterPreset.Strict, ItemRarity.Magic, Now);
        var rules = s.LootFilter().Rules;
        Assert.Single(rules, r => r == "hide: Normal or worse"); // strict's own normal-item rule is not repeated

        s.SetLootFilter(FilterPreset.Strict, ItemRarity.Normal, Now);
        preview = s.FilterPreview().ToDictionary(r => r.ItemId);
        Assert.Equal(FilterAction.Hide, preview["normal-cap"].Action);
        Assert.Equal(FilterAction.Highlight, preview["unique-helm"].Action);
        Assert.Equal(FilterPreset.Strict, Open().LootFilter().Preset); // saved
    }
}
