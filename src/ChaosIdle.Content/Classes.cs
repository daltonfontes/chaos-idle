using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    public static class Classes
    {
        public static readonly CharacterClassDefinition Guardian = new CharacterClassDefinition
        {
            Id = "class.guardian",
            Name = "Guardian",
            BaseModifiers = new[]
            {
                new Modifier(StatId.MaxLife, ModifierKind.Flat, 400, "class.guardian"),
                new Modifier(StatId.MaxMana, ModifierKind.Flat, 80, "class.guardian"),
                new Modifier(StatId.ManaRegenPerSecond, ModifierKind.Flat, 4, "class.guardian"),
                new Modifier(StatId.Accuracy, ModifierKind.Flat, 500, "class.guardian"),
                new Modifier(StatId.Armour, ModifierKind.Flat, 150, "class.guardian"),
                new Modifier(StatId.Evasion, ModifierKind.Flat, 50, "class.guardian"),
            },
        };
    }
}
