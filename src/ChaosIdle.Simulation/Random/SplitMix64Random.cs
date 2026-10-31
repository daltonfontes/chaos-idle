namespace ChaosIdle.Simulation.Random
{
    /// <summary>
    /// SplitMix64: tiny, fast, and fully specified, so a seed produces the same sequence on
    /// .NET, Mono/IL2CPP (Unity), and a future server. System.Random gives no such guarantee.
    /// </summary>
    public sealed class SplitMix64Random : IRandom
    {
        private ulong _state;

        public SplitMix64Random(ulong seed)
        {
            _state = seed;
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }
}
