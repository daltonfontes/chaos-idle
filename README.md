# Chaos Idle

An original mobile idle-hybrid ARPG (working title) focused on deep buildcraft,
automatic combat, and bounded offline expeditions.

## Status

**Milestone 1 — Combat proof of concept** is in progress. There is a headless,
deterministic C# combat simulator with one class (Guardian), three skills, four enemies
including a boss, two sample builds, and failure diagnosis.

## Layout

| Project | Purpose |
|---|---|
| `src/ChaosIdle.Domain` | Pure rules: stats and modifiers, damage types, combat formulas, skill/weapon/enemy/build definitions. |
| `src/ChaosIdle.Simulation` | Deterministic RNG, damage pipeline, combat simulator, failure analysis, batch runner. |
| `src/ChaosIdle.Content` | Original content with stable IDs: class, skills, weapons, monsters, encounters, sample builds. |
| `src/ChaosIdle.Cli` | Runs a seeded build comparison and prints reports. |
| `tests/ChaosIdle.Tests` | xUnit tests for formulas, pipeline, simulator invariants, diagnosis, and the milestone gate. |

The library projects target `netstandard2.1` with C# 9, so Unity can use them unchanged later.

The damage order and all formulas are documented in
[`docs/combat-pipeline.md`](docs/combat-pipeline.md).

## Running

Requires the .NET 8 SDK.

```bash
dotnet test
```

```bash
dotnet run --project src/ChaosIdle.Cli -- 200 1   # runs per build/encounter, first seed
```

## For Claude Code

The project skill at `.claude/skills/path-of-idle-game/SKILL.md` holds the design
principles, technical direction, and roadmap. Claude Code loads it automatically in this
repository.
