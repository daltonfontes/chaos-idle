using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Skills
{
    public enum SupportCompatibility
    {
        Compatible = 0,

        /// <summary>The skill lacks a tag the support needs (e.g. an attack support on a spell).</summary>
        MissingTag = 1,

        /// <summary>The skill has a tag the support refuses.</summary>
        ExcludedTag = 2,

        AlreadyLinked = 3,
        NoFreeSocket = 4,
    }

    /// <summary>
    /// Modifies only the skill it is linked to. Its modifiers are added to that skill's stat
    /// block and never to the character sheet (defence, life, other skills).
    /// </summary>
    public sealed record SupportDefinition
    {
        public const int MaxSupportsPerSkill = 3;

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>The skill must have all of these tags.</summary>
        public SkillTags RequiredTags { get; init; }

        /// <summary>The skill must have none of these tags.</summary>
        public SkillTags ExcludedTags { get; init; }

        public IReadOnlyList<Modifier> Modifiers { get; init; } = Array.Empty<Modifier>();

        public SupportCompatibility CanSupport(SkillDefinition skill)
        {
            if ((skill.Tags & RequiredTags) != RequiredTags) return SupportCompatibility.MissingTag;
            if ((skill.Tags & ExcludedTags) != 0) return SupportCompatibility.ExcludedTag;
            return SupportCompatibility.Compatible;
        }

        /// <summary>Checks adding this support to a skill that already has <paramref name="linked"/>.</summary>
        public SupportCompatibility CanLink(SkillDefinition skill, IReadOnlyCollection<SupportDefinition> linked)
        {
            var basic = CanSupport(skill);
            if (basic != SupportCompatibility.Compatible) return basic;
            if (linked.Any(s => s.Id == Id)) return SupportCompatibility.AlreadyLinked;
            if (linked.Count >= MaxSupportsPerSkill) return SupportCompatibility.NoFreeSocket;
            return SupportCompatibility.Compatible;
        }
    }
}
