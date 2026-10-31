using System.Collections.Generic;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;
using static ChaosIdle.Domain.Items.ItemClass;

namespace ChaosIdle.Content
{
    /// <summary>
    /// Affix tables. Three tiers each: T3 from item level 1, T2 from 5, T1 from 10
    /// (rarer: weights 100 / 60 / 25).
    /// </summary>
    public static class Affixes
    {
        private static readonly ItemClass[] Armour = { Helmet, BodyArmour, Boots, Shield };
        private static readonly ItemClass[] Jewellery = { Ring, Amulet };
        private static readonly ItemClass[] ResistGear = { Helmet, BodyArmour, Boots, Shield, Ring, Amulet };

        public static readonly AffixDefinition[] All =
        {
            // Prefixes
            P("affix.life", "life", StatId.MaxLife, ModifierKind.Flat, "+{0} to maximum Life", new[] { Helmet, BodyArmour, Boots, Shield, Ring, Amulet }, (10, 19), (20, 34), (35, 50)),
            P("affix.mana", "mana", StatId.MaxMana, ModifierKind.Flat, "+{0} to maximum Mana", new[] { Rod, Ring, Amulet, Helmet }, (10, 19), (20, 29), (30, 40)),
            P("affix.local_added_phys", "local_phys_flat", StatId.PhysicalDamage, ModifierKind.Flat, "Adds {0} Physical Damage", new[] { Mace }, (2, 4), (5, 8), (9, 13), local: true),
            P("affix.local_inc_phys", "local_phys_inc", StatId.PhysicalDamage, ModifierKind.Increased, "{0}% increased Physical Damage", new[] { Mace }, (20, 34), (35, 54), (55, 79), local: true),
            P("affix.local_armour", "local_armour_flat", StatId.Armour, ModifierKind.Flat, "+{0} to Armour", Armour, (15, 30), (31, 60), (61, 100), local: true),
            P("affix.local_inc_armour", "local_armour_inc", StatId.Armour, ModifierKind.Increased, "{0}% increased Armour", Armour, (15, 29), (30, 49), (50, 70), local: true),
            P("affix.spell_damage", "spell_damage", StatId.SpellDamage, ModifierKind.Increased, "{0}% increased Spell Damage", new[] { Rod, Amulet }, (10, 19), (20, 34), (35, 50)),
            P("affix.fire_damage", "fire_damage", StatId.FireDamage, ModifierKind.Increased, "{0}% increased Fire Damage", new[] { Rod, Ring, Amulet }, (8, 15), (16, 25), (26, 35)),
            P("affix.added_fire", "added_fire", StatId.FireDamage, ModifierKind.Flat, "Adds {0} Fire Damage", new[] { Mace, Ring, Amulet }, (2, 4), (5, 8), (9, 12)),

            // Suffixes
            S("affix.fire_res", "fire_res", StatId.FireResistance, ModifierKind.Flat, "+{0}% to Fire Resistance", ResistGear, (6, 11), (12, 17), (18, 24)),
            S("affix.cold_res", "cold_res", StatId.ColdResistance, ModifierKind.Flat, "+{0}% to Cold Resistance", ResistGear, (6, 11), (12, 17), (18, 24)),
            S("affix.lightning_res", "lightning_res", StatId.LightningResistance, ModifierKind.Flat, "+{0}% to Lightning Resistance", ResistGear, (6, 11), (12, 17), (18, 24)),
            S("affix.local_attack_speed", "local_attack_speed", StatId.AttackSpeed, ModifierKind.Increased, "{0}% increased Attack Speed", new[] { Mace }, (5, 7), (8, 10), (11, 14), local: true),
            S("affix.local_crit", "local_crit", StatId.CriticalChance, ModifierKind.Increased, "{0}% increased Critical Chance", new[] { Mace }, (15, 24), (25, 34), (35, 50), local: true),
            S("affix.cast_speed", "cast_speed", StatId.CastSpeed, ModifierKind.Increased, "{0}% increased Cast Speed", new[] { Rod, Ring, Amulet }, (5, 7), (8, 10), (11, 14)),
            S("affix.crit_chance", "crit_chance", StatId.CriticalChance, ModifierKind.Increased, "{0}% increased Critical Chance", new[] { Rod, Amulet }, (15, 24), (25, 34), (35, 50)),
            S("affix.crit_multi", "crit_multi", StatId.CriticalMultiplier, ModifierKind.Flat, "+{0}% to Critical Damage Multiplier", new[] { Mace, Rod, Amulet }, (8, 12), (13, 19), (20, 28)),
            S("affix.life_regen", "life_regen", StatId.LifeRegenPerSecond, ModifierKind.Flat, "Regenerate {0} Life per second", new[] { Ring, Amulet, BodyArmour, Helmet }, (1, 2), (3, 4), (5, 7)),
            S("affix.mana_regen", "mana_regen", StatId.ManaRegenPerSecond, ModifierKind.Increased, "{0}% increased Mana Regeneration", new[] { Rod, Ring, Amulet, Helmet }, (10, 19), (20, 29), (30, 40)),
            S("affix.block", "block", StatId.BlockChance, ModifierKind.Flat, "+{0}% chance to Block", new[] { Shield }, (2, 3), (4, 5), (6, 7)),
            S("affix.accuracy", "accuracy", StatId.Accuracy, ModifierKind.Flat, "+{0} to Accuracy", new[] { Mace, Ring, Helmet }, (30, 59), (60, 99), (100, 150)),
            S("affix.life_on_block", "life_on_block", StatId.LifeOnBlock, ModifierKind.Flat, "Gain {0} Life when you Block", new[] { Shield }, (5, 9), (10, 15), (16, 25)),
        };

        private static AffixDefinition P(string id, string group, StatId stat, ModifierKind kind, string text, IReadOnlyList<ItemClass> classes,
            (double, double) t3, (double, double) t2, (double, double) t1, bool local = false) =>
            Make(AffixKind.Prefix, id, group, stat, kind, text, classes, t3, t2, t1, local);

        private static AffixDefinition S(string id, string group, StatId stat, ModifierKind kind, string text, IReadOnlyList<ItemClass> classes,
            (double, double) t3, (double, double) t2, (double, double) t1, bool local = false) =>
            Make(AffixKind.Suffix, id, group, stat, kind, text, classes, t3, t2, t1, local);

        private static AffixDefinition Make(AffixKind affixKind, string id, string group, StatId stat, ModifierKind kind, string text,
            IReadOnlyList<ItemClass> classes, (double Min, double Max) t3, (double Min, double Max) t2, (double Min, double Max) t1, bool local) => new()
        {
            Id = id,
            Group = group,
            Kind = affixKind,
            Stat = stat,
            ModifierKind = kind,
            Text = text,
            Local = local,
            AllowedClasses = classes,
            Tiers = new[]
            {
                new AffixTier(3, 1, t3.Min, t3.Max, 100),
                new AffixTier(2, 5, t2.Min, t2.Max, 60),
                new AffixTier(1, 10, t1.Min, t1.Max, 25),
            },
        };
    }
}
