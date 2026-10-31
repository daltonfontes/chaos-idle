using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Damage;
using Xunit;

namespace ChaosIdle.Tests;

public class SupportTests
{
    [Fact]
    public void Compatibility_follows_explicit_tag_rules()
    {
        Assert.Equal(SupportCompatibility.Compatible, Supports.HeavyImpact.CanSupport(Skills.IronCleave));
        Assert.Equal(SupportCompatibility.MissingTag, Supports.HeavyImpact.CanSupport(Skills.CinderLance));
        Assert.Equal(SupportCompatibility.MissingTag, Supports.FocusedIntent.CanSupport(Skills.IronCleave));
        Assert.Equal(SupportCompatibility.MissingTag, Supports.ReadyRetort.CanSupport(Skills.IronCleave));
        Assert.Equal(SupportCompatibility.Compatible, Supports.ReadyRetort.CanSupport(Skills.BulwarkRetort));
        Assert.Equal(SupportCompatibility.ExcludedTag, Supports.FrugalCasting.CanSupport(Skills.BulwarkRetort));
        Assert.Equal(SupportCompatibility.Compatible, Supports.KeenEdge.CanSupport(Skills.CinderLance));
    }

    [Fact]
    public void Sockets_are_limited_and_duplicates_refused()
    {
        var c = StarterCharacters.NewGuardian();
        Assert.Equal(SupportCompatibility.Compatible, c.LinkSupport(Skills.IronCleave, Supports.HeavyImpact));
        Assert.Equal(SupportCompatibility.AlreadyLinked, c.LinkSupport(Skills.IronCleave, Supports.HeavyImpact));
        Assert.Equal(SupportCompatibility.Compatible, c.LinkSupport(Skills.IronCleave, Supports.Siphon));
        Assert.Equal(SupportCompatibility.Compatible, c.LinkSupport(Skills.IronCleave, Supports.KeenEdge));
        Assert.Equal(SupportCompatibility.NoFreeSocket, c.LinkSupport(Skills.IronCleave, Supports.SunderingFocus));
        Assert.Equal(SupportCompatibility.MissingTag, c.LinkSupport(Skills.IronCleave, Supports.FocusedIntent));
        Assert.Equal(3, c.SupportsFor(Skills.IronCleave).Count);
        Assert.True(c.UnlinkSupport(Skills.IronCleave, Supports.KeenEdge));
        Assert.Equal(2, c.SupportsFor(Skills.IronCleave).Count);
    }

    [Fact]
    public void Linking_to_a_skill_the_character_does_not_use_throws()
    {
        Assert.Throws<ArgumentException>(() => StarterCharacters.NewGuardian().LinkSupport(Skills.CinderLance, Supports.FocusedIntent));
    }

    [Fact]
    public void Supports_affect_only_their_skill_and_never_defences()
    {
        var c = StarterCharacters.NewGuardian();
        var before = c.ToBuild();
        c.LinkSupport(Skills.IronCleave, Supports.HeavyImpact);
        var after = c.ToBuild();

        var weapon = after.Weapon;
        double cleaveBefore = DamagePipeline.ExpectedHit(Skills.IronCleave, weapon, before.ComputeSkillStats(Skills.IronCleave)).Total;
        double cleaveAfter = DamagePipeline.ExpectedHit(Skills.IronCleave, weapon, after.ComputeSkillStats(Skills.IronCleave)).Total;
        Assert.Equal(cleaveBefore * 1.35, cleaveAfter, 6);

        double retortBefore = DamagePipeline.ExpectedHit(Skills.BulwarkRetort, weapon, before.ComputeSkillStats(Skills.BulwarkRetort)).Total;
        double retortAfter = DamagePipeline.ExpectedHit(Skills.BulwarkRetort, weapon, after.ComputeSkillStats(Skills.BulwarkRetort)).Total;
        Assert.Equal(retortBefore, retortAfter, 9);

        Assert.Equal(before.ComputeStats().Resolve(StatId.MaxLife), after.ComputeStats().Resolve(StatId.MaxLife));
        Assert.Equal(before.ComputeStats().Flat(StatId.PhysicalDamage), after.ComputeStats().Flat(StatId.PhysicalDamage));
    }

    [Fact]
    public void Support_costs_show_up_on_the_character_sheet()
    {
        var c = StarterCharacters.NewGuardian();
        var plain = BuildSummary.Compute(c.ToBuild());
        c.LinkSupport(Skills.IronCleave, Supports.HeavyImpact);
        var heavy = BuildSummary.Compute(c.ToBuild());

        Assert.Equal(plain.ManaCostPerUse * 1.3, heavy.ManaCostPerUse, 6);
        Assert.Equal(plain.ActionsPerSecond * 0.9, heavy.ActionsPerSecond, 6);
    }

    [Fact]
    public void Searing_edge_converts_attack_damage_to_fire()
    {
        var c = StarterCharacters.NewGuardian();
        c.LinkSupport(Skills.IronCleave, Supports.SearingEdge);
        var build = c.ToBuild();
        var hit = DamagePipeline.ExpectedHit(Skills.IronCleave, build.Weapon, build.ComputeSkillStats(Skills.IronCleave));
        Assert.True(hit[DamageType.Fire] > 0);
        Assert.Equal(hit[DamageType.Physical] * 1.15, hit[DamageType.Fire], 6); // 50/50 split, fire 15% more
    }

    [Fact]
    public void Stat_and_skill_conversions_over_100_percent_are_scaled_down()
    {
        var skill = Skills.IronCleave with { Conversions = new[] { new DamageConversion(DamageType.Physical, DamageType.Cold, 60) } };
        var stats = new StatBlock(new[] { new Modifier(StatId.PhysicalToFireConversion, ModifierKind.Flat, 60) });
        var conversions = DamagePipeline.EffectiveConversions(skill, stats);

        Assert.Equal(100, conversions.Where(c => c.From == DamageType.Physical).Sum(c => c.Percent), 9);
        Assert.Equal(50, conversions.Single(c => c.To == DamageType.Cold).Percent, 9);
        Assert.Equal(50, conversions.Single(c => c.To == DamageType.Fire).Percent, 9);
    }

    [Fact]
    public void Cooldown_recovery_lets_triggered_skills_fire_more_often()
    {
        var c = TestContent.StarterWithPassives();
        var without = new CombatSimulator().Run(c.ToBuild(), Encounters.WardensHearth, 1, new SimulationOptions { MaxDurationSeconds = 60 });
        c.LinkSupport(Skills.BulwarkRetort, Supports.ReadyRetort);
        var with = new CombatSimulator().Run(c.ToBuild(), Encounters.WardensHearth, 1, new SimulationOptions { MaxDurationSeconds = 60 });

        Assert.Equal(0.5 / 1.6, DamagePipeline.Cooldown(Skills.BulwarkRetort, c.ToBuild().ComputeSkillStats(Skills.BulwarkRetort)), 9);
        Assert.True(with.TriggeredSkillUses >= without.TriggeredSkillUses);
        Assert.True(with.DamageDealtBySkill[Skills.BulwarkRetort.Id] > without.DamageDealtBySkill[Skills.BulwarkRetort.Id]);
    }

    [Fact]
    public void Siphon_leech_comes_from_the_supported_skill()
    {
        var c = TestContent.StarterWithPassives();
        c.LinkSupport(Skills.IronCleave, Supports.Siphon);
        var r = new CombatSimulator().Run(c.ToBuild(), Encounters.AshenRoad, 1);
        Assert.True(r.LifeLeeched > 0);
        Assert.Equal(0, c.ToBuild().ComputeStats().Flat(StatId.LifeLeechPercent));
    }

    [Fact]
    public void Invalid_support_links_are_rejected_by_the_simulator()
    {
        var bad = SampleBuilds.Bulwark with
        {
            SkillSupports = new Dictionary<string, IReadOnlyList<SupportDefinition>> { [Skills.IronCleave.Id] = new[] { Supports.FocusedIntent } },
        };
        Assert.Throws<ArgumentException>(() => new CombatSimulator().Run(bad, Encounters.AshenRoad, 1));

        var unused = SampleBuilds.Bulwark with
        {
            SkillSupports = new Dictionary<string, IReadOnlyList<SupportDefinition>> { [Skills.CinderLance.Id] = new[] { Supports.FocusedIntent } },
        };
        Assert.NotEmpty(unused.Validate());
    }

    [Fact]
    public void Switching_skills_drops_supports_from_the_build_snapshot()
    {
        var c = StarterCharacters.NewGuardian();
        c.LinkSupport(Skills.IronCleave, Supports.HeavyImpact);
        c.PrimarySkill = Skills.CinderLance;
        var build = c.ToBuild();
        Assert.Empty(build.Validate());
        Assert.False(build.SkillSupports.ContainsKey(Skills.IronCleave.Id));
    }

    [Fact]
    public void Every_support_has_a_cost_or_restriction()
    {
        Assert.Equal(8, Supports.All.Length);
        Assert.Equal(Supports.All.Length, Supports.All.Select(s => s.Id).Distinct().Count());
        Assert.All(Supports.All, s =>
        {
            bool hasDownside = s.Modifiers.Any(m => (m.Stat == StatId.ManaCost && m.Value > 0) || (m.Kind == ModifierKind.More && m.Value < 0 && m.Stat != StatId.ManaCost));
            bool isRestricted = s.RequiredTags != SkillTags.None || s.ExcludedTags != SkillTags.None;
            Assert.True(hasDownside || isRestricted, $"{s.Id} is a free upgrade");
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }
}
