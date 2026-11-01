using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Offline
{
    /// <summary>
    /// A rough, documented loot value for balancing and telemetry only (no economy yet):
    /// normal 1, magic 3, rare 10, unique 40.
    /// </summary>
    public static class LootValue
    {
        public static double Of(Item item)
        {
            switch (item.Rarity)
            {
                case ItemRarity.Magic: return 3;
                case ItemRarity.Rare: return 10;
                case ItemRarity.Unique: return 40;
                default: return 1;
            }
        }

        public static double Of(IEnumerable<Item> items) => items.Sum(Of);
    }

    public sealed record RewardRate(double RunsPerHour, double ItemsPerHour, double ValuePerHour, double SuccessRate);

    public static class RewardRates
    {
        /// <summary>
        /// The better of the two ways to play a map actively: packs only, or packs plus boss.
        /// A real player skips a boss they cannot beat, so comparisons with offline use this.
        /// </summary>
        public static (RewardRate Rate, bool WithBoss) BestActive(ExpeditionRunner runner, CharacterBuild build, MapInstance map, ulong seed, int runs, OfflineSettings settings)
        {
            var withBoss = Active(runner, build, map, seed, runs, settings, includeBoss: true);
            var packs = Active(runner, build, map, seed, runs, settings, includeBoss: false);
            return withBoss.ValuePerHour >= packs.ValuePerHour ? (withBoss, true) : (packs, false);
        }

        /// <summary>
        /// Active play on a map with the same travel and death costs as offline but at full
        /// efficiency. Loot from kills before a failure is kept, as in expeditions.
        /// </summary>
        public static RewardRate Active(ExpeditionRunner runner, CharacterBuild build, MapInstance map, ulong seed, int runs, OfflineSettings settings,
            bool includeBoss = true)
        {
            double seconds = 0, value = 0;
            int items = 0, wins = 0;
            for (int i = 0; i < runs; i++)
            {
                var r = runner.RunMap(build, build.Id, map, includeBoss, Seeds.Derive(seed, (ulong)i));
                bool won = r.Outcome == ExpeditionOutcome.Completed;
                seconds += r.DurationSeconds + settings.SecondsBetweenRuns + (won ? 0 : settings.DeathPenaltySeconds);
                items += r.Loot.Count;
                value += LootValue.Of(r.Loot);
                if (won) wins++;
            }

            double hours = seconds / 3600.0;
            return new RewardRate(runs / hours, items / hours, value / hours, (double)wins / runs);
        }

        /// <summary>Per credited hour of absence (efficiency already applied by the calculator).</summary>
        public static RewardRate Offline(OfflineResult result)
        {
            double hours = result.CreditedTime.TotalHours;
            if (hours <= 0) return new RewardRate(0, 0, 0, 0);
            return new RewardRate(result.RunsAttempted / hours, result.Loot.Count / hours, LootValue.Of(result.Loot) / hours, result.SuccessRate);
        }
    }
}
