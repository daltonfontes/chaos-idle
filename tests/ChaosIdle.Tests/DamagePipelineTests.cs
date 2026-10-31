using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Damage;
using Xunit;

namespace ChaosIdle.Tests;

public class DamagePipelineTests
{
    private static readonly WeaponDefinition Weapon = new() { Id = "w", PhysicalMin = 10, PhysicalMax = 20, AttacksPerSecond = 2, CritChance = 5 };

    private static readonly SkillDefinition Attack = new()
    {
        Id = "test.attack", Tags = SkillTags.Attack, DamageEffectiveness = 200,
    };

    private static readonly SkillDefinition Spell = new()
    {
        Id = "test.spell", Tags = SkillTags.Spell, CastTimeSeconds = 1, BaseCritChance = 10, DamageEffectiveness = 50,
        BaseDamage = new[] { new DamageRange(DamageType.Fire, 10, 30) },
    };

    private static StatBlock Stats(params Modifier[] mods) => new(mods);
    private static Modifier M(StatId s, ModifierKind k, double v) => new(s, k, v);
    private const double NoCrit = 0.999;

    [Fact]
    public void Attack_scales_weapon_and_added_damage_then_increased_then_more()
    {
        var stats = Stats(
            M(StatId.PhysicalDamage, ModifierKind.Flat, 5),
            M(StatId.PhysicalDamage, ModifierKind.Increased, 50),
            M(StatId.AttackDamage, ModifierKind.Increased, 25),
            M(StatId.SpellDamage, ModifierKind.Increased, 500), // must not apply to attacks
            M(StatId.Damage, ModifierKind.More, 20));

        var hit = DamagePipeline.RollPlayerHit(Attack, Weapon, stats, new FixedRandom(NoCrit, 0.5));

        // base 15 * 200% = 30; added 5 * 200% = 10; (40) * 1.75 * 1.2 = 84
        Assert.Equal(84, hit.Damage[DamageType.Physical], 9);
        Assert.False(hit.IsCritical);
        Assert.False(hit.IsSpell);
    }

    [Fact]
    public void Spell_base_damage_ignores_effectiveness_but_added_damage_uses_it()
    {
        var stats = Stats(
            M(StatId.FireDamage, ModifierKind.Flat, 10),
            M(StatId.FireDamage, ModifierKind.Increased, 60),
            M(StatId.ElementalDamage, ModifierKind.Increased, 40),
            M(StatId.AttackDamage, ModifierKind.Increased, 500)); // must not apply to spells

        var hit = DamagePipeline.RollPlayerHit(Spell, Weapon, stats, new FixedRandom(NoCrit, 0.5));

        // base 20 + added 10 * 50% = 25; * (1 + 1.0) = 50
        Assert.Equal(50, hit.Damage[DamageType.Fire], 9);
        Assert.Equal(0, hit.Damage[DamageType.Physical]);
        Assert.True(hit.IsSpell);
    }

    [Fact]
    public void Conversion_happens_before_scaling_and_uses_final_type_modifiers()
    {
        var skill = Attack with
        {
            DamageEffectiveness = 100,
            Conversions = new[] { new DamageConversion(DamageType.Physical, DamageType.Fire, 50) },
        };
        var weapon = Weapon with { PhysicalMin = 100, PhysicalMax = 100 };
        var stats = Stats(M(StatId.FireDamage, ModifierKind.Increased, 100));

        var hit = DamagePipeline.RollPlayerHit(skill, weapon, stats, new FixedRandom(NoCrit));

        Assert.Equal(50, hit.Damage[DamageType.Physical], 9);
        Assert.Equal(100, hit.Damage[DamageType.Fire], 9);
    }

    [Fact]
    public void Conversions_do_not_chain()
    {
        var damage = DamageSet.Of(DamageType.Physical, 100);
        DamagePipeline.ApplyConversions(damage, new[]
        {
            new DamageConversion(DamageType.Physical, DamageType.Fire, 100),
            new DamageConversion(DamageType.Fire, DamageType.Cold, 100),
        });

        Assert.Equal(0, damage[DamageType.Physical], 9);
        Assert.Equal(100, damage[DamageType.Fire], 9);
        Assert.Equal(0, damage[DamageType.Cold], 9);
    }

    [Theory]
    [InlineData(0.19, true)]
    [InlineData(0.20, false)]
    public void Crit_chance_is_base_plus_flat_times_increased(double roll, bool expectCrit)
    {
        var stats = Stats(
            M(StatId.CriticalChance, ModifierKind.Increased, 100),
            M(StatId.CriticalMultiplier, ModifierKind.Flat, 50));

        // spell base crit 10% * 2 = 20%
        var hit = DamagePipeline.RollPlayerHit(Spell with { DamageEffectiveness = 0 }, Weapon, stats, new FixedRandom(0, 0.5, roll));

        Assert.Equal(expectCrit, hit.IsCritical);
        Assert.Equal(expectCrit ? 40 : 20, hit.Damage[DamageType.Fire], 9); // 150% + 50% = 2x
    }

    [Fact]
    public void Attack_hit_consumes_exactly_two_rolls()
    {
        var rng = new FixedRandom(0.5);
        DamagePipeline.RollPlayerHit(Attack, Weapon, Stats(), rng);
        Assert.Equal(2, rng.Consumed);
    }

    [Fact]
    public void Spells_cannot_be_evaded_or_blocked()
    {
        var defence = new DefenceStats { Evasion = 100_000, BlockChance = 75 };
        var result = DamagePipeline.Resolve(DamageSet.Of(DamageType.Fire, 100), isSpell: true, 1, 0, defence, new FixedRandom(0.0));
        Assert.Equal(HitOutcome.Hit, result.Outcome);
        Assert.Equal(100, result.Taken.Total, 9);
    }

    [Fact]
    public void Attacks_can_be_evaded()
    {
        var defence = new DefenceStats { Evasion = 400 }; // hit chance 0.5 vs accuracy 100
        var result = DamagePipeline.Resolve(DamageSet.Of(DamageType.Physical, 100), false, 100, 0, defence, new FixedRandom(0.0, 0.6, 0.9));
        Assert.Equal(HitOutcome.Evaded, result.Outcome);
        Assert.Equal(0, result.Taken.Total);
    }

    [Fact]
    public void Attacks_that_hit_can_be_blocked_and_deal_nothing()
    {
        var defence = new DefenceStats { BlockChance = 50 };
        var result = DamagePipeline.Resolve(DamageSet.Of(DamageType.Physical, 100), false, 100, 0, defence, new FixedRandom(0.0, 0.0, 0.3));
        Assert.Equal(HitOutcome.Blocked, result.Outcome);
        Assert.Equal(0, result.Taken.Total);
    }

    [Fact]
    public void Block_chance_is_capped_at_75_percent()
    {
        var defence = new DefenceStats { BlockChance = 100 };
        var result = DamagePipeline.Resolve(DamageSet.Of(DamageType.Physical, 100), false, 100, 0, defence, new FixedRandom(0.0, 0.0, 0.8));
        Assert.Equal(HitOutcome.Hit, result.Outcome);
    }

    [Fact]
    public void Armour_mitigates_physical_and_resistance_mitigates_elemental_with_penetration()
    {
        var incoming = new DamageSet { [DamageType.Physical] = 100, [DamageType.Fire] = 100 };
        var defence = new DefenceStats { Armour = 800, FireResistance = 90 };

        var result = DamagePipeline.Resolve(incoming, false, 100, 10, defence, new FixedRandom(0.0, 0.0, 0.99));

        Assert.Equal(HitOutcome.Hit, result.Outcome);
        Assert.Equal(50, result.Taken[DamageType.Physical], 9); // 50% armour reduction
        Assert.Equal(35, result.Taken[DamageType.Fire], 9);     // 75 cap - 10 pen = 65%
        Assert.Equal(50, result.PreventedByArmour, 9);
        Assert.Equal(65, result.PreventedByResistance, 9);
    }

    [Fact]
    public void Negative_resistance_increases_damage_taken()
    {
        var defence = new DefenceStats { ColdResistance = -50 };
        var result = DamagePipeline.Resolve(DamageSet.Of(DamageType.Cold, 100), true, 0, 0, defence, new FixedRandom(0.0));
        Assert.Equal(150, result.Taken.Total, 9);
    }

    [Fact]
    public void Resolve_always_consumes_two_rolls()
    {
        foreach (var isSpell in new[] { true, false })
        {
            var rng = new FixedRandom(0.0);
            DamagePipeline.Resolve(DamageSet.Of(DamageType.Physical, 10), isSpell, 1, 0, new DefenceStats { BlockChance = 75 }, rng);
            Assert.Equal(2, rng.Consumed);
        }
    }

    [Fact]
    public void Action_time_uses_weapon_rate_for_attacks_and_cast_time_for_spells()
    {
        var stats = Stats(
            M(StatId.AttackSpeed, ModifierKind.Increased, 25),
            M(StatId.CastSpeed, ModifierKind.Increased, 100));

        Assert.Equal(1 / 2.5, DamagePipeline.ActionTime(Attack, Weapon, stats), 9);
        Assert.Equal(0.5, DamagePipeline.ActionTime(Spell, Weapon, stats), 9);
    }
}
