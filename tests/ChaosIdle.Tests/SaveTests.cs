using System.Text.Json.Nodes;
using ChaosIdle.Content;
using ChaosIdle.Domain;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Offline;
using Xunit;

namespace ChaosIdle.Tests;

public class SaveTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SaveSerializer Serializer = new();
    private static readonly OfflineCalculator Offline = new(OfflineTests.Loot);

    internal static GameState RichState()
    {
        var c = ReferenceBuilds.IronBastion();
        c.Inventory.TryAdd(Uniques.KindledOath.Roll("bag-unique", 10, () => 0.3));
        c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronBand, ("affix.life", 22), ("affix.fire_res", 9)) with { Id = "bag-ring", Rarity = ItemRarity.Magic });
        var progress = MapProgress.Restore(2, new[] { Maps.SunkenCauseway.Id });
        var state = new GameState(c, progress, BuildRelevance.FilterFor(c.ToBuild(), strict: true));
        state.StartOffline(OfflineCheckpoint.Start("session.42", c, new MapInstance { Map = Maps.AshenTrail, Modifiers = new[] { MapModifiers.Savage } }, Now, 123456789012345678UL));
        state.CompleteOffline("session.older");
        return state;
    }

    private static GameState RoundTrip(GameState state, out IReadOnlyList<string> warnings)
    {
        string text = Serializer.Serialize(SaveMapper.ToSave(state), 5, Now);
        var (data, _) = Serializer.Deserialize(text);
        var loaded = SaveMapper.FromSave(data, ContentDatabase.Default);
        warnings = loaded.Warnings;
        return loaded.State;
    }

    [Fact]
    public void Round_trip_restores_the_whole_game_state()
    {
        var original = RichState();
        var loaded = RoundTrip(original, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(BuildSummary.Compute(original.Character.ToBuild()), BuildSummary.Compute(loaded.Character.ToBuild()));
        Assert.Equal(original.Character.Passives.Allocated.OrderBy(x => x), loaded.Character.Passives.Allocated.OrderBy(x => x));
        Assert.Equal(original.Character.SupportsFor(Skills.IronCleave).Select(s => s.Id), loaded.Character.SupportsFor(Skills.IronCleave).Select(s => s.Id));
        Assert.Equal(original.Character.Equipment.Items.Keys.OrderBy(x => x), loaded.Character.Equipment.Items.Keys.OrderBy(x => x));
        Assert.Equal(Describe(original.Character.Inventory.Items), Describe(loaded.Character.Inventory.Items));
        Assert.Equal("The Kindled Oath", loaded.Character.Inventory.Items[0].Name);
        Assert.Equal(2, loaded.Progress.HighestUnlockedTier);
        Assert.Contains(Maps.SunkenCauseway.Id, loaded.Progress.BossesDefeated);
        Assert.Equal(original.LootFilter.Rules.Select(r => r.Name), loaded.LootFilter.Rules.Select(r => r.Name));
        foreach (var item in original.Character.Inventory.Items)
            Assert.Equal(original.LootFilter.Evaluate(item), loaded.LootFilter.Evaluate(item));
        Assert.True(loaded.IsClaimed("session.older"));
    }

    [Fact]
    public void A_running_offline_session_resolves_identically_after_reload()
    {
        var original = RichState();
        var loaded = RoundTrip(original, out _);

        Assert.NotNull(loaded.Offline);
        Assert.Equal(123456789012345678UL, loaded.Offline!.Seed);
        var a = Offline.Resolve(original.Offline!, Now.AddHours(3));
        var b = Offline.Resolve(loaded.Offline, Now.AddHours(3));
        Assert.Equal(a.RunsCompleted, b.RunsCompleted);
        Assert.Equal(a.Loot.Select(i => i.Id), b.Loot.Select(i => i.Id));
    }

    [Fact]
    public void Gear_changed_after_starting_offline_does_not_leak_into_the_session()
    {
        var state = RichState();
        var before = Offline.Resolve(state.Offline!, Now.AddHours(2));
        state.Character.Equipment.Unequip(EquipmentSlot.MainHand);

        var loaded = RoundTrip(state, out _);
        var after = Offline.Resolve(loaded.Offline!, Now.AddHours(2));

        Assert.Null(loaded.Character.Equipment[EquipmentSlot.MainHand]);
        Assert.Equal(before.RunsCompleted, after.RunsCompleted);
        Assert.Equal(before.Loot.Count, after.Loot.Count);
    }

    [Fact]
    public void A_claimed_session_stays_claimed_after_reload()
    {
        var state = RichState();
        var session = state.Offline!;
        Assert.True(state.CompleteOffline(session.SessionId));
        var loaded = RoundTrip(state, out _);

        Assert.Null(loaded.Offline);
        Assert.False(loaded.CompleteOffline(session.SessionId));
        Assert.False(loaded.StartOffline(session));
    }

    [Fact]
    public void Save_text_holds_ids_and_rolls_not_content()
    {
        string text = Serializer.Serialize(SaveMapper.ToSave(RichState()), 1, Now);
        var root = JsonNode.Parse(text)!;

        Assert.Equal("chaos-idle-save", (string?)root["format"]);
        Assert.Equal(SaveSerializer.CurrentVersion, (int)root["schemaVersion"]!);
        Assert.StartsWith("sha256:", (string?)root["checksum"]);
        Assert.Equal("123456789012345678", (string?)root["payload"]!["offline"]!["seed"]);
        Assert.Contains("\"affixId\":\"affix.life\"", text);
        Assert.Contains("\"MainHand\":", text);
        Assert.DoesNotContain("Physical Damage", text); // no display text or stats copied
    }

    [Fact]
    public void Serialization_is_stable()
    {
        var state = RichState();
        Assert.Equal(Serializer.Serialize(SaveMapper.ToSave(state), 1, Now), Serializer.Serialize(SaveMapper.ToSave(state), 1, Now));
    }

    [Fact]
    public void Tampered_saves_fail_the_checksum()
    {
        string text = Serializer.Serialize(SaveMapper.ToSave(RichState()), 1, Now).Replace("\"highestUnlockedTier\":2", "\"highestUnlockedTier\":3");
        var e = Assert.Throws<SaveLoadException>(() => Serializer.Deserialize(text));
        Assert.Equal(SaveLoadError.ChecksumMismatch, e.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"format\":\"chaos-idle-save\",\"schemaVersion\":1,\"checksum\":\"x\",\"payl")]
    [InlineData("{\"format\":\"something-else\",\"schemaVersion\":1,\"checksum\":\"x\",\"payload\":{}}")]
    [InlineData("[1,2,3]")]
    public void Broken_files_are_reported_as_corrupt(string text)
    {
        Assert.Equal(SaveLoadError.Corrupt, Assert.Throws<SaveLoadException>(() => Serializer.Deserialize(text)).Error);
    }

    [Fact]
    public void Saves_from_a_newer_game_are_refused()
    {
        string text = new SaveSerializer(currentVersion: 2, migrations: new[] { new RenameMigration(1, "lootFilter", "lootFilter") })
            .Serialize(SaveMapper.ToSave(RichState()), 1, Now);
        var e = Assert.Throws<SaveLoadException>(() => Serializer.Deserialize(text));
        Assert.Equal(SaveLoadError.NewerVersion, e.Error);
    }

    [Fact]
    public void Old_saves_are_migrated_step_by_step()
    {
        // Pretend v1 is old: v1->v2 renames a field away, v2->v3 renames it back.
        string v1 = Serializer.Serialize(SaveMapper.ToSave(RichState()), 1, Now);
        var v3 = new SaveSerializer(new ISaveMigration[] { new RenameMigration(1, "lootFilter", "filterV2"), new RenameMigration(2, "filterV2", "lootFilter") }, currentVersion: 3);

        var (data, envelope) = v3.Deserialize(v1);

        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(2, envelope.MigrationsApplied.Count);
        Assert.NotEmpty(data.LootFilter.Rules);
        Assert.Empty(SaveMapper.FromSave(data, ContentDatabase.Default).Warnings);
    }

    [Fact]
    public void A_missing_migration_step_is_an_error()
    {
        string v1 = Serializer.Serialize(SaveMapper.ToSave(RichState()), 1, Now);
        var gap = new SaveSerializer(new ISaveMigration[] { new RenameMigration(2, "a", "b") }, currentVersion: 3);
        Assert.Equal(SaveLoadError.NoMigration, Assert.Throws<SaveLoadException>(() => gap.Deserialize(v1)).Error);
    }

    [Fact]
    public void Removed_content_is_dropped_with_warnings_not_crashes()
    {
        var data = SaveMapper.ToSave(RichState());
        var trimmed = new ContentRegistry(
            new[] { Classes.Guardian }, Skills.All, Supports.All.Where(s => s != Supports.Siphon),
            ItemBases.All.Where(b => b != ItemBases.IronBand), Affixes.All.Where(a => a.Id != "affix.crit_multi"),
            Uniques.All.Where(u => u != Uniques.KindledOath), new[] { PassiveTrees.Guardian }, Maps.All, MapModifiers.All.Where(m => m != MapModifiers.Savage));

        var loaded = SaveMapper.FromSave(data, trimmed);

        Assert.Contains(loaded.Warnings, w => w.Contains("sup.siphon"));
        Assert.Contains(loaded.Warnings, w => w.Contains("base.iron_band"));
        Assert.Contains(loaded.Warnings, w => w.Contains("affix.crit_multi"));
        Assert.Contains(loaded.Warnings, w => w.Contains("unique.kindled_oath"));
        Assert.Contains(loaded.Warnings, w => w.Contains("mapmod.savage"));
        Assert.DoesNotContain(loaded.State.Character.Inventory.Items, i => i.Base == ItemBases.IronBand || i.Unique != null);
        Assert.DoesNotContain(loaded.State.Character.SupportsFor(Skills.IronCleave), s => s.Id == "sup.siphon");
        Assert.Empty(loaded.State.Character.ToBuild().Validate());
    }

    [Fact]
    public void Missing_core_content_is_fatal()
    {
        var data = SaveMapper.ToSave(RichState());
        data.Character.Loadout.ClassId = "class.removed";
        Assert.Equal(SaveLoadError.MissingContent,
            Assert.Throws<SaveLoadException>(() => SaveMapper.FromSave(data, ContentDatabase.Default)).Error);
    }

    [Fact]
    public void Unreachable_passives_are_refunded_on_load()
    {
        var data = SaveMapper.ToSave(RichState());
        data.Character.Loadout.AllocatedPassives.Remove("pas.def.frame1"); // the gateway to the defence branch
        data.Character.Loadout.AllocatedPassives.Add("pas.unknown");

        var loaded = SaveMapper.FromSave(data, ContentDatabase.Default);
        var passives = loaded.State.Character.Passives;

        // The armour cluster only connects through frame1; the block nodes stay reachable through the melee branch.
        foreach (var cut in new[] { "pas.def.skin1", "pas.def.skin2", "pas.def.frame2", "pas.def.unbroken" })
        {
            Assert.False(passives.IsAllocated(cut));
            Assert.Contains(loaded.Warnings, w => w.Contains(cut));
        }

        Assert.True(passives.IsAllocated("pas.def.shieldwall"));
        Assert.Contains(loaded.Warnings, w => w.Contains("pas.unknown"));
        Assert.All(passives.Allocated, id => Assert.True(PassiveTrees.Guardian.Contains(id)));
        Assert.True(passives.PointsRemaining > 0);
    }

    [Fact]
    public void Content_registry_rejects_duplicate_ids()
    {
        Assert.Throws<ArgumentException>(() => new ContentRegistry(
            new[] { Classes.Guardian }, Skills.All.Append(Skills.IronCleave), Supports.All, ItemBases.All, Affixes.All,
            Uniques.All, new[] { PassiveTrees.Guardian }, Maps.All, MapModifiers.All));
        Assert.NotNull(ContentDatabase.Default.Affix("unique.stillwater_helm.cost"));
    }

    private static IEnumerable<string> Describe(IEnumerable<Item> items) =>
        items.Select(i => $"{i.Id}|{i.Base.Id}|{i.Rarity}|{i.ItemLevel}|{i.Unique?.Id}|{string.Join(",", i.Affixes.Select(a => $"{a.Affix.Id}:{a.Tier}:{a.Value}"))}");

    private sealed class RenameMigration : ISaveMigration
    {
        private readonly string _from, _to;

        public RenameMigration(int fromVersion, string from, string to)
        {
            FromVersion = fromVersion;
            _from = from;
            _to = to;
        }

        public int FromVersion { get; }
        public string Description => $"rename {_from} to {_to}";

        public void Apply(JsonObject payload)
        {
            if (_from == _to || !payload.ContainsKey(_from)) return;
            var node = payload[_from];
            payload.Remove(_from);
            payload[_to] = node;
        }
    }
}

public class SaveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chaos-idle-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private SaveStore Store(SaveSerializer? s = null) => new(_dir, "slot1", ContentDatabase.Default, s);

    [Fact]
    public void Empty_slot_loads_nothing()
    {
        var r = Store().Load();
        Assert.Null(r.Game);
        Assert.Equal(SaveSource.None, r.Source);
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void Save_and_load_round_trip_with_increasing_revision()
    {
        var store = Store();
        var state = SaveTests.RichState();
        store.Save(state, Now);
        store.Save(state, Now.AddMinutes(1));

        var r = store.Load();
        Assert.Equal(SaveSource.Main, r.Source);
        Assert.Equal(2, r.Game!.State.Revision);
        Assert.Equal(2, r.Envelope!.Revision);
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.MainPath + ".tmp"));
    }

    [Fact]
    public void A_damaged_main_file_falls_back_to_the_backup()
    {
        var store = Store();
        var state = SaveTests.RichState();
        store.Save(state, Now);
        store.Save(state, Now.AddMinutes(1));
        var text = File.ReadAllText(store.MainPath);
        File.WriteAllText(store.MainPath, text.Substring(0, text.Length / 2)); // simulated torn write

        var r = store.Load();
        Assert.Equal(SaveSource.Backup, r.Source);
        Assert.Equal(1, r.Game!.State.Revision);
        Assert.Single(r.Problems);
    }

    [Fact]
    public void Both_files_damaged_reports_every_problem()
    {
        var store = Store();
        store.Save(SaveTests.RichState(), Now);
        store.Save(SaveTests.RichState(), Now);
        File.WriteAllText(store.MainPath, "garbage");
        File.WriteAllText(store.BackupPath, "{}");

        var r = store.Load();
        Assert.Null(r.Game);
        Assert.Equal(2, r.Problems.Count);
    }

    [Fact]
    public void A_newer_save_is_never_silently_replaced_by_an_older_backup()
    {
        var store = Store();
        store.Save(SaveTests.RichState(), Now);
        var newer = new SaveSerializer(currentVersion: 2, migrations: Array.Empty<ISaveMigration>());
        new SaveStore(_dir, "slot1", ContentDatabase.Default, newer).Save(SaveTests.RichState(), Now);

        var e = Assert.Throws<SaveLoadException>(() => store.Load());
        Assert.Equal(SaveLoadError.NewerVersion, e.Error);
    }
}
