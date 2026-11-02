using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChaosIdle.Content;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.App
{
    public sealed record MapChoice(string Id, string Name, int Tier, bool Unlocked, string BossName, int AreaLevel, bool IsOfflineMap);

    public sealed record ModifierOption(string Id, string Name, string Description, string Reward);

    public sealed record MapPreview(string MapId, IReadOnlyList<string> ModifierIds, bool CanRun, IReadOnlyList<string> Lines);

    /// <summary>Map selection, modifiers, run estimates and the offline farming choice.</summary>
    public sealed partial class GameSession
    {
        /// <summary>Simulations used for the success estimate on the map screen.</summary>
        public const int EstimateRuns = 20;

        public IReadOnlyList<MapChoice> Maps()
        {
            var offline = EffectiveOfflineMap();
            return Content.Maps.All.Select(m => new MapChoice(m.Id, m.Name, m.Tier, State.Progress.CanRun(m),
                m.Boss.Enemies[0].Name, m.AreaLevel, offline.Map.Id == m.Id)).ToList();
        }

        public IReadOnlyList<ModifierOption> MapModifiers() =>
            Content.MapModifiers.All.Select(m => new ModifierOption(m.Id, m.Name, m.Description, RewardText(m))).ToList();

        /// <summary>
        /// What a run would be like: packs, boss, modifier rules, loot bonus, and an estimate from
        /// <see cref="EstimateRuns"/> quick simulations with the current build.
        /// </summary>
        public MapPreview PreviewMap(string mapId, IReadOnlyList<string> modifierIds)
        {
            var (instance, error) = BuildInstance(mapId, modifierIds);
            if (instance == null) return new MapPreview(mapId, modifierIds, false, new[] { error! });

            var map = instance.Map;
            var lines = new List<string>
            {
                $"{map.Name} — tier {map.Tier}, item level {map.AreaLevel}",
                "Packs: " + string.Join(" → ", map.Packs.Select(p => $"{p.Name} ({p.Enemies.Count})")),
                $"Boss: {map.Boss.Enemies[0].Name}",
            };

            if (instance.Modifiers.Count == 0) lines.Add("No modifiers: normal danger, normal loot.");
            foreach (var m in instance.Modifiers) lines.Add($"• {m.Name}: {m.Description} ({RewardText(m)})");
            if (instance.Modifiers.Count > 0)
                lines.Add($"Loot bonus: +{instance.ItemQuantityPercent:0}% quantity, +{instance.ItemRarityPercent:0}% rarity");

            bool unlocked = State.Progress.CanRun(map);
            if (!unlocked) lines.Add($"Locked: defeat the tier {map.Tier - 1} boss to open it.");

            var estimate = Estimate(instance);
            lines.Add($"Estimate over {EstimateRuns} simulated runs: packs cleared {estimate.Packs.ToString("P0", CultureInfo.InvariantCulture)}, " +
                      $"boss beaten {estimate.Boss.ToString("P0", CultureInfo.InvariantCulture)}.");
            if (estimate.TopReason != null) lines.Add("Main danger: " + estimate.TopReason);

            return new MapPreview(mapId, instance.Modifiers.Select(m => m.Id).ToList(), unlocked, lines);
        }

        /// <summary>Runs a map actively with the chosen modifiers (boss included).</summary>
        public IReadOnlyList<string> RunExpedition(string mapId, IReadOnlyList<string> modifierIds, DateTime nowUtc)
        {
            var (instance, error) = BuildInstance(mapId, modifierIds);
            if (instance == null) return new[] { error! };
            return RunMapInstance(instance, nowUtc);
        }

        /// <summary>Chooses the map offline sessions farm (packs only). Takes effect at the next pause.</summary>
        public string SetOfflineMap(string mapId, IReadOnlyList<string> modifierIds, DateTime nowUtc)
        {
            var (instance, error) = BuildInstance(mapId, modifierIds);
            if (instance == null) return error!;
            if (!State.Progress.CanRun(instance.Map)) return $"{instance.Map.Name} is locked, so it can't be farmed offline yet.";
            State.OfflineMap = instance;
            Save(nowUtc);
            return $"Offline farming: {instance.Name}. Bosses are never fought offline.";
        }

        /// <summary>The map offline will actually use: the chosen one if unlocked, else the highest unlocked map.</summary>
        public MapInstance EffectiveOfflineMap()
        {
            if (State.OfflineMap != null && State.Progress.CanRun(State.OfflineMap.Map)) return State.OfflineMap;
            var map = Content.Maps.All.Where(State.Progress.CanRun).OrderByDescending(m => m.Tier).First();
            return new MapInstance { Map = map };
        }

        private (MapInstance? Instance, string? Error) BuildInstance(string mapId, IReadOnlyList<string> modifierIds)
        {
            var map = Content.Maps.All.FirstOrDefault(m => m.Id == mapId);
            if (map == null) return (null, $"Unknown map {mapId}.");
            var modifiers = new List<MapModifier>();
            foreach (var id in modifierIds)
            {
                var m = Content.MapModifiers.All.FirstOrDefault(x => x.Id == id);
                if (m == null) return (null, $"Unknown map modifier {id}.");
                modifiers.Add(m);
            }

            var instance = new MapInstance { Map = map, Modifiers = modifiers };
            if (modifiers.Count > MapInstance.MaxModifiers) return (null, $"At most {MapInstance.MaxModifiers} modifiers per map.");
            if (modifiers.Select(m => m.Id).Distinct().Count() != modifiers.Count) return (null, "Each modifier can be picked once.");
            return (instance, null);
        }

        private (double Packs, double Boss, string? TopReason) Estimate(MapInstance instance)
        {
            var build = Character.ToBuild();
            int packsOk = 0, bossOk = 0;
            var reasons = new Dictionary<string, int>();
            for (int i = 0; i < EstimateRuns; i++)
            {
                var report = _runner.RunMap(build, Character.Id, instance, includeBoss: true, Seeds.Derive(0xE57, (ulong)i));
                bool packs = report.Encounters.Count == report.EncountersTotal || report.FailedEncounter?.Encounter.Enemies.Any(e => e.IsBoss) == true;
                if (packs) packsOk++;
                if (report.Outcome == ExpeditionOutcome.Completed) bossOk++;
                foreach (var d in report.Diagnoses) reasons[d.Message] = reasons.TryGetValue(d.Message, out var n) ? n + 1 : 1;
            }

            string? top = reasons.Count == 0 ? null : reasons.OrderByDescending(kv => kv.Value).First().Key;
            return ((double)packsOk / EstimateRuns, (double)bossOk / EstimateRuns, top);
        }

        private static string RewardText(MapModifier m)
        {
            var parts = new List<string>();
            if (m.ItemQuantityPercent > 0) parts.Add($"+{m.ItemQuantityPercent:0}% item quantity");
            if (m.ItemRarityPercent > 0) parts.Add($"+{m.ItemRarityPercent:0}% item rarity");
            return string.Join(", ", parts);
        }
    }
}
