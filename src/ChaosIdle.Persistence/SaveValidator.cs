using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain;
using ChaosIdle.Domain.Items;

namespace ChaosIdle.Persistence
{
    /// <summary>Limits a save must respect to be accepted by the server.</summary>
    public sealed record SaveRules
    {
        public static readonly SaveRules Default = new SaveRules();

        public int MaxPassivePoints { get; init; } = 18;
        public int MaxInventoryCapacity { get; init; } = 20;

        /// <summary>The highest area level any content drops at.</summary>
        public int MaxItemLevel { get; init; } = 12;

        public int MaxMapTier { get; init; } = 3;
    }

    /// <summary>
    /// Checks that a save describes things the game could actually have produced: every rolled
    /// value lies inside its tier, affixes are allowed on their item, rarity limits hold, uniques
    /// carry exactly their own modifiers, and budgets are respected. It cannot prove a player
    /// earned an item; it stops impossible items. Run after a clean load (no warnings).
    /// </summary>
    public static class SaveValidator
    {
        public static IReadOnlyList<string> Validate(SaveData save, ContentRegistry content, SaveRules? rules = null)
        {
            rules ??= SaveRules.Default;
            var errors = new List<string>();
            var c = save.Character;

            if (string.IsNullOrWhiteSpace(c.Id)) errors.Add("Character has no id.");
            if (c.Loadout.PassivePoints < 0 || c.Loadout.PassivePoints > rules.MaxPassivePoints)
                errors.Add($"Passive points {c.Loadout.PassivePoints} outside 0..{rules.MaxPassivePoints}.");
            if (c.InventoryCapacity < 1 || c.InventoryCapacity > rules.MaxInventoryCapacity)
                errors.Add($"Inventory capacity {c.InventoryCapacity} outside 1..{rules.MaxInventoryCapacity}.");
            if (c.Inventory.Count > c.InventoryCapacity) errors.Add("More items in the bag than it holds.");

            var allItems = c.Inventory.Concat(c.Loadout.Equipment.Values).ToList();
            var duplicateIds = allItems.GroupBy(i => i.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var id in duplicateIds) errors.Add($"Item id {id} appears more than once.");
            foreach (var item in allItems) ValidateItem(item, content, rules, errors);

            var p = save.MapProgress;
            if (p.HighestUnlockedTier < 1 || p.HighestUnlockedTier > rules.MaxMapTier + 1)
                errors.Add($"Unlocked tier {p.HighestUnlockedTier} outside 1..{rules.MaxMapTier + 1}.");
            if (p.HighestUnlockedTier > p.BossesDefeated.Count + 1)
                errors.Add("More tiers unlocked than bosses defeated.");
            foreach (var id in p.BossesDefeated.Where(id => content.Map(id) == null))
                errors.Add($"Unknown map {id} in defeated bosses.");

            return errors;
        }

        private static void ValidateItem(ItemSave item, ContentRegistry content, SaveRules rules, List<string> errors)
        {
            string where = $"Item {item.Id}";
            var itemBase = content.ItemBase(item.BaseId);
            if (itemBase == null || !Enum.TryParse<ItemRarity>(item.Rarity, out var rarity))
            {
                errors.Add($"{where}: unknown base or rarity.");
                return;
            }

            if (item.ItemLevel < 1 || item.ItemLevel > rules.MaxItemLevel)
                errors.Add($"{where}: item level {item.ItemLevel} outside 1..{rules.MaxItemLevel}.");

            var affixes = item.Affixes.Select(a => (Save: a, Def: content.Affix(a.AffixId))).ToList();
            if (affixes.Any(a => a.Def == null))
            {
                errors.Add($"{where}: unknown affix.");
                return;
            }

            if (rarity == ItemRarity.Unique)
            {
                var unique = item.UniqueId == null ? null : content.Unique(item.UniqueId);
                if (unique == null || unique.Base.Id != item.BaseId)
                {
                    errors.Add($"{where}: unique does not match its base.");
                    return;
                }

                var expected = unique.Modifiers.Select(m => m.Id).OrderBy(x => x, StringComparer.Ordinal);
                if (!expected.SequenceEqual(affixes.Select(a => a.Save.AffixId).OrderBy(x => x, StringComparer.Ordinal)))
                    errors.Add($"{where}: modifiers differ from {unique.Id}.");
            }
            else
            {
                if (item.UniqueId != null) errors.Add($"{where}: only uniques may name a unique.");
                int prefixes = affixes.Count(a => a.Def!.Kind == AffixKind.Prefix);
                int suffixes = affixes.Count - prefixes;
                int max = rarity == ItemRarity.Normal ? 0 : rarity == ItemRarity.Magic ? 2 : 6;
                int perKind = rarity == ItemRarity.Magic ? 1 : 3;
                if (affixes.Count > max || prefixes > perKind || suffixes > perKind)
                    errors.Add($"{where}: too many affixes for a {rarity} item.");
                if (affixes.Select(a => a.Def!.Group).Distinct().Count() != affixes.Count)
                    errors.Add($"{where}: two affixes from the same group.");
                foreach (var a in affixes.Where(a => !a.Def!.AllowedClasses.Contains(itemBase.Class)))
                    errors.Add($"{where}: {a.Save.AffixId} cannot roll on {itemBase.Class}.");
            }

            foreach (var (s, def) in affixes)
            {
                var tier = def!.Tiers.FirstOrDefault(t => t.Tier == s.Tier);
                if (tier == null) errors.Add($"{where}: {s.AffixId} has no tier {s.Tier}.");
                else if (s.Value < tier.Min || s.Value > tier.Max) errors.Add($"{where}: {s.AffixId} value {s.Value} outside {tier.Min}..{tier.Max}.");
                else if (rarity != ItemRarity.Unique && tier.MinItemLevel > item.ItemLevel) errors.Add($"{where}: {s.AffixId} tier {s.Tier} needs item level {tier.MinItemLevel}.");
            }
        }
    }
}
