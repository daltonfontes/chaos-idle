using System;
using System.Collections.Generic;
using ChaosIdle.Domain.Combat;

namespace ChaosIdle.Domain.Monsters
{
    public sealed record EnemyAttackDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public IReadOnlyList<DamageRange> Damage { get; init; } = Array.Empty<DamageRange>();

        /// <summary>Spells cannot be evaded or blocked in Milestone 1.</summary>
        public bool IsSpell { get; init; }

        /// <summary>0 = usable every action (the fallback attack).</summary>
        public double CooldownSeconds { get; init; }
    }

    public sealed record EnemyDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsBoss { get; init; }
        public double MaxLife { get; init; }
        public double Armour { get; init; }
        public double Evasion { get; init; }
        public double Accuracy { get; init; }
        public double FireResistance { get; init; }
        public double ColdResistance { get; init; }
        public double LightningResistance { get; init; }

        /// <summary>Seconds between actions.</summary>
        public double ActionTimeSeconds { get; init; } = 1.0;

        /// <summary>
        /// Checked in order each action; the first attack off cooldown is used. The last entry
        /// must have no cooldown so the enemy always has something to do.
        /// </summary>
        public IReadOnlyList<EnemyAttackDefinition> Attacks { get; init; } = Array.Empty<EnemyAttackDefinition>();

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (MaxLife <= 0) errors.Add($"{Id}: MaxLife must be positive.");
            if (ActionTimeSeconds <= 0) errors.Add($"{Id}: ActionTimeSeconds must be positive.");
            if (Attacks.Count == 0) errors.Add($"{Id}: needs at least one attack.");
            else if (Attacks[Attacks.Count - 1].CooldownSeconds > 0)
                errors.Add($"{Id}: last attack must have no cooldown.");
            return errors;
        }
    }

    /// <summary>Enemies fought one at a time, in order (single-target in Milestone 1).</summary>
    public sealed record Encounter
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public IReadOnlyList<EnemyDefinition> Enemies { get; init; } = Array.Empty<EnemyDefinition>();
    }
}
