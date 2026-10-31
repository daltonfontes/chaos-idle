using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Diagnostics;

namespace ChaosIdle.Simulation.Combat
{
    public sealed record BatchSummary
    {
        public string BuildId { get; init; } = "";
        public string EncounterId { get; init; } = "";
        public int Runs { get; init; }
        public int Victories { get; init; }
        public int Deaths { get; init; }
        public int Timeouts { get; init; }
        public double VictoryRate => Runs == 0 ? 0 : (double)Victories / Runs;

        /// <summary>Median fight duration across victories; null if none.</summary>
        public double? MedianClearTime { get; init; }

        public double? MedianBossKillTime { get; init; }
        public double MeanDamagePerSecond { get; init; }
        public double MeanDamageTakenPerSecond { get; init; }
        public double MeanFinalLifeFraction { get; init; }

        /// <summary>How many runs reported each diagnosis.</summary>
        public IReadOnlyDictionary<DiagnosisCode, int> DiagnosisCounts { get; init; } = new Dictionary<DiagnosisCode, int>();
    }

    /// <summary>Runs one build against one encounter over consecutive seeds and aggregates results.</summary>
    public static class BatchRunner
    {
        public static BatchSummary Run(CharacterBuild build, Encounter encounter, ulong firstSeed, int runs, SimulationOptions? options = null)
        {
            var simulator = new CombatSimulator();
            var reports = new List<CombatReport>(runs);
            for (int i = 0; i < runs; i++)
            {
                reports.Add(simulator.Run(build, encounter, firstSeed + (ulong)i, options));
            }

            return Summarize(build.Id, encounter.Id, reports);
        }

        public static BatchSummary Summarize(string buildId, string encounterId, IReadOnlyList<CombatReport> reports)
        {
            var wins = reports.Where(r => r.Outcome == CombatOutcome.Victory).ToList();
            var bossTimes = reports.Select(r => r.BossKillTime).Where(t => t.HasValue).Select(t => t!.Value).ToList();
            var diagnoses = new Dictionary<DiagnosisCode, int>();
            foreach (var code in reports.SelectMany(r => r.Diagnoses.Select(d => d.Code).Distinct()))
            {
                diagnoses.TryGetValue(code, out var n);
                diagnoses[code] = n + 1;
            }

            return new BatchSummary
            {
                BuildId = buildId,
                EncounterId = encounterId,
                Runs = reports.Count,
                Victories = wins.Count,
                Deaths = reports.Count(r => r.Outcome == CombatOutcome.Death),
                Timeouts = reports.Count(r => r.Outcome == CombatOutcome.Timeout),
                MedianClearTime = Median(wins.Select(r => r.DurationSeconds).ToList()),
                MedianBossKillTime = Median(bossTimes),
                MeanDamagePerSecond = reports.Count == 0 ? 0 : reports.Average(r => r.DamagePerSecond),
                MeanDamageTakenPerSecond = reports.Count == 0 ? 0 : reports.Average(r => r.DurationSeconds > 0 ? r.DamageTaken.Total / r.DurationSeconds : 0),
                MeanFinalLifeFraction = reports.Count == 0 ? 0 : reports.Average(r => r.FinalLife / r.MaxLife),
                DiagnosisCounts = diagnoses,
            };
        }

        private static double? Median(List<double> values)
        {
            if (values.Count == 0) return null;
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
        }
    }
}
