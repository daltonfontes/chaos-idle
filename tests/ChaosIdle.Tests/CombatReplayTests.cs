using ChaosIdle.App;
using ChaosIdle.Content;
using ChaosIdle.Simulation.Combat;
using Xunit;

namespace ChaosIdle.Tests;

public class CombatReplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chaos-idle-replay-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 7, 3, 18, 0, 0, DateTimeKind.Utc);
    private ulong _seed = 1;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    public void Recording_a_timeline_does_not_change_the_fight(ulong seed)
    {
        var build = StarterCharacters.NewGuardian().ToBuild();
        var plain = new CombatSimulator().Run(build, Encounters.AshenRoad, seed);
        var recorded = new CombatSimulator().Run(build, Encounters.AshenRoad, seed, new SimulationOptions { RecordTimeline = true });

        Assert.Empty(plain.Timeline);
        Assert.NotEmpty(recorded.Timeline);
        Assert.Equal(plain.Outcome, recorded.Outcome);
        Assert.Equal(plain.DurationSeconds, recorded.DurationSeconds);
        Assert.Equal(plain.TotalDamageDealt, recorded.TotalDamageDealt);
        Assert.Equal(plain.FinalLife, recorded.FinalLife);
    }

    [Fact]
    public void Timeline_matches_the_report()
    {
        var build = StarterCharacters.NewGuardian().ToBuild();
        var r = new CombatSimulator().Run(build, Encounters.AshenRoad, 3, new SimulationOptions { RecordTimeline = true });
        var t = r.Timeline;

        Assert.Equal(BeatKind.EnemySpawned, t[0].Kind);
        Assert.Equal(r.Kills.Count, t.Count(b => b.Kind == BeatKind.EnemyKilled));
        Assert.Equal(r.HitsTaken, t.Count(b => b.Kind == BeatKind.EnemyHit));
        Assert.Equal(r.HitsBlocked, t.Count(b => b.Kind == BeatKind.PlayerBlocked));
        Assert.Equal(r.HitsEvaded, t.Count(b => b.Kind == BeatKind.PlayerEvaded));
        Assert.Equal(r.TotalDamageDealt, t.Where(b => b.Kind == BeatKind.PlayerHit).Sum(b => b.Amount), 6);
        Assert.Equal(r.FinalLife, t[^1].PlayerLife, 6);
        Assert.True(t.Zip(t.Skip(1)).All(p => p.First.Time <= p.Second.Time), "beats are in time order");
        Assert.All(t, b => Assert.InRange(b.EnemyLife, 0, b.EnemyMaxLife));
    }

    [Fact]
    public void Session_keeps_the_last_map_run_as_a_replay()
    {
        var s = GameSession.LoadOrCreate(_dir, Now, () => _seed++);
        Assert.Null(s.LastFight);

        s.RunExpedition(Maps.SunkenCauseway.Id, Now);
        var fight = s.LastFight!;

        Assert.Equal(Maps.SunkenCauseway.Name, fight.MapName);
        Assert.Equal("Guardian", fight.ClassName);
        Assert.True(fight.HeroMaxLife > 0);
        Assert.NotEmpty(fight.Stages);
        Assert.StartsWith("Pack 1 of", fight.Stages[0].Title);
        Assert.Equal(0, fight.Stages[0].StartTime);
        Assert.All(fight.Stages, st => Assert.Contains(fight.Beats, b => b.Kind == BeatKind.EnemySpawned && b.Time == st.StartTime));
        Assert.InRange(fight.Beats[^1].Time, 0, fight.DurationSeconds);
        Assert.Contains(fight.Outcome, new[] { "Map cleared", "Defeated", "Out of time" });
        if (fight.Outcome == "Map cleared") Assert.True(fight.Stages[^1].IsBoss);
    }
}
