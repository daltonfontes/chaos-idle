using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Offline;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.Cli;

/// <summary>Milestone 4: offline sessions on each map, compared with active play.</summary>
internal static class OfflineCommand
{
    public static void Run(string[] args)
    {
        double hours = args.Length > 0 ? double.Parse(args[0]) : 10;
        ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;

        var loot = new LootGenerator(ItemBases.All, Affixes.All, uniques: Uniques.All);
        var calculator = new OfflineCalculator(loot);
        var runner = new ExpeditionRunner(loot);
        var start = new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc);

        var cases = new (Func<PlayerCharacter> Make, MapInstance Map)[]
        {
            (ReferenceBuilds.IronBastion, new MapInstance { Map = Maps.SunkenCauseway }),
            (ReferenceBuilds.IronBastion, new MapInstance { Map = Maps.RimewoodHollow, Modifiers = new[] { MapModifiers.Savage, MapModifiers.Frostbitten } }),
            (ReferenceBuilds.EmberLancer, new MapInstance { Map = Maps.AshenTrail, Modifiers = new[] { MapModifiers.Hardened } }),
            (ReferenceBuilds.EmberLancer, new MapInstance { Map = Maps.RimewoodHollow, Modifiers = new[] { MapModifiers.Frostbitten, MapModifiers.Frenzied } }),
        };

        foreach (var (make, map) in cases)
        {
            var character = make();
            var checkpoint = OfflineCheckpoint.Start($"session.{character.Id}.{map.Map.Id}", character, map, start, seed);
            var result = calculator.Resolve(checkpoint, start.AddHours(hours));
            var claim = result.Claim(character.Inventory, BuildRelevance.FilterFor(character.ToBuild(), strict: true), calculator.Settings);

            Console.WriteLine();
            Console.WriteLine($"── {character.Name} on {map.Name} ──");
            foreach (var line in ReportText.Return(result, claim, $"Boss ({map.Map.Boss.Enemies[0].Name}) is fought only in active play.")) Console.WriteLine(line);

            var (active, withBoss) = RewardRates.BestActive(runner, character.ToBuild(), map, seed, 30, calculator.Settings);
            var offline = RewardRates.Offline(result);
            string how = withBoss ? $"with boss, win {active.SuccessRate:P0}" : "packs only, boss skipped";
            Console.WriteLine($"Per hour — best active: {active.ValuePerHour:0} value ({active.ItemsPerHour:0} items, {how}); " +
                              $"offline: {offline.ValuePerHour:0} value ({offline.ItemsPerHour:0} items) = {offline.ValuePerHour / active.ValuePerHour:P0} of active");
        }
    }
}
