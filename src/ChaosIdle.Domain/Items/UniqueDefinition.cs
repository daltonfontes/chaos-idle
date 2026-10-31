using System;
using System.Collections.Generic;
using System.Linq;

namespace ChaosIdle.Domain.Items
{
    /// <summary>
    /// A named item with a fixed set of modifiers on a fixed base. Each modifier is an
    /// <see cref="AffixDefinition"/> with a single tier giving its roll range, so uniques reuse the
    /// item, tooltip and comparison code unchanged. Uniques should enable a strategy and carry a
    /// real drawback, not just be bigger rares.
    /// </summary>
    public sealed record UniqueDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public ItemBase Base { get; init; } = new ItemBase();

        /// <summary>Relative chance among eligible uniques.</summary>
        public int DropWeight { get; init; } = 100;

        public string FlavourText { get; init; } = "";
        public IReadOnlyList<AffixDefinition> Modifiers { get; init; } = Array.Empty<AffixDefinition>();

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Id)) errors.Add("Unique has no Id.");
            if (Modifiers.Count == 0) errors.Add($"{Id}: no modifiers.");
            foreach (var m in Modifiers)
            {
                errors.AddRange(m.Validate().Select(e => $"{Id}: {e}"));
                if (m.Tiers.Count != 1) errors.Add($"{Id}: modifier {m.Id} must have exactly one tier.");
                if (!m.AllowedClasses.Contains(Base.Class)) errors.Add($"{Id}: modifier {m.Id} not allowed on {Base.Class}.");
            }

            return errors;
        }

        /// <summary>Rolls each modifier's value within its range.</summary>
        public Item Roll(string itemId, int itemLevel, Func<double> roll) => new Item
        {
            Id = itemId,
            Base = Base,
            Rarity = ItemRarity.Unique,
            ItemLevel = itemLevel,
            Unique = this,
            Affixes = Modifiers.Select(m =>
            {
                var t = m.Tiers[0];
                double value = Math.Min(t.Max, Math.Floor(t.Min + roll() * (t.Max - t.Min + 1)));
                return new RolledAffix(m, t.Tier, value);
            }).ToList(),
        };
    }
}
