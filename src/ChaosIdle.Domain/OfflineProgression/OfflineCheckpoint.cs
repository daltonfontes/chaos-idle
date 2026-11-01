using System;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Maps;

namespace ChaosIdle.Domain.OfflineProgression
{
    public sealed record OfflineSettings
    {
        public static readonly OfflineSettings Default = new OfflineSettings();

        /// <summary>Absences longer than this are credited as exactly this long.</summary>
        public TimeSpan MaxCreditedTime { get; init; } = TimeSpan.FromHours(8);

        /// <summary>Shorter absences earn nothing (avoids app-switch farming and noise).</summary>
        public TimeSpan MinimumAbsence { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>Offline time counts at this rate compared with active play.</summary>
        public double EfficiencyPercent { get; init; } = 60;

        /// <summary>Walking between packs, looting, resetting.</summary>
        public double SecondsBetweenRuns { get; init; } = 15;

        /// <summary>Extra time lost after a failed run (recovering, returning).</summary>
        public double DeathPenaltySeconds { get; init; } = 60;

        /// <summary>Most items an offline session can bring back.</summary>
        public int StorageCap { get; init; } = 60;

        /// <summary>Detailed simulations used to estimate the whole session.</summary>
        public int SampleRuns { get; init; } = 24;
    }

    /// <summary>
    /// Everything needed to resolve an offline session later, frozen at the moment it starts:
    /// a snapshot of the build (gear changes afterwards do not apply), the map, the seed and
    /// the start time. A checkpoint can be claimed once.
    /// </summary>
    public sealed record OfflineCheckpoint
    {
        public string SessionId { get; init; } = "";
        public string CharacterId { get; init; } = "";

        /// <summary>The character's loadout when the session started (saved with the session).</summary>
        public PlayerCharacter? Loadout { get; init; }

        public CharacterBuild Build { get; init; } = new CharacterBuild();
        public MapInstance Map { get; init; } = new MapInstance();
        public DateTime StartedAtUtc { get; init; }
        public ulong Seed { get; init; }

        public static OfflineCheckpoint Start(string sessionId, PlayerCharacter character, MapInstance map, DateTime nowUtc, ulong seed)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Use UTC times.", nameof(nowUtc));
            var errors = map.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            var frozen = character.CloneLoadout();
            return new OfflineCheckpoint
            {
                SessionId = sessionId,
                CharacterId = character.Id,
                Loadout = frozen,
                Build = frozen.ToBuild(),
                Map = map,
                StartedAtUtc = nowUtc,
                Seed = seed,
            };
        }
    }
}
