using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Loot
{
    public enum FilterAction
    {
        Show = 0,

        /// <summary>Picked up first and flagged in reports.</summary>
        Highlight = 1,

        /// <summary>Never picked up.</summary>
        Hide = 2,
    }

    /// <summary>All set conditions must match. Unset conditions match everything.</summary>
    public sealed record LootFilterRule
    {
        public string Name { get; init; } = "";
        public FilterAction Action { get; init; }

        public ItemRarity? MinRarity { get; init; }
        public ItemRarity? MaxRarity { get; init; }

        /// <summary>Empty = any class.</summary>
        public IReadOnlyList<ItemClass> Classes { get; init; } = Array.Empty<ItemClass>();

        public int MinItemLevel { get; init; }

        /// <summary>
        /// Stats the player cares about. With <see cref="MinMatchingAffixes"/> &gt; 0 the item
        /// needs at least that many affixes on these stats; with 0, at most
        /// <see cref="MaxMatchingAffixes"/>.
        /// </summary>
        public IReadOnlyList<StatId> WantedStats { get; init; } = Array.Empty<StatId>();

        public int MinMatchingAffixes { get; init; }
        public int? MaxMatchingAffixes { get; init; }

        public bool Matches(Item item)
        {
            if (MinRarity.HasValue && item.Rarity < MinRarity.Value) return false;
            if (MaxRarity.HasValue && item.Rarity > MaxRarity.Value) return false;
            if (Classes.Count > 0 && !Classes.Contains(item.Class)) return false;
            if (item.ItemLevel < MinItemLevel) return false;

            if (WantedStats.Count > 0)
            {
                int matching = item.Affixes.Count(a => WantedStats.Contains(a.Affix.Stat));
                if (matching < MinMatchingAffixes) return false;
                if (MaxMatchingAffixes.HasValue && matching > MaxMatchingAffixes.Value) return false;
            }

            return true;
        }
    }

    public sealed record FilterDecision(FilterAction Action, string RuleName);

    /// <summary>Ordered rules; the first matching rule decides. No match = <see cref="DefaultAction"/>.</summary>
    public sealed record LootFilter
    {
        public static readonly LootFilter ShowEverything = new LootFilter { Name = "Show everything" };

        public string Name { get; init; } = "";
        public IReadOnlyList<LootFilterRule> Rules { get; init; } = Array.Empty<LootFilterRule>();
        public FilterAction DefaultAction { get; init; } = FilterAction.Show;

        public FilterDecision Evaluate(Item item)
        {
            foreach (var rule in Rules)
            {
                if (rule.Matches(item)) return new FilterDecision(rule.Action, rule.Name);
            }

            return new FilterDecision(DefaultAction, "(default)");
        }
    }
}
