using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;
using Xunit;

namespace ChaosIdle.Tests;

public class LootGeneratorTests
{
    private static readonly LootGenerator Generator = new(ItemBases.All, Affixes.All);

    [Fact]
    public void Same_seed_generates_the_same_item()
    {
        var a = Generator.Generate(10, false, new SplitMix64Random(5));
        var b = Generator.Generate(10, false, new SplitMix64Random(5));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Base, b.Base);
        Assert.Equal(a.Rarity, b.Rarity);
        Assert.Equal(a.Affixes.Select(x => (x.Affix.Id, x.Tier, x.Value)), b.Affixes.Select(x => (x.Affix.Id, x.Tier, x.Value)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public void Generated_items_always_obey_affix_rules(int itemLevel)
    {
        var rng = new SplitMix64Random((ulong)itemLevel);
        var ids = new HashSet<string>();
        for (int i = 0; i < 3000; i++)
        {
            var item = Generator.Generate(itemLevel, i % 3 == 0, rng);
            Assert.True(ids.Add(item.Id), "duplicate item id");
            Assert.True(item.Base.DropLevel <= itemLevel);

            switch (item.Rarity)
            {
                case ItemRarity.Normal:
                    Assert.Empty(item.Affixes);
                    break;
                case ItemRarity.Magic:
                    Assert.InRange(item.Affixes.Count, 1, 2);
                    Assert.True(item.CountAffixes(AffixKind.Prefix) <= 1 && item.CountAffixes(AffixKind.Suffix) <= 1);
                    break;
                case ItemRarity.Rare:
                    Assert.InRange(item.Affixes.Count, 1, 5);
                    Assert.True(item.CountAffixes(AffixKind.Prefix) <= 3 && item.CountAffixes(AffixKind.Suffix) <= 3);
                    break;
            }

            Assert.Equal(item.Affixes.Count, item.Affixes.Select(a => a.Affix.Group).Distinct().Count());
            foreach (var a in item.Affixes)
            {
                Assert.Contains(item.Class, a.Affix.AllowedClasses);
                var tier = a.Affix.Tiers.Single(t => t.Tier == a.Tier);
                Assert.True(tier.MinItemLevel <= itemLevel, $"T{a.Tier} {a.Affix.Id} on ilvl {itemLevel}");
                Assert.InRange(a.Value, tier.Min, tier.Max);
                Assert.Equal(Math.Floor(a.Value), a.Value);
            }
        }
    }

    [Fact]
    public void Rare_items_usually_get_at_least_three_affixes()
    {
        var rng = new SplitMix64Random(9);
        var rares = Enumerable.Range(0, 3000).Select(_ => Generator.Generate(10, true, rng)).Where(i => i.Rarity == ItemRarity.Rare).ToList();
        Assert.NotEmpty(rares);
        // Only classes with few eligible affixes (e.g. maces at low tiers) can fall short.
        Assert.True(rares.Count(r => r.Affixes.Count >= 3) >= rares.Count * 0.95);
    }

    [Fact]
    public void Rarity_follows_configured_weights()
    {
        var rng = new SplitMix64Random(3);
        const int n = 20_000;
        var counts = Enumerable.Range(0, n).Select(_ => Generator.RollRarity(false, rng)).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        Assert.InRange(counts[ItemRarity.Normal] / (double)n, 0.68, 0.72);
        Assert.InRange(counts[ItemRarity.Magic] / (double)n, 0.23, 0.27);
        Assert.InRange(counts[ItemRarity.Rare] / (double)n, 0.04, 0.06);
    }

    [Fact]
    public void Bosses_drop_more_rares()
    {
        var rng = new SplitMix64Random(4);
        int normal = Enumerable.Range(0, 5000).Count(_ => Generator.RollRarity(false, rng) == ItemRarity.Rare);
        int boss = Enumerable.Range(0, 5000).Count(_ => Generator.RollRarity(true, rng) == ItemRarity.Rare);
        Assert.True(boss > normal * 2);
    }

    [Fact]
    public void Custom_settings_can_force_rarity()
    {
        var onlyRares = new LootGenerator(ItemBases.All, Affixes.All, new LootSettings { NormalWeight = 0, MagicWeight = 0, RareWeight = 1 });
        var rng = new SplitMix64Random(1);
        Assert.All(Enumerable.Range(0, 200).Select(_ => onlyRares.Generate(10, false, rng)), i => Assert.Equal(ItemRarity.Rare, i.Rarity));
    }

    [Fact]
    public void Drops_respect_guaranteed_count_and_chance()
    {
        var rng = new SplitMix64Random(2);
        var never = Monsters.RustHound with { ItemDropChance = 0, GuaranteedDrops = 0 };
        var always = Monsters.RustHound with { ItemDropChance = 1, GuaranteedDrops = 2 };
        Assert.Empty(Generator.RollDrops(never, 5, rng));
        Assert.Equal(3, Generator.RollDrops(always, 5, rng).Count);
        var bossDrops = Generator.RollDrops(Monsters.AshWarden, 5, rng);
        Assert.InRange(bossDrops.Count, 3, 4); // 3 guaranteed + possible bonus
        Assert.All(bossDrops, i => Assert.Equal(5, i.ItemLevel));
    }

    [Fact]
    public void Low_level_drops_never_roll_top_tiers()
    {
        var rng = new SplitMix64Random(8);
        var items = Enumerable.Range(0, 2000).Select(_ => Generator.Generate(4, true, rng));
        Assert.DoesNotContain(items.SelectMany(i => i.Affixes), a => a.Tier < 3);
    }

    [Fact]
    public void All_affix_content_is_valid()
    {
        Assert.All(Affixes.All, a => Assert.Empty(a.Validate()));
        Assert.Equal(Affixes.All.Length, Affixes.All.Select(a => a.Group).Distinct().Count());
        Assert.InRange(Affixes.All.Length, 20, 50);
    }

    [Fact]
    public void Invalid_affix_tables_are_rejected()
    {
        var bad = Affixes.All[0] with { Tiers = Array.Empty<AffixTier>() };
        Assert.Throws<ArgumentException>(() => new LootGenerator(ItemBases.All, new[] { bad }));

        var badLocal = Affixes.All[0] with { Local = true }; // max life cannot be local
        Assert.NotEmpty(badLocal.Validate());
    }
}
