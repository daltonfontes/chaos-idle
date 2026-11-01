using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;

namespace ChaosIdle.Domain.Profile
{
    /// <summary>
    /// Everything that is saved for one character slot. The save format maps exactly this.
    /// </summary>
    public sealed class GameState
    {
        /// <summary>How many claimed offline session IDs are remembered (oldest dropped first).</summary>
        public const int ClaimedSessionMemory = 100;

        private readonly List<string> _claimedSessions = new List<string>();

        public GameState(PlayerCharacter character, MapProgress progress, LootFilter lootFilter)
        {
            Character = character;
            Progress = progress;
            LootFilter = lootFilter;
        }

        public PlayerCharacter Character { get; }
        public MapProgress Progress { get; }
        public LootFilter LootFilter { get; set; }

        /// <summary>The offline session running while the player is away, if any.</summary>
        public OfflineCheckpoint? Offline { get; private set; }

        /// <summary>Incremented on every save; later used to detect sync conflicts.</summary>
        public long Revision { get; set; }

        public IReadOnlyList<string> ClaimedSessions => _claimedSessions;

        public bool StartOffline(OfflineCheckpoint checkpoint)
        {
            if (Offline != null || _claimedSessions.Contains(checkpoint.SessionId)) return false;
            Offline = checkpoint;
            return true;
        }

        /// <summary>
        /// Marks the running session as claimed and clears it. Returns false if this session was
        /// already claimed (e.g. a stale save was restored), so its rewards must not be paid.
        /// </summary>
        public bool CompleteOffline(string sessionId)
        {
            if (_claimedSessions.Contains(sessionId)) return false;
            if (Offline?.SessionId == sessionId) Offline = null;
            _claimedSessions.Add(sessionId);
            while (_claimedSessions.Count > ClaimedSessionMemory) _claimedSessions.RemoveAt(0);
            return true;
        }

        public bool IsClaimed(string sessionId) => _claimedSessions.Contains(sessionId);

        /// <summary>Used by the save loader only.</summary>
        public void Restore(OfflineCheckpoint? offline, IEnumerable<string> claimedSessions)
        {
            Offline = offline;
            _claimedSessions.Clear();
            _claimedSessions.AddRange(claimedSessions.Distinct().Take(ClaimedSessionMemory));
        }
    }
}
