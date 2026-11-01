using System.Collections.Generic;

namespace ChaosIdle.Domain.Maps
{
    /// <summary>
    /// Which map tiers a character may run. Only defeating a map's boss in active play unlocks
    /// the next tier; offline expeditions farm unlocked maps but never unlock anything.
    /// </summary>
    public sealed class MapProgress
    {
        private readonly HashSet<string> _bossesDefeated = new HashSet<string>();

        public int HighestUnlockedTier { get; private set; } = 1;
        public IReadOnlyCollection<string> BossesDefeated => _bossesDefeated;

        /// <summary>Rebuilds progress from a save.</summary>
        public static MapProgress Restore(int highestUnlockedTier, IEnumerable<string> bossesDefeated)
        {
            var p = new MapProgress { HighestUnlockedTier = highestUnlockedTier < 1 ? 1 : highestUnlockedTier };
            foreach (var id in bossesDefeated) p._bossesDefeated.Add(id);
            return p;
        }

        public bool CanRun(MapDefinition map) => map.Tier <= HighestUnlockedTier;

        /// <summary>Records an active boss kill. Returns true if it unlocked a new tier.</summary>
        public bool RecordActiveBossKill(MapDefinition map)
        {
            _bossesDefeated.Add(map.Id);
            if (map.Tier == HighestUnlockedTier)
            {
                HighestUnlockedTier++;
                return true;
            }

            return false;
        }
    }
}
