using ChaosIdle.Content;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;
using ChaosIdle.Simulation.Reporting;
using Xunit;

namespace ChaosIdle.Tests;

public class ExpeditionTests
{
    private static readonly LootGenerator Loot = new(ItemBases.All, Affixes.All);
    private static readonly ExpeditionRunner Runner = new(Loot);

    private static ExpeditionDefinition Exp(params ChaosIdle.Domain.Monsters.Encounter[] encounters) =>
        new() { Id = "expedition.test", Name = "Test Run", AreaLevel = 5, Encounters = encounters };

    [Fact]
    public void Same_seed_same_expedition()
    {
        var c = StarterCharacters.NewGuardian();
        var a = Runner.Run(c, Expeditions.AshenTrail, 7);
        var b = Runner.Run(c, Expeditions.AshenTrail, 7);
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(a.DurationSeconds, b.DurationSeconds);
        Assert.Equal(a.Loot.Select(i => i.Id), b.Loot.Select(i => i.Id));
    }

    [Fact]
    public void Combat_uses_its_own_seed_stream_independent_of_loot()
    {
        var c = StarterCharacters.NewGuardian();
        var report = Runner.Run(c, Expeditions.RoadPatrol, 11);
        var direct = new CombatSimulator().Run(c.ToBuild(), Encounters.AshenRoad, Seeds.Derive(11, 0));
        Assert.Equal(direct.DurationSeconds, report.Encounters[0].Combat.DurationSeconds);

        var noLoot = new ExpeditionRunner(new LootGenerator(ItemBases.All, Affixes.All, new LootSettings { NormalWeight = 0, MagicWeight = 0, RareWeight = 1 }));
        Assert.Equal(report.DurationSeconds, noLoot.Run(c, Expeditions.RoadPatrol, 11).DurationSeconds);
    }

    [Fact]
    public void Expedition_stops_at_the_first_failure_and_keeps_earlier_loot()
    {
        var killer = TestContent.Dummy(1_000_000, TestContent.FireBolt(10_000));
        var exp = Exp(TestContent.LootPinata(), TestContent.Fight(killer), Encounters.AshenRoad);

        var report = Runner.Run(StarterCharacters.NewGuardian(), exp, 1);

        Assert.Equal(ExpeditionOutcome.Failed, report.Outcome);
        Assert.Equal(2, report.Encounters.Count);
        Assert.Same(report.Encounters[1], report.FailedEncounter);
        Assert.Equal(8, report.Loot.Count);
        Assert.NotEmpty(report.Diagnoses);
    }

    [Fact]
    public void Loot_only_comes_from_kills()
    {
        var droppy = TestContent.Dummy(1_000_000) with { ItemDropChance = 1, GuaranteedDrops = 5 };
        var report = Runner.Run(StarterCharacters.NewGuardian(), Exp(TestContent.Fight(droppy)), 1, new SimulationOptions { MaxDurationSeconds = 10 });
        Assert.Equal(ExpeditionOutcome.Failed, report.Outcome);
        Assert.Empty(report.Loot);
    }

    [Fact]
    public void Loot_can_be_claimed_once_and_overflow_is_reported()
    {
        var report = Runner.Run(StarterCharacters.NewGuardian(), Exp(TestContent.LootPinata()), 3);
        var bag = new Inventory(5);

        var claim = report.ClaimLoot(bag);

        Assert.Equal(5, claim.Kept.Count);
        Assert.Equal(3, claim.LeftBehind.Count);
        Assert.Contains("left behind", ReportText.Claim(claim));
        Assert.Throws<InvalidOperationException>(() => report.ClaimLoot(new Inventory(20)));
    }

    [Fact]
    public void Running_does_not_modify_the_character()
    {
        var c = StarterCharacters.NewGuardian();
        Runner.Run(c, Expeditions.AshenTrail, 1);
        Assert.Empty(c.Inventory.Items);
    }

    [Fact]
    public void Item_levels_come_from_the_area()
    {
        var report = Runner.Run(StarterCharacters.NewGuardian(), Expeditions.RoadPatrol, 5);
        Assert.All(report.Loot, i => Assert.Equal(Expeditions.RoadPatrol.AreaLevel, i.ItemLevel));
    }
}

/// <summary>
/// Milestone 2 gate, domain half: a player can complete an expedition and understand its result.
/// (The Unity half of the gate needs the mobile client.)
/// </summary>
public class Milestone2GateTests
{
    private static readonly ExpeditionRunner Runner = new(new LootGenerator(ItemBases.All, Affixes.All));

    private static ChaosIdle.Domain.Characters.PlayerCharacter Starter()
    {
        var c = StarterCharacters.NewGuardian();
        c.Passives = PassiveAllocation.FromPath(PassiveTrees.Guardian, StarterCharacters.StartingPassivePoints, StarterCharacters.BulwarkPath);
        return c;
    }

    [Fact]
    public void A_starter_guardian_reliably_completes_the_first_expedition()
    {
        var reports = Enumerable.Range(1, 30).Select(s => Runner.Run(Starter(), Expeditions.RoadPatrol, (ulong)s)).ToList();
        Assert.True(reports.Count(r => r.Outcome == ExpeditionOutcome.Completed) >= 27);
    }

    [Fact]
    public void Every_expedition_result_reads_as_a_clear_report()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            foreach (var expedition in Expeditions.All)
            {
                var report = Runner.Run(Starter(), expedition, seed);
                var lines = ReportText.Expedition(report);

                Assert.StartsWith(expedition.Name, lines[0]);
                Assert.Contains(report.Outcome == ExpeditionOutcome.Completed ? "COMPLETED" : "FAILED", lines[0]);
                Assert.StartsWith("Loot:", lines[^1]);
                Assert.All(lines, l => Assert.True(l.Length <= 200, $"line too long for a phone: {l}"));

                if (report.Outcome == ExpeditionOutcome.Failed)
                {
                    Assert.NotEmpty(report.Diagnoses);
                    Assert.Contains("Why it failed:", lines);
                    Assert.Contains(lines, l => l.StartsWith("Try: ") && l.Length > "Try: ".Length);
                }
            }
        }
    }

    [Fact]
    public void Every_death_has_at_least_one_diagnosis()
    {
        var sim = new CombatSimulator();
        var builds = SampleBuilds.All.Concat(new[] { Starter().ToBuild(), StarterCharacters.NewGuardian().ToBuild() });
        foreach (var build in builds)
        foreach (var encounter in Encounters.All)
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var r = sim.Run(build, encounter, seed);
            if (r.Outcome != CombatOutcome.Victory) Assert.NotEmpty(r.Diagnoses);
        }
    }
}
