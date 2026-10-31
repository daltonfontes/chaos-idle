using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;

namespace ChaosIdle.Content
{
    public static class StarterCharacters
    {
        public const int StartingPassivePoints = 12;
        public const int InventoryCapacity = 20;

        /// <summary>A fresh Guardian: normal mace and buckler, Iron Cleave + Bulwark Retort, no passives spent.</summary>
        public static PlayerCharacter NewGuardian(string id = "char.guardian", string name = "Guardian")
        {
            var character = new PlayerCharacter(id, name, Classes.Guardian,
                PassiveAllocation.Empty(PassiveTrees.Guardian, StartingPassivePoints), InventoryCapacity)
            {
                PrimarySkill = Skills.IronCleave,
            };
            character.TriggeredSkills.Add(Skills.BulwarkRetort);
            character.Equipment.Equip(Starter(ItemBases.IronboundMace, "starter-mace"), EquipmentSlot.MainHand);
            character.Equipment.Equip(Starter(ItemBases.RivetedBuckler, "starter-buckler"), EquipmentSlot.OffHand);
            return character;
        }

        /// <summary>Suggested first 12 points for a block/armour Guardian.</summary>
        public static readonly string[] BulwarkPath =
        {
            "pas.def.frame1", "pas.def.guard1", "pas.def.guard2", "pas.def.shieldwall",
            "pas.def.skin1", "pas.def.frame2", "pas.def.unbroken",
            "pas.mel.hands1", "pas.mel.swing", "pas.mel.hands2", "pas.mel.answer", "pas.wrd.ember",
        };

        private static Item Starter(ItemBase itemBase, string id) => new()
        {
            Id = id,
            Base = itemBase,
            Rarity = ItemRarity.Normal,
            ItemLevel = 1,
        };
    }
}
