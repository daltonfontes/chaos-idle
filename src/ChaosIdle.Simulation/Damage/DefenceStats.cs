using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Simulation.Damage
{
    public sealed record DefenceStats
    {
        public double Armour { get; init; }
        public double Evasion { get; init; }

        /// <summary>Percent, before the 75% cap.</summary>
        public double BlockChance { get; init; }

        public double FireResistance { get; init; }
        public double ColdResistance { get; init; }
        public double LightningResistance { get; init; }

        public double Resistance(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return FireResistance;
                case DamageType.Cold: return ColdResistance;
                case DamageType.Lightning: return LightningResistance;
                default: return 0;
            }
        }

        public static DefenceStats From(StatBlock stats) => new DefenceStats
        {
            Armour = stats.Resolve(StatId.Armour),
            Evasion = stats.Resolve(StatId.Evasion),
            BlockChance = stats.Flat(StatId.BlockChance),
            FireResistance = stats.Flat(StatId.FireResistance),
            ColdResistance = stats.Flat(StatId.ColdResistance),
            LightningResistance = stats.Flat(StatId.LightningResistance),
        };

        public static DefenceStats From(EnemyDefinition enemy) => new DefenceStats
        {
            Armour = enemy.Armour,
            Evasion = enemy.Evasion,
            FireResistance = enemy.FireResistance,
            ColdResistance = enemy.ColdResistance,
            LightningResistance = enemy.LightningResistance,
        };
    }
}
