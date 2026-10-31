using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    /// <summary>
    /// Support modifiers. Each changes how its linked skill behaves or what it costs; none is a
    /// free upgrade. Compatibility is explicit through required and excluded tags.
    /// </summary>
    public static class Supports
    {
        public static readonly SupportDefinition HeavyImpact = new()
        {
            Id = "sup.heavy_impact", Name = "Heavy Impact",
            Description = "Melee attacks only. 35% more Physical Damage, 10% less Attack Speed, 30% more Mana Cost.",
            RequiredTags = SkillTags.Attack | SkillTags.Melee,
            Modifiers = new[] { M(StatId.PhysicalDamage, ModifierKind.More, 35), M(StatId.AttackSpeed, ModifierKind.More, -10), M(StatId.ManaCost, ModifierKind.More, 30) },
        };

        public static readonly SupportDefinition SearingEdge = new()
        {
            Id = "sup.searing_edge", Name = "Searing Edge",
            Description = "Attacks only. 50% of Physical Damage converted to Fire, 15% more Fire Damage, 20% more Mana Cost.",
            RequiredTags = SkillTags.Attack,
            Modifiers = new[] { M(StatId.PhysicalToFireConversion, ModifierKind.Flat, 50), M(StatId.FireDamage, ModifierKind.More, 15), M(StatId.ManaCost, ModifierKind.More, 20) },
        };

        public static readonly SupportDefinition FocusedIntent = new()
        {
            Id = "sup.focused_intent", Name = "Focused Intent",
            Description = "Spells only. 30% more Spell Damage, 40% more Mana Cost.",
            RequiredTags = SkillTags.Spell,
            Modifiers = new[] { M(StatId.SpellDamage, ModifierKind.More, 30), M(StatId.ManaCost, ModifierKind.More, 40) },
        };

        public static readonly SupportDefinition KeenEdge = new()
        {
            Id = "sup.keen_edge", Name = "Keen Edge",
            Description = "100% increased Critical Chance, +30% Critical Damage Multiplier, 20% more Mana Cost.",
            Modifiers = new[] { M(StatId.CriticalChance, ModifierKind.Increased, 100), M(StatId.CriticalMultiplier, ModifierKind.Flat, 30), M(StatId.ManaCost, ModifierKind.More, 20) },
        };

        public static readonly SupportDefinition Siphon = new()
        {
            Id = "sup.siphon", Name = "Siphon",
            Description = "2% of damage dealt by this skill is leeched as Life. 10% less Damage.",
            Modifiers = new[] { M(StatId.LifeLeechPercent, ModifierKind.Flat, 2), M(StatId.Damage, ModifierKind.More, -10) },
        };

        public static readonly SupportDefinition ReadyRetort = new()
        {
            Id = "sup.ready_retort", Name = "Ready Retort",
            Description = "Triggered skills only. 60% increased Cooldown Recovery, 20% more Damage.",
            RequiredTags = SkillTags.Triggered,
            Modifiers = new[] { M(StatId.CooldownRecovery, ModifierKind.Increased, 60), M(StatId.Damage, ModifierKind.More, 20) },
        };

        public static readonly SupportDefinition FrugalCasting = new()
        {
            Id = "sup.frugal_casting", Name = "Frugal Casting",
            Description = "Not for triggered skills (they cost nothing). 35% less Mana Cost, 10% less Damage.",
            ExcludedTags = SkillTags.Triggered,
            Modifiers = new[] { M(StatId.ManaCost, ModifierKind.More, -35), M(StatId.Damage, ModifierKind.More, -10) },
        };

        public static readonly SupportDefinition SunderingFocus = new()
        {
            Id = "sup.sundering_focus", Name = "Sundering Focus",
            Description = "Hits penetrate 15% Elemental Resistance. 20% more Mana Cost.",
            Modifiers = new[] { M(StatId.ElementalPenetration, ModifierKind.Flat, 15), M(StatId.ManaCost, ModifierKind.More, 20) },
        };

        public static readonly SupportDefinition[] All =
        {
            HeavyImpact, SearingEdge, FocusedIntent, KeenEdge, Siphon, ReadyRetort, FrugalCasting, SunderingFocus,
        };

        private static Modifier M(StatId stat, ModifierKind kind, double value) => new(stat, kind, value);
    }
}
