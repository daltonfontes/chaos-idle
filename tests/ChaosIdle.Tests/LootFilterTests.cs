using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Reporting;
using Xunit;
using static ChaosIdle.Tests.ItemTests;

namespace ChaosIdle.Tests;

public class LootFilterTests
{
    private static Item Magic(ItemBase b, params (string, double)[] affixes) => Make(b, affixes) with { Rarity = ItemRarity.Magic };

    [Fact]
    public void First_matching_rule_wins_and_default_applies_otherwise()
    {
        var filter = new LootFilter
        {
            Rules = new[]
            {
                new LootFilterRule { Name = "Rings", Action = FilterAction.Highlight, Classes = new[] { ItemClass.Ring } },
                new LootFilterRule { Name = "Normals", Action = FilterAction.Hide, MaxRarity = ItemRarity.Normal },
            },
            DefaultAction = FilterAction.Show,
        };

        Assert.Equal(new FilterDecision(FilterAction.Highlight, "Rings"), filter.Evaluate(Make(ItemBases.IronBand)));
        Assert.Equal(new FilterDecision(FilterAction.Hide, "Normals"), filter.Evaluate(Make(ItemBases.IronCap)));
        Assert.Equal(FilterAction.Show, filter.Evaluate(Make(ItemBases.IronCap, ("affix.life", 20))).Action);
    }

    [Fact]
    public void Wanted_stat_conditions_count_matching_affixes()
    {
        var two = new LootFilterRule { WantedStats = new[] { StatId.MaxLife, StatId.FireResistance }, MinMatchingAffixes = 2 };
        var none = new LootFilterRule { WantedStats = new[] { StatId.MaxLife }, MaxMatchingAffixes = 0 };

        Assert.True(two.Matches(Make(ItemBases.IronCap, ("affix.life", 20), ("affix.fire_res", 10))));
        Assert.False(two.Matches(Make(ItemBases.IronCap, ("affix.life", 20))));
        Assert.True(none.Matches(Make(ItemBases.IronCap, ("affix.cold_res", 10))));
        Assert.False(none.Matches(Make(ItemBases.IronCap, ("affix.life", 20))));
    }

    [Fact]
    public void Build_relevance_follows_the_skill()
    {
        var melee = StatsOf(ReferenceBuilds.IronBastion());
        var spell = StatsOf(ReferenceBuilds.EmberLancer());

        Assert.Contains(StatId.AttackSpeed, melee);
        Assert.Contains(StatId.BlockChance, melee);
        Assert.DoesNotContain(StatId.CastSpeed, melee);
        Assert.Contains(StatId.CastSpeed, spell);
        Assert.Contains(StatId.FireDamage, spell);
        Assert.DoesNotContain(StatId.AttackSpeed, spell);

        static IReadOnlyList<StatId> StatsOf(ChaosIdle.Domain.Characters.PlayerCharacter c) => BuildRelevance.StatsFor(c.ToBuild());
    }

    [Fact]
    public void Build_preset_highlights_uniques_and_on_build_items_and_hides_off_build_magic()
    {
        var filter = BuildRelevance.FilterFor(ReferenceBuilds.EmberLancer().ToBuild());

        Assert.Equal(FilterAction.Highlight, filter.Evaluate(Uniques.KindledOath.Roll("u", 10, () => 0.5)).Action);
        Assert.Equal(FilterAction.Highlight, filter.Evaluate(Make(ItemBases.EmberRod, ("affix.spell_damage", 30), ("affix.cast_speed", 9))).Action);
        Assert.Equal(FilterAction.Hide, filter.Evaluate(Magic(ItemBases.IronboundMace, ("affix.local_attack_speed", 9))).Action);
        Assert.Equal(FilterAction.Show, filter.Evaluate(Make(ItemBases.ScaleHauberk)).Action);
        Assert.Equal(FilterAction.Hide, BuildRelevance.FilterFor(ReferenceBuilds.EmberLancer().ToBuild(), strict: true).Evaluate(Make(ItemBases.ScaleHauberk)).Action);
    }

    [Fact]
    public void Claiming_through_a_filter_skips_hidden_and_takes_highlighted_first()
    {
        var runner = new ExpeditionRunner(new LootGenerator(ItemBases.All, Affixes.All, uniques: Uniques.All));
        var report = runner.Run(StarterCharacters.NewGuardian(), new ChaosIdle.Domain.Expeditions.ExpeditionDefinition
        {
            Id = "expedition.test", Name = "Test", AreaLevel = 10, Encounters = new[] { TestContent.LootPinata() },
        }, 4);
        Assert.Equal(8, report.Loot.Count);

        var filter = new LootFilter
        {
            Rules = new[]
            {
                new LootFilterRule { Name = "Hide normals", Action = FilterAction.Hide, MaxRarity = ItemRarity.Normal },
                new LootFilterRule { Name = "Last item", Action = FilterAction.Highlight, Classes = new[] { report.Loot[^1].Class } },
            },
        };
        int normals = report.Loot.Count(i => i.Rarity == ItemRarity.Normal);
        var bag = new Inventory(1);
        var claim = report.ClaimLoot(bag, filter);

        Assert.Equal(normals, claim.Hidden.Count);
        Assert.DoesNotContain(claim.Hidden, bag.Items.Contains);
        if (report.Loot[^1].Rarity != ItemRarity.Normal)
        {
            Assert.Contains(bag.Items[0], claim.Highlighted);
        }

        Assert.Equal(report.Loot.Count, claim.Kept.Count + claim.LeftBehind.Count + claim.Hidden.Count);
    }

    [Fact]
    public void Claim_text_mentions_filtered_and_highlighted_items()
    {
        var a = Make(ItemBases.IronCap) with { Id = "a" };
        var b = Make(ItemBases.IronBand) with { Id = "b" };
        var text = ReportText.Claim(new LootClaim(new[] { b }, Array.Empty<Item>(), new[] { a }, new[] { b }));
        Assert.Contains("1 highlighted ★", text);
        Assert.Contains("1 hidden by your loot filter", text);
    }
}
