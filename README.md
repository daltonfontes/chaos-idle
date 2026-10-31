# Chaos Idle

An original mobile idle-hybrid ARPG (working title) focused on deep buildcraft,
automatic combat, and bounded offline expeditions.

## Status

- **Milestone 1 — combat proof of concept:** done. It's a headless, deterministic combat
  simulator with failure diagnosis, and two sample builds with clearly different outcomes.
- **Milestone 2 — vertical slice:** the domain half is done.
  - Done: passive tree, items and affixes, loot, inventory, item comparison, expeditions,
    and player-facing reports.
  - Not done: the Unity client.

  See [`docs/milestone-2.md`](docs/milestone-2.md).
- **Milestone 3 — buildcraft:** the domain half is done.
  - Done: 8 support modifiers with explicit compatibility rules, 4 unique items each with a
    drawback, a loot filter with a build-aware preset, and scenario-based build comparison.
  - Gate: two equally invested builds are both viable, with opposite strengths. See
    [`docs/milestone-3.md`](docs/milestone-3.md).

## Layout

| Project | Purpose |
|---|---|
| `src/ChaosIdle.Domain` | Pure rules: stats and modifiers, combat formulas, skills, passive tree, items, equipment, inventory, characters, expeditions. |
| `src/ChaosIdle.Simulation` | Deterministic random numbers, damage pipeline, combat simulator, failure analysis, loot generation, build summary and item comparison, expedition runner, player-facing report text. |
| `src/ChaosIdle.Content` | Original content with stable IDs: class, skills, passive tree, item bases, affixes, monsters, encounters, expeditions, starter character. |
| `src/ChaosIdle.Cli` | `compare` (the Milestone 1 build table), `expedition` (the Milestone 2 loop as text), and `builds` (the Milestone 3 reference-build comparison). |
| `tests/ChaosIdle.Tests` | xUnit tests: formulas, pipeline, simulator invariants, diagnosis, passives, items, loot, comparison, expeditions, and both milestone gates. |

The library projects target `netstandard2.1` with C# 9, so Unity can use them unchanged later.

The damage order and all formulas are documented in
[`docs/combat-pipeline.md`](docs/combat-pipeline.md).

## Running

Requires the .NET 8 SDK.

```bash
dotnet test
```

```bash
dotnet run --project src/ChaosIdle.Cli -- compare 200 1   # runs per build/encounter, first seed
```

```bash
dotnet run --project src/ChaosIdle.Cli -- expedition 1 8  # seed, number of farming runs
```

```bash
dotnet run --project src/ChaosIdle.Cli -- builds 100 1    # runs per scenario, first seed
```

## For Claude Code

The project skill at `.claude/skills/path-of-idle-game/SKILL.md` holds the design
principles, technical direction, and roadmap. Claude Code loads it automatically in this
repository.
