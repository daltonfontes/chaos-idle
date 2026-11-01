using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Offline
{
    public sealed class OfflineResult
    {
        internal OfflineResult() { }

        public OfflineCheckpoint Checkpoint { get; internal set; } = new OfflineCheckpoint();
        public DateTime ResolvedAtUtc { get; internal set; }
        public TimeSpan TimeAway { get; internal set; }

        /// <summary>Time that counted, after the cap (before efficiency).</summary>
        public TimeSpan CreditedTime { get; internal set; }

        public bool WasCapped { get; internal set; }

        /// <summary>The clock went backwards: nothing is credited.</summary>
        public bool ClockAnomaly { get; internal set; }

        public bool TooShort { get; internal set; }

        public int RunsCompleted { get; internal set; }
        public int RunsFailed { get; internal set; }
        public int RunsAttempted => RunsCompleted + RunsFailed;
        public double SuccessRate => RunsAttempted == 0 ? 0 : (double)RunsCompleted / RunsAttempted;

        /// <summary>Loot from completed runs, before the loot filter and storage cap.</summary>
        public IReadOnlyList<Item> Loot { get; internal set; } = Array.Empty<Item>();

        /// <summary>How many of the sampled failures reported each diagnosis.</summary>
        public IReadOnlyDictionary<DiagnosisCode, int> FailureReasons { get; internal set; } = new Dictionary<DiagnosisCode, int>();

        /// <summary>Example messages, one per diagnosis code, taken from the sampled failures.</summary>
        public IReadOnlyList<Diagnosis> FailureExamples { get; internal set; } = Array.Empty<Diagnosis>();

        public bool IsClaimed { get; private set; }

        /// <summary>
        /// Brings the loot home through the filter: hidden items are dropped, highlighted items
        /// first, then the rest, up to both the storage cap and the free bag space.
        /// Claiming twice throws, so a session's rewards can never be collected twice.
        /// </summary>
        public OfflineClaim Claim(Inventory inventory, LootFilter? filter, OfflineSettings settings)
        {
            if (IsClaimed) throw new InvalidOperationException($"Offline session {Checkpoint.SessionId} was already claimed.");
            IsClaimed = true;
            filter ??= LootFilter.ShowEverything;

            var decisions = Loot.Select(i => (Item: i, Action: filter.Evaluate(i).Action)).ToList();
            var hidden = decisions.Where(d => d.Action == FilterAction.Hide).Select(d => d.Item).ToList();
            var highlighted = decisions.Where(d => d.Action == FilterAction.Highlight).Select(d => d.Item).ToList();
            var ordered = highlighted.Concat(decisions.Where(d => d.Action == FilterAction.Show).Select(d => d.Item)).ToList();

            var kept = new List<Item>();
            var overCap = new List<Item>();
            var noRoom = new List<Item>();
            foreach (var item in ordered)
            {
                if (kept.Count >= settings.StorageCap) overCap.Add(item);
                else if (inventory.TryAdd(item)) kept.Add(item);
                else noRoom.Add(item);
            }

            return new OfflineClaim(kept, hidden, highlighted, overCap, noRoom);
        }
    }

    public sealed record OfflineClaim(
        IReadOnlyList<Item> Kept,
        IReadOnlyList<Item> Hidden,
        IReadOnlyList<Item> Highlighted,
        IReadOnlyList<Item> OverStorageCap,
        IReadOnlyList<Item> NoBagSpace);

    /// <summary>
    /// Resolves an offline session without simulating every run.
    ///
    /// 1. Credited time = clamp(now − start, 0, cap); a clock that went backwards credits nothing.
    /// 2. Effective time = credited × efficiency.
    /// 3. A fixed number of detailed, seeded runs of the map's packs (no boss) are simulated.
    /// 4. The session is played out as a timeline: each run draws one of the sampled outcomes
    ///    (bootstrap), costs its duration plus travel (plus a death penalty on failure), and
    ///    completed runs roll real loot for the enemies that sample killed. A run that would
    ///    not finish before the time runs out earns nothing.
    ///
    /// Cost is O(samples) combat simulations + O(runs) loot rolls, independent of fight length.
    /// Everything is derived from the checkpoint seed, so resolving the same checkpoint at the
    /// same time always gives the same result.
    /// </summary>
    public sealed class OfflineCalculator
    {
        private readonly LootGenerator _loot;
        private readonly OfflineSettings _settings;

        public OfflineCalculator(LootGenerator loot, OfflineSettings? settings = null)
        {
            _loot = loot;
            _settings = settings ?? OfflineSettings.Default;
        }

        public OfflineSettings Settings => _settings;

        public OfflineResult Resolve(OfflineCheckpoint checkpoint, DateTime nowUtc)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Use UTC times.", nameof(nowUtc));

            var result = new OfflineResult { Checkpoint = checkpoint, ResolvedAtUtc = nowUtc };
            var away = nowUtc - checkpoint.StartedAtUtc;
            if (away < TimeSpan.Zero)
            {
                result.ClockAnomaly = true;
                return result;
            }

            result.TimeAway = away;
            result.WasCapped = away > _settings.MaxCreditedTime;
            result.CreditedTime = result.WasCapped ? _settings.MaxCreditedTime : away;
            if (away < _settings.MinimumAbsence)
            {
                result.TooShort = true;
                result.CreditedTime = TimeSpan.Zero;
                return result;
            }

            var samples = Sample(checkpoint);
            PlayOut(checkpoint, samples, result);
            return result;
        }

        /// <summary>Detailed simulations of one pass through the map's packs.</summary>
        public IReadOnlyList<ExpeditionReport> Sample(OfflineCheckpoint checkpoint)
        {
            var runner = new ExpeditionRunner(_loot);
            return Enumerable.Range(0, _settings.SampleRuns)
                .Select(i => runner.RunMap(checkpoint.Build, checkpoint.CharacterId, checkpoint.Map, includeBoss: false,
                    Seeds.Derive(checkpoint.Seed, 10_000 + (ulong)i)))
                .ToList();
        }

        private void PlayOut(OfflineCheckpoint checkpoint, IReadOnlyList<ExpeditionReport> samples, OfflineResult result)
        {
            double budget = result.CreditedTime.TotalSeconds * _settings.EfficiencyPercent / 100.0;
            var pick = new SplitMix64Random(Seeds.Derive(checkpoint.Seed, 20_000));
            var lootRng = new SplitMix64Random(Seeds.Derive(checkpoint.Seed, 30_000));
            var expedition = checkpoint.Map.ToExpedition(includeBoss: false);
            var bonus = new LootBonus(checkpoint.Map.ItemQuantityPercent, checkpoint.Map.ItemRarityPercent);
            var loot = new List<Item>();
            var reasons = new Dictionary<DiagnosisCode, int>();
            var examples = new Dictionary<DiagnosisCode, Diagnosis>();

            double elapsed = 0;
            while (true)
            {
                var sample = samples[(int)(pick.NextDouble() * samples.Count)];
                bool success = sample.Outcome == ExpeditionOutcome.Completed;
                double cost = sample.DurationSeconds + _settings.SecondsBetweenRuns + (success ? 0 : _settings.DeathPenaltySeconds);
                if (elapsed + cost > budget) break;
                elapsed += cost;

                if (!success)
                {
                    result.RunsFailed++;
                    foreach (var d in sample.Diagnoses)
                    {
                        reasons.TryGetValue(d.Code, out var n);
                        reasons[d.Code] = n + 1;
                        if (!examples.ContainsKey(d.Code)) examples[d.Code] = d;
                    }

                    continue; // a failed run brings nothing home
                }

                result.RunsCompleted++;
                for (int e = 0; e < sample.Encounters.Count; e++)
                {
                    var encounter = expedition.Encounters[e];
                    int kills = sample.Encounters[e].Combat.Kills.Count;
                    for (int k = 0; k < kills; k++)
                    {
                        loot.AddRange(_loot.RollDrops(encounter.Enemies[k], expedition.AreaLevel, lootRng, bonus));
                    }
                }
            }

            result.Loot = loot;
            result.FailureReasons = reasons;
            result.FailureExamples = examples.Values.ToList();
        }
    }
}
