using ChaosIdle.Simulation.Random;
using Xunit;

namespace ChaosIdle.Tests;

public class RandomTests
{
    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new SplitMix64Random(42);
        var b = new SplitMix64Random(42);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void Sequence_is_pinned_across_platforms()
    {
        // Reference SplitMix64 output for seed 0. If this changes, every saved seed changes meaning.
        var rng = new SplitMix64Random(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextUInt64());
    }

    [Fact]
    public void Doubles_are_in_unit_interval_and_roughly_uniform()
    {
        var rng = new SplitMix64Random(7);
        double sum = 0;
        const int n = 100_000;
        for (int i = 0; i < n; i++)
        {
            double d = rng.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999999999);
            sum += d;
        }

        Assert.InRange(sum / n, 0.49, 0.51);
    }
}
