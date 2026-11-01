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

        /// <summary>Cold caster: unblockable spells that punish low cold resistance.</summary>
        public static readonly EnemyDefinition FrostboundSeer = new EnemyDefinition
        {
            Id = "monster.frostbound_seer",
            Name = "Frostbound Seer",
            MaxLife = 220,
            ColdResistance = 40,
            Accuracy = 450,
            ActionTimeSeconds = 1.5,
            ItemDropChance = 0.18,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.frostbound_seer.rime_bolt", Name = "Rime Bolt", IsSpell = true,
                    Damage = new[] { new DamageRange(DamageType.Cold, 30, 44) },
                },
            },
        };

        /// <summary>Very fast, evasive lightning attacker: many small hits.</summary>
        public static readonly EnemyDefinition StormhideRunner = new EnemyDefinition
        {
            Id = "monster.stormhide_runner",
            Name = "Stormhide Runner",
            MaxLife = 200,
            Evasion = 300,
            LightningResistance = 30,
            Accuracy = 500,
            ActionTimeSeconds = 0.6,
            ItemDropChance = 0.15,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.stormhide_runner.static_rake", Name = "Static Rake",
                    Damage = new[] { new DamageRange(DamageType.Lightning, 10, 18) },
                },
            },
        };

        /// <summary>Tier 1 boss: slow, armoured, telegraphed physical crushes.</summary>
        public static readonly EnemyDefinition BogTyrant = new EnemyDefinition
        {
            Id = "boss.bog_tyrant",
            Name = "The Bog Tyrant",
            IsBoss = true,
            MaxLife = 2500,
            Armour = 500,
            Accuracy = 450,
            ActionTimeSeconds = 2.0,
            GuaranteedDrops = 2,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.bog_tyrant.mire_crush", Name = "Mire Crush", CooldownSeconds = 6,
                    Damage = new[] { new DamageRange(DamageType.Physical, 70, 100) },
                },
                new EnemyAttackDefinition
                {
                    Id = "attack.bog_tyrant.sludge_swipe", Name = "Sludge Swipe",
                    Damage = new[] { new DamageRange(DamageType.Physical, 30, 45) },
                },
            },
        };

        /// <summary>Tier 3 boss: cold spells and mixed cold/physical lashes.</summary>
        public static readonly EnemyDefinition RimeheartMatron = new EnemyDefinition
        {
            Id = "boss.rimeheart_matron",
            Name = "The Rimeheart Matron",
            IsBoss = true,
            MaxLife = 9000,
            Armour = 300,
            FireResistance = 20,
            ColdResistance = 50,
            LightningResistance = 20,
            Accuracy = 700,
            ActionTimeSeconds = 1.3,
            GuaranteedDrops = 4,
            Attacks = new[]
            {
                new EnemyAttackDefinition
                {
                    Id = "attack.rimeheart_matron.glacial_requiem", Name = "Glacial Requiem", IsSpell = true, CooldownSeconds = 7,
                    Damage = new[] { new DamageRange(DamageType.Cold, 110, 150) },
                },
                new EnemyAttackDefinition
                {
                    Id = "attack.rimeheart_matron.frost_lash", Name = "Frost Lash",
                    Damage = new[] { new DamageRange(DamageType.Cold, 35, 50), new DamageRange(DamageType.Physical, 20, 30) },
                },
            },
        };

        public static readonly EnemyDefinition[] All =
        {
            RustHound, CinderWisp, BogBrute, AshWarden, FrostboundSeer, StormhideRunner, BogTyrant, RimeheartMatron,
        };
    }
}
