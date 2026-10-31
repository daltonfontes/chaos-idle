using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Damage;
using ChaosIdle.Simulation.Diagnostics;
using ChaosIdle.Simulation.Random;

namespace ChaosIdle.Simulation.Combat
{
    /// <summary>
    /// Headless, deterministic, event-driven combat: one character against an ordered list of
    /// enemies, one at a time. Same build + encounter + seed always yields the same report.
    /// </summary>
    public sealed class CombatSimulator
    {
        private const double Epsilon = 1e-9;

        public CombatReport Run(CharacterBuild build, Encounter encounter, ulong seed, SimulationOptions? options = null)
        {
            options ??= SimulationOptions.Default;
            var errors = build.Validate().Concat(encounter.Enemies.SelectMany(e => e.Validate())).ToList();
            if (encounter.Enemies.Count == 0) errors.Add($"{encounter.Id}: encounter has no enemies.");
            if (errors.Count > 0)
            {
                throw new ArgumentException("Invalid simulation input:\n" + string.Join("\n", errors));
            }

            return new SimulationRun(build, encounter, seed, options).Execute();
        }

        private sealed class SimulationRun
        {
            private readonly CharacterBuild _build;
            private readonly Encounter _encounter;
            private readonly SimulationOptions _options;
            private readonly IRandom _rng;
            private readonly CombatReport _report;

            private readonly StatBlock _stats;
            private readonly DefenceStats _defence;
            private readonly double _maxLife;
            private readonly double _maxMana;
            private readonly double _lifeRegen;
            private readonly double _manaRegen;
            private readonly Dictionary<string, StatBlock> _skillStats = new Dictionary<string, StatBlock>();
            private readonly double _lifeOnBlock;
            private readonly double _actionTime;
            private readonly double _manaCost;
            private readonly double[] _triggerReadyAt;

            private readonly Dictionary<string, double> _damageBySkill = new Dictionary<string, double>();
            private readonly List<EnemyKill> _kills = new List<EnemyKill>();
            private readonly Queue<DamageTakenEvent> _recent = new Queue<DamageTakenEvent>();

            private double _time;
            private double _life;
            private double _mana;
            private double _playerNext;

            private int _enemyIndex;
            private EnemyDefinition _enemy = null!;
            private DefenceStats _enemyDefence = null!;
            private double _enemyLife;
            private double _enemyNext;
            private double _enemySpawnedAt;
            private double[] _enemyAttackReadyAt = Array.Empty<double>();

            public SimulationRun(CharacterBuild build, Encounter encounter, ulong seed, SimulationOptions options)
            {
                _build = build;
                _encounter = encounter;
                _options = options;
                _rng = new SplitMix64Random(seed);
                _stats = build.ComputeStats();
                _defence = DefenceStats.From(_stats);

                _maxLife = Math.Max(1, _stats.Resolve(StatId.MaxLife));
                _maxMana = Math.Max(0, _stats.Resolve(StatId.MaxMana));
                _lifeRegen = Math.Max(0, _stats.Resolve(StatId.LifeRegenPerSecond));
                _manaRegen = Math.Max(0, _stats.Resolve(StatId.ManaRegenPerSecond));
                _lifeOnBlock = Math.Max(0, _stats.Flat(StatId.LifeOnBlock));
                foreach (var skill in build.TriggeredSkills.Append(build.PrimarySkill))
                {
                    _skillStats[skill.Id] = build.ComputeSkillStats(skill);
                }

                var primaryStats = _skillStats[build.PrimarySkill.Id];
                _actionTime = DamagePipeline.ActionTime(build.PrimarySkill, build.Weapon, primaryStats);
                _manaCost = DamagePipeline.ManaCost(build.PrimarySkill, primaryStats);
                _triggerReadyAt = new double[build.TriggeredSkills.Count];

                _life = _maxLife;
                _mana = _maxMana;
                _report = new CombatReport
                {
                    BuildId = build.Id,
                    EncounterId = encounter.Id,
                    Seed = seed,
                    EnemiesTotal = encounter.Enemies.Count,
                    MaxLife = _maxLife,
                };
            }

            public CombatReport Execute()
            {
                SpawnEnemy(0);
                CombatOutcome outcome;

                while (true)
                {
                    double next = Math.Min(_playerNext, _enemyNext);
                    if (next > _options.MaxDurationSeconds)
                    {
                        AdvanceTo(_options.MaxDurationSeconds);
                        outcome = CombatOutcome.Timeout;
                        break;
                    }

                    AdvanceTo(next);

                    // Ties go to the player: deterministic and documented.
                    if (_playerNext <= _enemyNext)
                    {
                        PlayerAct();
                    }
                    else
                    {
                        EnemyAct();
                    }

                    if (_enemyLife <= 0)
                    {
                        _kills.Add(new EnemyKill(_enemy.Id, _enemy.Name, _enemy.IsBoss, _enemySpawnedAt, _time));
                        if (_enemyIndex + 1 >= _encounter.Enemies.Count)
                        {
                            outcome = CombatOutcome.Victory;
                            break;
                        }

                        SpawnEnemy(_enemyIndex + 1);
                    }

                    if (_life <= 0)
                    {
                        outcome = CombatOutcome.Death;
                        break;
                    }
                }

                return Finish(outcome);
            }

            private void SpawnEnemy(int index)
            {
                _enemyIndex = index;
                _enemy = _encounter.Enemies[index];
                _enemyDefence = DefenceStats.From(_enemy);
                _enemyLife = _enemy.MaxLife;
                _enemySpawnedAt = _time;
                _enemyNext = _time + _enemy.ActionTimeSeconds;

                // Abilities with a cooldown start on cooldown: big attacks need a wind-up.
                _enemyAttackReadyAt = _enemy.Attacks.Select(a => _time + a.CooldownSeconds).ToArray();
            }

            private void AdvanceTo(double time)
            {
                double dt = time - _time;
                if (dt > 0)
                {
                    double lifeGain = Math.Min(_lifeRegen * dt, _maxLife - _life);
                    if (lifeGain > 0)
                    {
                        _life += lifeGain;
                        _report.LifeRegenerated += lifeGain;
                    }

                    _mana = Math.Min(_maxMana, _mana + _manaRegen * dt);
                }

                _time = time;
            }

            private void PlayerAct()
            {
                var skill = _build.PrimarySkill;
                if (_mana + Epsilon < _manaCost)
                {
                    double wait = _manaRegen > 0 ? (_manaCost - _mana) / _manaRegen : double.PositiveInfinity;
                    _report.TimeOutOfMana += Math.Min(wait, _options.MaxDurationSeconds - _time);
                    _playerNext = _time + wait;
                    return;
                }

                _mana = Math.Max(0, _mana - _manaCost);
                _report.PrimarySkillUses++;
                HitEnemy(skill);
                _playerNext = _time + _actionTime;
            }

            private void HitEnemy(SkillDefinition skill)
            {
                // Offence uses the skill's own stat block (character stats + its supports).
                var stats = _skillStats[skill.Id];
                var hit = DamagePipeline.RollPlayerHit(skill, _build.Weapon, stats, _rng);
                var result = DamagePipeline.Resolve(hit.Damage, hit.IsSpell, stats.Resolve(StatId.Accuracy),
                    stats.Flat(StatId.ElementalPenetration), _enemyDefence, _rng);
                double dealt = Math.Min(result.Taken.Total, Math.Max(0, _enemyLife));
                _enemyLife -= result.Taken.Total;

                _damageBySkill.TryGetValue(skill.Id, out var sum);
                _damageBySkill[skill.Id] = sum + dealt;
                _report.TotalDamageDealt += dealt;

                double leech = Math.Min(dealt * Math.Max(0, stats.Flat(StatId.LifeLeechPercent)) / 100.0, _maxLife - _life);
                if (leech > 0)
                {
                    _life += leech;
                    _report.LifeLeeched += leech;
                }
            }

            private void EnemyAct()
            {
                int attackIndex = _enemy.Attacks.Count - 1;
                for (int i = 0; i < _enemy.Attacks.Count; i++)
                {
                    if (_enemyAttackReadyAt[i] <= _time + Epsilon)
                    {
                        attackIndex = i;
                        break;
                    }
                }

                var attack = _enemy.Attacks[attackIndex];
                _enemyAttackReadyAt[attackIndex] = _time + attack.CooldownSeconds;
                _enemyNext = _time + _enemy.ActionTimeSeconds;

                var incoming = DamagePipeline.RollEnemyHit(attack, _rng);
                var result = DamagePipeline.Resolve(incoming, attack.IsSpell, _enemy.Accuracy, 0, _defence, _rng);

                switch (result.Outcome)
                {
                    case HitOutcome.Evaded:
                        _report.HitsEvaded++;
                        return;
                    case HitOutcome.Blocked:
                        _report.HitsBlocked++;
                        double gain = Math.Min(_lifeOnBlock, _maxLife - _life);
                        if (gain > 0)
                        {
                            _life += gain;
                            _report.LifeGainedOnBlock += gain;
                        }

                        FireTriggers(SkillTrigger.OnBlock);
                        return;
                }

                _report.HitsTaken++;
                _report.DamageTakenBeforeMitigation.Add(result.Incoming);
                _report.DamageTaken.Add(result.Taken);
                _report.PreventedByArmour += result.PreventedByArmour;
                _report.PreventedByResistance += result.PreventedByResistance;
                _life -= result.Taken.Total;

                var evt = new DamageTakenEvent(_time, _enemy.Id, attack.Id, attack.Name, result.Taken);
                _recent.Enqueue(evt);
                while (_recent.Count > 0 && _recent.Peek().Time < _time - _options.RecentDamageWindowSeconds)
                {
                    _recent.Dequeue();
                }

                if (_life <= 0)
                {
                    _report.KillingBlow = evt;
                }
            }

            private void FireTriggers(SkillTrigger trigger)
            {
                for (int i = 0; i < _build.TriggeredSkills.Count; i++)
                {
                    var skill = _build.TriggeredSkills[i];
                    if (skill.Trigger != trigger || _triggerReadyAt[i] > _time + Epsilon || _enemyLife <= 0)
                    {
                        continue;
                    }

                    _triggerReadyAt[i] = _time + DamagePipeline.Cooldown(skill, _skillStats[skill.Id]);
                    _report.TriggeredSkillUses++;
                    HitEnemy(skill);
                }
            }

            private CombatReport Finish(CombatOutcome outcome)
            {
                _report.Outcome = outcome;
                _report.DurationSeconds = _time;
                _report.FinalLife = Math.Max(0, _life);
                _report.Kills = _kills;
                _report.DamageDealtBySkill = _damageBySkill;
                _report.CurrentEnemyLifeFraction = outcome == CombatOutcome.Victory ? 0 : Math.Max(0, _enemyLife) / _enemy.MaxLife;
                _report.RecentDamageTaken = _recent.Where(e => e.Time >= _time - _options.RecentDamageWindowSeconds).ToList();
                _report.Diagnoses = FailureAnalyzer.Analyze(_report, _defence);
                return _report;
            }
        }
    }
}
