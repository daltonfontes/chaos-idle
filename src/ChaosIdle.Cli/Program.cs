using System.Globalization;
using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Simulation.Combat;

// Usage: dotnet run --project src/ChaosIdle.Cli -- [runs=200] [firstSeed=1]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

int runs = args.Length > 0 ? int.Parse(args[0]) : 200;
ulong firstSeed = args.Length > 1 ? ulong.Parse(args[1]) : 1;

Console.WriteLine($"Chaos Idle — Milestone 1 build comparison ({runs} seeded runs each, seeds {firstSeed}..{firstSeed + (ulong)runs - 1})");
Console.WriteLine();
Console.WriteLine($"{"Build",-10} {"Encounter",-24} {"Win",6} {"Died",5} {"T/O",4} {"Clear(s)",9} {"Boss(s)",8} {"DPS",7} {"DmgIn/s",8} {"Life%",6}  Top diagnoses");
Console.WriteLine(new string('-', 120));

foreach (var build in SampleBuilds.All)
{
    foreach (var encounter in Encounters.All)
    {
        var s = BatchRunner.Run(build, encounter, firstSeed, runs);
        string top = string.Join(", ", s.DiagnosisCounts.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} x{kv.Value}"));
        Console.WriteLine(
            $"{Short(build),-10} {encounter.Name,-24} {s.VictoryRate,6:P0} {s.Deaths,5} {s.Timeouts,4} " +
            $"{Fmt(s.MedianClearTime),9} {Fmt(s.MedianBossKillTime),8} {s.MeanDamagePerSecond,7:0} " +
            $"{s.MeanDamageTakenPerSecond,8:0.0} {s.MeanFinalLifeFraction,6:P0}  {top}");
    }
}

Console.WriteLine();
Console.WriteLine("Sample boss reports (first seed):");
foreach (var build in SampleBuilds.All)
{
    PrintReport(new CombatSimulator().Run(build, Encounters.WardensHearth, firstSeed), build, Encounters.WardensHearth);
}

static string Short(CharacterBuild b) => b.Id.Replace("build.", "");
static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.0") : "-";

static void PrintReport(CombatReport r, CharacterBuild build, Encounter encounter)
{
    Console.WriteLine();
    Console.WriteLine($"== {build.Name} vs {encounter.Name} (seed {r.Seed}) ==");
    Console.WriteLine($"Outcome: {r.Outcome} after {r.DurationSeconds:0.0}s, life {r.FinalLife:0}/{r.MaxLife:0}, enemies {r.Kills.Count}/{r.EnemiesTotal}");
    if (r.Outcome != CombatOutcome.Victory)
        Console.WriteLine($"Enemy left at {r.CurrentEnemyLifeFraction:P0} life");
    Console.WriteLine($"Damage dealt: {r.TotalDamageDealt:0} ({r.DamagePerSecond:0}/s) — " +
        string.Join(", ", r.DamageDealtBySkill.Select(kv => $"{kv.Key} {kv.Value:0}")));
    Console.WriteLine($"Hits: taken {r.HitsTaken}, blocked {r.HitsBlocked}, evaded {r.HitsEvaded}; triggered skill uses {r.TriggeredSkillUses}");
    Console.WriteLine($"Damage taken: {r.DamageTaken.Total:0} ({r.DamageTaken}); prevented by armour {r.PreventedByArmour:0}, by resistance {r.PreventedByResistance:0}");
    Console.WriteLine($"Recovery: regen {r.LifeRegenerated:0}, leech {r.LifeLeeched:0}, on block {r.LifeGainedOnBlock:0}; out of mana {r.TimeOutOfMana:0.0}s");
    if (r.KillingBlow != null)
        Console.WriteLine($"Killing blow: {r.KillingBlow.AttackName} for {r.KillingBlow.Total:0} at {r.KillingBlow.Time:0.0}s");
    foreach (var d in r.Diagnoses)
        Console.WriteLine($"  ! {d.Code}: {d.Message}");
}
