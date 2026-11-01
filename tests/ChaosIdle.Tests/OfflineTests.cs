using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Offline;
using ChaosIdle.Simulation.Random;
using ChaosIdle.Simulation.Reporting;
using Xunit;

namespace ChaosIdle.Tests;

public class OfflineTests
{
    internal static readonly LootGenerator Loot = new(ItemBases.All, Affixes.All, uniques: Uniques.All);
    internal static readonly DateTime Start = new(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);
    private static readonly OfflineCalculator Calc = new(Loot);
    private static readonly MapInstance Causeway = new() { Map = Maps.SunkenCauseway };

    private static OfflineCheckpoint Checkpoint(ChaosIdle.Domain.Characters.PlayerCharacter? c = null, MapInstance? map = null, ulong seed = 7) =>
        OfflineCheckpoint.Start("session.test", c ?? ReferenceBuilds.IronBastion(), map ?? Causeway, Start, seed);

    [Fact]
    public void Same_checkpoint_and_time_give_the_same_result()
    {
        var cp = Checkpoint();
        var a = Calc.Resolve(cp, Start.AddHours(3));
        var b = Calc.Resolve(cp, Start.AddHours(3));
        Assert.Equal(a.RunsCompleted, b.RunsCompleted);
        Assert.Equal(a.Loot.Select(i => i.Id), b.Loot.Select(i => i.Id));
    }

    [Fact]
    public void Credit_is_capped()
    {
        var cp = Checkpoint();
        var eight = Calc.Resolve(cp, Start.AddHours(8));
        var month = Calc.Resolve(cp, Start.AddDays(30));
        Assert.False(eight.WasCapped);
        Assert.True(month.WasCapped);
        Assert.Equal(TimeSpan.FromHours(8), month.CreditedTime);
        Assert.Equal(eight.RunsAttempted, month.RunsAttempted);
        Assert.Equal(eight.Loot.Select(i => i.Id), month.Loot.Select(i => i.Id));
    }

    [Fact]
    public void Clock_moving_backwards_credits_nothing()
    {
        var r = Calc.Resolve(Checkpoint(), Start.AddHours(-2));
        Assert.True(r.ClockAnomaly);
        Assert.Equal(0, r.RunsAttempted);
        Assert.Empty(r.Loot);
        Assert.Contains("clock moved backwards", ReportText.Return(r, null)[0]);
    }

    [Fact]
    public void Short_absences_earn_nothing()
    {
        var r = Calc.Resolve(Checkpoint(), Start.AddSeconds(45));
        Assert.True(r.TooShort);
        Assert.Equal(0, r.RunsAttempted);
    }

    [Fact]
    public void Non_utc_times_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Calc.Resolve(Checkpoint(), DateTime.Now));
    }

    [Fact]
    public void Rewards_grow_with_time_until_the_cap()
    {
        var cp = Checkpoint();
        var two = Calc.Resolve(cp, Start.AddHours(2));
        var four = Calc.Resolve(cp, Start.AddHours(4));
        Assert.InRange(four.RunsAttempted / (double)two.RunsAttempted, 1.9, 2.1);
    }

    [Fact]
    public void The_build_is_frozen_when_the_session_starts()
    {
        var c = ReferenceBuilds.IronBastion();
        var cp = Checkpoint(c);
        var before = Calc.Resolve(cp, Start.AddHours(2));
        c.Equipment.Unequip(EquipmentSlot.MainHand);
        var after = Calc.Resolve(cp, Start.AddHours(2));
        Assert.Equal(before.RunsCompleted, after.RunsCompleted);
    }

    [Fact]
    public void Offline_runs_never_include_the_boss()
    {
        var samples = Calc.Sample(Checkpoint());
        Assert.All(samples, s => Assert.DoesNotContain(s.Encounters, e => e.Encounter.Enemies.Any(x => x.IsBoss)));
    }

    [Fact]
    public void Claim_respects_filter_storage_cap_and_happens_once()
    {
        var settings = new OfflineSettings { StorageCap = 15 };
        var calc = new OfflineCalculator(Loot, settings);
        var r = calc.Resolve(Checkpoint(), Start.AddHours(4));
        var filter = new LootFilter { Rules = new[] { new LootFilterRule { Name = "normals", Action = FilterAction.Hide, MaxRarity = ItemRarity.Normal } } };
        var bag = new Inventory(1000);

        var claim = r.Claim(bag, filter, settings);

        Assert.Equal(15, claim.Kept.Count);
        Assert.All(claim.Kept, i => Assert.NotEqual(ItemRarity.Normal, i.Rarity));
        Assert.Equal(r.Loot.Count(i => i.Rarity == ItemRarity.Normal), claim.Hidden.Count);
        Assert.Equal(r.Loot.Count, claim.Kept.Count + claim.Hidden.Count + claim.OverStorageCap.Count + claim.NoBagSpace.Count);
        Assert.Throws<InvalidOperationException>(() => r.Claim(new Inventory(10), null, settings));
    }

    [Fact]
    public void Highlighted_items_are_claimed_first()
    {
        var settings = new OfflineSettings { StorageCap = 5 };
        var r = new OfflineCalculator(Loot, settings).Resolve(Checkpoint(), Start.AddHours(4));
        var filter = new LootFilter { Rules = new[] { new LootFilterRule { Name = "rares", Action = FilterAction.Highlight, MinRarity = ItemRarity.Rare } } };
        var claim = r.Claim(new Inventory(1000), filter, settings);
        int rares = r.Loot.Count(i => i.Rarity >= ItemRarity.Rare);
        Assert.True(rares > 0);
        Assert.Equal(Math.Min(5, rares), claim.Kept.Count(i => i.Rarity >= ItemRarity.Rare));
    }

    [Fact]
    public void Extrapolation_matches_brute_force_simulation()
    {
        var settings = OfflineSettings.Default;
        var cp = Checkpoint(seed: 3);
        var estimate = Calc.Resolve(cp, Start.AddHours(2));

        // Brute force: actually simulate every run of the same session with fresh seeds.
        var runner = new ExpeditionRunner(Loot);
        double budget = 2 * 3600 * settings.EfficiencyPercent / 100.0, t = 0;
        int runs = 0, items = 0;
        for (ulong i = 0; ; i++)
        {
            var r = runner.RunMap(cp.Build, cp.CharacterId, cp.Map, false, Seeds.Derive(999, i));
            bool ok = r.Outcome == ExpeditionOutcome.Completed;
            double cost = r.DurationSeconds + settings.SecondsBetweenRuns + (ok ? 0 : settings.DeathPenaltySeconds);
            if (t + cost > budget) break;
            t += cost;
            runs++;
            if (ok) items += r.Loot.Count;
        }

        Assert.InRange(estimate.RunsAttempted / (double)runs, 0.9, 1.1);
        Assert.InRange(estimate.Loot.Count / (double)items, 0.8, 1.2);
    }

    [Fact]
    public void A_build_that_fails_offline_is_told_why()
    {
        var starter = TestContent.StarterWithPassives();
        var hard = new MapInstance { Map = Maps.RimewoodHollow, Modifiers = new[] { MapModifiers.Frostbitten, MapModifiers.Savage } };
        var r = Calc.Resolve(OfflineCheckpoint.Start("s", starter, hard, Start, 1), Start.AddHours(2));

        Assert.True(r.RunsFailed > 0);
        Assert.NotEmpty(r.FailureReasons);
        var lines = ReportText.Return(r, null);
        Assert.Contains(lines, l => l.Contains("failed and brought nothing back"));
        Assert.Contains(lines, l => l.StartsWith("Try: "));
    }

    [Fact]
    public void Return_text_explains_the_cap_and_the_claim()
    {
        var settings = OfflineSettings.Default;
        var r = Calc.Resolve(Checkpoint(), Start.AddHours(12));
        var claim = r.Claim(new Inventory(20), null, settings);
        var lines = ReportText.Return(r, claim, "Boss is fought only in active play.");

        Assert.Equal("Welcome back. You were away 12h 00m; 8h 00m counted (offline limit).", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Brought home 20"));
        Assert.Contains(lines, l => l.Contains("no bag space"));
        Assert.Equal("Boss is fought only in active play.", lines[^1]);
        Assert.All(lines, l => Assert.True(l.Length <= 200));
    }
}

/// <summary>
/// Milestone 4 gate: returning after a meaningful absence feels rewarding without making active
/// play irrelevant.
/// </summary>
public class Milestone4GateTests
{
    private static readonly OfflineCalculator Calc = new(OfflineTests.Loot);
    private static readonly ExpeditionRunner Runner = new(OfflineTests.Loot);

    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "bastion", Maps.SunkenCauseway.Id, Array.Empty<string>() },
        new object[] { "bastion", Maps.RimewoodHollow.Id, new[] { MapModifiers.Savage.Id } },
        new object[] { "lancer", Maps.AshenTrail.Id, new[] { MapModifiers.Hardened.Id } },
        new object[] { "lancer", Maps.RimewoodHollow.Id, new[] { MapModifiers.Frostbitten.Id } },
    };

    private static (ChaosIdle.Domain.Characters.PlayerCharacter, MapInstance) Make(string build, string mapId, string[] mods) => (
        build == "bastion" ? ReferenceBuilds.IronBastion() : ReferenceBuilds.EmberLancer(),
        new MapInstance { Map = Maps.All.Single(m => m.Id == mapId), Modifiers = mods.Select(id => MapModifiers.All.Single(m => m.Id == id)).ToArray() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_night_away_is_rewarding(string build, string mapId, string[] mods)
    {
        var (c, map) = Make(build, mapId, mods);
        var r = Calc.Resolve(OfflineCheckpoint.Start("s", c, map, OfflineTests.Start, 11), OfflineTests.Start.AddHours(8));

        Assert.True(r.SuccessRate >= 0.9);
        Assert.True(r.Loot.Count >= 100, $"{r.Loot.Count} items");
        Assert.True(r.Loot.Count(i => i.Rarity >= ItemRarity.Rare) >= 10);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Active_play_stays_clearly_better_per_hour(string build, string mapId, string[] mods)
    {
        var (c, map) = Make(build, mapId, mods);
        var settings = new OfflineSettings { SampleRuns = 40 };
        var calc = new OfflineCalculator(OfflineTests.Loot, settings);
        var r = calc.Resolve(OfflineCheckpoint.Start("s", c, map, OfflineTests.Start, 11), OfflineTests.Start.AddHours(8));

        var (active, _) = RewardRates.BestActive(Runner, c.ToBuild(), map, 11, 60, settings);
        var offline = RewardRates.Offline(r);

        // Efficiency is 60%; allow for loot noise but never let offline approach active.
        Assert.True(offline.ItemsPerHour <= active.ItemsPerHour * 0.75, $"items/h offline {offline.ItemsPerHour:0} vs active {active.ItemsPerHour:0}");
        Assert.True(offline.ValuePerHour <= active.ValuePerHour * 0.85, $"value/h offline {offline.ValuePerHour:0} vs active {active.ValuePerHour:0}");
    }

    [Fact]
    public void Staying_away_longer_than_the_cap_earns_nothing_extra()
    {
        var c = ReferenceBuilds.IronBastion();
        var cp = OfflineCheckpoint.Start("s", c, new MapInstance { Map = Maps.SunkenCauseway }, OfflineTests.Start, 2);
        Assert.Equal(LootValue.Of(Calc.Resolve(cp, OfflineTests.Start.AddHours(8)).Loot), LootValue.Of(Calc.Resolve(cp, OfflineTests.Start.AddDays(7)).Loot));
    }

    [Fact]
    public void Bosses_and_tier_unlocks_need_active_play()
    {
        var c = ReferenceBuilds.IronBastion();
        var map = new MapInstance { Map = Maps.SunkenCauseway };
        var offline = Calc.Resolve(OfflineCheckpoint.Start("s", c, map, OfflineTests.Start, 3), OfflineTests.Start.AddHours(8));
        Assert.True(offline.RunsCompleted > 0);
        Assert.DoesNotContain(Calc.Sample(offline.Checkpoint), s => s.Encounters.Any(e => e.Encounter.Enemies.Any(x => x.IsBoss)));

        var active = Runner.RunMap(c.ToBuild(), c.Id, map, includeBoss: true, 3);
        Assert.Equal(ExpeditionOutcome.Completed, active.Outcome);
        var progress = new MapProgress();
        Assert.True(progress.RecordActiveBossKill(map.Map));
        Assert.Equal(2, progress.HighestUnlockedTier);
    }
}
