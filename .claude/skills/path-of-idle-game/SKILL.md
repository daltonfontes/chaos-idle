---
name: path-of-idle-game
description: >-
  Helps design and develop an ambitious mobile idle-hybrid ARPG inspired by the
  build depth of Path of Exile 1 and the combat/class identity of Path of Exile 2.
  Use when planning architecture, combat simulation, passive trees, skills and
  support gems, procedural loot, crafting, offline progression, maps, bosses,
  mobile UX, backend synchronization, balancing, testing, or roadmap tasks for
  this game. Create original content rather than copying protected PoE assets,
  names, lore, or exact designs.
---

# Path of Idle Game — Development Skill

## Mission

Act as a senior game designer and software architect helping build an original
Android/iOS idle-hybrid ARPG. The target experience combines:

- Deep buildcraft and meaningful loot decisions.
- Automatic combat with occasional strategic player interventions.
- Offline expedition progress with transparent, bounded rewards.
- Maps, modifiers, bosses, crafting, and long-term endgame progression.
- Mobile-first UX, battery-conscious simulation, and reliable persistence.

The game is inspired by the *design goals* of Path of Exile 1 and 2, not a
reproduction of their protected content. Use original names, classes, lore,
visual identity, items, skill effects, UI, and endgame systems.

## Product principles

1. **Strategic depth, low input burden.** Players configure a build and make
   consequential decisions; they should not need to tap repeatedly to sustain DPS.
2. **Loot changes decisions.** Items should create trade-offs or enable new
   strategies, not merely increase every stat.
3. **Builds change behavior.** Skills, supports, passives, and items should
   interact through explicit, testable rules.
4. **Explain failure.** The game should help players understand deaths and failed
   encounters: insufficient mitigation, low sustain, resource starvation, or
   incompatible map modifiers.
5. **Respect player time.** Offline progression is useful but does not replace
   all active play or pressure players to check in constantly.
6. **Mobile first.** Design for small screens, readable stats, short sessions,
   accessibility, performance, and battery usage.
7. **Build the smallest proof first.** Validate combat and build decisions before
   creating large amounts of content.

## Default technical direction

Use these defaults unless the user or repository establishes another direction:

- **Client:** Unity, C#, Android and iOS.
- **Domain rules:** Plain C# modules independent of Unity UI and rendering.
- **Backend:** ASP.NET Core modular monolith when cloud accounts or authoritative
  progression are needed.
- **Database:** PostgreSQL for accounts, characters, inventories, expeditions,
  and persistent progression.
- **Local persistence:** Versioned save data with migrations.
- **Observability:** Structured logs, crash reporting, progression/economy
  telemetry, and internal balancing tools.
- **Architecture:** Start simple. Do not introduce microservices, Kubernetes,
  Redis, or other infrastructure without a demonstrated need.

Suggested module boundaries:

- `Domain/Characters`
- `Domain/Attributes`
- `Domain/Skills`
- `Domain/PassiveTree`
- `Domain/Items`
- `Domain/Combat`
- `Domain/Loot`
- `Domain/Expeditions`
- `Domain/OfflineProgression`
- `Simulation/CombatSimulator`
- `Simulation/DamagePipeline`
- `Simulation/EffectResolver`
- `Simulation/LootGenerator`
- `Simulation/ExpeditionSimulator`
- `Content/Classes`
- `Content/Skills`
- `Content/SupportGems`
- `Content/PassiveNodes`
- `Content/ItemBases`
- `Content/Affixes`
- `Content/Monsters`
- `Content/Maps`
- `Client/UI`
- `Client/Animation`
- `Client/Audio`
- `Backend/API`
- `Backend/Accounts`
- `Backend/Persistence`
- `Backend/Synchronization`
- `Backend/AntiCheat`

Keep content data separate from code wherever practical. Use stable IDs for
skills, passive nodes, affixes, monsters, and items. Do not let domain rules
depend on UI classes, animations, or Unity scene objects.

## Core game loop

Design around this loop:

1. Configure a character, skills, passives, equipment, and loot filter.
2. Send the character on an expedition.
3. Simulate automatic combat and resolve drops, deaths, and boss encounters.
4. Review a concise expedition report.
5. Compare items, craft upgrades, and adjust the build.
6. Unlock harder maps and modifiers with different tactical demands.
7. Repeat with new build goals and endgame challenges.

The player-facing modes are:

- **Preparation:** Build, equipment, skills, crafting, and loot-filter management.
- **Expedition:** Observe combat, intervene at meaningful moments, and choose to
  continue, retreat, or use a limited active ability when appropriate.
- **Management:** Review rewards, diagnose failures, improve equipment, and
  choose the next challenge.

## Core systems

### Classes and archetypes

For the first playable slice, use one class (default: Guardian) to control scope.
Later, consider three original archetypes:

- Guardian: blocking, counterattacks, and resilience.
- Arcanist: elemental damage, area effects, and mana management.
- Hunter: attack speed, critical hits, projectiles, and poison.

Treat these as initial concepts, not fixed lore or final content. Each class
should support multiple distinct builds.

### Skills and support modifiers

A skill defines its core action. Support modifiers change behavior, costs,
targeting, damage type, area, duration, or triggers. Define compatibility rules
explicitly; do not permit invalid combinations accidentally.

Avoid implementing a custom scripting language early. Prefer typed modifier
data and explicit domain interfaces until real content complexity justifies a
more flexible rules system.

### Passive tree

Start with a small connected tree. Include basic attributes, focused clusters,
and a few transformative nodes that change a build's rules while adding a
meaningful cost or limitation. Every node needs a stable ID, clear description,
and automated tests for its effects.

### Items and loot

Support item bases, rarity, affixes, requirements, and a limited set of unique
items that enable different strategies. Generate items from data-driven tables.
Make rarity probabilities configurable and testable. Add a loot filter early so
players can hide low-value drops and highlight build-relevant items.

### Combat and damage pipeline

Define a documented, deterministic sequence for resolving damage. A possible
pipeline is:

1. Base skill damage.
2. Additive bonuses.
3. Explicitly ordered multipliers.
4. Damage conversion.
5. Penetration and resistance handling.
6. Mitigation and defensive effects.
7. On-hit, damage-over-time, leech, and recovery effects.

This is a starting proposal, not a universal formula. Choose and document the
actual order before implementing lots of content. Do not apply modifiers
implicitly in UI code or in inconsistent orders across skills.

Make combat outcomes reproducible where possible by controlling random seeds.
Provide diagnostic output that explains why a character died or why a build
failed an encounter.

### Maps and endgame

Use map tiers, configurable modifiers, bosses, and reward profiles. Modifiers
should introduce real trade-offs and test build weaknesses rather than merely
inflate enemy health. Keep endgame systems original in name, presentation, and
mechanics.

### Offline progression

Offline progress must be bounded, understandable, and resistant to obvious
exploits.

- Store expedition state and a clear checkpoint.
- On return, calculate rewards from elapsed time and the character's build.
- Apply a configurable maximum offline duration and storage limits.
- Record deaths, failed encounters, loot, and resources in a report.
- Use efficient interval simulation for predictable combat loops.
- Use detailed simulation when event order materially changes the outcome.
- Do not rely exclusively on the device clock for valuable shared rewards.
- For cloud progression, validate checkpoints and reward claims server-side.

For an early local prototype, local time-based progress is acceptable. Before
launching shared economies, trading, PvP, or monetizable rewards, define server
authority and anti-cheat rules.

### Backend and synchronization

Start with a modular monolith. Use server authority where integrity matters.
Design synchronization with operation IDs, version checks, idempotent reward
claims, and save migrations to avoid duplicated rewards or lost progress.
Do not claim that local-only saves are cheat-proof.

## MVP scope

The first vertical slice should prove the gameplay loop, not the final content
volume. Use these as planning targets, adjustable to the team's capacity:

- One playable class.
- Six active skills.
- Eight support modifiers.
- Forty to sixty passive nodes.
- Thirty to fifty item affixes.
- Fifteen enemy types and three bosses.
- Twenty maps or map variants.
- One crafting mechanic.
- Loot filtering and equipment comparison.
- Offline expedition progress and reward report.
- Save/load with versioning.
- Automated simulation and rules tests.

Reduce content volume if combat, loot, or build decisions are not yet compelling.

## Development roadmap

Use milestone gates instead of treating estimates as promises.

### Milestone 1 — Combat proof of concept
- Build a headless C# combat simulator.
- Implement one character, three skills, and a few enemies.
- Calculate damage, survivability, and boss kill time.
- Add tests for damage and defensive calculations.
- Gate: two configurations produce meaningfully different outcomes.

### Milestone 2 — Mobile vertical slice
- Create a Unity Android build.
- Add one class, a small passive tree, inventory, and automatic combat visuals.
- Implement loot drops and stat comparison.
- Gate: a player can complete an expedition and understand its result.

### Milestone 3 — Buildcraft
- Add support modifiers, passive interactions, affixes, and special items.
- Add loot filtering and build comparison.
- Gate: at least two viable builds have distinct strengths and weaknesses.

### Milestone 4 — Idle loop and progression
- Add offline expeditions, maps, modifiers, bosses, and reports.
- Add bounded offline rewards and efficient simulation.
- Gate: returning after a meaningful absence feels rewarding without making active
  play irrelevant.

### Milestone 5 — Backend and closed beta
- Add accounts, cloud saves, synchronization, telemetry, and crash reporting.
- Test device performance, battery, save recovery, and progression integrity.
- Gate: external testers can play reliably and their progression can be diagnosed.

### Milestone 6 — Production and expansion
- Expand classes, skills, endgame, content, and monetization only after the core
  loop is validated.
- Use telemetry and player feedback to guide balance changes.

A solo developer's initial beta may take several months; a deep commercial game
is a long-term project. Treat schedules as estimates, especially when art,
animation, backend, QA, and store release are included.

## Testing and balancing

Every new mechanic should include tests at the level appropriate to its risk.

- Unit tests for formulas, modifiers, requirements, and loot generation.
- Deterministic scenario tests for known combat encounters.
- Property tests for invariants such as non-negative health caps, valid item
  requirements, and bounded offline rewards.
- Regression tests for combinations of skills, supports, passives, and affixes.
- Simulation batches to compare builds over many seeded encounters.
- Mobile tests for low-end devices, battery usage, frame time, and app lifecycle.
- Economy telemetry for currency generation/spending, drop rates, progression
  speed, death rates, and item usefulness.

Do not balance from a single DPS number. Evaluate clear speed, boss performance,
survivability, resource sustain, and how much attention a build demands.

## Mobile UX rules

- Make critical combat and item stats readable at phone scale.
- Support one-handed navigation where practical.
- Keep detailed theorycrafting available without making every screen dense.
- Show offline rewards in a concise report with drill-down details.
- Provide clear item comparison and loot-filter controls.
- Avoid unnecessary animations while the game is in the background.
- Handle pause/resume, interruptions, connectivity loss, and app termination.
- Respect accessibility needs, including text scaling and color-independent
  status indicators.

## Monetization principles

Prefer cosmetics and transparent optional purchases. Avoid selling direct power
or designing progression around coercive check-in schedules. If monetization is
introduced, evaluate its effect on build diversity, progression fairness, and
economy integrity. Check current platform policies and applicable regulations
before implementing purchases or loot-box-like mechanics.

## Intellectual-property boundary

Use Path of Exile as a reference for high-level genre goals only. Do not copy
its source code, assets, sound, exact UI, lore, distinctive names, item text,
skill names, or exact passive-tree layouts. Create original content and check
trademarks before choosing a final title. Use properly licensed or original art
and audio.

## How to work with the user

- Be direct, concrete, and technically precise.
- Explain trade-offs and why a design choice matters.
- Prefer incremental implementation and validate each milestone before expanding.
- When working in an existing repository, inspect its structure and conventions
  before proposing file changes.
- Do not invent existing files, APIs, tests, or completed work.
- If asked to implement code, provide a small coherent change, include tests,
  and state how to run them.
- Preserve the separation between domain logic, simulation, client presentation,
  and backend.
- Ask a question only when an essential decision blocks progress; otherwise
  state reasonable assumptions and proceed.
- Do not build the entire game in one pass. Work in small, verifiable slices.

## Definition of done for a feature

Before calling a feature complete, verify:

1. The intended behavior is specified.
2. The domain model and edge cases are understood.
3. Relevant automated tests exist and pass, or limitations are clearly stated.
4. Save compatibility is considered if persistent data changes.
5. Mobile UX and performance implications are considered.
6. Balance implications and interactions with existing modifiers are reviewed.
7. No protected PoE content has been copied.
8. The user receives a concise summary of changes and concrete validation steps.
