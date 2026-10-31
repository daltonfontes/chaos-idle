using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    /// <summary>Unique items: each enables a strategy and costs something real.</summary>
    public static class Uniques
    {
        /// <summary>Turns a melee build towards fire: conversion lets fire passives and penetration apply.</summary>
        public static readonly UniqueDefinition KindledOath = new()
        {
            Id = "unique.kindled_oath", Name = "The Kindled Oath", Base = ItemBases.IronboundMace,
            FlavourText = "Sworn in smoke, kept in flame.",
            Modifiers = new[]
            {
                U("unique.kindled_oath.phys", StatId.PhysicalDamage, ModifierKind.Increased, "{0}% increased Physical Damage", ItemClass.Mace, 40, 60, local: true),
                U("unique.kindled_oath.conversion", StatId.PhysicalToFireConversion, ModifierKind.Flat, "{0}% of Physical Damage converted to Fire", ItemClass.Mace, 60, 60),
                U("unique.kindled_oath.added_fire", StatId.FireDamage, ModifierKind.Flat, "Adds {0} Fire Damage", ItemClass.Mace, 4, 6),
                U("unique.kindled_oath.fire_res", StatId.FireResistance, ModifierKind.Flat, "{0}% to Fire Resistance", ItemClass.Mace, -15, -15),
            },
        };

        /// <summary>Spell block without spending the Bastion keystone, paid for in life.</summary>
        public static readonly UniqueDefinition MirrorglassWard = new()
        {
            Id = "unique.mirrorglass_ward", Name = "Mirrorglass Ward", Base = ItemBases.RivetedBuckler,
            FlavourText = "What you send, it returns.",
            Modifiers = new[]
            {
                U("unique.mirrorglass_ward.spell_block", StatId.BlockAppliesToSpells, ModifierKind.Flat, "Your chance to Block applies to Spells", ItemClass.Shield, 1, 1),
                U("unique.mirrorglass_ward.block", StatId.BlockChance, ModifierKind.Flat, "+{0}% chance to Block", ItemClass.Shield, 6, 10),
                U("unique.mirrorglass_ward.life", StatId.MaxLife, ModifierKind.Flat, "{0} to maximum Life", ItemClass.Shield, -60, -60),
            },
        };

        /// <summary>Swaps passive regeneration for leech: strong while hitting, weak while not.</summary>
        public static readonly UniqueDefinition BloodboundPendant = new()
        {
            Id = "unique.bloodbound_pendant", Name = "Bloodbound Pendant", Base = ItemBases.BonePendant,
            FlavourText = "It drinks only when you do.",
            Modifiers = new[]
            {
                U("unique.bloodbound_pendant.leech", StatId.LifeLeechPercent, ModifierKind.Flat, "{0}% of damage dealt leeched as Life", ItemClass.Amulet, 2, 3),
                U("unique.bloodbound_pendant.life", StatId.MaxLife, ModifierKind.Flat, "+{0} to maximum Life", ItemClass.Amulet, 20, 30),
                U("unique.bloodbound_pendant.no_regen", StatId.LifeRegenPerSecond, ModifierKind.More, "50% less Life Regeneration", ItemClass.Amulet, -50, -50),
            },
        };

        /// <summary>Cheap spells for a mana-hungry build, at the cost of fire resistance.</summary>
        public static readonly UniqueDefinition StillwaterHelm = new()
        {
            Id = "unique.stillwater_helm", Name = "Stillwater Helm", Base = ItemBases.IronCap,
            FlavourText = "The calm before the pyre.",
            Modifiers = new[]
            {
                U("unique.stillwater_helm.cost", StatId.ManaCost, ModifierKind.More, "Skills cost 30% less Mana", ItemClass.Helmet, -30, -30),
                U("unique.stillwater_helm.mana", StatId.MaxMana, ModifierKind.Flat, "+{0} to maximum Mana", ItemClass.Helmet, 30, 40),
                U("unique.stillwater_helm.fire_res", StatId.FireResistance, ModifierKind.Flat, "{0}% to Fire Resistance", ItemClass.Helmet, -10, -10),
            },
        };

        public static readonly UniqueDefinition[] All = { KindledOath, MirrorglassWard, BloodboundPendant, StillwaterHelm };

        private static AffixDefinition U(string id, StatId stat, ModifierKind kind, string text, ItemClass itemClass, double min, double max, bool local = false) => new()
        {
            Id = id,
            Group = id,
            Kind = AffixKind.Prefix,
            Stat = stat,
            ModifierKind = kind,
            Local = local,
            Text = text,
            AllowedClasses = new[] { itemClass },
            Tiers = new[] { new AffixTier(1, 1, min, max, 1) },
        };
    }
}
