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

        public StatBlock ComputeStats() => new StatBlock(Class.BaseModifiers.Concat(Modifiers));

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(PrimarySkill.Validate());
            if (PrimarySkill.Trigger != SkillTrigger.None)
                errors.Add($"{Id}: primary skill {PrimarySkill.Id} is a triggered skill.");
            if (PrimarySkill.IsAttack && Weapon.AttacksPerSecond <= 0)
                errors.Add($"{Id}: attack skills need a weapon with a positive attack rate.");

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
