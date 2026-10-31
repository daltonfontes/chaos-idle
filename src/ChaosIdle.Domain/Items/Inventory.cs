using System.Collections.Generic;

namespace ChaosIdle.Domain.Items
{
    /// <summary>Fixed-capacity bag. Items that do not fit are rejected, never silently dropped.</summary>
    public sealed class Inventory
    {
        private readonly List<Item> _items = new List<Item>();

        public Inventory(int capacity)
        {
            if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }
        public IReadOnlyList<Item> Items => _items;
        public int FreeSpace => Capacity - _items.Count;
        public bool IsFull => FreeSpace <= 0;

        public bool TryAdd(Item item)
        {
            if (IsFull || _items.Contains(item)) return false;
            _items.Add(item);
            return true;
        }

        public bool Remove(Item item) => _items.Remove(item);

        public bool Contains(Item item) => _items.Contains(item);
    }
}
