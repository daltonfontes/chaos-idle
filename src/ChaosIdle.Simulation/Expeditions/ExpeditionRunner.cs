using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Items;
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
        /// Moves loot into the inventory. Items that do not fit are left behind and reported.
        /// A report can be claimed once.
        /// </summary>
        public LootClaim ClaimLoot(Inventory inventory)
        {
            if (IsClaimed) throw new InvalidOperationException("Expedition loot was already claimed.");
            IsClaimed = true;

            var kept = new List<Item>();
            var leftBehind = new List<Item>();
            foreach (var item in Loot)
            {
                (inventory.TryAdd(item) ? kept : leftBehind).Add(item);
            }

            return new LootClaim(kept, leftBehind);
        }
    }

    public sealed record LootClaim(IReadOnlyList<Item> Kept, IReadOnlyList<Item> LeftBehind);

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
