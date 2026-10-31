using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Items
{
    public enum ItemRarity
    {
        Normal = 0,
        Magic = 1,
        Rare = 2,

        /// <summary>Fixed, named item with build-enabling modifiers and a drawback.</summary>
        Unique = 3,
    }

    public sealed record RolledAffix(AffixDefinition Affix, int Tier, double Value)
    {
        public string Text => string.Format(CultureInfo.InvariantCulture, Affix.Text, Value);
    }

    public sealed record Item
    {
        public string Id { get; init; } = "";
        public ItemBase Base { get; init; } = new ItemBase();
        public ItemRarity Rarity { get; init; }
        public int ItemLevel { get; init; } = 1;
        public IReadOnlyList<RolledAffix> Affixes { get; init; } = Array.Empty<RolledAffix>();

        /// <summary>Set for unique items; their modifiers are in <see cref="Affixes"/>.</summary>
        public UniqueDefinition? Unique { get; init; }

        public string Name => Unique?.Name ?? Base.Name;
        public ItemClass Class => Base.Class;

        private string Source => "item:" + Id;

        /// <summary>Weapon after local affixes. Only valid for weapons.</summary>
        public WeaponDefinition ToWeapon()
        {
            if (!Class.IsWeapon()) throw new InvalidOperationException($"{Id} ({Class}) is not a weapon.");

            double addedPhys = SumLocal(StatId.PhysicalDamage, ModifierKind.Flat);
            double incPhys = SumLocal(StatId.PhysicalDamage, ModifierKind.Increased);
            double incSpeed = SumLocal(StatId.AttackSpeed, ModifierKind.Increased);
            double incCrit = SumLocal(StatId.CriticalChance, ModifierKind.Increased);
            double physScale = 1 + incPhys / 100.0;

            return new WeaponDefinition
            {
                Id = Base.Id,
                Name = Base.Name,
                PhysicalMin = (Base.PhysicalMin + addedPhys) * physScale,
                PhysicalMax = (Base.PhysicalMax + addedPhys) * physScale,
                AttacksPerSecond = Base.AttacksPerSecond * (1 + incSpeed / 100.0),
                CritChance = Base.CritChance * (1 + incCrit / 100.0),
            };
        }

        /// <summary>Armour granted to the character after local affixes.</summary>
        public double LocalArmour =>
            (Base.Armour + SumLocal(StatId.Armour, ModifierKind.Flat)) * (1 + SumLocal(StatId.Armour, ModifierKind.Increased) / 100.0);

        public double LocalEvasion =>
            (Base.Evasion + SumLocal(StatId.Evasion, ModifierKind.Flat)) * (1 + SumLocal(StatId.Evasion, ModifierKind.Increased) / 100.0);

        /// <summary>Everything this item gives the character (weapon stats excluded; see <see cref="ToWeapon"/>).</summary>
        public IEnumerable<Modifier> CharacterModifiers()
        {
            foreach (var m in Base.Implicits) yield return m with { Source = Source };

            if (!Class.IsWeapon())
            {
                double armour = LocalArmour;
                double evasion = LocalEvasion;
                if (armour > 0) yield return new Modifier(StatId.Armour, ModifierKind.Flat, armour, Source);
                if (evasion > 0) yield return new Modifier(StatId.Evasion, ModifierKind.Flat, evasion, Source);
            }

            if (Base.BlockChance > 0) yield return new Modifier(StatId.BlockChance, ModifierKind.Flat, Base.BlockChance, Source);

            foreach (var a in Affixes.Where(a => !a.Affix.Local))
            {
                yield return new Modifier(a.Affix.Stat, a.Affix.ModifierKind, a.Value, Source);
            }
        }

        public int CountAffixes(AffixKind kind) => Affixes.Count(a => a.Affix.Kind == kind);

        private double SumLocal(StatId stat, ModifierKind kind) =>
            Affixes.Where(a => a.Affix.Local && a.Affix.Stat == stat && a.Affix.ModifierKind == kind).Sum(a => a.Value);
    }
}
