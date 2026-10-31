using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Loot
{
    public sealed record LootSettings
    {
        public static readonly LootSettings Default = new LootSettings();

        public int NormalWeight { get; init; } = 70;
        public int MagicWeight { get; init; } = 25;
        public int RareWeight { get; init; } = 5;

        /// <summary>Bosses multiply the magic and rare weights by this.</summary>
        public int BossRarityMultiplier { get; init; } = 4;

        public int MagicMinAffixes { get; init; } = 1;
        public int MagicMaxAffixes { get; init; } = 2;
        public int MagicMaxPerKind { get; init; } = 1;
        public int RareMinAffixes { get; init; } = 3;
        public int RareMaxAffixes { get; init; } = 5;
        public int RareMaxPerKind { get; init; } = 3;
    }

    /// <summary>Data-driven, seeded item generation.</summary>
    public sealed class LootGenerator
    {
        private readonly IReadOnlyList<ItemBase> _bases;
        private readonly IReadOnlyList<AffixDefinition> _affixes;

        public LootGenerator(IReadOnlyList<ItemBase> bases, IReadOnlyList<AffixDefinition> affixes, LootSettings? settings = null)
        {
            var errors = affixes.SelectMany(a => a.Validate()).ToList();
            if (bases.Count == 0) errors.Add("No item bases.");
            if (errors.Count > 0) throw new ArgumentException("Invalid loot tables:\n" + string.Join("\n", errors));

            _bases = bases;
            _affixes = affixes;
            Settings = settings ?? LootSettings.Default;
        }

        public LootSettings Settings { get; }

        public IReadOnlyList<Item> RollDrops(EnemyDefinition enemy, int areaLevel, IRandom rng)
        {
            int count = enemy.GuaranteedDrops + (rng.NextDouble() < enemy.ItemDropChance ? 1 : 0);
            var drops = new List<Item>(count);
            for (int i = 0; i < count; i++)
            {
                drops.Add(Generate(areaLevel, enemy.IsBoss, rng));
            }

            return drops;
        }

        public Item Generate(int itemLevel, bool fromBoss, IRandom rng)
        {
            var eligibleBases = _bases.Where(b => b.DropLevel <= itemLevel).ToList();
            if (eligibleBases.Count == 0) eligibleBases = _bases.ToList();
            var itemBase = Pick(eligibleBases, b => b.DropWeight, rng);

            var rarity = RollRarity(fromBoss, rng);
            var affixes = RollAffixes(itemBase.Class, itemLevel, rarity, rng);

            return new Item
            {
                Id = NewId(rng),
                Base = itemBase,
                Rarity = affixes.Count == 0 ? ItemRarity.Normal : rarity,
                ItemLevel = itemLevel,
                Affixes = affixes,
            };
        }

        public ItemRarity RollRarity(bool fromBoss, IRandom rng)
        {
            int bonus = fromBoss ? Settings.BossRarityMultiplier : 1;
            var table = new[]
            {
                (Rarity: ItemRarity.Normal, Weight: Settings.NormalWeight),
                (Rarity: ItemRarity.Magic, Weight: Settings.MagicWeight * bonus),
                (Rarity: ItemRarity.Rare, Weight: Settings.RareWeight * bonus),
            };
            return Pick(table, t => t.Weight, rng).Rarity;
        }

        private List<RolledAffix> RollAffixes(ItemClass itemClass, int itemLevel, ItemRarity rarity, IRandom rng)
        {
            var rolled = new List<RolledAffix>();
            if (rarity == ItemRarity.Normal) return rolled;

            int min = rarity == ItemRarity.Magic ? Settings.MagicMinAffixes : Settings.RareMinAffixes;
            int max = rarity == ItemRarity.Magic ? Settings.MagicMaxAffixes : Settings.RareMaxAffixes;
            int perKind = rarity == ItemRarity.Magic ? Settings.MagicMaxPerKind : Settings.RareMaxPerKind;
            int target = min + (int)Math.Floor(rng.NextDouble() * (max - min + 1));

            var usedGroups = new HashSet<string>();
            for (int i = 0; i < target; i++)
            {
                var candidates = _affixes
                    .Where(a => a.AllowedClasses.Contains(itemClass))
                    .Where(a => !usedGroups.Contains(a.Group))
                    .Where(a => rolled.Count(r => r.Affix.Kind == a.Kind) < perKind)
                    .Select(a => (Affix: a, Tiers: a.Tiers.Where(t => t.MinItemLevel <= itemLevel).ToList()))
                    .Where(c => c.Tiers.Count > 0)
                    .ToList();
                if (candidates.Count == 0) break;

                var choice = Pick(candidates, c => c.Tiers.Sum(t => t.Weight), rng);
                var tier = Pick(choice.Tiers, t => t.Weight, rng);
                double value = Math.Min(tier.Max, Math.Floor(tier.Min + rng.NextDouble() * (tier.Max - tier.Min + 1)));

                usedGroups.Add(choice.Affix.Group);
                rolled.Add(new RolledAffix(choice.Affix, tier.Tier, value));
            }

            // Stable display order: prefixes first, then by affix id.
            return rolled.OrderBy(r => r.Affix.Kind).ThenBy(r => r.Affix.Id, StringComparer.Ordinal).ToList();
        }

        private static T Pick<T>(IReadOnlyList<T> items, Func<T, int> weight, IRandom rng)
        {
            int total = items.Sum(weight);
            double roll = rng.NextDouble() * total;
            foreach (var item in items)
            {
                roll -= weight(item);
                if (roll < 0) return item;
            }

            return items[items.Count - 1];
        }

        private static string NewId(IRandom rng)
        {
            ulong a = (ulong)(rng.NextDouble() * (1UL << 53));
            ulong b = (ulong)(rng.NextDouble() * (1UL << 53));
            return $"itm-{a:x14}{b:x14}";
        }
    }
}
