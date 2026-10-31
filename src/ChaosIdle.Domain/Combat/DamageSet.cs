using System;

namespace ChaosIdle.Domain.Combat
{
    /// <summary>An amount of damage split by <see cref="DamageType"/>. Mutable for allocation-light simulation.</summary>
    public sealed class DamageSet
    {
        private readonly double[] _values = new double[DamageTypes.Count];

        public double this[DamageType type]
        {
            get => _values[(int)type];
            set => _values[(int)type] = value;
        }

        public double Total
        {
            get
            {
                double sum = 0;
                for (int i = 0; i < _values.Length; i++)
                {
                    sum += _values[i];
                }

                return sum;
            }
        }

        public static DamageSet Of(DamageType type, double amount)
        {
            var set = new DamageSet();
            set[type] = amount;
            return set;
        }

        public DamageSet Clone()
        {
            var copy = new DamageSet();
            Array.Copy(_values, copy._values, _values.Length);
            return copy;
        }

        public void Add(DamageSet other)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] += other._values[i];
            }
        }

        public DamageType Dominant()
        {
            int best = 0;
            for (int i = 1; i < _values.Length; i++)
            {
                if (_values[i] > _values[best])
                {
                    best = i;
                }
            }

            return (DamageType)best;
        }

        public override string ToString() =>
            $"phys {_values[0]:0.#} / fire {_values[1]:0.#} / cold {_values[2]:0.#} / light {_values[3]:0.#}";
    }
}
