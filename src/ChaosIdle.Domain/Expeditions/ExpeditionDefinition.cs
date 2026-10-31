using System;
using System.Collections.Generic;
using ChaosIdle.Domain.Monsters;

namespace ChaosIdle.Domain.Expeditions
{
    /// <summary>
    /// An ordered run of encounters. The character starts each encounter at full life and mana
    /// (Milestone 2 rule) and the expedition stops at the first failed encounter.
    /// </summary>
    public sealed record ExpeditionDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";

        /// <summary>Item level of everything that drops here.</summary>
        public int AreaLevel { get; init; } = 1;

        public IReadOnlyList<Encounter> Encounters { get; init; } = Array.Empty<Encounter>();
    }
}
