namespace ChaosIdle.Domain.Items
{
    /// <summary>Milestone 1 weapon: a fixed base with no affixes yet.</summary>
    public sealed record WeaponDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public double PhysicalMin { get; init; }
        public double PhysicalMax { get; init; }
        public double AttacksPerSecond { get; init; } = 1.0;
        public double CritChance { get; init; } = 5.0;
    }
}
