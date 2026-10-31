using System.Collections.Generic;
using System.Linq;
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
            var damage = ScaledDamage(skill, weapon, stats, rng.NextDouble);

            // 5. Critical strike. The roll is always consumed so RNG streams stay aligned across builds.
            bool isCritical = rng.NextDouble() < CritChance(skill, weapon, stats);
            if (isCritical)
            {
                double multiplier = CritMultiplier(stats);
                foreach (var type in DamageTypes.All)
                {
                    damage[type] *= multiplier;
                }
            }

            return new OutgoingHit(damage, isCritical, skill.IsSpell);
        }

        /// <summary>
        /// Average hit before defender mitigation, with crits folded in as an expectation.
        /// Used for tooltips and item comparison; the simulator never uses it.
        /// </summary>
        public static DamageSet ExpectedHit(SkillDefinition skill, WeaponDefinition weapon, StatBlock stats)
        {
            var damage = ScaledDamage(skill, weapon, stats, () => 0.5);
            double critFactor = 1 + CritChance(skill, weapon, stats) * (CritMultiplier(stats) - 1);
            foreach (var type in DamageTypes.All)
            {
                damage[type] *= critFactor;
            }

            return damage;
        }

        /// <summary>Probability in [0, 1].</summary>
        public static double CritChance(SkillDefinition skill, WeaponDefinition weapon, StatBlock stats)
        {
            double baseCrit = skill.IsAttack ? weapon.CritChance : skill.BaseCritChance;
            double percent = (baseCrit + stats.Flat(StatId.CriticalChance)) * stats.ScalingMultiplier(StatId.CriticalChance);
            return CombatFormulas.PercentToChance(percent, CombatFormulas.MaxCritChance);
        }

        public static double CritMultiplier(StatBlock stats) =>
            (CombatFormulas.BaseCritMultiplierPercent + stats.Flat(StatId.CriticalMultiplier)) / 100.0;

        /// <summary>Mana per use: (skill cost + flat) scaled by ManaCost increased/more, never negative.</summary>
        public static double ManaCost(SkillDefinition skill, StatBlock stats) =>
            System.Math.Max(0, (skill.ManaCost + stats.Flat(StatId.ManaCost)) * stats.ScalingMultiplier(StatId.ManaCost));

        /// <summary>Steps 1-4. <paramref name="roll"/> supplies uniform values for base damage rolls.</summary>
        private static DamageSet ScaledDamage(SkillDefinition skill, WeaponDefinition weapon, StatBlock stats, System.Func<double> roll)
        {
            var damage = new DamageSet();
            double effectiveness = skill.DamageEffectiveness / 100.0;

            // 1. Base damage.
            if (skill.IsAttack)
            {
                damage[DamageType.Physical] += Lerp(weapon.PhysicalMin, weapon.PhysicalMax, roll()) * effectiveness;
            }
            else
            {
                foreach (var range in skill.BaseDamage)
                {
                    damage[range.Type] += Lerp(range.Min, range.Max, roll());
                }
            }

            // 2. Flat added damage, scaled by effectiveness.
            foreach (var type in DamageTypes.All)
            {
                damage[type] += stats.Flat(TypeStat(type)) * effectiveness;
            }

            // 3. Conversion (from pre-conversion amounts; no chaining).
            ApplyConversions(damage, EffectiveConversions(skill, stats));

            // 4. Increased (summed) then More (multiplied), by final damage type and skill tags.
            foreach (var type in DamageTypes.All)
            {
                if (damage[type] > 0)
                {
                    damage[type] *= DamageMultiplier(type, skill, stats);
                }
            }

            return damage;
        }

        /// <summary>
        /// The skill's own conversions plus conversions granted by stats (supports, items).
        /// If more than 100% of a type would be converted, every conversion from that type is
        /// scaled down proportionally so the total is exactly 100%.
        /// </summary>
        public static IReadOnlyList<DamageConversion> EffectiveConversions(SkillDefinition skill, StatBlock stats)
        {
            var all = new List<DamageConversion>(skill.Conversions);
            AddStatConversion(all, stats, StatId.PhysicalToFireConversion, DamageType.Fire);
            AddStatConversion(all, stats, StatId.PhysicalToColdConversion, DamageType.Cold);
            AddStatConversion(all, stats, StatId.PhysicalToLightningConversion, DamageType.Lightning);

            var result = new List<DamageConversion>(all.Count);
            foreach (var group in all.GroupBy(c => c.From))
            {
                double total = group.Sum(c => c.Percent);
                double scale = total > 100 ? 100 / total : 1;
                foreach (var c in group)
                {
                    result.Add(scale == 1 ? c : c with { Percent = c.Percent * scale });
                }
            }

            return result;
        }

        private static void AddStatConversion(List<DamageConversion> list, StatBlock stats, StatId stat, DamageType to)
        {
            double percent = stats.Flat(stat);
            if (percent > 0) list.Add(new DamageConversion(DamageType.Physical, to, percent));
        }

        /// <summary>Cooldown after CooldownRecovery scaling.</summary>
        public static double Cooldown(SkillDefinition skill, StatBlock stats) =>
            skill.CooldownSeconds / stats.ScalingMultiplier(StatId.CooldownRecovery);

        public static DamageSet RollEnemyHit(EnemyAttackDefinition attack, IRandom rng)
        {
            var damage = new DamageSet();
            foreach (var range in attack.Damage)
            {
                damage[range.Type] += Lerp(range.Min, range.Max, rng.NextDouble());
            }

            return damage;
        }

        /// <summary>Steps 6-9: apply the defender's avoidance and mitigation.</summary>
        public static HitResult Resolve(DamageSet incoming, bool isSpell, double attackerAccuracy, double penetration, DefenceStats defender, IRandom rng)
        {
            // Both rolls are always consumed, whatever the outcome, for stable RNG streams.
            double evadeRoll = rng.NextDouble();
            double blockRoll = rng.NextDouble();

            // 6. Evasion (attacks only).
            if (!isSpell && evadeRoll >= CombatFormulas.HitChance(attackerAccuracy, defender.Evasion))
            {
                return new HitResult(HitOutcome.Evaded, incoming, new DamageSet(), 0, 0);
            }

            // 7. Block (attacks; spells too if the defender has BlockSpells): the hit is fully negated.
            if ((!isSpell || defender.BlockSpells)
                && blockRoll < CombatFormulas.PercentToChance(defender.BlockChance, CombatFormulas.MaxBlockChance))
            {
                return new HitResult(HitOutcome.Blocked, incoming, new DamageSet(), 0, 0);
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

        private static double Lerp(double min, double max, double t) => min + (max - min) * t;
    }
}
