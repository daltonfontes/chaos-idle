using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.Cli;

/// <summary>Milestone 3: profile the two reference builds and list each one's strengths.</summary>
internal static class BuildsCommand
{
    public static readonly BuildScenario[] Scenarios =
    {
        new("Ashen Road", Encounters.AshenRoad),
        new("Warden's Hearth", Encounters.WardensHearth),
    };

    public static void Run(string[] args)
    {
        int runs = args.Length > 0 ? int.Parse(args[0]) : 100;
        ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;

        var bastion = ReferenceBuilds.IronBastion();
        var lancer = ReferenceBuilds.EmberLancer();
        var a = BuildComparer.Profile(bastion.ToBuild(), Scenarios, seed, runs);
        var b = BuildComparer.Profile(lancer.ToBuild(), Scenarios, seed, runs);

        foreach (var (character, profile) in new[] { (bastion, a), (lancer, b) })
        {
            Console.WriteLine();
            Console.WriteLine($"── {profile.BuildName} ──");
            Console.WriteLine($"  Skill: {character.PrimarySkill.Name} + {string.Join(", ", character.SupportsFor(character.PrimarySkill).Select(s => s.Name))}");
            foreach (var t in character.TriggeredSkills)
                Console.WriteLine($"  Trigger: {t.Name} + {string.Join(", ", character.SupportsFor(t).Select(s => s.Name))}");
            var s = profile.Sheet;
            Console.WriteLine($"  Life {s.MaxLife:0}  Armour {s.Armour:0}  Block {s.BlockChance:0}%  Fire res {s.FireResistance:0}%  DPS {s.HitDamagePerSecond:0}  Mana {s.ManaSpentPerSecond:0.#}/s of {s.ManaRegenPerSecond:0.#}/s regen");
            foreach (var (scenario, r) in profile.Results)
            {
                Console.WriteLine($"  {scenario.Name,-16} win {r.VictoryRate,5:P0}  median {Fmt(r.MedianClearTime)}s  dmg in {r.MeanDamageTakenPerSecond,5:0.0}/s  life left {r.MeanFinalLifeFraction:P0}  " +
                                  string.Join(", ", r.DiagnosisCounts.Select(kv => $"{kv.Key} x{kv.Value}")));
            }

            Console.WriteLine($"  Viable (≥80% everywhere): {(profile.IsViable(0.8) ? "yes" : "NO")}");
        }

        Console.WriteLine();
        foreach (var line in ReportText.BuildComparison(a, b, BuildComparer.Compare(a, b))) Console.WriteLine(line);
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.0") : "-";
}
