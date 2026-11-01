using ChaosIdle.Content;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;
using ChaosIdle.Simulation.Analysis;

namespace ChaosIdle.Cli;

/// <summary>Writes a save for the Iron Bastion (with a running offline session), reloads it, and reports.</summary>
internal static class SaveCommand
{
    public static void Run(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "chaos-idle-saves");
        var now = DateTime.UtcNow;

        var character = ReferenceBuilds.IronBastion();
        var state = new GameState(character, new MapProgress(), BuildRelevance.FilterFor(character.ToBuild(), strict: true));
        state.StartOffline(OfflineCheckpoint.Start("session.demo", character, new MapInstance { Map = Maps.SunkenCauseway }, now, 42));

        var store = new SaveStore(dir, "demo", ContentDatabase.Default);
        store.Save(state, now);
        store.Save(state, now);

        var result = store.Load();
        var loaded = result.Game!.State;
        Console.WriteLine($"Saved to {store.MainPath} ({new FileInfo(store.MainPath).Length:N0} bytes), backup at {store.BackupPath}");
        Console.WriteLine($"Loaded from {result.Source}: schema v{result.Envelope!.SchemaVersion}, revision {loaded.Revision}, {result.Game.Warnings.Count} warning(s)");
        Console.WriteLine($"Character {loaded.Character.Name}: {loaded.Character.Passives.PointsSpent} passives, {loaded.Character.Equipment.Items.Count} items equipped");
        Console.WriteLine($"Offline session: {loaded.Offline?.SessionId} on {loaded.Offline?.Map.Name}");
        bool same = BuildSummary.Compute(character.ToBuild()) == BuildSummary.Compute(loaded.Character.ToBuild());
        Console.WriteLine($"Character sheet identical after reload: {(same ? "yes" : "NO")}");
        if (!same) Environment.ExitCode = 1;
    }
}
