using System;
using System.IO;
using System.Linq;
using ChaosIdle.App;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// Headless check run by CI inside the real Godot runtime: start a game, run an expedition,
/// background the app, relaunch it, and come back hours later. Exit code 0 means it all worked.
/// </summary>
public partial class Smoke : Node
{
    public override void _Ready()
    {
        int code = 0;
        string dir = ProjectSettings.GlobalizePath("user://smoke-" + DateTime.UtcNow.Ticks);
        try
        {
            var now = DateTime.UtcNow;
            var session = GameSession.LoadOrCreate(dir, now);
            Check(session.CharacterSheet()[0].StartsWith("Guardian"), "character sheet");

            var report = session.RunExpedition(session.Expeditions().First(e => e.Unlocked).Id, now);
            GD.Print(string.Join("\n", report));
            Check(report.Count > 2, "expedition report");

            session.OnPause(now);
            var relaunched = GameSession.LoadOrCreate(dir, now.AddMinutes(1));
            var welcome = relaunched.OnResume(now.AddHours(3));
            GD.Print(string.Join("\n", welcome));
            Check(welcome.Count > 0 && welcome[0].StartsWith("Welcome back."), "offline return");
            Check(relaunched.Inventory().Count > 0, "offline loot in the bag");
            GD.Print("SMOKE OK");
        }
        catch (Exception e)
        {
            GD.PrintErr("SMOKE FAILED: " + e);
            code = 1;
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        GetTree().Quit(code);
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException("Check failed: " + what);
    }
}
