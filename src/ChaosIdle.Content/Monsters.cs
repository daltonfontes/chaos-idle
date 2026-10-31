using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Monsters;

namespace ChaosIdle.Content
{
    public static class Monsters
    {
        /// <summary>Fast, evasive, weak: punishes low accuracy, chips through low armour.</summary>
        public static readonly EnemyDefinition RustHound = new EnemyDefinition
        {
            Id = "monster.rust_hound",
            Name = "Rust Hound",
            MaxLife = 180,
            Armour = 50,
            Evasion = 200,
            Accuracy = 400,
            ActionTimeSeconds = 0.9,
            ItemDropChance = 0.12,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.rust_hound.bite", Name = "Bite",
                    Damage = new[] { new DamageRange(DamageType.Physical, 12, 20) },
                },
            },
        };

        /// <summary>Fragile fire caster: its spells cannot be blocked, only resisted.</summary>
        public static readonly EnemyDefinition CinderWisp = new EnemyDefinition
        {
            Id = "monster.cinder_wisp",
            Name = "Cinder Wisp",
            MaxLife = 140,
            FireResistance = 40,
            Accuracy = 400,
            ActionTimeSeconds = 1.4,
            ItemDropChance = 0.15,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.cinder_wisp.ember_spit", Name = "Ember Spit", IsSpell = true,
                    Damage = new[] { new DamageRange(DamageType.Fire, 22, 32) },
                },
            },
        };

        /// <summary>Slow, armoured bruiser: punishes physical damage builds.</summary>
        public static readonly EnemyDefinition BogBrute = new EnemyDefinition
        {
            Id = "monster.bog_brute",
            Name = "Bog Brute",
            MaxLife = 520,
            Armour = 600,
            Accuracy = 350,
            ActionTimeSeconds = 1.8,
            ItemDropChance = 0.35,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.bog_brute.slam", Name = "Slam",
                    Damage = new[] { new DamageRange(DamageType.Physical, 35, 55) },
                },
            },
        };

        /// <summary>
        /// Boss. Mixes a heavy physical maul (tests armour/block/life pool) with an unblockable
        /// fire spell (tests fire resistance).
        /// </summary>
        public static readonly EnemyDefinition AshWarden = new EnemyDefinition
        {
            Id = "boss.ash_warden",
            Name = "The Ash Warden",
            IsBoss = true,
            MaxLife = 6000,
            Armour = 400,
            FireResistance = 30,
            ColdResistance = 30,
            LightningResistance = 30,
            Accuracy = 600,
            ActionTimeSeconds = 1.5,
            GuaranteedDrops = 3,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.ash_warden.cinderfall", Name = "Cinderfall", IsSpell = true, CooldownSeconds = 8,
                    Damage = new[] { new DamageRange(DamageType.Fire, 90, 130) },
                },
                new EnemyAttackDefinition
                {
                    Id = "attack.ash_warden.crushing_maul", Name = "Crushing Maul", CooldownSeconds = 6,
                    Damage = new[] { new DamageRange(DamageType.Physical, 130, 190) },
                },
                new EnemyAttackDefinition
                {
                    Id = "attack.ash_warden.ash_swipe", Name = "Ash Swipe",
                    Damage = new[] { new DamageRange(DamageType.Physical, 50, 75) },
                },
            },
        };

        public static readonly EnemyDefinition[] All = { RustHound, CinderWisp, BogBrute, AshWarden };
    }
}
