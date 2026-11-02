using System;
using System.Threading.Tasks;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// CI-only: opens the real main screen, plays one expedition, and saves a PNG of each tab.
/// Run with a display (e.g. xvfb) and the Compatibility renderer. The output folder comes
/// from the SCREENSHOT_DIR environment variable (default user://screenshots).
/// </summary>
public partial class Screenshots : Node
{
    public override async void _Ready()
    {
        int code = 0;
        try
        {
            string dir = System.Environment.GetEnvironmentVariable("SCREENSHOT_DIR") ?? ProjectSettings.GlobalizePath("user://screenshots");
            System.IO.Directory.CreateDirectory(dir);

            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await Frames(5);

            main.PlayFirstUnlocked();
            main.ShowTab(0);
            main.FreezeFight(boss: false, secondsIn: 3);
            await Capture(dir, "1-adventure.png");

            main.FreezeFight(boss: true, secondsIn: 12);
            await Capture(dir, "6-fight-boss.png");

            main.ShowTab(2);
            await Frames(2);
            main.PreviewPassive("pas.key.bastion");
            await Capture(dir, "2-passives.png");

            main.ShowTab(3);
            await Frames(2);
            main.PreviewSupport("skill.iron_cleave", "sup.heavy_impact");
            await Capture(dir, "3-skills.png");

            main.ShowTab(1);
            await Frames(2);
            main.ChooseMap("map.sunken_causeway", "mapmod.savage", "mapmod.hardened");
            await Capture(dir, "4-maps.png");

            main.ShowTab(4);
            await Frames(2);
            main.SetLootFilter(ChaosIdle.App.FilterPreset.Strict, ChaosIdle.Domain.Items.ItemRarity.Magic);
            await Capture(dir, "5-loot.png");

            GD.Print("SCREENSHOTS OK: " + dir);
        }
        catch (Exception e)
        {
            GD.PrintErr("SCREENSHOTS FAILED: " + e);
            code = 1;
        }

        GetTree().Quit(code);
    }

    private async Task Capture(string dir, string name)
    {
        await Frames(4);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(System.IO.Path.Combine(dir, name));
        if (error != Error.Ok) throw new InvalidOperationException($"Saving {name} failed: {error}");
        GD.Print($"saved {name} ({image.GetWidth()}x{image.GetHeight()})");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
