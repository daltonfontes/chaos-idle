using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Analysis;

namespace ChaosIdle.App
{
    public enum FilterPreset
    {
        /// <summary>Pick up everything.</summary>
        ShowEverything = 0,

        /// <summary>Highlight uniques and items with two or more stats the build uses; hide off-build magic items.</summary>
        Focused = 1,

        /// <summary>Focused, and also hide normal items (for characters with full gear).</summary>
        Strict = 2,
    }

    public sealed record LootFilterView(string Name, FilterPreset? Preset, ItemRarity MinimumRarity, IReadOnlyList<string> Rules);

    public sealed record FilterPreviewRow(string ItemId, string Label, FilterAction Action, string RuleName);

    /// <summary>Loot filter screen: presets, a minimum rarity, readable rules, and a preview on the bag.</summary>
    public sealed partial class GameSession
    {
        private const string MinimumRarityRule = "Below minimum rarity";

        public LootFilterView LootFilter()
        {
            var f = State.LootFilter;
            var preset = PresetOf(f);
            var minimum = f.Rules.FirstOrDefault(r => r.Name == MinimumRarityRule)?.MaxRarity is ItemRarity below
                ? below + 1
                : ItemRarity.Normal;
            return new LootFilterView(f.Name, preset, minimum, f.Rules.Select(Describe).Append($"Anything else: {Verb(f.DefaultAction)}").ToList());
        }

        /// <summary>
        /// Replaces the filter with a preset, then hides everything below <paramref name="minimumRarity"/>
        /// (uniques are never hidden by this rule).
        /// </summary>
        public string SetLootFilter(FilterPreset preset, ItemRarity minimumRarity, DateTime nowUtc)
        {
            var build = Character.ToBuild();
            var filter = preset switch
            {
                FilterPreset.Focused => BuildRelevance.FilterFor(build),
                FilterPreset.Strict => BuildRelevance.FilterFor(build, strict: true),
                _ => Domain.Loot.LootFilter.ShowEverything,
            };

            if (minimumRarity > ItemRarity.Normal)
            {
                var hideBelow = new LootFilterRule { Name = MinimumRarityRule, Action = FilterAction.Hide, MaxRarity = minimumRarity - 1 };
                // After highlight rules (so a highlighted item still shows), before everything else.
                // Plain "hide low rarity" rules that the new rule already covers are dropped, not repeated.
                var rules = filter.Rules.Where(r => r.Action == FilterAction.Highlight)
                    .Append(hideBelow)
                    .Concat(filter.Rules.Where(r => r.Action != FilterAction.Highlight && !CoveredBy(r, minimumRarity)))
                    .ToList();
                filter = filter with { Rules = rules };
            }

            State.LootFilter = filter with { Name = PresetName(preset) + (minimumRarity > ItemRarity.Normal ? $", {minimumRarity}+" : "") };
            Save(nowUtc);
            return $"Loot filter: {State.LootFilter.Name}.";
        }

        private static bool CoveredBy(LootFilterRule r, ItemRarity minimum) =>
            r.Action == FilterAction.Hide && r.MaxRarity.HasValue && r.MaxRarity < minimum
            && !r.MinRarity.HasValue && r.Classes.Count == 0 && r.MinItemLevel == 0 && r.WantedStats.Count == 0;

        /// <summary>What the current filter would do with each item in the bag.</summary>
        public IReadOnlyList<FilterPreviewRow> FilterPreview() =>
            Character.Inventory.Items.Select(i =>
            {
                var d = State.LootFilter.Evaluate(i);
                return new FilterPreviewRow(i.Id, $"{i.Name} [{i.Rarity}]", d.Action, d.RuleName);
            }).ToList();

        private static FilterPreset? PresetOf(LootFilter f)
        {
            if (f.Name.StartsWith(PresetName(FilterPreset.Strict), StringComparison.Ordinal) || f.Name.StartsWith("Strict:", StringComparison.Ordinal)) return FilterPreset.Strict;
            if (f.Name.StartsWith(PresetName(FilterPreset.Focused), StringComparison.Ordinal) || f.Name.StartsWith("Focused:", StringComparison.Ordinal)) return FilterPreset.Focused;
            if (f.Name.StartsWith(PresetName(FilterPreset.ShowEverything), StringComparison.Ordinal)) return FilterPreset.ShowEverything;
            return null;
        }

        private static string PresetName(FilterPreset p) => p switch
        {
            FilterPreset.Focused => "Focused on your build",
            FilterPreset.Strict => "Strict (full gear)",
            _ => "Show everything",
        };

        private static string Verb(FilterAction a) => a switch
        {
            FilterAction.Highlight => "highlight ★",
            FilterAction.Hide => "hide",
            _ => "show",
        };

        /// <summary>A rule in words, e.g. "Highlight ★: Magic or better, with 2+ of: life, fire resistance".</summary>
        private static string Describe(LootFilterRule r)
        {
            var conditions = new List<string>();
            if (r.MinRarity.HasValue && r.MaxRarity.HasValue && r.MinRarity == r.MaxRarity) conditions.Add($"{r.MinRarity} items");
            else
            {
                if (r.MinRarity.HasValue) conditions.Add($"{r.MinRarity} or better");
                if (r.MaxRarity.HasValue) conditions.Add($"{r.MaxRarity} or worse");
            }

            if (r.Classes.Count > 0) conditions.Add(string.Join("/", r.Classes));
            if (r.MinItemLevel > 0) conditions.Add($"item level {r.MinItemLevel}+");
            if (r.WantedStats.Count > 0)
            {
                string stats = string.Join(", ", r.WantedStats.Take(6).Select(StatText)) + (r.WantedStats.Count > 6 ? ", …" : "");
                if (r.MinMatchingAffixes > 0) conditions.Add($"with {r.MinMatchingAffixes}+ of: {stats}");
                else if (r.MaxMatchingAffixes == 0) conditions.Add($"with none of: {stats}");
            }

            string what = conditions.Count == 0 ? "everything" : string.Join(", ", conditions);
            return $"{Verb(r.Action)}: {what}";
        }

        private static string StatText(StatId s)
        {
            var name = s.ToString();
            var spaced = string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
            return spaced.ToLowerInvariant().Replace(" per second", "/s");
        }
    }
}
