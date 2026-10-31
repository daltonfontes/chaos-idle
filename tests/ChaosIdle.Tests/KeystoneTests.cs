using ChaosIdle.Content;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Damage;
using Xunit;

namespace ChaosIdle.Tests;

public class KeystoneTests
{
    private static readonly string[] ToBastion = { "pas.def.frame1", "pas.def.guard1", "pas.def.guard2", "pas.def.shieldwall", "pas.key.bastion" };

    [Fact]
    public void Bastion_lets_block_apply_to_spells_at_the_cost_of_life()
    {
        var character = StarterCharacters.NewGuardian();
        character.Passives = PassiveAllocation.FromPath(PassiveTrees.Guardian, 12, ToBastion.Take(4).ToArray());
        var without = character.ToBuild().ComputeStats();
        character.Passives = character.Passives.Allocate("pas.key.bastion");
        var with = character.ToBuild().ComputeStats();

        Assert.False(DefenceStats.From(without).BlockSpells);
        Assert.True(DefenceStats.From(with).BlockSpells);
        Assert.Equal(without.Resolve(StatId.MaxLife) * 0.8, with.Resolve(StatId.MaxLife), 6);
    }

    [Fact]
    public void Spell_block_blocks_spells_but_spells_are_still_never_evaded()
    {
        var defence = new DefenceStats { BlockChance = 50, BlockSpells = true, Evasion = 1_000_000 };
        var blocked = DamagePipeline.Resolve(DamageSet.Of(DamageType.Fire, 100), true, 1, 0, defence, new FixedRandom(0, 0.99, 0.2));
        var hit = DamagePipeline.Resolve(DamageSet.Of(DamageType.Fire, 100), true, 1, 0, defence, new FixedRandom(0, 0.99, 0.7));

        Assert.Equal(HitOutcome.Blocked, blocked.Outcome);
        Assert.Equal(HitOutcome.Hit, hit.Outcome);
    }

    [Fact]
    public void Overcharge_trades_mana_cost_for_spell_damage()
    {
        var stats = new StatBlock(Array.Empty<Modifier>());
        var overcharged = new StatBlock(new[]
        {
            new Modifier(StatId.SpellDamage, ModifierKind.More, 40),
            new Modifier(StatId.ManaCost, ModifierKind.More, 50),
        });
        var skill = Skills.CinderLance;
        var weapon = Weapons.AshwoodRod;

        Assert.Equal(DamagePipeline.ExpectedHit(skill, weapon, stats).Total * 1.4, DamagePipeline.ExpectedHit(skill, weapon, overcharged).Total, 6);
        Assert.Equal(skill.ManaCost * 1.5, DamagePipeline.ManaCost(skill, overcharged), 6);
    }

    [Fact]
    public void Overcharge_does_not_affect_attacks()
    {
        var overcharged = new StatBlock(new[] { new Modifier(StatId.SpellDamage, ModifierKind.More, 40) });
        var plain = new StatBlock(Array.Empty<Modifier>());
        Assert.Equal(
            DamagePipeline.ExpectedHit(Skills.IronCleave, Weapons.IronboundMace, plain).Total,
            DamagePipeline.ExpectedHit(Skills.IronCleave, Weapons.IronboundMace, overcharged).Total, 9);
    }

    [Fact]
    public void Mana_cost_scaling_never_goes_negative()
    {
        var stats = new StatBlock(new[] { new Modifier(StatId.ManaCost, ModifierKind.Flat, -100) });
        Assert.Equal(0, DamagePipeline.ManaCost(Skills.IronCleave, stats));
    }

    [Fact]
    public void Expected_hit_folds_in_crit()
    {
        var stats = new StatBlock(new[] { new Modifier(StatId.CriticalMultiplier, ModifierKind.Flat, 50) });
        var skill = Skills.CinderLance; // 40-60 fire, 6% crit, 2.0x multiplier
        Assert.Equal(50 * (1 + 0.06 * 1.0), DamagePipeline.ExpectedHit(skill, Weapons.AshwoodRod, stats).Total, 9);
    }
}
