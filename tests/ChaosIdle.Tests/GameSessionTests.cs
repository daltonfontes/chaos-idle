using ChaosIdle.App;
using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Persistence;
using Xunit;

namespace ChaosIdle.Tests;

public class GameSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chaos-idle-session-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 7, 1, 18, 0, 0, DateTimeKind.Utc);
    private ulong _seed = 1;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private GameSession Open() => GameSession.LoadOrCreate(_dir, Now, () => _seed++);

    [Fact]
    public void A_new_player_gets_a_saved_starter_guardian()
    {
        var s = Open();
        Assert.Contains("new Guardian", s.LoadNotices[0]);
        Assert.Equal(StarterCharacters.StartingPassivePoints, s.Character.Passives.PointsSpent);
        Assert.True(File.Exists(Path.Combine(_dir, GameSession.Slot + ".json")));
        Assert.StartsWith("Guardian — Iron Cleave", s.CharacterSheet()[0]);
    }

    [Fact]
    public void Only_unlocked_tiers_can_be_run()
    {
        var s = Open();
        var choices = s.Expeditions();
        Assert.True(choices.Single(c => c.Tier == 1).Unlocked);
        Assert.False(choices.Single(c => c.Tier == 2).Unlocked);
        Assert.Contains("locked", s.RunExpedition(Maps.AshenTrail.Id, Now)[0]);
    }

    [Fact]
    public void Running_an_expedition_reports_claims_and_saves()
    {
        var s = Open();
        var lines = s.RunExpedition(Maps.SunkenCauseway.Id, Now);

        Assert.StartsWith("Sunken Causeway:", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Added "));
        var reloaded = Open();
        Assert.Equal(s.State.Revision, reloaded.State.Revision);
        Assert.Equal(s.Character.Inventory.Items.Select(i => i.Id), reloaded.Character.Inventory.Items.Select(i => i.Id));
    }

    [Fact]
    public void Inventory_actions_describe_equip_and_discard()
    {
        var s = Open();
        var ring = ItemTests.Make(ItemBases.IronBand, ("affix.life", 30)) with { Id = "ring-1" };
        var cap = ItemTests.Make(ItemBases.IronCap) with { Id = "cap-1" };
        s.Character.Inventory.TryAdd(ring);
        s.Character.Inventory.TryAdd(cap);

        var rows = s.Inventory();
        Assert.Equal("Upgrade", rows.Single(r => r.ItemId == "ring-1").Verdict);
        Assert.Contains(s.Describe("ring-1"), l => l.Contains("UPGRADE"));

        Assert.Equal("Equipped Iron Band in Ring1.", s.Equip("ring-1", Now));
        Assert.Same(ring, s.Character.Equipment[EquipmentSlot.Ring1]);
        Assert.Equal("Discarded Iron Cap.", s.Discard("cap-1", Now));
        Assert.Empty(s.Character.Inventory.Items);
        Assert.Throws<ArgumentException>(() => s.Describe("missing"));
    }

    [Fact]
    public void Pausing_and_resuming_runs_local_offline_progress()
    {
        var s = Open();
        s.OnPause(Now);
        Assert.NotNull(s.State.Offline);
        s.OnPause(Now.AddSeconds(5)); // repeated pause events keep the same session
        var session = s.State.Offline!.SessionId;

        Assert.Empty(s.OnResume(Now.AddSeconds(20))); // a quick app switch is not a return
        Assert.Equal(session, s.State.Offline!.SessionId);

        var lines = s.OnResume(Now.AddHours(3));
        Assert.StartsWith("Welcome back.", lines[0]);
        Assert.Null(s.State.Offline);
        Assert.True(s.State.IsClaimed(session));
        Assert.NotEmpty(s.Character.Inventory.Items);
        Assert.Empty(s.OnResume(Now.AddHours(4)));
    }

    [Fact]
    public void Offline_progress_survives_the_app_being_killed()
    {
        Open().OnPause(Now);
        var restarted = Open(); // process killed while in the background, then relaunched
        Assert.NotNull(restarted.State.Offline);
        Assert.StartsWith("Welcome back.", restarted.OnResume(Now.AddHours(2))[0]);
    }

    [Fact]
    public void An_unreadable_save_is_never_overwritten()
    {
        Open();
        File.WriteAllText(Path.Combine(_dir, GameSession.Slot + ".json"), "garbage");
        File.WriteAllText(Path.Combine(_dir, GameSession.Slot + ".json.bak"), "garbage");
        Assert.Throws<SaveLoadException>(() => Open());
        Assert.Equal("garbage", File.ReadAllText(Path.Combine(_dir, GameSession.Slot + ".json")));
    }
}
