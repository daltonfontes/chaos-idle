using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Combat;

namespace ChaosIdle.Simulation.Analysis
{
    /// <param name="AffectsVerdict">False for changes shown for information only.</param>
    public sealed record StatChange(string Label, double Before, double After, bool HigherIsBetter, string Format, bool AffectsVerdict = true)
    {
        public double Delta => After - Before;
        public bool IsImprovement => HigherIsBetter ? Delta > 0 : Delta < 0;
    }

    public enum ComparisonVerdict
    {
        NoChange = 0,
        Upgrade = 1,
        Downgrade = 2,

        /// <summary>Some numbers go up and some go down: the player decides.</summary>
        TradeOff = 3,
    }

    public sealed record ItemComparison
    {
        public Item Candidate { get; init; } = new Item();
        public EquipmentSlot Slot { get; init; }
        public Item? Replaced { get; init; }
        public BuildSummary Before { get; init; } = new BuildSummary();
        public BuildSummary After { get; init; } = new BuildSummary();
        public IReadOnlyList<StatChange> Changes { get; init; } = Array.Empty<StatChange>();

        public ComparisonVerdict Verdict
        {
            get
            {
                bool up = Changes.Any(c => c.AffectsVerdict && c.IsImprovement);
                bool down = Changes.Any(c => c.AffectsVerdict && !c.IsImprovement);
                if (up && down) return ComparisonVerdict.TradeOff;
                if (up) return ComparisonVerdict.Upgrade;
                if (down) return ComparisonVerdict.Downgrade;
                return ComparisonVerdict.NoChange;
            }
        }
    }

    public static class ItemComparer
    {
        private const double Epsilon = 1e-6;

        /// <summary>Character-sheet comparison of equipping <paramref name="candidate"/> into <paramref name="slot"/>.</summary>
        public static ItemComparison Compare(PlayerCharacter character, Item candidate, EquipmentSlot slot)
        {
            if (!Equipment.Fits(candidate, slot))
            {
                throw new ArgumentException($"{candidate.Name} does not fit {slot}.");
            }

            var swapped = character.Equipment.Clone();
            var replaced = swapped.Equip(candidate, slot);
            var before = BuildSummary.Compute(character.ToBuild());
            var after = BuildSummary.Compute(character.ToBuild(swapped));

            return new ItemComparison
            {
                Candidate = candidate,
                Slot = slot,
                Replaced = replaced,
                Before = before,
                After = after,
                Changes = Diff(before, after),
            };
        }

        /// <summary>
        /// Slower, outcome-based comparison: runs the same seeds against an encounter with and
        /// without the item. Use for "will this help me beat that boss?".
        /// </summary>
        public static (BatchSummary Before, BatchSummary After) Simulate(
            PlayerCharacter character, Item candidate, EquipmentSlot slot, Encounter encounter, ulong firstSeed, int runs)
        {
            var swapped = character.Equipment.Clone();
            swapped.Equip(candidate, slot);
            return (
                BatchRunner.Run(character.ToBuild(), encounter, firstSeed, runs),
                BatchRunner.Run(character.ToBuild(swapped), encounter, firstSeed, runs));
        }

        public static IReadOnlyList<StatChange> Diff(BuildSummary b, BuildSummary a)
        {
            // Spending more mana only matters once regeneration stops covering it.
            bool manaMatters = !(b.ManaSustainable && a.ManaSustainable);
            var all = new[]
            {
                new StatChange("Damage per second", b.HitDamagePerSecond, a.HitDamagePerSecond, true, "0.#"),
                new StatChange("Average hit", b.AverageHit, a.AverageHit, true, "0.#"),
                new StatChange("Actions per second", b.ActionsPerSecond, a.ActionsPerSecond, true, "0.##"),
                new StatChange("Critical chance", b.CritChance * 100, a.CritChance * 100, true, "0.#'%'"),
                new StatChange("Maximum life", b.MaxLife, a.MaxLife, true, "0"),
                new StatChange("Life regeneration /s", b.LifeRegenPerSecond, a.LifeRegenPerSecond, true, "0.#"),
                new StatChange("Maximum mana", b.MaxMana, a.MaxMana, true, "0"),
                new StatChange("Mana regeneration /s", b.ManaRegenPerSecond, a.ManaRegenPerSecond, true, "0.#"),
                new StatChange("Mana spent /s", b.ManaSpentPerSecond, a.ManaSpentPerSecond, false, "0.#", manaMatters),
                new StatChange("Armour", b.Armour, a.Armour, true, "0"),
                new StatChange("Evasion", b.Evasion, a.Evasion, true, "0"),
                new StatChange("Block chance", b.BlockChance, a.BlockChance, true, "0'%'"),
                new StatChange("Fire resistance", b.FireResistance, a.FireResistance, true, "0'%'"),
                new StatChange("Cold resistance", b.ColdResistance, a.ColdResistance, true, "0'%'"),
                new StatChange("Lightning resistance", b.LightningResistance, a.LightningResistance, true, "0'%'"),
            };

            return all.Where(c => Math.Abs(c.Delta) > Epsilon).ToList();
        }
    }
}
