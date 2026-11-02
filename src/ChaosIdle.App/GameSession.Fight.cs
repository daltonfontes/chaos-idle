using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Simulation.Combat;
using ChaosIdle.Simulation.Expeditions;

namespace ChaosIdle.App
{
    /// <summary>One encounter of a replay: where it starts on the replay's clock.</summary>
    public sealed record FightStage(string Title, double StartTime, bool IsBoss);

    /// <summary>
    /// The last active map run, beat by beat, for the client to animate. Times are seconds from
    /// the start of the run; every encounter's beats follow the previous encounter's.
    /// </summary>
    public sealed record FightReplay(
        string HeroName,
        string ClassName,
        double HeroMaxLife,
        string MapName,
        IReadOnlyList<FightStage> Stages,
        IReadOnlyList<CombatBeat> Beats,
        double DurationSeconds,
        string Outcome);

    public sealed partial class GameSession
    {
        private static readonly SimulationOptions ReplayOptions = new SimulationOptions { RecordTimeline = true };

        /// <summary>The last map run in this session, or null if none ran yet (replays are not saved).</summary>
        public FightReplay? LastFight { get; private set; }

        private void RememberFight(ExpeditionReport report, string mapName)
        {
            var stages = new List<FightStage>();
            var beats = new List<CombatBeat>();
            double offset = 0;
            for (int i = 0; i < report.Encounters.Count; i++)
            {
                var e = report.Encounters[i];
                bool boss = e.Encounter.Enemies.Any(x => x.IsBoss);
                string title = boss ? $"Boss: {e.Encounter.Name}" : $"Pack {i + 1} of {report.EncountersTotal}: {e.Encounter.Name}";
                stages.Add(new FightStage(title, offset, boss));
                beats.AddRange(e.Combat.Timeline.Select(b => b with { Time = b.Time + offset }));
                offset += e.Combat.DurationSeconds;
            }

            var failed = report.FailedEncounter?.Combat.Outcome;
            string outcome = failed switch
            {
                null => "Map cleared",
                CombatOutcome.Death => "Defeated",
                _ => "Out of time",
            };
            double maxLife = report.Encounters.Count > 0 ? report.Encounters[0].Combat.MaxLife : 1;
            LastFight = new FightReplay(Character.Name, Character.Class.Name, maxLife, mapName, stages, beats, offset, outcome);
        }
    }
}
