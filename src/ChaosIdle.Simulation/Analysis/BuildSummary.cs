using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Damage;

namespace ChaosIdle.Simulation.Analysis
{
    /// <summary>
    /// Static character-sheet numbers derived from a build, using the same formulas as the
    /// simulator. Expected values only: no enemy defences, no mana starvation.
    /// </summary>
    public sealed record BuildSummary
    {
        /// <summary>Hit size used to express armour as a percentage.</summary>
        public const double ReferencePhysicalHit = 100;

        public double MaxLife { get; init; }
        public double LifeRegenPerSecond { get; init; }
        public double MaxMana { get; init; }
        public double ManaRegenPerSecond { get; init; }
        public double Armour { get; init; }
        public double ArmourReductionVsReferenceHit { get; init; }
        public double Evasion { get; init; }
        public double BlockChance { get; init; }
        public bool BlocksSpells { get; init; }
        public double FireResistance { get; init; }
        public double ColdResistance { get; init; }
        public double LightningResistance { get; init; }

        public double AverageHit { get; init; }
        public double CritChance { get; init; }
        public double ActionsPerSecond { get; init; }
        public double HitDamagePerSecond { get; init; }
        public double ManaCostPerUse { get; init; }
        public double ManaSpentPerSecond => ManaCostPerUse * ActionsPerSecond;

        /// <summary>True when regeneration alone pays for using the primary skill continuously.</summary>
        public bool ManaSustainable => ManaSpentPerSecond <= ManaRegenPerSecond + 1e-9;

        public static BuildSummary Compute(CharacterBuild build)
        {
            var stats = build.ComputeStats();
            var defence = DefenceStats.From(stats);
            var skill = build.PrimarySkill;
            double hit = DamagePipeline.ExpectedHit(skill, build.Weapon, stats).Total;
            double rate = 1.0 / DamagePipeline.ActionTime(skill, build.Weapon, stats);

            return new BuildSummary
            {
                MaxLife = stats.Resolve(StatId.MaxLife),
                LifeRegenPerSecond = stats.Resolve(StatId.LifeRegenPerSecond),
                MaxMana = stats.Resolve(StatId.MaxMana),
                ManaRegenPerSecond = stats.Resolve(StatId.ManaRegenPerSecond),
                Armour = defence.Armour,
                ArmourReductionVsReferenceHit = CombatFormulas.ArmourReduction(defence.Armour, ReferencePhysicalHit),
                Evasion = defence.Evasion,
                BlockChance = System.Math.Min(defence.BlockChance, CombatFormulas.MaxBlockChance),
                BlocksSpells = defence.BlockSpells,
                FireResistance = System.Math.Min(defence.FireResistance, CombatFormulas.MaxResistance),
                ColdResistance = System.Math.Min(defence.ColdResistance, CombatFormulas.MaxResistance),
                LightningResistance = System.Math.Min(defence.LightningResistance, CombatFormulas.MaxResistance),
                AverageHit = hit,
                CritChance = DamagePipeline.CritChance(skill, build.Weapon, stats),
                ActionsPerSecond = rate,
                HitDamagePerSecond = hit * rate,
                ManaCostPerUse = DamagePipeline.ManaCost(skill, stats),
            };
        }
    }
}
