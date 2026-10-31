using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Skills;

namespace ChaosIdle.Domain.Characters
{
    public enum EquipResult
    {
        Equipped = 0,
        NotInInventory = 1,
        WrongSlot = 2,
    }

    /// <summary>
    /// The player's persistent character: class, passives, gear, bag, and skills. Mutable state
    /// for the client; call <see cref="ToBuild"/> for an immutable snapshot to simulate.
    /// </summary>
    public sealed class PlayerCharacter
    {
        /// <summary>Used for attacks when no weapon is equipped.</summary>
        public static readonly WeaponDefinition Unarmed = new WeaponDefinition
        {
            Id = "weapon.unarmed",
            Name = "Bare Fists",
            PhysicalMin = 2,
            PhysicalMax = 6,
            AttacksPerSecond = 1.2,
            CritChance = 0,
        };

        public PlayerCharacter(string id, string name, CharacterClassDefinition characterClass, PassiveAllocation passives, int inventoryCapacity)
        {
            Id = id;
            Name = name;
            Class = characterClass;
            Passives = passives;
            Inventory = new Inventory(inventoryCapacity);
        }

        public string Id { get; }
        public string Name { get; }
        public CharacterClassDefinition Class { get; }
        public PassiveAllocation Passives { get; set; }
        public Equipment Equipment { get; } = new Equipment();
        public Inventory Inventory { get; }
        public SkillDefinition PrimarySkill { get; set; } = new SkillDefinition();
        public List<SkillDefinition> TriggeredSkills { get; } = new List<SkillDefinition>();

        public WeaponDefinition Weapon => Equipment[EquipmentSlot.MainHand]?.ToWeapon() ?? Unarmed;

        public CharacterBuild ToBuild() => ToBuild(Equipment);

        /// <summary>Snapshot with alternative equipment, e.g. to preview an item swap.</summary>
        public CharacterBuild ToBuild(Equipment equipment) => new CharacterBuild
        {
            Id = Id,
            Name = Name,
            Class = Class,
            Weapon = equipment[EquipmentSlot.MainHand]?.ToWeapon() ?? Unarmed,
            PrimarySkill = PrimarySkill,
            TriggeredSkills = TriggeredSkills.ToArray(),
            Modifiers = Passives.Modifiers().Concat(equipment.CharacterModifiers()).ToArray(),
        };

        /// <summary>Moves an item from the bag into a slot; the replaced item goes back into the bag.</summary>
        public EquipResult EquipFromInventory(Item item, EquipmentSlot slot)
        {
            if (!Inventory.Contains(item)) return EquipResult.NotInInventory;
            if (!Equipment.Fits(item, slot)) return EquipResult.WrongSlot;

            Inventory.Remove(item);
            var previous = Equipment.Equip(item, slot);
            if (previous != null) Inventory.TryAdd(previous); // always fits: a space was just freed
            return EquipResult.Equipped;
        }
    }
}
