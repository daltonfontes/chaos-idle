using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Damage;

namespace ChaosIdle.Simulation.Analysis
{
    /// <summary>Which stats matter to a build, for loot filters and highlighting.</summary>
    public static class BuildRelevance
    {
        public static IReadOnlyList<StatId> StatsFor(CharacterBuild build)
        {
            var stats = new List<StatId>
            {
                StatId.MaxLife, StatId.LifeRegenPerSecond,
                StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance,
            };

            var skill = build.PrimarySkill;
            var skillStats = build.ComputeSkillStats(skill);
            var expected = DamagePipeline.ExpectedHit(skill, build.Weapon, skillStats);
            foreach (var type in DamageTypes.All.Where(t => expected[t] > 0))
            {
                stats.Add(DamagePipeline.TypeStat(type));
            }

            stats.Add(StatId.CriticalChance);
            stats.Add(StatId.CriticalMultiplier);

            if (skill.IsAttack)
            {
                stats.Add(StatId.AttackSpeed);
                stats.Add(StatId.Accuracy);
                stats.Add(StatId.PhysicalDamage); // local weapon damage still feeds converted attacks
            }

            if (skill.IsSpell)
            {
                stats.Add(StatId.SpellDamage);
                stats.Add(StatId.CastSpeed);
                stats.Add(StatId.MaxMana);
                stats.Add(StatId.ManaRegenPerSecond);
            }

            var sheet = build.ComputeStats();
            if (sheet.Flat(StatId.BlockChance) > 0 || build.TriggeredSkills.Count > 0)
            {
                stats.Add(StatId.BlockChance);
                stats.Add(StatId.LifeOnBlock);
            }

            if (sheet.Resolve(StatId.Armour) > 0) stats.Add(StatId.Armour);
            return stats.Distinct().ToList();
        }

        /// <summary>
        /// Preset: always highlight uniques and items with at least two wanted stats; hide magic
        /// items with no wanted stat. Normal items stay visible (a plain base can still fill an
        /// empty slot) unless <paramref name="strict"/> is set, for characters with full gear.
        /// </summary>
        public static LootFilter FilterFor(CharacterBuild build, bool strict = false)
        {
            var wanted = StatsFor(build);
            var rules = new List<LootFilterRule>
            {
                new LootFilterRule { Name = "Uniques", Action = FilterAction.Highlight, MinRarity = ItemRarity.Unique },
                new LootFilterRule { Name = "Build affixes x2+", Action = FilterAction.Highlight, MinRarity = ItemRarity.Magic, WantedStats = wanted, MinMatchingAffixes = 2 },
                new LootFilterRule { Name = "Off-build magic", Action = FilterAction.Hide, MinRarity = ItemRarity.Magic, MaxRarity = ItemRarity.Magic, WantedStats = wanted, MaxMatchingAffixes = 0 },
            };
            if (strict)
            {
                rules.Add(new LootFilterRule { Name = "Normal items", Action = FilterAction.Hide, MaxRarity = ItemRarity.Normal });
            }

            return new LootFilter { Name = (strict ? "Strict: " : "Focused: ") + build.Name, Rules = rules };
        }
    }
}
