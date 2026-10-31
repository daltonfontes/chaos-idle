using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Reporting;
using Xunit;
using static ChaosIdle.Tests.ItemTests;

namespace ChaosIdle.Tests;

public class ItemComparisonTests
{
    [Fact]
    public void Strictly_better_weapon_is_an_upgrade()
    {
        var character = StarterCharacters.NewGuardian();
        var better = Make(ItemBases.IronboundMace, ("affix.local_inc_phys", 50));
        var c = ItemComparer.Compare(character, better, EquipmentSlot.MainHand);

        Assert.Equal(ComparisonVerdict.Upgrade, c.Verdict);
        Assert.Equal(character.Equipment[EquipmentSlot.MainHand], c.Replaced);
        Assert.Equal(c.Before.HitDamagePerSecond * 1.5, c.After.HitDamagePerSecond, 6);
    }

    [Fact]
    public void Identical_item_is_no_change()
    {
        var character = StarterCharacters.NewGuardian();
        var c = ItemComparer.Compare(character, Make(ItemBases.IronboundMace), EquipmentSlot.MainHand);
        Assert.Equal(ComparisonVerdict.NoChange, c.Verdict);
        Assert.Empty(c.Changes);
    }

    [Fact]
    public void Mixed_stats_are_a_trade_off()
    {
        var character = StarterCharacters.NewGuardian();
        var fastButWeak = Make(ItemBases.AshwoodRod); // faster, more crit, much less damage
        var c = ItemComparer.Compare(character, fastButWeak, EquipmentSlot.MainHand);

        Assert.Equal(ComparisonVerdict.TradeOff, c.Verdict);
        Assert.Contains(c.Changes, x => x.Label == "Damage per second" && !x.IsImprovement);
        Assert.Contains(c.Changes, x => x.Label == "Actions per second" && x.IsImprovement);
    }

    [Fact]
    public void Removing_defence_is_a_downgrade()
    {
        var character = StarterCharacters.NewGuardian();
        var helmet = Make(ItemBases.IronCap, ("affix.life", 30));
        character.Equipment.Equip(helmet, EquipmentSlot.Helmet);

        var c = ItemComparer.Compare(character, Make(ItemBases.IronCap), EquipmentSlot.Helmet);
        Assert.Equal(ComparisonVerdict.Downgrade, c.Verdict);
    }

    [Fact]
    public void Extra_mana_spending_is_informational_while_sustainable()
    {
        var character = StarterCharacters.NewGuardian();
        var faster = Make(ItemBases.IronboundMace, ("affix.local_attack_speed", 10));
        var c = ItemComparer.Compare(character, faster, EquipmentSlot.MainHand);

        Assert.True(c.Before.ManaSustainable && c.After.ManaSustainable);
        var mana = Assert.Single(c.Changes, x => x.Label == "Mana spent /s");
        Assert.False(mana.AffectsVerdict);
        Assert.Equal(ComparisonVerdict.Upgrade, c.Verdict);
    }

    [Fact]
    public void Comparing_does_not_change_the_character()
    {
        var character = StarterCharacters.NewGuardian();
        var mace = character.Equipment[EquipmentSlot.MainHand];
        ItemComparer.Compare(character, Make(ItemBases.FlangedMace), EquipmentSlot.MainHand);
        Assert.Same(mace, character.Equipment[EquipmentSlot.MainHand]);
    }

    [Fact]
    public void Wrong_slot_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ItemComparer.Compare(StarterCharacters.NewGuardian(), Make(ItemBases.IronCap), EquipmentSlot.MainHand));
    }

    [Fact]
    public void Comparison_text_uses_words_and_arrows_not_just_colour()
    {
        var character = StarterCharacters.NewGuardian();
        var lines = ReportText.Comparison(ItemComparer.Compare(character, Make(ItemBases.AshwoodRod), EquipmentSlot.MainHand));
        Assert.Contains("TRADE-OFF", lines[0]);
        Assert.Contains(lines, l => l.Contains("▲ better"));
        Assert.Contains(lines, l => l.Contains("▼ worse"));
    }

    [Fact]
    public void Simulated_comparison_runs_both_sides_on_the_same_seeds()
    {
        var character = TestContent.StarterWithPassives();
        var (before, after) = ItemComparer.Simulate(character, Make(ItemBases.IronboundMace, ("affix.local_inc_phys", 79)), EquipmentSlot.MainHand, Encounters.AshenRoad, 1, 10);
        Assert.Equal(10, before.Runs);
        Assert.Equal(10, after.Runs);
        Assert.True(after.MeanDamagePerSecond > before.MeanDamagePerSecond * 1.3);
        Assert.NotNull(after.MedianClearTime);
        Assert.True(before.MedianClearTime is null || after.MedianClearTime < before.MedianClearTime);
    }
}
