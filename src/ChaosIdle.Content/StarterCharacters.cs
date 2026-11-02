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
            var character = new PlayerCharacter(id, name, Classes.Guardian, EmptyPassives(StartingPassivePoints), InventoryCapacity)
            {
                PrimarySkill = Skills.IronCleave,
            };
            character.TriggeredSkills.Add(Skills.BulwarkRetort);
            character.Equipment.Equip(Starter(ItemBases.IronboundMace, "starter-mace"), EquipmentSlot.MainHand);
            character.Equipment.Equip(Starter(ItemBases.RivetedBuckler, "starter-buckler"), EquipmentSlot.OffHand);
            return character;
        }

        /// <summary>No points spent yet, starting at the Guardian's start on the shared tree.</summary>
        public static PassiveAllocation EmptyPassives(int points) =>
            PassiveAllocation.Empty(PassiveTrees.World, points, PassiveTrees.World.StartNodeFor(Classes.Guardian.Id));

        /// <summary>
        /// Suggested first passives for a block/life Guardian, as targets: the shortest paths to
        /// them are taken in order, so the suggestion follows layout changes.
        /// </summary>
        public static readonly string[] StarterTargets =
        {
            PassiveTrees.Notable("vitality"), PassiveTrees.Notable("bulwark"),
            PassiveTrees.Minor("bulwark", 3), PassiveTrees.Minor("iron", 4),
        };

        /// <summary>The suggested 12-point allocation (see <see cref="StarterTargets"/>).</summary>
        public static PassiveAllocation StarterPassives() =>
            PassiveAllocation.FromTargets(EmptyPassives(StartingPassivePoints), StarterTargets);

        private static Item Starter(ItemBase itemBase, string id) => new()
        {
            Id = id,
            Base = itemBase,
            Rarity = ItemRarity.Normal,
            ItemLevel = 1,
        };
    }
}
