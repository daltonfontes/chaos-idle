using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Items
{
    public sealed class Equipment
    {
        private readonly Dictionary<EquipmentSlot, Item> _items = new Dictionary<EquipmentSlot, Item>();

        public IReadOnlyDictionary<EquipmentSlot, Item> Items => _items;

        public Item? this[EquipmentSlot slot] => _items.TryGetValue(slot, out var item) ? item : null;

        public static bool Fits(Item item, EquipmentSlot slot) => item.Class.SlotsFor().Contains(slot);

        /// <summary>Equips the item and returns whatever was in the slot.</summary>
        public Item? Equip(Item item, EquipmentSlot slot)
        {
            if (!Fits(item, slot))
            {
                throw new System.ArgumentException($"{item.Name} ({item.Class}) does not fit {slot}.");
            }

            var previous = this[slot];
            _items[slot] = item;
            return previous;
        }

        public Item? Unequip(EquipmentSlot slot)
        {
            var previous = this[slot];
            _items.Remove(slot);
            return previous;
        }

        public Equipment Clone()
        {
            var copy = new Equipment();
            foreach (var kv in _items) copy._items[kv.Key] = kv.Value;
            return copy;
        }

        public IEnumerable<Modifier> CharacterModifiers() =>
            _items.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value.CharacterModifiers());
    }
}
