using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;
using Xunit;

namespace ChaosIdle.Tests;

public class ItemTests
{
    internal static AffixDefinition Affix(string id) => Affixes.All.Single(a => a.Id == id);

    internal static Item Make(ItemBase itemBase, params (string AffixId, double Value)[] affixes) => new()
    {
        Id = "test-" + itemBase.Id,
        Base = itemBase,
        Rarity = affixes.Length == 0 ? ItemRarity.Normal : ItemRarity.Rare,
        ItemLevel = 10,
        Affixes = affixes.Select(a => new RolledAffix(Affix(a.AffixId), 1, a.Value)).ToArray(),
    };

    [Fact]
    public void Local_weapon_affixes_modify_the_weapon_only()
    {
        var mace = Make(ItemBases.IronboundMace,
            ("affix.local_added_phys", 6), ("affix.local_inc_phys", 50), ("affix.local_attack_speed", 10), ("affix.local_crit", 20));

        var w = mace.ToWeapon();
        Assert.Equal((14 + 6) * 1.5, w.PhysicalMin, 9);
        Assert.Equal((24 + 6) * 1.5, w.PhysicalMax, 9);
        Assert.Equal(1.25 * 1.1, w.AttacksPerSecond, 9);
        Assert.Equal(5 * 1.2, w.CritChance, 9);
        Assert.Empty(mace.CharacterModifiers());
    }

    [Fact]
    public void Armour_pieces_turn_local_armour_into_character_armour()
    {
        var hauberk = Make(ItemBases.ScaleHauberk, ("affix.local_armour", 60), ("affix.local_inc_armour", 50), ("affix.life", 30));
        Assert.Equal((140 + 60) * 1.5, hauberk.LocalArmour, 9);

        var mods = hauberk.CharacterModifiers().ToList();
        Assert.Contains(mods, m => m.Stat == StatId.Armour && m.Kind == ModifierKind.Flat && Math.Abs(m.Value - 300) < 1e-9);
        Assert.Contains(mods, m => m.Stat == StatId.MaxLife && m.Value == 30);
        Assert.DoesNotContain(mods, m => m.Stat == StatId.Armour && m.Kind == ModifierKind.Increased);
    }

    [Fact]
    public void Implicits_and_shield_block_apply_to_the_character()
    {
        var ring = Make(ItemBases.CopperBand);
        Assert.Contains(ring.CharacterModifiers(), m => m.Stat == StatId.FireResistance && m.Value == 6);

        var shield = Make(ItemBases.RivetedBuckler);
        Assert.Contains(shield.CharacterModifiers(), m => m.Stat == StatId.BlockChance && m.Value == 20);
    }

    [Fact]
    public void Non_weapons_cannot_become_weapons()
    {
        Assert.Throws<InvalidOperationException>(() => Make(ItemBases.IronCap).ToWeapon());
    }

    [Fact]
    public void Affix_text_shows_the_rolled_value()
    {
        var item = Make(ItemBases.IronCap, ("affix.life", 23));
        Assert.Equal("+23 to maximum Life", item.Affixes[0].Text);
    }

    [Fact]
    public void Inventory_respects_capacity()
    {
        var bag = new Inventory(2);
        Assert.True(bag.TryAdd(Make(ItemBases.IronCap) with { Id = "a" }));
        Assert.True(bag.TryAdd(Make(ItemBases.IronCap) with { Id = "b" }));
        Assert.False(bag.TryAdd(Make(ItemBases.IronCap) with { Id = "c" }));
        Assert.True(bag.IsFull);
    }

    [Fact]
    public void Equipping_from_the_bag_swaps_the_old_item_back_in()
    {
        var character = StarterCharacters.NewGuardian();
        var oldMace = character.Equipment[EquipmentSlot.MainHand]!;
        var newMace = Make(ItemBases.FlangedMace);
        character.Inventory.TryAdd(newMace);

        Assert.Equal(EquipResult.WrongSlot, character.EquipFromInventory(newMace, EquipmentSlot.Helmet));
        Assert.Equal(EquipResult.Equipped, character.EquipFromInventory(newMace, EquipmentSlot.MainHand));
        Assert.Same(newMace, character.Equipment[EquipmentSlot.MainHand]);
        Assert.Contains(oldMace, character.Inventory.Items);
        Assert.DoesNotContain(newMace, character.Inventory.Items);
        Assert.Equal(EquipResult.NotInInventory, character.EquipFromInventory(newMace, EquipmentSlot.MainHand));
    }

    [Fact]
    public void Rings_fit_either_ring_slot_only()
    {
        var ring = Make(ItemBases.IronBand);
        Assert.True(Equipment.Fits(ring, EquipmentSlot.Ring1));
        Assert.True(Equipment.Fits(ring, EquipmentSlot.Ring2));
        Assert.False(Equipment.Fits(ring, EquipmentSlot.Amulet));
        Assert.Throws<ArgumentException>(() => new Equipment().Equip(ring, EquipmentSlot.Amulet));
    }

    [Fact]
    public void Character_without_a_weapon_fights_unarmed()
    {
        var character = StarterCharacters.NewGuardian();
        character.Equipment.Unequip(EquipmentSlot.MainHand);
        Assert.Equal(PlayerCharacter.Unarmed, character.ToBuild().Weapon);
    }

    [Fact]
    public void Build_snapshot_combines_class_passives_and_gear()
    {
        var character = StarterCharacters.NewGuardian();
        var build = character.ToBuild();
        Assert.Equal(20, build.ComputeStats().Flat(StatId.BlockChance)); // buckler
        Assert.Equal(190, build.ComputeStats().Resolve(StatId.Armour)); // class 150 + buckler 40
        Assert.Equal("base.ironbound_mace", build.Weapon.Id);
    }
}
