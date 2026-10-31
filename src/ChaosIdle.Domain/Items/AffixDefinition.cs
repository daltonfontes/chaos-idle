using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Items
{
    public enum AffixKind
    {
        Prefix = 0,
        Suffix = 1,
    }

    /// <param name="Tier">1 is the best tier.</param>
    public sealed record AffixTier(int Tier, int MinItemLevel, double Min, double Max, int Weight);

    public sealed record AffixDefinition
    {
        public string Id { get; init; } = "";

        /// <summary>Only one affix per group can appear on an item.</summary>
        public string Group { get; init; } = "";

        public AffixKind Kind { get; init; }
        public StatId Stat { get; init; }
        public ModifierKind ModifierKind { get; init; }

        /// <summary>
        /// Local affixes modify the item's own base values (weapon damage/speed/crit, armour,
        /// evasion) instead of the character.
        /// </summary>
        public bool Local { get; init; }

        /// <summary>Display template; {0} is the rolled value.</summary>
        public string Text { get; init; } = "";

        public IReadOnlyList<ItemClass> AllowedClasses { get; init; } = Array.Empty<ItemClass>();
        public IReadOnlyList<AffixTier> Tiers { get; init; } = Array.Empty<AffixTier>();

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Id)) errors.Add("Affix has no Id.");
            if (string.IsNullOrWhiteSpace(Group)) errors.Add($"{Id}: no group.");
            if (AllowedClasses.Count == 0) errors.Add($"{Id}: no allowed item classes.");
            if (Tiers.Count == 0) errors.Add($"{Id}: no tiers.");
            bool fixedValue = Tiers.Count > 0 && Tiers.All(t => t.Min == t.Max);
            if (!Text.Contains("{0}") && !fixedValue) errors.Add($"{Id}: text has no {{0}} placeholder but its value varies.");
            foreach (var t in Tiers)
            {
                if (t.Min > t.Max) errors.Add($"{Id} T{t.Tier}: min > max.");
                if (t.Weight <= 0) errors.Add($"{Id} T{t.Tier}: weight must be positive.");
            }

            if (Local)
            {
                bool weaponLocal = Stat == StatId.PhysicalDamage || Stat == StatId.AttackSpeed || Stat == StatId.CriticalChance;
                bool armourLocal = Stat == StatId.Armour || Stat == StatId.Evasion;
                if (!weaponLocal && !armourLocal) errors.Add($"{Id}: stat {Stat} cannot be local.");
                foreach (var c in AllowedClasses)
                {
                    if (weaponLocal && !c.IsWeapon()) errors.Add($"{Id}: local weapon affix on {c}.");
                    if (armourLocal && c.IsWeapon()) errors.Add($"{Id}: local armour affix on {c}.");
                }
            }

            return errors;
        }
    }
}
