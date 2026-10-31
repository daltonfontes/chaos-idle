using System.Collections.Generic;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Damage
{
    public sealed class OutgoingHit
    {
        public OutgoingHit(DamageSet damage, bool isCritical, bool isSpell)
        {
            Damage = damage;
            IsCritical = isCritical;
            IsSpell = isSpell;
        }

        public DamageSet Damage { get; }
        public bool IsCritical { get; }
        public bool IsSpell { get; }
    }

    public enum HitOutcome
    {
        Hit = 0,
        Evaded = 1,
        Blocked = 2,
    }

    public sealed class HitResult
    {
        public HitResult(HitOutcome outcome, DamageSet incoming, DamageSet taken, double preventedByArmour, double preventedByResistance)
        {
            Outcome = outcome;
            Incoming = incoming;
            Taken = taken;
            PreventedByArmour = preventedByArmour;
            PreventedByResistance = preventedByResistance;
        }

        public HitOutcome Outcome { get; }

        /// <summary>Damage before defender mitigation.</summary>
        public DamageSet Incoming { get; }

        /// <summary>Damage after mitigation; all zero when evaded or blocked.</summary>
        public DamageSet Taken { get; }

        public double PreventedByArmour { get; }

        /// <summary>Negative when the defender's resistance is below zero.</summary>
        public double PreventedByResistance { get; }
    }

    /// <summary>
    /// The single place hits are calculated. Order is documented in docs/combat-pipeline.md;
    /// changing it is a balance change and must update that document and its tests.
    /// </summary>
    public static class DamagePipeline
    {
        /// <summary>Steps 1-5: roll an outgoing player hit.</summary>
        public static OutgoingHit RollPlayerHit(SkillDefinition skill, WeaponDefinition weapon, StatBlock stats, IRandom rng)
        {
            var damage = new DamageSet();
            double effectiveness = skill.DamageEffectiveness / 100.0;

            // 1. Base damage.
            if (skill.IsAttack)
            {
                damage[DamageType.Physical] += Roll(weapon.PhysicalMin, weapon.PhysicalMax, rng) * effectiveness;
            }
            else
            {
                foreach (var range in skill.BaseDamage)
                {
                    damage[range.Type] += Roll(range.Min, range.Max, rng);
                }
            }

            // 2. Flat added damage, scaled by effectiveness.
            foreach (var type in DamageTypes.All)
            {
                damage[type] += stats.Flat(TypeStat(type)) * effectiveness;
            }

            // 3. Conversion (from pre-conversion amounts; no chaining).
            ApplyConversions(damage, skill.Conversions);

            // 4. Increased (summed) then More (multiplied), by final damage type and skill tags.
            foreach (var type in DamageTypes.All)
            {
                if (damage[type] > 0)
                {
                    damage[type] *= DamageMultiplier(type, skill, stats);
                }
            }

            // 5. Critical strike. The roll is always consumed so RNG streams stay aligned across builds.
            double baseCrit = skill.IsAttack ? weapon.CritChance : skill.BaseCritChance;
            double critPercent = (baseCrit + stats.Flat(StatId.CriticalChance)) * stats.ScalingMultiplier(StatId.CriticalChance);
            bool isCritical = rng.NextDouble() < CombatFormulas.PercentToChance(critPercent, CombatFormulas.MaxCritChance);
            if (isCritical)
            {
                double multiplier = (CombatFormulas.BaseCritMultiplierPercent + stats.Flat(StatId.CriticalMultiplier)) / 100.0;
                foreach (var type in DamageTypes.All)
                {
                    damage[type] *= multiplier;
                }
            }

            return new OutgoingHit(damage, isCritical, skill.IsSpell);
        }

        public static DamageSet RollEnemyHit(EnemyAttackDefinition attack, IRandom rng)
        {
            var damage = new DamageSet();
            foreach (var range in attack.Damage)
            {
                damage[range.Type] += Roll(range.Min, range.Max, rng);
            }

            return damage;
        }

        /// <summary>Steps 6-9: apply the defender's avoidance and mitigation.</summary>
        public static HitResult Resolve(DamageSet incoming, bool isSpell, double attackerAccuracy, double penetration, DefenceStats defender, IRandom rng)
        {
            // Both rolls are always consumed, whatever the outcome, for stable RNG streams.
            double evadeRoll = rng.NextDouble();
            double blockRoll = rng.NextDouble();

            if (!isSpell)
            {
                // 6. Evasion (attacks only).
                if (evadeRoll >= CombatFormulas.HitChance(attackerAccuracy, defender.Evasion))
                {
                    return new HitResult(HitOutcome.Evaded, incoming, new DamageSet(), 0, 0);
                }

                // 7. Block (attacks only): the hit is fully negated.
                if (blockRoll < CombatFormulas.PercentToChance(defender.BlockChance, CombatFormulas.MaxBlockChance))
                {
                    return new HitResult(HitOutcome.Blocked, incoming, new DamageSet(), 0, 0);
                }
            }

            var taken = new DamageSet();
            double armourPrevented = 0;
            double resistPrevented = 0;
            foreach (var type in DamageTypes.All)
            {
                double amount = incoming[type];
                if (amount <= 0)
                {
                    continue;
                }

                if (type == DamageType.Physical)
                {
                    // 8. Armour (physical only).
                    double reduction = CombatFormulas.ArmourReduction(defender.Armour, amount);
                    armourPrevented += amount * reduction;
                    taken[type] = amount * (1 - reduction);
                }
                else
                {
                    // 9. Resistance after cap, minus penetration.
                    double resistance = CombatFormulas.EffectiveResistance(defender.Resistance(type), penetration);
                    resistPrevented += amount * resistance / 100.0;
                    taken[type] = amount * (1 - resistance / 100.0);
                }
            }

            return new HitResult(HitOutcome.Hit, incoming, taken, armourPrevented, resistPrevented);
        }

        public static StatId TypeStat(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return StatId.FireDamage;
                case DamageType.Cold: return StatId.ColdDamage;
                case DamageType.Lightning: return StatId.LightningDamage;
                default: return StatId.PhysicalDamage;
            }
        }

        /// <summary>(1 + sum of relevant Increased) * product of relevant More.</summary>
        public static double DamageMultiplier(DamageType type, SkillDefinition skill, StatBlock stats)
        {
            var relevant = new List<StatId> { StatId.Damage, TypeStat(type) };
            if (type.IsElemental()) relevant.Add(StatId.ElementalDamage);
            if (skill.IsAttack) relevant.Add(StatId.AttackDamage);
            if (skill.IsSpell) relevant.Add(StatId.SpellDamage);

            double increased = 0;
            double more = 1;
            foreach (var stat in relevant)
            {
                increased += stats.IncreasedPercent(stat);
                more *= stats.MoreMultiplier(stat);
            }

            return System.Math.Max(0, 1 + increased / 100.0) * more;
        }

        public static void ApplyConversions(DamageSet damage, IReadOnlyList<DamageConversion> conversions)
        {
            if (conversions.Count == 0)
            {
                return;
            }

            var original = damage.Clone();
            foreach (var c in conversions)
            {
                double moved = original[c.From] * c.Percent / 100.0;
                damage[c.From] -= moved;
                damage[c.To] += moved;
            }
        }

        /// <summary>Seconds between uses of the primary skill.</summary>
        public static double ActionTime(SkillDefinition skill, WeaponDefinition weapon, StatBlock stats)
        {
            if (skill.IsAttack)
            {
                double rate = weapon.AttacksPerSecond * stats.ScalingMultiplier(StatId.AttackSpeed);
                return 1.0 / rate;
            }

            return skill.CastTimeSeconds / stats.ScalingMultiplier(StatId.CastSpeed);
        }

        private static double Roll(double min, double max, IRandom rng) => min + (max - min) * rng.NextDouble();
    }
}
