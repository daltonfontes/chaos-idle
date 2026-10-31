using System.Collections.Generic;

namespace ChaosIdle.Domain.Combat
{
    public enum DamageType
    {
        Physical = 0,
        Fire = 1,
        Cold = 2,
        Lightning = 3,
    }

    public static class DamageTypes
    {
        public const int Count = 4;

        public static readonly IReadOnlyList<DamageType> All = new[]
        {
            DamageType.Physical, DamageType.Fire, DamageType.Cold, DamageType.Lightning,
        };

        public static bool IsElemental(this DamageType type) => type != DamageType.Physical;
    }
}
