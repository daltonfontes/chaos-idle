namespace ChaosIdle.Simulation.Random
{
    public static class Seeds
    {
        /// <summary>
        /// Derives an independent seed for a numbered stream (e.g. combat vs loot for each
        /// encounter), so adding loot rolls never changes combat outcomes and vice versa.
        /// </summary>
        public static ulong Derive(ulong seed, ulong stream)
        {
            unchecked
            {
                return new SplitMix64Random(seed ^ ((stream + 1) * 0xD1B54A32D192ED03UL)).NextUInt64();
            }
        }
    }
}
