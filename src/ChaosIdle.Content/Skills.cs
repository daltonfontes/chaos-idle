using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Skills;

namespace ChaosIdle.Content
{
    public static class Skills
    {
        /// <summary>Plain melee weapon strike; the Guardian's bread and butter.</summary>
        public static readonly SkillDefinition IronCleave = new SkillDefinition
        {
            Id = "skill.iron_cleave",
            Name = "Iron Cleave",
            Tags = SkillTags.Attack | SkillTags.Melee,
            ManaCost = 3,
            DamageEffectiveness = 120,
        };

        /// <summary>Counter-strike fired when a hit is blocked. Rewards investing in block.</summary>
        public static readonly SkillDefinition BulwarkRetort = new SkillDefinition
        {
            Id = "skill.bulwark_retort",
            Name = "Bulwark Retort",
            Tags = SkillTags.Attack | SkillTags.Melee | SkillTags.Triggered,
            Trigger = SkillTrigger.OnBlock,
            CooldownSeconds = 0.5,
            DamageEffectiveness = 180,
        };

        /// <summary>Fire spell: high damage, high mana cost, independent of weapon.</summary>
        public static readonly SkillDefinition CinderLance = new SkillDefinition
        {
            Id = "skill.cinder_lance",
            Name = "Cinder Lance",
            Tags = SkillTags.Spell,
            ManaCost = 10,
            BaseDamage = new[] { new DamageRange(DamageType.Fire, 40, 60) },
            BaseCritChance = 6,
            CastTimeSeconds = 0.8,
            DamageEffectiveness = 90,
        };

        public static readonly SkillDefinition[] All = { IronCleave, BulwarkRetort, CinderLance };
    }
}
