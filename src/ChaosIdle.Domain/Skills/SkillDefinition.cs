using System;
using System.Collections.Generic;
using ChaosIdle.Domain.Combat;

namespace ChaosIdle.Domain.Skills
{
    [Flags]
    public enum SkillTags
    {
        None = 0,
        Attack = 1,
        Spell = 2,
        Melee = 4,
        Triggered = 8,
    }

    public enum SkillTrigger
    {
        /// <summary>Used by the auto-combat loop as the primary skill.</summary>
        None = 0,

        /// <summary>Fires when the character blocks a hit (costs no mana, respects cooldown).</summary>
        OnBlock = 1,
    }

    public sealed record SkillDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public SkillTags Tags { get; init; }
        public double ManaCost { get; init; }

        /// <summary>Spells only: the skill's own base damage.</summary>
        public IReadOnlyList<DamageRange> BaseDamage { get; init; } = Array.Empty<DamageRange>();

        /// <summary>
        /// Percent. Attacks scale weapon damage by this; every skill scales flat added damage by it.
        /// </summary>
        public double DamageEffectiveness { get; init; } = 100;

        /// <summary>Spells only; attacks use the weapon's crit chance.</summary>
        public double BaseCritChance { get; init; }

        /// <summary>Spells only; attacks use the weapon's attack rate.</summary>
        public double CastTimeSeconds { get; init; }

        public IReadOnlyList<DamageConversion> Conversions { get; init; } = Array.Empty<DamageConversion>();
        public SkillTrigger Trigger { get; init; } = SkillTrigger.None;
        public double CooldownSeconds { get; init; }

        public bool IsAttack => (Tags & SkillTags.Attack) != 0;
        public bool IsSpell => (Tags & SkillTags.Spell) != 0;

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Id)) errors.Add("Skill has no Id.");
            if (IsAttack == IsSpell) errors.Add($"{Id}: must be exactly one of Attack or Spell.");
            if (IsSpell && CastTimeSeconds <= 0) errors.Add($"{Id}: spells need a positive cast time.");
            if (IsSpell && BaseDamage.Count == 0) errors.Add($"{Id}: spells need base damage.");
            if (ManaCost < 0) errors.Add($"{Id}: mana cost cannot be negative.");
            if (DamageEffectiveness < 0) errors.Add($"{Id}: damage effectiveness cannot be negative.");
            if (Trigger != SkillTrigger.None && (Tags & SkillTags.Triggered) == 0)
                errors.Add($"{Id}: triggered skills must carry the Triggered tag.");
            if (Trigger == SkillTrigger.None && (Tags & SkillTags.Triggered) != 0)
                errors.Add($"{Id}: Triggered tag without a trigger.");

            var convertedFrom = new Dictionary<DamageType, double>();
            foreach (var c in Conversions)
            {
                if (c.From == c.To) errors.Add($"{Id}: conversion from {c.From} to itself.");
                convertedFrom.TryGetValue(c.From, out var sum);
                convertedFrom[c.From] = sum + c.Percent;
            }

            foreach (var kv in convertedFrom)
            {
                if (kv.Value > 100) errors.Add($"{Id}: more than 100% of {kv.Key} converted.");
            }

            return errors;
        }
    }
}
