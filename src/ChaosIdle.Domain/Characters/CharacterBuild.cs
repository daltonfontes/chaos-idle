using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Characters
{
    public sealed record CharacterClassDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public IReadOnlyList<Modifier> BaseModifiers { get; init; } = Array.Empty<Modifier>();
    }

    /// <summary>
    /// A complete configuration to simulate. In Milestone 1, passives and gear are represented
    /// as a flat list of modifiers; later milestones derive that list from real systems.
    /// </summary>
    public sealed record CharacterBuild
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public CharacterClassDefinition Class { get; init; } = new CharacterClassDefinition();
        public WeaponDefinition Weapon { get; init; } = new WeaponDefinition();
        public SkillDefinition PrimarySkill { get; init; } = new SkillDefinition();
        public IReadOnlyList<SkillDefinition> TriggeredSkills { get; init; } = Array.Empty<SkillDefinition>();
        public IReadOnlyList<Modifier> Modifiers { get; init; } = Array.Empty<Modifier>();

        /// <summary>Supports linked to each skill, keyed by skill id.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<SupportDefinition>> SkillSupports { get; init; } =
            new Dictionary<string, IReadOnlyList<SupportDefinition>>();

        /// <summary>Character-wide stats: defences, life, mana. Supports are not included.</summary>
        public StatBlock ComputeStats() => new StatBlock(Class.BaseModifiers.Concat(Modifiers));

        public IReadOnlyList<SupportDefinition> SupportsFor(SkillDefinition skill) =>
            SkillSupports.TryGetValue(skill.Id, out var supports) ? supports : Array.Empty<SupportDefinition>();

        /// <summary>Stats used when this skill is used: character stats plus the skill's own supports.</summary>
        public StatBlock ComputeSkillStats(SkillDefinition skill) =>
            new StatBlock(Class.BaseModifiers.Concat(Modifiers).Concat(SupportsFor(skill).SelectMany(s => s.Modifiers)));

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(PrimarySkill.Validate());
            if (PrimarySkill.Trigger != SkillTrigger.None)
                errors.Add($"{Id}: primary skill {PrimarySkill.Id} is a triggered skill.");
            if (PrimarySkill.IsAttack && Weapon.AttacksPerSecond <= 0)
                errors.Add($"{Id}: attack skills need a weapon with a positive attack rate.");

            var skillIds = new HashSet<string>(TriggeredSkills.Select(s => s.Id)) { PrimarySkill.Id };
            foreach (var kv in SkillSupports)
            {
                if (!skillIds.Contains(kv.Key)) errors.Add($"{Id}: supports linked to {kv.Key}, which the build does not use.");
            }

            foreach (var skill in TriggeredSkills.Append(PrimarySkill))
            {
                var linked = new List<SupportDefinition>();
                foreach (var support in SupportsFor(skill))
                {
                    var check = support.CanLink(skill, linked);
                    if (check != SupportCompatibility.Compatible) errors.Add($"{Id}: {support.Id} on {skill.Id}: {check}.");
                    linked.Add(support);
                }
            }

            foreach (var s in TriggeredSkills)
            {
                errors.AddRange(s.Validate());
                if (s.Trigger == SkillTrigger.None)
                    errors.Add($"{Id}: {s.Id} is in the triggered slot but has no trigger.");
            }

            return errors;
        }
    }
}
