using ChaosIdle.Content;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;
using Xunit;

namespace ChaosIdle.Tests;

public class CombatSimulatorTests
{
    private readonly CombatSimulator _sim = new();

    [Fact]
    public void Same_seed_produces_identical_reports()
    {
        foreach (var build in SampleBuilds.All)
        {
            var a = _sim.Run(build, Encounters.AshenRoad, 99);
            var b = _sim.Run(build, Encounters.AshenRoad, 99);
            Assert.Equal(a.Outcome, b.Outcome);
            Assert.Equal(a.DurationSeconds, b.DurationSeconds);
            Assert.Equal(a.FinalLife, b.FinalLife);
            Assert.Equal(a.TotalDamageDealt, b.TotalDamageDealt);
            Assert.Equal(a.HitsBlocked, b.HitsBlocked);
            Assert.Equal(a.Kills.Select(k => k.KilledAt), b.Kills.Select(k => k.KilledAt));
        }
    }

    [Fact]
    public void Different_seeds_produce_different_fights()
    {
        var durations = Enumerable.Range(1, 10)
            .Select(s => _sim.Run(SampleBuilds.Bulwark, Encounters.AshenRoad, (ulong)s).DurationSeconds)
            .Distinct()
            .Count();
        Assert.True(durations > 1);
    }

    [Theory]
    [InlineData("build.bulwark")]
    [InlineData("build.pyre")]
    public void Invariants_hold_over_many_seeds(string buildId)
    {
        var build = SampleBuilds.All.Single(b => b.Id == buildId);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            foreach (var encounter in Encounters.All)
            {
                var r = _sim.Run(build, encounter, seed);
                Assert.InRange(r.FinalLife, 0, r.MaxLife);
                Assert.InRange(r.DurationSeconds, 0, SimulationOptions.Default.MaxDurationSeconds);
                Assert.True(r.DamageTaken.Total <= r.DamageTakenBeforeMitigation.Total + 1e-9);
                Assert.Equal(r.TotalDamageDealt, r.DamageDealtBySkill.Values.Sum(), 6);
                Assert.True(r.TriggeredSkillUses <= r.HitsBlocked);

                switch (r.Outcome)
                {
                    case CombatOutcome.Victory:
                        Assert.Equal(r.EnemiesTotal, r.Kills.Count);
                        Assert.True(r.FinalLife > 0);
                        Assert.Equal(encounter.Enemies.Sum(e => e.MaxLife), r.TotalDamageDealt, 6);
                        break;
                    case CombatOutcome.Death:
                        Assert.Equal(0, r.FinalLife);
                        Assert.NotNull(r.KillingBlow);
                        Assert.NotEmpty(r.Diagnoses);
                        break;
                }
            }
        }
    }

    [Fact]
    public void Blocking_triggers_counterattack_and_life_on_block()
    {
        var r = _sim.Run(SampleBuilds.Bulwark, Encounters.WardensHearth, 1);
        Assert.True(r.HitsBlocked > 0);
        Assert.True(r.TriggeredSkillUses > 0);
        Assert.True(r.DamageDealtBySkill[Skills.BulwarkRetort.Id] > 0);
        Assert.True(r.LifeGainedOnBlock > 0);
    }

    [Fact]
    public void Without_block_the_counterattack_never_fires()
    {
        var noBlock = SampleBuilds.Bulwark.With(new Modifier(StatId.BlockChance, ModifierKind.Flat, -45));
        var r = _sim.Run(noBlock, Encounters.AshenRoad, 1);
        Assert.Equal(0, r.HitsBlocked);
        Assert.Equal(0, r.TriggeredSkillUses);
        Assert.False(r.DamageDealtBySkill.ContainsKey(Skills.BulwarkRetort.Id));
    }

    [Fact]
    public void Timeout_is_reported_as_insufficient_damage()
    {
        var r = _sim.Run(SampleBuilds.Bulwark, TestContent.Fight(TestContent.Dummy()), 1,
            new SimulationOptions { MaxDurationSeconds = 30 });

        Assert.Equal(CombatOutcome.Timeout, r.Outcome);
        Assert.Equal(30, r.DurationSeconds, 9);
        Assert.Contains(r.Diagnoses, d => d.Code == DiagnosisCode.InsufficientDamage);
    }

    [Fact]
    public void Unaffordable_skill_without_regen_is_resource_starvation()
    {
        var build = SampleBuilds.Pyre with
        {
            PrimarySkill = Skills.CinderLance with { ManaCost = 1000 },
            Modifiers = SampleBuilds.Pyre.Modifiers.Where(m => m.Stat != StatId.ManaRegenPerSecond).Append(
                new Modifier(StatId.ManaRegenPerSecond, ModifierKind.More, -100)).ToArray(),
        };

        var r = _sim.Run(build, TestContent.Fight(TestContent.Dummy()), 1, new SimulationOptions { MaxDurationSeconds = 20 });

        Assert.Equal(CombatOutcome.Timeout, r.Outcome);
        Assert.Equal(0, r.PrimarySkillUses);
        Assert.Equal(20, r.TimeOutOfMana, 6);
        Assert.Contains(r.Diagnoses, d => d.Code == DiagnosisCode.ResourceStarvation);
    }

    [Fact]
    public void Expensive_skill_waits_for_mana_and_reports_starvation()
    {
        var build = SampleBuilds.Pyre with { PrimarySkill = Skills.CinderLance with { ManaCost = 40 } };
        var r = _sim.Run(build, Encounters.AshenRoad, 1);

        Assert.True(r.TimeOutOfMana > 0);
        Assert.Contains(r.Diagnoses, d => d.Code == DiagnosisCode.ResourceStarvation);
    }

    [Fact]
    public void Dying_to_fire_with_low_fire_resistance_is_diagnosed()
    {
        var caster = TestContent.Dummy(1_000_000, TestContent.FireBolt(120));
        var r = _sim.Run(SampleBuilds.Pyre, TestContent.Fight(caster), 1);

        Assert.Equal(CombatOutcome.Death, r.Outcome);
        var finding = Assert.Single(r.Diagnoses, d => d.Code == DiagnosisCode.LowResistance);
        Assert.Equal(DamageType.Fire, finding.DamageType);
        Assert.DoesNotContain(r.Diagnoses, d => d.Code == DiagnosisCode.LowPhysicalMitigation);
    }

    [Fact]
    public void Killing_blow_over_threshold_is_a_spike()
    {
        var caster = TestContent.Dummy(1_000_000, TestContent.FireBolt(10_000));
        var r = _sim.Run(SampleBuilds.Bulwark, TestContent.Fight(caster), 1);

        Assert.Equal(CombatOutcome.Death, r.Outcome);
        Assert.Equal(1.0, r.DurationSeconds, 9); // first enemy action
        Assert.Contains(r.Diagnoses, d => d.Code == DiagnosisCode.LargeHitSpike);
    }

    [Fact]
    public void Cooldown_attacks_start_on_cooldown()
    {
        var big = TestContent.FireBolt(10_000) with { CooldownSeconds = 5 };
        var enemy = TestContent.Dummy(1_000_000, big, TestContent.Harmless);
        var r = _sim.Run(SampleBuilds.Bulwark, TestContent.Fight(enemy), 1);

        Assert.Equal(CombatOutcome.Death, r.Outcome);
        Assert.Equal(5.0, r.KillingBlow!.Time, 9);
    }

    [Fact]
    public void Invalid_builds_are_rejected()
    {
        var bad = SampleBuilds.Bulwark with { PrimarySkill = Skills.BulwarkRetort };
        Assert.Throws<ArgumentException>(() => _sim.Run(bad, Encounters.AshenRoad, 1));
    }

    [Fact]
    public void Empty_encounters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => _sim.Run(SampleBuilds.Bulwark, TestContent.Fight(), 1));
    }
}
