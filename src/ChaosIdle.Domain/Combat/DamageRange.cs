namespace ChaosIdle.Domain.Combat
{
    public sealed record DamageRange(DamageType Type, double Min, double Max);

    /// <summary>Moves a percentage of one damage type into another before scaling is applied.</summary>
    public sealed record DamageConversion(DamageType From, DamageType To, double Percent);
}
