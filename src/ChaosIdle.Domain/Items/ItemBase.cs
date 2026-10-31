using System;
using System.Collections.Generic;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Items
{
    /// <summary>A droppable item type. Weapon fields apply to weapons; defence fields to armour pieces.</summary>
    public sealed record ItemBase
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public ItemClass Class { get; init; }

        /// <summary>Minimum area level for this base to drop.</summary>
        public int DropLevel { get; init; } = 1;

        /// <summary>Relative chance among eligible bases.</summary>
        public int DropWeight { get; init; } = 100;

        public double PhysicalMin { get; init; }
        public double PhysicalMax { get; init; }
        public double AttacksPerSecond { get; init; }
        public double CritChance { get; init; }

        public double Armour { get; init; }
        public double Evasion { get; init; }
        public double BlockChance { get; init; }

        /// <summary>Fixed, always-present global modifiers.</summary>
        public IReadOnlyList<Modifier> Implicits { get; init; } = Array.Empty<Modifier>();
    }
}
