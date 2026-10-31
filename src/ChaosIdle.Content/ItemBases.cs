using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    public static class ItemBases
    {
        public static readonly ItemBase IronboundMace = new() { Id = "base.ironbound_mace", Name = "Ironbound Mace", Class = ItemClass.Mace, PhysicalMin = 14, PhysicalMax = 24, AttacksPerSecond = 1.25, CritChance = 5 };
        public static readonly ItemBase FlangedMace = new() { Id = "base.flanged_mace", Name = "Flanged Mace", Class = ItemClass.Mace, DropLevel = 5, PhysicalMin = 20, PhysicalMax = 33, AttacksPerSecond = 1.15, CritChance = 5 };
        public static readonly ItemBase AshwoodRod = new() { Id = "base.ashwood_rod", Name = "Ashwood Rod", Class = ItemClass.Rod, PhysicalMin = 6, PhysicalMax = 11, AttacksPerSecond = 1.4, CritChance = 7, Implicits = new[] { new Modifier(StatId.SpellDamage, ModifierKind.Increased, 15) } };
        public static readonly ItemBase EmberRod = new() { Id = "base.ember_rod", Name = "Ember Rod", Class = ItemClass.Rod, DropLevel = 5, PhysicalMin = 8, PhysicalMax = 14, AttacksPerSecond = 1.4, CritChance = 8, Implicits = new[] { new Modifier(StatId.SpellDamage, ModifierKind.Increased, 25) } };
        public static readonly ItemBase RivetedBuckler = new() { Id = "base.riveted_buckler", Name = "Riveted Buckler", Class = ItemClass.Shield, Armour = 40, BlockChance = 20 };
        public static readonly ItemBase OakPavise = new() { Id = "base.oak_pavise", Name = "Oak Pavise", Class = ItemClass.Shield, DropLevel = 5, Armour = 90, BlockChance = 25 };
        public static readonly ItemBase IronCap = new() { Id = "base.iron_cap", Name = "Iron Cap", Class = ItemClass.Helmet, Armour = 40 };
        public static readonly ItemBase ScaleHauberk = new() { Id = "base.scale_hauberk", Name = "Scale Hauberk", Class = ItemClass.BodyArmour, Armour = 140 };
        public static readonly ItemBase QuiltedCoat = new() { Id = "base.quilted_coat", Name = "Quilted Coat", Class = ItemClass.BodyArmour, Evasion = 120 };
        public static readonly ItemBase HobnailBoots = new() { Id = "base.hobnail_boots", Name = "Hobnail Boots", Class = ItemClass.Boots, Armour = 30 };
        public static readonly ItemBase CopperBand = new() { Id = "base.copper_band", Name = "Copper Band", Class = ItemClass.Ring, Implicits = new[] { new Modifier(StatId.FireResistance, ModifierKind.Flat, 6) } };
        public static readonly ItemBase IronBand = new() { Id = "base.iron_band", Name = "Iron Band", Class = ItemClass.Ring, Implicits = new[] { new Modifier(StatId.Accuracy, ModifierKind.Flat, 40) } };
        public static readonly ItemBase BonePendant = new() { Id = "base.bone_pendant", Name = "Bone Pendant", Class = ItemClass.Amulet, Implicits = new[] { new Modifier(StatId.MaxLife, ModifierKind.Flat, 10) } };

        public static readonly ItemBase[] All =
        {
            IronboundMace, FlangedMace, AshwoodRod, EmberRod, RivetedBuckler, OakPavise, IronCap,
            ScaleHauberk, QuiltedCoat, HobnailBoots, CopperBand, IronBand, BonePendant,
        };
    }
}
