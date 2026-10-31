using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;

namespace ChaosIdle.Simulation.Analysis
{
    public sealed record BuildScenario(string Name, Encounter Encounter);

    /// <summary>How a build performs across scenarios over many seeds.</summary>
    public sealed record BuildProfile
    {
        public string BuildId { get; init; } = "";
        public string BuildName { get; init; } = "";
        public BuildSummary Sheet { get; init; } = new BuildSummary();
        public IReadOnlyList<(BuildScenario Scenario, BatchSummary Result)> Results { get; init; } =
            Array.Empty<(BuildScenario, BatchSummary)>();

        public BatchSummary this[string scenario] => Results.Single(r => r.Scenario.Name == scenario).Result;

        /// <summary>Wins at least <paramref name="minVictoryRate"/> of runs in every scenario.</summary>
        public bool IsViable(double minVictoryRate) => Results.All(r => r.Result.VictoryRate >= minVictoryRate);
    }

    /// <param name="Leader">BuildId of the build that is better on this metric.</param>
    public sealed record BuildContrast(string Scenario, string Metric, string Leader, double Ratio, string Text);

    public static class BuildComparer
    {
        /// <summary>Victory rates must differ by this much to count as a contrast.</summary>
        public const double VictoryRateGap = 0.2;

        /// <summary>Times and damage rates must differ by this factor to count.</summary>
        public const double MeaningfulRatio = 1.25;

        public static BuildProfile Profile(CharacterBuild build, IReadOnlyList<BuildScenario> scenarios, ulong firstSeed, int runs) => new()
        {
            BuildId = build.Id,
            BuildName = build.Name,
            Sheet = BuildSummary.Compute(build),
            Results = scenarios.Select(s => (s, BatchRunner.Run(build, s.Encounter, firstSeed, runs))).ToList(),
        };

        /// <summary>
        /// Every meaningful difference between two profiles on the same scenarios. Never a single
        /// score: clear speed, success, damage taken, life left and mana are reported separately.
        /// </summary>
        public static IReadOnlyList<BuildContrast> Compare(BuildProfile a, BuildProfile b)
        {
            var contrasts = new List<BuildContrast>();
            foreach (var (scenario, ra) in a.Results)
            {
                var rb = b[scenario.Name];
                string s = scenario.Name;

                double winGap = ra.VictoryRate - rb.VictoryRate;
                if (Math.Abs(winGap) >= VictoryRateGap)
                {
                    var (lead, hi, lo) = winGap > 0 ? (a, ra, rb) : (b, rb, ra);
                    contrasts.Add(new BuildContrast(s, "Victory rate", lead.BuildId, hi.VictoryRate / Math.Max(lo.VictoryRate, 0.01),
                        $"{lead.BuildName} succeeds at {s} far more often ({hi.VictoryRate:P0} vs {lo.VictoryRate:P0})."));
                }

                if (ra.MedianClearTime.HasValue && rb.MedianClearTime.HasValue)
                {
                    AddRatio(contrasts, s, "Clear time", a, b, ra.MedianClearTime.Value, rb.MedianClearTime.Value, lowerIsBetter: true,
                        (lead, ratio) => $"{lead.BuildName} finishes {s} {ratio:0.0}× faster.");
                }

                AddRatio(contrasts, s, "Damage taken", a, b, ra.MeanDamageTakenPerSecond, rb.MeanDamageTakenPerSecond, lowerIsBetter: true,
                    (lead, ratio) => $"{lead.BuildName} takes {1 - 1 / ratio:P0} less damage per second in {s}.");

                double lifeGap = ra.MeanFinalLifeFraction - rb.MeanFinalLifeFraction;
                if (Math.Abs(lifeGap) >= 0.15)
                {
                    var (lead, hi, lo) = lifeGap > 0 ? (a, ra, rb) : (b, rb, ra);
                    contrasts.Add(new BuildContrast(s, "Life left", lead.BuildId, hi.MeanFinalLifeFraction / Math.Max(lo.MeanFinalLifeFraction, 0.01),
                        $"{lead.BuildName} ends {s} with more life left ({hi.MeanFinalLifeFraction:P0} vs {lo.MeanFinalLifeFraction:P0})."));
                }

                double starveA = Share(ra, DiagnosisCode.ResourceStarvation);
                double starveB = Share(rb, DiagnosisCode.ResourceStarvation);
                if (Math.Abs(starveA - starveB) >= VictoryRateGap)
                {
                    var (lead, other, otherShare) = starveA < starveB ? (a, b, starveB) : (b, a, starveA);
                    contrasts.Add(new BuildContrast(s, "Mana", lead.BuildId, 1 + otherShare,
                        $"{other.BuildName} runs short of mana in {otherShare:P0} of {s} runs; {lead.BuildName} does not."));
                }
            }

            return contrasts;
        }

        private static double Share(BatchSummary r, DiagnosisCode code) =>
            r.Runs == 0 || !r.DiagnosisCounts.TryGetValue(code, out var n) ? 0 : (double)n / r.Runs;

        private static void AddRatio(List<BuildContrast> list, string scenario, string metric, BuildProfile a, BuildProfile b,
            double va, double vb, bool lowerIsBetter, Func<BuildProfile, double, string> text)
        {
            if (va <= 0 || vb <= 0) return;
            bool aLeads = lowerIsBetter ? va < vb : va > vb;
            double ratio = Math.Max(va, vb) / Math.Min(va, vb);
            if (ratio < MeaningfulRatio) return;
            var lead = aLeads ? a : b;
            list.Add(new BuildContrast(scenario, metric, lead.BuildId, ratio, text(lead, ratio)));
        }
    }
}
