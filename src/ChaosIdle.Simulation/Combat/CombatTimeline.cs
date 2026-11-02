using ChaosIdle.Domain.Combat;

namespace ChaosIdle.Simulation.Combat
{
    public enum BeatKind
    {
        /// <summary>A new enemy steps in (Label = its name).</summary>
        EnemySpawned = 0,
        /// <summary>The player's skill hits (Label = skill name).</summary>
        PlayerHit = 1,
        /// <summary>The player's skill misses (evaded by the enemy).</summary>
        PlayerMissed = 2,
        /// <summary>An enemy attack hits the player (Label = attack name).</summary>
        EnemyHit = 3,
        /// <summary>The player evades an enemy attack.</summary>
        PlayerEvaded = 4,
        /// <summary>The player blocks an enemy attack.</summary>
        PlayerBlocked = 5,
        EnemyKilled = 6,
        PlayerDied = 7,
        TimedOut = 8,
        /// <summary>The player waits for mana.</summary>
        OutOfMana = 9,
    }

    /// <summary>
    /// One moment of a fight, for replaying it on screen. Life values are after the beat.
    /// Recorded only when <see cref="SimulationOptions.RecordTimeline"/> is set.
    /// </summary>
    public sealed record CombatBeat(
        double Time,
        BeatKind Kind,
        string Label,
        double Amount,
        bool IsCritical,
        bool IsTriggered,
        DamageType Element,
        double PlayerLife,
        double EnemyLife,
        double EnemyMaxLife,
        bool EnemyIsBoss);
}
