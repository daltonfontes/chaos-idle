using System.Collections.Generic;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Simulation.Diagnostics;

namespace ChaosIdle.Simulation.Combat
{
    public enum CombatOutcome
    {
        Victory = 0,
        Death = 1,

        /// <summary>The time limit ran out with the character alive.</summary>
        Timeout = 2,
    }

    public sealed record EnemyKill(string EnemyId, string EnemyName, bool IsBoss, double SpawnedAt, double KilledAt)
    {
        public double KillTime => KilledAt - SpawnedAt;
    }

    public sealed record DamageTakenEvent(double Time, string EnemyId, string AttackId, string AttackName, DamageSet Damage)
    {
        public double Total => Damage.Total;
    }

    public sealed class CombatReport
    {
        public string BuildId { get; internal set; } = "";
        public string EncounterId { get; internal set; } = "";
        public ulong Seed { get; internal set; }

        public CombatOutcome Outcome { get; internal set; }
        public double DurationSeconds { get; internal set; }
        public int EnemiesTotal { get; internal set; }
        public IReadOnlyList<EnemyKill> Kills { get; internal set; } = new List<EnemyKill>();

        /// <summary>Remaining life fraction of the enemy being fought when combat ended; 0 on victory.</summary>
        public double CurrentEnemyLifeFraction { get; internal set; }

        public double MaxLife { get; internal set; }
        public double FinalLife { get; internal set; }

        public IReadOnlyDictionary<string, double> DamageDealtBySkill { get; internal set; } = new Dictionary<string, double>();
        public double TotalDamageDealt { get; internal set; }

        /// <summary>Damage of hits that connected, before armour and resistances.</summary>
        public DamageSet DamageTakenBeforeMitigation { get; internal set; } = new DamageSet();
        public DamageSet DamageTaken { get; internal set; } = new DamageSet();
        public double PreventedByArmour { get; internal set; }
        public double PreventedByResistance { get; internal set; }
        public int HitsTaken { get; internal set; }
        public int HitsBlocked { get; internal set; }
        public int HitsEvaded { get; internal set; }

        public double LifeRegenerated { get; internal set; }
        public double LifeLeeched { get; internal set; }
        public double LifeGainedOnBlock { get; internal set; }
        public double TotalRecovery => LifeRegenerated + LifeLeeched + LifeGainedOnBlock;

        public int PrimarySkillUses { get; internal set; }
        public int TriggeredSkillUses { get; internal set; }
        public double TimeOutOfMana { get; internal set; }

        public DamageTakenEvent? KillingBlow { get; internal set; }

        /// <summary>Hits taken in the final <see cref="SimulationOptions.RecentDamageWindowSeconds"/>.</summary>
        public IReadOnlyList<DamageTakenEvent> RecentDamageTaken { get; internal set; } = new List<DamageTakenEvent>();

        public IReadOnlyList<Diagnosis> Diagnoses { get; internal set; } = new List<Diagnosis>();

        /// <summary>Kill time of the first boss defeated, if any.</summary>
        public double? BossKillTime
        {
            get
            {
                foreach (var kill in Kills)
                {
                    if (kill.IsBoss) return kill.KillTime;
                }

                return null;
            }
        }

        public double DamagePerSecond => DurationSeconds > 0 ? TotalDamageDealt / DurationSeconds : 0;
    }

    public sealed record SimulationOptions
    {
        public static readonly SimulationOptions Default = new SimulationOptions();

        public double MaxDurationSeconds { get; init; } = 300;
        public double RecentDamageWindowSeconds { get; init; } = 5;
    }
}
