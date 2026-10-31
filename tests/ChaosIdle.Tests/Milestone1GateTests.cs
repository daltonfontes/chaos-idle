using ChaosIdle.Content;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>
/// Milestone 1 gate: two configurations produce meaningfully different outcomes.
/// Bulwark trades clear speed for boss safety; Pyre trades safety for clear speed.
/// If balance changes break these, that is a design decision, not a test to loosen casually.
/// </summary>
public class Milestone1GateTests
{
    private const int Runs = 100;

    [Fact]
    public void Pyre_clears_trash_much_faster_than_bulwark()
    {
        var bulwark = BatchRunner.Run(SampleBuilds.Bulwark, Encounters.AshenRoad, 1, Runs);
        var pyre = BatchRunner.Run(SampleBuilds.Pyre, Encounters.AshenRoad, 1, Runs);

        Assert.True(bulwark.VictoryRate >= 0.95);
        Assert.True(pyre.VictoryRate >= 0.95);
        Assert.True(pyre.MedianClearTime * 2 < bulwark.MedianClearTime,
            $"pyre {pyre.MedianClearTime:0.0}s vs bulwark {bulwark.MedianClearTime:0.0}s");
    }

    [Fact]
    public void Bulwark_beats_the_boss_and_pyre_does_not()
    {
        var bulwark = BatchRunner.Run(SampleBuilds.Bulwark, Encounters.WardensHearth, 1, Runs);
        var pyre = BatchRunner.Run(SampleBuilds.Pyre, Encounters.WardensHearth, 1, Runs);

        Assert.True(bulwark.VictoryRate >= 0.9, $"bulwark won {bulwark.VictoryRate:P0}");
        Assert.True(pyre.VictoryRate <= 0.1, $"pyre won {pyre.VictoryRate:P0}");
        Assert.NotNull(bulwark.MedianBossKillTime);
    }

    [Fact]
    public void Pyre_boss_failures_are_explained_as_physical_mitigation()
    {
        var pyre = BatchRunner.Run(SampleBuilds.Pyre, Encounters.WardensHearth, 1, Runs);
        pyre.DiagnosisCounts.TryGetValue(DiagnosisCode.LowPhysicalMitigation, out var count);
        Assert.True(count >= pyre.Deaths * 0.8, $"{count} of {pyre.Deaths} deaths diagnosed as low physical mitigation");
    }
}
