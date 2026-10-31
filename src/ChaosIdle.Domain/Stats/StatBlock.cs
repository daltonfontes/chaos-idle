using System.Collections.Generic;

namespace ChaosIdle.Domain.Stats
{
    /// <summary>
    /// Aggregates modifiers. Resolved value = Flat * (1 + sum(Increased)/100) * product(1 + More/100).
    /// </summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<StatId, double> _flat = new Dictionary<StatId, double>();
        private readonly Dictionary<StatId, double> _increased = new Dictionary<StatId, double>();
        private readonly Dictionary<StatId, double> _more = new Dictionary<StatId, double>();
        private readonly List<Modifier> _modifiers;

        public StatBlock(IEnumerable<Modifier> modifiers)
        {
            _modifiers = new List<Modifier>(modifiers);
            foreach (var m in _modifiers)
            {
                switch (m.Kind)
                {
                    case ModifierKind.Flat:
                        _flat[m.Stat] = Flat(m.Stat) + m.Value;
                        break;
                    case ModifierKind.Increased:
                        _increased[m.Stat] = IncreasedPercent(m.Stat) + m.Value;
                        break;
                    case ModifierKind.More:
                        _more[m.Stat] = MoreMultiplier(m.Stat) * (1.0 + m.Value / 100.0);
                        break;
                }
            }
        }

        public IReadOnlyList<Modifier> Modifiers => _modifiers;

        public double Flat(StatId stat) => _flat.TryGetValue(stat, out var v) ? v : 0.0;

        public double IncreasedPercent(StatId stat) => _increased.TryGetValue(stat, out var v) ? v : 0.0;

        public double MoreMultiplier(StatId stat) => _more.TryGetValue(stat, out var v) ? v : 1.0;

        /// <summary>Multiplier from Increased and More only (ignores Flat).</summary>
        public double ScalingMultiplier(StatId stat) =>
            (1.0 + IncreasedPercent(stat) / 100.0) * MoreMultiplier(stat);

        public double Resolve(StatId stat) => Flat(stat) * ScalingMultiplier(stat);
    }
}
