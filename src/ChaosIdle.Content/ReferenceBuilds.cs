using System;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;

namespace ChaosIdle.Content
{
    /// <summary>
    /// Two equally invested Guardians for the Milestone 3 gate. Unlike the Milestone 1 sample
    /// builds, these are assembled only from real systems: passive allocations, items with
    /// affixes from the real tables (or uniques), and linked supports. Gear is at roughly tier 2
    /// so neither build relies on perfect rolls.
    /// </summary>
    public static class ReferenceBuilds
    {
        public const int PassivePoints = 18;

        /// <summary>Melee, block and counter-attacks: tanky and steady.</summary>
        public static PlayerCharacter IronBastion()
        {
            var c = Create("ref.iron_bastion", "Iron Bastion",
                PassiveTrees.Notable("bulwark"), PassiveTrees.Notable("vitality"), PassiveTrees.Notable("iron"),
                PassiveTrees.Minor("bulwark", 3), PassiveTrees.Minor("iron", 3), PassiveTrees.Minor("iron", 4),
                PassiveTrees.Minor("bloodletting", 4), PassiveTrees.Minor("vitality", 3));

            c.PrimarySkill = Skills.IronCleave;
            c.TriggeredSkills.Add(Skills.BulwarkRetort);
            Link(c, Skills.IronCleave, Supports.HeavyImpact, Supports.Siphon, Supports.KeenEdge);
            Link(c, Skills.BulwarkRetort, Supports.ReadyRetort, Supports.HeavyImpact);

            Equip(c, EquipmentSlot.MainHand, Rare(ItemBases.FlangedMace, ("affix.local_inc_phys", 45), ("affix.local_added_phys", 7), ("affix.local_attack_speed", 9), ("affix.accuracy", 80)));
            Equip(c, EquipmentSlot.OffHand, Rare(ItemBases.OakPavise, ("affix.life", 30), ("affix.block", 5), ("affix.life_on_block", 12), ("affix.fire_res", 15)));
            Equip(c, EquipmentSlot.Helmet, Rare(ItemBases.IronCap, ("affix.life", 30), ("affix.local_inc_armour", 40), ("affix.fire_res", 15)));
            Equip(c, EquipmentSlot.BodyArmour, Rare(ItemBases.ScaleHauberk, ("affix.life", 34), ("affix.local_armour", 50), ("affix.local_inc_armour", 45), ("affix.fire_res", 14)));
            Equip(c, EquipmentSlot.Boots, Rare(ItemBases.HobnailBoots, ("affix.life", 28), ("affix.local_armour", 40), ("affix.fire_res", 12)));
            Equip(c, EquipmentSlot.Ring1, Rare(ItemBases.IronBand, ("affix.life", 25), ("affix.accuracy", 70), ("affix.life_regen", 3)));
            Equip(c, EquipmentSlot.Ring2, Rare(ItemBases.CopperBand, ("affix.life", 25), ("affix.fire_res", 14), ("affix.mana_regen", 20)));
            Equip(c, EquipmentSlot.Amulet, Rare(ItemBases.BonePendant, ("affix.life", 30), ("affix.crit_multi", 15), ("affix.fire_res", 12)));
            return c;
        }

        /// <summary>Overcharged fire spell: fast clears, thin defences, hungry for mana.</summary>
        public static PlayerCharacter EmberLancer()
        {
            var c = Create("ref.ember_lancer", "Ember Lancer",
                PassiveTrees.Notable("vitality"), PassiveTrees.Notable("hearth"), "pas.key.overcharge");

            c.PrimarySkill = Skills.CinderLance;
            Link(c, Skills.CinderLance, Supports.FocusedIntent, Supports.KeenEdge, Supports.SunderingFocus);

            Equip(c, EquipmentSlot.MainHand, Rare(ItemBases.EmberRod, ("affix.spell_damage", 30), ("affix.fire_damage", 20), ("affix.cast_speed", 9), ("affix.crit_chance", 30)));
            Equip(c, EquipmentSlot.OffHand, Rare(ItemBases.OakPavise, ("affix.life", 30), ("affix.block", 5), ("affix.fire_res", 15)));
            Equip(c, EquipmentSlot.Helmet, Uniques.StillwaterHelm.Roll("ref.ember_lancer.helmet", 10, () => 0.5));
            Equip(c, EquipmentSlot.BodyArmour, Rare(ItemBases.ScaleHauberk, ("affix.life", 34), ("affix.fire_res", 15), ("affix.life_regen", 3)));
            Equip(c, EquipmentSlot.Boots, Rare(ItemBases.HobnailBoots, ("affix.life", 28), ("affix.fire_res", 12)));
            Equip(c, EquipmentSlot.Ring1, Rare(ItemBases.CopperBand, ("affix.fire_damage", 18), ("affix.mana_regen", 25), ("affix.cast_speed", 8)));
            Equip(c, EquipmentSlot.Ring2, Rare(ItemBases.CopperBand, ("affix.fire_damage", 18), ("affix.life", 25), ("affix.fire_res", 14)));
            Equip(c, EquipmentSlot.Amulet, Rare(ItemBases.BonePendant, ("affix.spell_damage", 30), ("affix.crit_chance", 30), ("affix.crit_multi", 18)));
            return c;
        }

        /// <summary>Passives are given as targets; the shortest paths to them are taken in order.</summary>
        private static PlayerCharacter Create(string id, string name, params string[] targets) =>
            new PlayerCharacter(id, name, Classes.Guardian,
                PassiveAllocation.FromTargets(StarterCharacters.EmptyPassives(PassivePoints), targets), StarterCharacters.InventoryCapacity);

        private static void Link(PlayerCharacter c, Domain.Skills.SkillDefinition skill, params Domain.Skills.SupportDefinition[] supports)
        {
            foreach (var s in supports)
            {
                var result = c.LinkSupport(skill, s);
                if (result != Domain.Skills.SupportCompatibility.Compatible)
                    throw new InvalidOperationException($"{c.Name}: cannot link {s.Id} to {skill.Id}: {result}.");
            }
        }

        private static void Equip(PlayerCharacter c, EquipmentSlot slot, Item item) => c.Equipment.Equip(item, slot);

        /// <summary>A rare with chosen affix values; each value must fall inside one of the affix's tiers.</summary>
        public static Item Rare(ItemBase itemBase, params (string AffixId, double Value)[] affixes) => new()
        {
            Id = $"ref.{itemBase.Id}.{string.Join(".", affixes.Select(a => a.AffixId.Replace("affix.", "")))}",
            Base = itemBase,
            Rarity = ItemRarity.Rare,
            ItemLevel = 10,
            Affixes = affixes.Select(a =>
            {
                var def = Affixes.All.Single(x => x.Id == a.AffixId);
                if (!def.AllowedClasses.Contains(itemBase.Class)) throw new InvalidOperationException($"{a.AffixId} cannot roll on {itemBase.Class}.");
                var tier = def.Tiers.FirstOrDefault(t => a.Value >= t.Min && a.Value <= t.Max)
                    ?? throw new InvalidOperationException($"{a.Value} is outside every tier of {a.AffixId}.");
                return new RolledAffix(def, tier.Tier, a.Value);
            }).ToList(),
        };
    }
}
