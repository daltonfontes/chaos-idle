using ChaosIdle.Content;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Damage;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;
using ChaosIdle.Simulation.Reporting;
using Xunit;

namespace ChaosIdle.Tests;

public class UniqueTests
{
    private static Item Roll(UniqueDefinition u) => u.Roll("test-" + u.Id, 10, () => 0.5);

    [Fact]
    public void All_uniques_are_valid_and_have_a_drawback()
    {
        Assert.All(Uniques.All, u => Assert.Empty(u.Validate()));
        Assert.All(Uniques.All, u => Assert.Contains(u.Modifiers, m => m.Tiers[0].Max < 0));
    }

    [Fact]
    public void Kindled_oath_turns_a_melee_build_towards_fire()
    {
        var c = StarterCharacters.NewGuardian();
        c.Equipment.Equip(Roll(Uniques.KindledOath), EquipmentSlot.MainHand);
        var build = c.ToBuild();
        var hit = DamagePipeline.ExpectedHit(Skills.IronCleave, build.Weapon, build.ComputeSkillStats(Skills.IronCleave));

        Assert.True(hit[DamageType.Fire] > hit[DamageType.Physical]);
        Assert.Equal(-15, build.ComputeStats().Flat(StatId.FireResistance));

        // Fire passives now scale the melee skill.
        var withHearth = c.ToBuild() with { Modifiers = build.Modifiers.Append(new Modifier(StatId.FireDamage, ModifierKind.Increased, 50)).ToArray() };
        double boosted = DamagePipeline.ExpectedHit(Skills.IronCleave, build.Weapon, withHearth.ComputeSkillStats(Skills.IronCleave)).Total;
        Assert.True(boosted > hit.Total * 1.2);
    }

    [Fact]
    public void Mirrorglass_ward_grants_spell_block_for_life()
    {
        var c = StarterCharacters.NewGuardian();
        double lifeBefore = c.ToBuild().ComputeStats().Resolve(StatId.MaxLife);
        c.Equipment.Equip(Roll(Uniques.MirrorglassWard), EquipmentSlot.OffHand);
        var stats = c.ToBuild().ComputeStats();

        Assert.True(DefenceStats.From(stats).BlockSpells);
        Assert.Equal(lifeBefore - 60, stats.Resolve(StatId.MaxLife), 6);
    }

    [Fact]
    public void Bloodbound_pendant_trades_regeneration_for_leech_on_every_skill()
    {
        var c = StarterCharacters.NewGuardian();
        c.Equipment.Equip(Roll(Uniques.BloodboundPendant), EquipmentSlot.Amulet);
        var build = c.ToBuild();
        Assert.Equal(0.5, build.ComputeStats().MoreMultiplier(StatId.LifeRegenPerSecond), 9);
        Assert.True(build.ComputeSkillStats(Skills.BulwarkRetort).Flat(StatId.LifeLeechPercent) >= 2);
    }

    [Fact]
    public void Stillwater_helm_makes_skills_cheaper()
    {
        var c = StarterCharacters.NewGuardian();
        double before = BuildSummary.Compute(c.ToBuild()).ManaCostPerUse;
        c.Equipment.Equip(Roll(Uniques.StillwaterHelm), EquipmentSlot.Helmet);
        Assert.Equal(before * 0.7, BuildSummary.Compute(c.ToBuild()).ManaCostPerUse, 9);
    }

    [Fact]
    public void Uniques_drop_with_their_name_and_fixed_modifiers()
    {
        var onlyUniques = new LootGenerator(ItemBases.All, Affixes.All,
            new LootSettings { NormalWeight = 0, MagicWeight = 0, RareWeight = 0, UniqueWeight = 1 }, Uniques.All);
        var rng = new SplitMix64Random(1);
        var items = Enumerable.Range(0, 200).Select(_ => onlyUniques.Generate(10, false, rng)).ToList();

        Assert.All(items, i => Assert.Equal(ItemRarity.Unique, i.Rarity));
        Assert.Equal(Uniques.All.Length, items.Select(i => i.Unique!.Id).Distinct().Count());
        Assert.All(items, i =>
        {
            Assert.Equal(i.Unique!.Base, i.Base);
            Assert.Equal(i.Unique.Modifiers.Count, i.Affixes.Count);
            Assert.All(i.Affixes, a => Assert.InRange(a.Value, a.Affix.Tiers[0].Min, a.Affix.Tiers[0].Max));
        });
    }

    [Fact]
    public void Without_uniques_the_unique_roll_never_happens()
    {
        var gen = new LootGenerator(ItemBases.All, Affixes.All, new LootSettings { NormalWeight = 0, MagicWeight = 0, RareWeight = 1, UniqueWeight = 1000 });
        var rng = new SplitMix64Random(2);
        Assert.All(Enumerable.Range(0, 100).Select(_ => gen.Generate(10, true, rng)), i => Assert.Equal(ItemRarity.Rare, i.Rarity));
    }

    [Fact]
    public void Unique_tooltip_shows_name_base_and_flavour_without_tiers()
    {
        var lines = ReportText.ItemTooltip(Roll(Uniques.StillwaterHelm));
        Assert.StartsWith("Stillwater Helm (Iron Cap) [Unique]", lines[0]);
        Assert.Contains("  Skills cost 30% less Mana", lines);
        Assert.Contains(lines, l => l.Contains("-10% to Fire Resistance"));
        Assert.DoesNotContain(lines, l => l.Contains("(T1)"));
        Assert.Contains(lines, l => l.Contains("calm before the pyre"));
    }

    [Fact]
    public void Unique_comparison_shows_its_trade_off()
    {
        var c = StarterCharacters.NewGuardian();
        var comparison = ItemComparer.Compare(c, Roll(Uniques.KindledOath), EquipmentSlot.MainHand);
        Assert.Equal(ComparisonVerdict.TradeOff, comparison.Verdict);
        Assert.Contains(comparison.Changes, x => x.Label == "Fire resistance" && !x.IsImprovement);
    }
}
