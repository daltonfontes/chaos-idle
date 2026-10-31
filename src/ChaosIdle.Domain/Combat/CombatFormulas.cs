using System;

namespace ChaosIdle.Domain.Combat
{
    /// <summary>
    /// Pure combat formulas. All constants are original to this project and tuned for
    /// readability first; see docs/combat-pipeline.md for the rationale.
    /// </summary>
    public static class CombatFormulas
    {
        /// <summary>Higher = armour is weaker against big hits.</summary>
        public const double ArmourHitFactor = 8.0;
        public const double MaxArmourReduction = 0.85;

        public const double MaxResistance = 75.0;
        public const double MinResistance = -100.0;

        /// <summary>How much one point of evasion counts against one point of accuracy.</summary>
        public const double EvasionWeight = 0.25;
        public const double MinHitChance = 0.05;

        public const double MaxBlockChance = 75.0;
        public const double MaxCritChance = 100.0;
        public const double BaseCritMultiplierPercent = 150.0;

        /// <summary>
        /// Fraction of a physical hit prevented by armour: armour / (armour + 8 * hit), capped at 85%.
        /// Armour is strong against many small hits and weak against single large hits.
        /// </summary>
        public static double ArmourReduction(double armour, double physicalHit)
        {
            if (armour <= 0 || physicalHit <= 0)
            {
                return 0;
            }

            double reduction = armour / (armour + ArmourHitFactor * physicalHit);
            return Math.Min(reduction, MaxArmourReduction);
        }

        /// <summary>
        /// Resistance after the 75% cap, then penetration. Penetration can push the value
        /// below zero (down to -100%) but never above the cap.
        /// </summary>
        public static double EffectiveResistance(double resistance, double penetration)
        {
            double capped = Math.Min(resistance, MaxResistance);
            return Clamp(capped - Math.Max(0, penetration), MinResistance, MaxResistance);
        }

        /// <summary>Chance for an attack to hit: accuracy / (accuracy + evasion * 0.25), within [5%, 100%].</summary>
        public static double HitChance(double accuracy, double evasion)
        {
            if (evasion <= 0)
            {
                return 1.0;
            }

            if (accuracy <= 0)
            {
                return MinHitChance;
            }

            return Clamp(accuracy / (accuracy + evasion * EvasionWeight), MinHitChance, 1.0);
        }

        /// <summary>Converts a percentage stat (e.g. block 40) to a probability in [0, cap/100].</summary>
        public static double PercentToChance(double percent, double capPercent) =>
            Clamp(percent, 0, capPercent) / 100.0;

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);
    }
}
