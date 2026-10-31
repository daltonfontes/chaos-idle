using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Tests;

/// <summary>Returns queued values in order, then <paramref name="fallback"/> forever.</summary>
internal sealed class FixedRandom : IRandom
{
    private readonly Queue<double> _values;
    private readonly double _fallback;

    public FixedRandom(double fallback, params double[] values)
    {
        _fallback = fallback;
        _values = new Queue<double>(values);
    }

    public int Consumed { get; private set; }

    public double NextDouble()
    {
        Consumed++;
        return _values.Count > 0 ? _values.Dequeue() : _fallback;
    }
}
