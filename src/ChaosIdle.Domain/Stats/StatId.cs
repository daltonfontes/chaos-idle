namespace ChaosIdle.Domain.Stats
{
    /// <summary>
    /// Stable stat identifiers. Values are persisted in content data, so never renumber;
    /// only append.
    /// Damage stats use <see cref="ModifierKind.Flat"/> for added damage and
    /// Increased/More for scaling.
    /// </summary>
    public enum StatId
    {
        MaxLife = 1,
        LifeRegenPerSecond = 2,
        MaxMana = 3,
        ManaRegenPerSecond = 4,

        Armour = 10,
        Evasion = 11,
        BlockChance = 12,
        FireResistance = 13,
        ColdResistance = 14,
        LightningResistance = 15,

        Accuracy = 20,
        AttackSpeed = 21,
        CastSpeed = 22,
        CriticalChance = 23,
        CriticalMultiplier = 24,
        ElementalPenetration = 25,

        Damage = 30,
        PhysicalDamage = 31,
        FireDamage = 32,
        ColdDamage = 33,
        LightningDamage = 34,
        ElementalDamage = 35,
        AttackDamage = 36,
        SpellDamage = 37,

        LifeLeechPercent = 40,
        LifeOnBlock = 41,
    }
}
