using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Stats;
using static ChaosIdle.Content.Monsters;

namespace ChaosIdle.Content
{
    public static class Maps
    {
        public static readonly MapDefinition SunkenCauseway = new()
        {
            Id = "map.sunken_causeway", Name = "Sunken Causeway", Tier = 1, AreaLevel = 6,
            Packs = new[]
            {
                Pack("encounter.causeway_1", "Causeway Shallows", RustHound, BogBrute, RustHound, RustHound),
                Pack("encounter.causeway_2", "Causeway Depths", BogBrute, RustHound, CinderWisp, BogBrute),
            },
            Boss = Pack("encounter.bog_tyrant", "Tyrant's Mire", BogTyrant),
        };

        public static readonly MapDefinition AshenTrail = new()
        {
            Id = "map.ashen_trail", Name = "Ashen Trail", Tier = 2, AreaLevel = 9,
            Packs = new[] { Encounters.AshenRoad, Encounters.AshenRoad },
            Boss = Encounters.WardensHearth,
        };

        public static readonly MapDefinition RimewoodHollow = new()
        {
            Id = "map.rimewood_hollow", Name = "Rimewood Hollow", Tier = 3, AreaLevel = 12,
            Packs = new[]
            {
                Pack("encounter.rimewood_1", "Frozen Thicket", FrostboundSeer, StormhideRunner, StormhideRunner, RustHound, FrostboundSeer),
                Pack("encounter.rimewood_2", "Hollow Heart", StormhideRunner, FrostboundSeer, BogBrute, StormhideRunner, FrostboundSeer),
            },
            Boss = Pack("encounter.rimeheart", "Matron's Bower", RimeheartMatron),
        };

        public static readonly MapDefinition[] All = { SunkenCauseway, AshenTrail, RimewoodHollow };

        private static Encounter Pack(string id, string name, params EnemyDefinition[] enemies) => new() { Id = id, Name = name, Enemies = enemies };
    }

    /// <summary>Each modifier tests a different weakness and pays for it in loot.</summary>
    public static class MapModifiers
    {
        public static readonly MapModifier Hardened = new()
        {
            Id = "mapmod.hardened", Name = "Hardened Foes", Description = "Enemies have 40% more life.",
            EnemyLifeMore = 40, ItemQuantityPercent = 12,
        };

        public static readonly MapModifier Savage = new()
        {
            Id = "mapmod.savage", Name = "Savage Foes", Description = "Enemies deal 25% more damage.",
            EnemyDamageMore = 25, ItemQuantityPercent = 12, ItemRarityPercent = 10,
        };

        public static readonly MapModifier Frenzied = new()
        {
            Id = "mapmod.frenzied", Name = "Frenzied Foes", Description = "Enemies act 25% faster.",
            EnemySpeedMore = 25, ItemQuantityPercent = 15,
        };

        public static readonly MapModifier Frostbitten = new()
        {
            Id = "mapmod.frostbitten", Name = "Frostbitten", Description = "Enemy hits deal 30% of their damage as extra cold.",
            ExtraDamagePercent = 30, ExtraDamageType = DamageType.Cold, ItemQuantityPercent = 10, ItemRarityPercent = 10,
        };

        public static readonly MapModifier SearingAir = new()
        {
            Id = "mapmod.searing_air", Name = "Searing Air", Description = "You have −25% fire resistance.",
            PlayerModifiers = new[] { new Modifier(StatId.FireResistance, ModifierKind.Flat, -25) }, ItemRarityPercent = 20,
        };

        public static readonly MapModifier Withering = new()
        {
            Id = "mapmod.withering", Name = "Withering", Description = "You have 60% less life regeneration.",
            PlayerModifiers = new[] { new Modifier(StatId.LifeRegenPerSecond, ModifierKind.More, -60) }, ItemQuantityPercent = 10,
        };

        public static readonly MapModifier Warded = new()
        {
            Id = "mapmod.warded", Name = "Warded Foes", Description = "Enemies have +25% elemental resistances.",
            EnemyElementalResistance = 25, ItemRarityPercent = 15,
        };

        public static readonly MapModifier Ironclad = new()
        {
            Id = "mapmod.ironclad", Name = "Ironclad Foes", Description = "Enemies have +600 armour.",
            EnemyArmour = 600, ItemRarityPercent = 15,
        };

        public static readonly MapModifier[] All = { Hardened, Savage, Frenzied, Frostbitten, SearingAir, Withering, Warded, Ironclad };
    }
}
