using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Expeditions
{
    public enum ExpeditionOutcome
    {
        Completed = 0,
        Failed = 1,
    }

    public sealed record EncounterResult(Encounter Encounter, CombatReport Combat, IReadOnlyList<Item> Loot);

    public sealed class ExpeditionReport
    {
        internal ExpeditionReport(ExpeditionDefinition expedition, string characterId, ulong seed,
            IReadOnlyList<EncounterResult> encounters, int encountersTotal)
        {
            Expedition = expedition;
            CharacterId = characterId;
            Seed = seed;
            Encounters = encounters;
            EncountersTotal = encountersTotal;
        }

        public ExpeditionDefinition Expedition { get; }
        public string CharacterId { get; }
        public ulong Seed { get; }
        public IReadOnlyList<EncounterResult> Encounters { get; }
        public int EncountersTotal { get; }

        public ExpeditionOutcome Outcome =>
            Encounters.Count == EncountersTotal && Encounters.All(e => e.Combat.Outcome == CombatOutcome.Victory)
                ? ExpeditionOutcome.Completed
                : ExpeditionOutcome.Failed;

        /// <summary>The encounter that ended the expedition early, if any.</summary>
        public EncounterResult? FailedEncounter =>
            Encounters.FirstOrDefault(e => e.Combat.Outcome != CombatOutcome.Victory);

        public IReadOnlyList<Diagnosis> Diagnoses => FailedEncounter?.Combat.Diagnoses ?? Array.Empty<Diagnosis>();

        public double DurationSeconds => Encounters.Sum(e => e.Combat.DurationSeconds);

        /// <summary>Everything that dropped, including drops before a failure (they are kept).</summary>
        public IReadOnlyList<Item> Loot => Encounters.SelectMany(e => e.Loot).ToList();

        public bool IsClaimed { get; private set; }

        /// <summary>
        /// Moves loot into the inventory through a loot filter. Hidden items are never picked up;
        /// highlighted items are picked up first so a nearly full bag keeps what matters. Items
        /// that do not fit are left behind and reported. A report can be claimed once.
        /// </summary>
        public LootClaim ClaimLoot(Inventory inventory, LootFilter? filter = null)
        {
            if (IsClaimed) throw new InvalidOperationException("Expedition loot was already claimed.");
            IsClaimed = true;
            filter ??= LootFilter.ShowEverything;

            var decisions = Loot.Select(i => (Item: i, Decision: filter.Evaluate(i))).ToList();
            var hidden = decisions.Where(d => d.Decision.Action == FilterAction.Hide).Select(d => d.Item).ToList();
            var highlighted = decisions.Where(d => d.Decision.Action == FilterAction.Highlight).Select(d => d.Item).ToList();
            var shown = decisions.Where(d => d.Decision.Action == FilterAction.Show).Select(d => d.Item);

            var kept = new List<Item>();
            var leftBehind = new List<Item>();
            foreach (var item in highlighted.Concat(shown))
            {
                (inventory.TryAdd(item) ? kept : leftBehind).Add(item);
            }

            return new LootClaim(kept, leftBehind, hidden, highlighted);
        }
    }

    /// <param name="Hidden">Items the loot filter hid; never picked up.</param>
    /// <param name="Highlighted">Items the loot filter highlighted (kept or left behind).</param>
    public sealed record LootClaim(IReadOnlyList<Item> Kept, IReadOnlyList<Item> LeftBehind,
        IReadOnlyList<Item> Hidden, IReadOnlyList<Item> Highlighted);

    public sealed class ExpeditionRunner
    {
        private readonly CombatSimulator _combat = new CombatSimulator();
        private readonly LootGenerator _loot;

        public ExpeditionRunner(LootGenerator loot)
        {
            _loot = loot;
        }

        /// <summary>
        /// Runs encounters in order until one fails. Pure: does not modify the character;
        /// call <see cref="ExpeditionReport.ClaimLoot"/> to collect rewards.
        /// </summary>
        public ExpeditionReport Run(PlayerCharacter character, ExpeditionDefinition expedition, ulong seed, SimulationOptions? options = null)
        {
            if (expedition.Encounters.Count == 0) throw new ArgumentException($"{expedition.Id} has no encounters.");

            var build = character.ToBuild();
            var results = new List<EncounterResult>();
            for (int i = 0; i < expedition.Encounters.Count; i++)
            {
                var encounter = expedition.Encounters[i];
                var combat = _combat.Run(build, encounter, Seeds.Derive(seed, (ulong)(2 * i)), options);

                var lootRng = new SplitMix64Random(Seeds.Derive(seed, (ulong)(2 * i + 1)));
                var loot = new List<Item>();
                for (int k = 0; k < combat.Kills.Count; k++)
                {
                    loot.AddRange(_loot.RollDrops(encounter.Enemies[k], expedition.AreaLevel, lootRng));
                }

                results.Add(new EncounterResult(encounter, combat, loot));
                if (combat.Outcome != CombatOutcome.Victory) break;
            }

            return new ExpeditionReport(expedition, character.Id, seed, results, expedition.Encounters.Count);
        }
    }
}
