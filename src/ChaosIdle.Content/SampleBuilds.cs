using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    /// <summary>
    /// Two deliberately contrasting Guardian builds used for the Milestone 1 gate. Their
    /// modifier lists stand in for passives and gear until those systems exist.
    /// </summary>
    public static class SampleBuilds
    {
        /// <summary>Block, armour, counter-attacks. Slow and steady; built for bosses.</summary>
        public static readonly CharacterBuild Bulwark = new CharacterBuild
        {
            Id = "build.bulwark",
            Name = "Bulwark (block / armour / counter)",
            Class = Classes.Guardian,
            Weapon = Weapons.IronboundMace,
            PrimarySkill = Skills.IronCleave,
            TriggeredSkills = new[] { Skills.BulwarkRetort },
            Modifiers = new[]
            {
                M(StatId.BlockChance, ModifierKind.Flat, 45),
                M(StatId.Armour, ModifierKind.Flat, 900),
                M(StatId.Armour, ModifierKind.Increased, 80),
                M(StatId.MaxLife, ModifierKind.Increased, 50),
                M(StatId.LifeOnBlock, ModifierKind.Flat, 30),
                M(StatId.LifeRegenPerSecond, ModifierKind.Flat, 10),
                M(StatId.FireResistance, ModifierKind.Flat, 70),
                M(StatId.ColdResistance, ModifierKind.Flat, 50),
                M(StatId.LightningResistance, ModifierKind.Flat, 50),
                M(StatId.PhysicalDamage, ModifierKind.Increased, 60),
                M(StatId.AttackSpeed, ModifierKind.Increased, 10),
                M(StatId.ManaRegenPerSecond, ModifierKind.Increased, 50),
            },
        };

        /// <summary>Fire spell glass cannon. Melts packs; fragile against heavy physical hits.</summary>
        public static readonly CharacterBuild Pyre = new CharacterBuild
        {
            Id = "build.pyre",
            Name = "Pyre (fire spell / crit)",
            Class = Classes.Guardian,
            Weapon = Weapons.AshwoodRod,
            PrimarySkill = Skills.CinderLance,
            Modifiers = new[]
            {
                M(StatId.SpellDamage, ModifierKind.Increased, 90),
                M(StatId.FireDamage, ModifierKind.Increased, 70),
                M(StatId.CastSpeed, ModifierKind.Increased, 25),
                M(StatId.CriticalChance, ModifierKind.Increased, 120),
                M(StatId.CriticalMultiplier, ModifierKind.Flat, 60),
                M(StatId.ElementalPenetration, ModifierKind.Flat, 20),
                M(StatId.MaxMana, ModifierKind.Flat, 70),
                M(StatId.ManaRegenPerSecond, ModifierKind.Increased, 150),
                M(StatId.FireResistance, ModifierKind.Flat, 40),
                M(StatId.MaxLife, ModifierKind.Increased, 15),
                M(StatId.LifeRegenPerSecond, ModifierKind.Flat, 4),
            },
        };

        public static readonly CharacterBuild[] All = { Bulwark, Pyre };

        private static Modifier M(StatId stat, ModifierKind kind, double value) => new Modifier(stat, kind, value, "sample");
    }
}
