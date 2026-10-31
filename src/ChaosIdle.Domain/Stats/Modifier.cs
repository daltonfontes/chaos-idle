namespace ChaosIdle.Domain.Stats
{
    public enum ModifierKind
    {
        /// <summary>Added to the base value.</summary>
        Flat = 0,

        /// <summary>Percent; all Increased modifiers on a stat are summed, then applied once.</summary>
        Increased = 1,

        /// <summary>Percent; each More modifier is its own multiplier.</summary>
        More = 2,
    }

    /// <param name="Source">Stable ID of what granted it (class, passive, item, skill) for breakdowns.</param>
    public sealed record Modifier(StatId Stat, ModifierKind Kind, double Value, string Source = "");
}
