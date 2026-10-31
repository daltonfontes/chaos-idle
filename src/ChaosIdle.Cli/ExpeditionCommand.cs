using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.Cli;

/// <summary>
/// Milestone 2 loop, text only: build a Guardian, try the boss expedition, farm loot,
/// compare and equip upgrades, try again.
/// </summary>
internal static class ExpeditionCommand
{
    public static void Run(string[] args)
    {
        ulong seed = args.Length > 0 ? ulong.Parse(args[0]) : 1;
        int patrols = args.Length > 1 ? int.Parse(args[1]) : 8;

        var runner = new ExpeditionRunner(new LootGenerator(ItemBases.All, Affixes.All));
        var guardian = StarterCharacters.NewGuardian();
        guardian.Passives = PassiveAllocation.FromPath(PassiveTrees.Guardian, StarterCharacters.StartingPassivePoints, StarterCharacters.BulwarkPath);

        Section("Character");
        PrintSheet(guardian);

        Section("Attempt 1");
        var firstLoot = RunAndClaim(runner, guardian, Expeditions.AshenTrail, Seeds.Derive(seed, 1000));
        EquipUpgrades(guardian, firstLoot);

        for (int i = 0; i < patrols; i++)
        {
            Section($"Farming {Expeditions.RoadPatrol.Name} #{i + 1}");
            var claimed = RunAndClaim(runner, guardian, Expeditions.RoadPatrol, Seeds.Derive(seed, (ulong)i));
            EquipUpgrades(guardian, claimed);
        }

        Section("Character after farming");
        PrintSheet(guardian);

        Section("Attempt 2");
        RunAndClaim(runner, guardian, Expeditions.AshenTrail, Seeds.Derive(seed, 1001));
    }

    private static IReadOnlyList<Item> RunAndClaim(ExpeditionRunner runner, PlayerCharacter character, ExpeditionDefinition expedition, ulong seed)
    {
        var report = runner.Run(character, expedition, seed);
        foreach (var line in ReportText.Expedition(report)) Console.WriteLine(line);
        var claim = report.ClaimLoot(character.Inventory);
        Console.WriteLine(ReportText.Claim(claim));
        return claim.Kept;
    }

    /// <summary>
    /// Demo policy only: equip an item when the comparison is a strict upgrade. Real players
    /// decide trade-offs themselves; that is the point of the comparison view.
    /// </summary>
    private static void EquipUpgrades(PlayerCharacter character, IReadOnlyList<Item> newItems)
    {
        foreach (var item in newItems)
        {
            foreach (var slot in item.Class.SlotsFor())
            {
                var comparison = ItemComparer.Compare(character, item, slot);
                if (comparison.Verdict == ComparisonVerdict.TradeOff || comparison.Verdict == ComparisonVerdict.Upgrade)
                {
                    foreach (var line in ReportText.ItemTooltip(item)) Console.WriteLine(line);
                    foreach (var line in ReportText.Comparison(comparison)) Console.WriteLine(line);
                }

                if (comparison.Verdict == ComparisonVerdict.Upgrade)
                {
                    character.EquipFromInventory(item, slot);
                    Console.WriteLine($"  → equipped in {slot}");
                    break;
                }
            }
        }
    }

    private static void PrintSheet(PlayerCharacter character)
    {
        var s = BuildSummary.Compute(character.ToBuild());
        Console.WriteLine($"{character.Name}: {character.Passives.PointsSpent}/{character.Passives.TotalPoints} passives, skill {character.PrimarySkill.Name}");
        Console.WriteLine($"  Life {s.MaxLife:0} (+{s.LifeRegenPerSecond:0.#}/s)  Mana {s.MaxMana:0} (+{s.ManaRegenPerSecond:0.#}/s, uses {s.ManaSpentPerSecond:0.#}/s)");
        Console.WriteLine($"  Armour {s.Armour:0} ({s.ArmourReductionVsReferenceHit:P0} vs a 100 hit)  Block {s.BlockChance:0}%  Res {s.FireResistance:0}/{s.ColdResistance:0}/{s.LightningResistance:0}%");
        Console.WriteLine($"  DPS {s.HitDamagePerSecond:0.#} (avg hit {s.AverageHit:0.#} × {s.ActionsPerSecond:0.##}/s, crit {s.CritChance:P0})");
        Console.WriteLine("  Gear: " + string.Join(", ", character.Equipment.Items.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.Name} [{kv.Value.Rarity}]")));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"── {title} ──");
    }
}
