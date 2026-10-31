using System.Collections.Generic;

namespace ChaosIdle.Domain.Items
{
    /// <summary>Persisted by name in content and saves; only append.</summary>
    public enum ItemClass
    {
        Mace = 1,
        Rod = 2,
        Shield = 3,
        Helmet = 4,
        BodyArmour = 5,
        Boots = 6,
        Ring = 7,
        Amulet = 8,
    }

    public enum EquipmentSlot
    {
        MainHand = 1,
        OffHand = 2,
        Helmet = 3,
        BodyArmour = 4,
        Boots = 5,
        Ring1 = 6,
        Ring2 = 7,
        Amulet = 8,
    }

    public static class ItemClasses
    {
        public static bool IsWeapon(this ItemClass c) => c == ItemClass.Mace || c == ItemClass.Rod;

        public static IReadOnlyList<EquipmentSlot> SlotsFor(this ItemClass c)
        {
            switch (c)
            {
                case ItemClass.Mace:
                case ItemClass.Rod:
                    return new[] { EquipmentSlot.MainHand };
                case ItemClass.Shield: return new[] { EquipmentSlot.OffHand };
                case ItemClass.Helmet: return new[] { EquipmentSlot.Helmet };
                case ItemClass.BodyArmour: return new[] { EquipmentSlot.BodyArmour };
                case ItemClass.Boots: return new[] { EquipmentSlot.Boots };
                case ItemClass.Ring: return new[] { EquipmentSlot.Ring1, EquipmentSlot.Ring2 };
                case ItemClass.Amulet: return new[] { EquipmentSlot.Amulet };
                default: return new EquipmentSlot[0];
            }
        }
    }
}
