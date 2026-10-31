using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChaosIdle.Domain.Items;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Diagnostics;
using ChaosIdle.Simulation.Expeditions;

namespace ChaosIdle.Simulation.Reporting
{
    /// <summary>
    /// Plain-text, English player-facing summaries. The Unity client can show these lines as-is
    /// until it has its own layouts; keep them short enough for a phone screen.
    /// Indicators never rely on colour alone: arrows and words carry the meaning.
    /// </summary>
    public static class ReportText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static IReadOnlyList<string> Expedition(ExpeditionReport report)
        {
            var lines = new List<string>();
            string status = report.Outcome == ExpeditionOutcome.Completed ? "COMPLETED" : "FAILED";
            lines.Add($"{report.Expedition.Name}: {status} in {Time(report.DurationSeconds)}");

            foreach (var e in report.Encounters)
            {
                var c = e.Combat;
                string life = (c.FinalLife / c.MaxLife).ToString("P0", Inv);
                switch (c.Outcome)
                {
                    case CombatOutcome.Victory:
                        string boss = c.BossKillTime.HasValue ? $", boss down in {Time(c.BossKillTime.Value)}" : "";
                        lines.Add($"  ✓ {e.Encounter.Name}: cleared {c.Kills.Count}/{c.EnemiesTotal} in {Time(c.DurationSeconds)}{boss}, life left {life}");
                        foreach (var d in c.Diagnoses) lines.Add($"    note: {d.Message}");
                        break;
                    case CombatOutcome.Death:
                        lines.Add($"  ✗ {e.Encounter.Name}: died after {Time(c.DurationSeconds)} ({c.Kills.Count}/{c.EnemiesTotal} defeated, last enemy at {c.CurrentEnemyLifeFraction.ToString("P0", Inv)} life)");
                        break;
                    default:
                        lines.Add($"  ✗ {e.Encounter.Name}: ran out of time ({c.Kills.Count}/{c.EnemiesTotal} defeated, last enemy at {c.CurrentEnemyLifeFraction.ToString("P0", Inv)} life)");
                        break;
                }
            }

            int skipped = report.EncountersTotal - report.Encounters.Count;
            if (skipped > 0) lines.Add($"  – {skipped} encounter(s) not reached");

            if (report.Diagnoses.Count > 0)
            {
                lines.Add("Why it failed:");
                foreach (var d in report.Diagnoses) lines.Add($"  • {d.Message}");
                lines.Add("Try: " + string.Join(" ", report.Diagnoses.Select(d => Advice(d.Code)).Distinct()));
            }

            lines.Add($"Loot: {LootLine(report.Loot)}");
            return lines;
        }

        public static string LootLine(IReadOnlyCollection<Item> loot)
        {
            if (loot.Count == 0) return "nothing";
            var byRarity = loot.GroupBy(i => i.Rarity).OrderByDescending(g => g.Key)
                .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");
            return $"{loot.Count} item(s) ({string.Join(", ", byRarity)})";
        }

        public static string Claim(LootClaim claim) =>
            claim.LeftBehind.Count == 0
                ? $"Added {claim.Kept.Count} item(s) to your bag."
                : $"Added {claim.Kept.Count} item(s); {claim.LeftBehind.Count} left behind because your bag is full.";

        public static string Advice(DiagnosisCode code)
        {
            switch (code)
            {
                case DiagnosisCode.InsufficientDamage: return "Raise damage: weapon upgrades, damage passives.";
                case DiagnosisCode.LowResistance: return "Cap the named resistance (75%) with gear or passives.";
                case DiagnosisCode.LowPhysicalMitigation: return "Add armour, block, or more maximum life.";
                case DiagnosisCode.LargeHitSpike: return "Raise maximum life or mitigation against the big hit.";
                case DiagnosisCode.LowSustain: return "Add life regeneration, leech, or life on block.";
                case DiagnosisCode.ResourceStarvation: return "Add mana regeneration or reduce mana costs.";
                case DiagnosisCode.Overwhelmed: return "Improve life, armour, and resistances together, or raise damage to end fights sooner.";
                default: return "";
            }
        }

        public static IReadOnlyList<string> ItemTooltip(Item item)
        {
            var lines = new List<string> { $"{item.Name} [{item.Rarity}] — item level {item.ItemLevel}" };
            if (item.Class.IsWeapon())
            {
                var w = item.ToWeapon();
                lines.Add($"  Physical damage {w.PhysicalMin.ToString("0", Inv)}–{w.PhysicalMax.ToString("0", Inv)}, " +
                          $"{w.AttacksPerSecond.ToString("0.00", Inv)} attacks/s, crit {w.CritChance.ToString("0.#", Inv)}%");
            }
            else
            {
                if (item.LocalArmour > 0) lines.Add($"  Armour {item.LocalArmour.ToString("0", Inv)}");
                if (item.LocalEvasion > 0) lines.Add($"  Evasion {item.LocalEvasion.ToString("0", Inv)}");
                if (item.Base.BlockChance > 0) lines.Add($"  Block chance {item.Base.BlockChance.ToString("0", Inv)}%");
            }

            foreach (var a in item.Affixes) lines.Add($"  {a.Text}  (T{a.Tier})");
            return lines;
        }

        public static IReadOnlyList<string> Comparison(ItemComparison comparison)
        {
            string replacing = comparison.Replaced != null ? $"replacing {comparison.Replaced.Name}" : "into an empty slot";
            var lines = new List<string> { $"{comparison.Candidate.Name} → {comparison.Slot}, {replacing}: {VerdictText(comparison.Verdict)}" };
            foreach (var c in comparison.Changes)
            {
                string arrow = !c.AffectsVerdict ? "• info  " : (c.IsImprovement ? "▲ better" : "▼ worse");
                lines.Add($"  {arrow}  {c.Label}: {c.Before.ToString(c.Format, Inv)} → {c.After.ToString(c.Format, Inv)}");
            }

            return lines;
        }

        private static string VerdictText(ComparisonVerdict v)
        {
            switch (v)
            {
                case ComparisonVerdict.Upgrade: return "UPGRADE";
                case ComparisonVerdict.Downgrade: return "DOWNGRADE";
                case ComparisonVerdict.TradeOff: return "TRADE-OFF";
                default: return "NO CHANGE";
            }
        }

        public static string Time(double seconds) =>
            seconds >= 60
                ? $"{(int)(seconds / 60)}m {((int)seconds % 60).ToString("00", Inv)}s"
                : $"{seconds.ToString("0.0", Inv)}s";
    }
}
