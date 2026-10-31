using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Damage;

namespace ChaosIdle.Simulation.Diagnostics
{
    public enum DiagnosisCode
    {
        InsufficientDamage = 1,
        LowResistance = 2,
        LowPhysicalMitigation = 3,
        LargeHitSpike = 4,
        LowSustain = 5,
        ResourceStarvation = 6,
    }

    /// <param name="DamageType">Set for type-specific findings such as low resistance.</param>
    public sealed record Diagnosis(DiagnosisCode Code, string Message, DamageType? DamageType = null);

    /// <summary>
    /// Turns a combat report into player-facing reasons for failure. Rules are deliberately
    /// simple, explicit thresholds so each one can be tested and explained.
    /// </summary>
    public static class FailureAnalyzer
    {
        public const double DominantShare = 0.5;
        public const double SpikeShareOfMaxLife = 0.4;
        public const double LowArmourEfficiency = 0.3;
        public const double LowSustainShare = 0.15;
        public const double StarvationShare = 0.15;

        public static IReadOnlyList<Diagnosis> Analyze(CombatReport report, DefenceStats defence)
        {
            var findings = new List<Diagnosis>();

            if (report.Outcome == CombatOutcome.Timeout)
            {
                findings.Add(new Diagnosis(DiagnosisCode.InsufficientDamage,
                    $"Ran out of time with the current enemy at {report.CurrentEnemyLifeFraction:P0} life. More damage is needed."));
            }

            if (report.Outcome == CombatOutcome.Death)
            {
                AnalyzeDeath(report, defence, findings);
            }

            if (report.DurationSeconds > 0 && report.TimeOutOfMana / report.DurationSeconds >= StarvationShare)
            {
                findings.Add(new Diagnosis(DiagnosisCode.ResourceStarvation,
                    $"Spent {report.TimeOutOfMana / report.DurationSeconds:P0} of the fight waiting for mana. Lower costs or raise mana regeneration."));
            }

            return findings;
        }

        private static void AnalyzeDeath(CombatReport report, DefenceStats defence, List<Diagnosis> findings)
        {
            var recent = new DamageSet();
            foreach (var e in report.RecentDamageTaken)
            {
                recent.Add(e.Damage);
            }

            if (recent.Total > 0)
            {
                var dominant = recent.Dominant();
                double share = recent[dominant] / recent.Total;
                if (share >= DominantShare)
                {
                    if (dominant.IsElemental() && defence.Resistance(dominant) < CombatFormulas.MaxResistance)
                    {
                        findings.Add(new Diagnosis(DiagnosisCode.LowResistance,
                            $"{share:P0} of recent damage was {dominant} with only {defence.Resistance(dominant):0}% {dominant} resistance (cap {CombatFormulas.MaxResistance:0}%).",
                            dominant));
                    }

                    if (dominant == DamageType.Physical)
                    {
                        double before = report.DamageTakenBeforeMitigation[DamageType.Physical];
                        double efficiency = before > 0 ? report.PreventedByArmour / before : 0;
                        if (efficiency < LowArmourEfficiency)
                        {
                            findings.Add(new Diagnosis(DiagnosisCode.LowPhysicalMitigation,
                                $"{share:P0} of recent damage was Physical and armour prevented only {efficiency:P0} of physical hits.",
                                DamageType.Physical));
                        }
                    }
                }
            }

            if (report.KillingBlow != null && report.KillingBlow.Total >= SpikeShareOfMaxLife * report.MaxLife)
            {
                findings.Add(new Diagnosis(DiagnosisCode.LargeHitSpike,
                    $"Killed by {report.KillingBlow.AttackName} for {report.KillingBlow.Total:0} ({report.KillingBlow.Total / report.MaxLife:P0} of max life). Raise max life or avoid/mitigate that hit."));
            }

            double taken = report.DamageTaken.Total;
            if (taken > 0 && report.TotalRecovery / taken < LowSustainShare)
            {
                findings.Add(new Diagnosis(DiagnosisCode.LowSustain,
                    $"Recovered only {report.TotalRecovery:0} life against {taken:0} damage taken ({report.TotalRecovery / taken:P0})."));
            }
        }
    }
}
