using ChaosIdle.Domain.Items;

namespace ChaosIdle.Content
{
    public static class Weapons
    {
        public static readonly WeaponDefinition IronboundMace = new WeaponDefinition
        {
            Id = "weapon.ironbound_mace",
            Name = "Ironbound Mace",
            PhysicalMin = 20,
            PhysicalMax = 34,
            AttacksPerSecond = 1.25,
            CritChance = 5,
        };

        public static readonly WeaponDefinition AshwoodRod = new WeaponDefinition
        {
            Id = "weapon.ashwood_rod",
            Name = "Ashwood Rod",
            PhysicalMin = 6,
            PhysicalMax = 11,
            AttacksPerSecond = 1.4,
            CritChance = 7,
        };

        public static readonly WeaponDefinition[] All = { IronboundMace, AshwoodRod };
    }
}
