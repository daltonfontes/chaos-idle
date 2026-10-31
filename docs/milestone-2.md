# Milestone 2 — vertical slice

**Gate:** a player can complete an expedition and understand its result.

Milestone 2 has two halves:

- **Domain half: done.** All the rules for the slice, written as plain C#, with tests.
- **Client half: not started.** The Unity Android client and combat visuals. They need the
  Unity editor, which isn't available in the cloud environment.

## What exists (domain)

### Passive tree — `Domain/PassiveTree`, `Content/PassiveTrees.cs`
- The tree is an undirected graph with one free start node. Construction rejects a tree with
  duplicate IDs, unknown links, no start or several starts, or nodes the start can't reach.
- `PassiveAllocation` is immutable. Allocating a node requires an adjacent allocated node
  and a free point. Refunding is refused if it would cut other nodes off from the start.
  Each refusal has a typed reason (`AllocationError`) that the UI can show.
- The Guardian tree has 26 nodes in four branches: defence, melee, spell, and wards.
- It has two keystones, each changing a rule at a real cost:
  - **Unyielding Bastion**: block also applies to spells, but you have 20% less maximum life.
  - **Overcharge**: 40% more spell damage, but skills cost 50% more mana.

### Items — `Domain/Items`, `Content/ItemBases.cs`, `Content/Affixes.cs`
- 13 bases across 8 item classes and 8 equipment slots. Rings fit either ring slot.
- 22 affix groups: 9 prefixes and 13 suffixes, each with three tiers. Tier 3 can roll from
  item level 1, tier 2 from item level 5, and tier 1 from item level 10.
- **Local affixes** change the item's own base values: weapon damage, speed and crit, and
  armour or evasion on armour pieces. All other affixes, plus implicits and shield block,
  apply to the character.
- **Rarity**:
  - Normal items have no affixes.
  - Magic items have 1–2 affixes (at most one prefix and one suffix).
  - Rare items have 3–5 affixes (at most three prefixes and three suffixes).
  - An item never has two affixes from the same group.
- `Inventory` has a fixed capacity and never drops items silently.
  `PlayerCharacter.EquipFromInventory` swaps the old item back into the bag.

### Loot — `Simulation/Loot/LootGenerator.cs`
- Loot is generated from data tables using a seed. Rarity weights default to 70/25/5;
  bosses multiply the magic and rare weights by 4. All weights are configurable.
- Each enemy has `ItemDropChance` and `GuaranteedDrops`. The Ash Warden always drops 3 items.
- Item level comes from the expedition's area level.
- Loot uses its own random stream (`Seeds.Derive`), so changing loot tables never changes
  combat outcomes.

### Comparison — `Simulation/Analysis`
- `BuildSummary` is the character sheet: life, mana and its upkeep, armour, block,
  resistances, expected DPS, and crit. It's computed with the same pipeline functions the
  simulator uses.
- `ItemComparer.Compare` gives a stat-by-stat diff and a verdict: `Upgrade`, `Downgrade`,
  `TradeOff`, or `NoChange`. Extra mana spending is informational only while regeneration
  still covers it.
- `ItemComparer.Simulate` answers "will this help against that encounter?" by running the
  same seeds with and without the item.

### Expeditions — `Domain/Expeditions`, `Simulation/Expeditions`
- An expedition is an ordered list of encounters. The character starts each encounter at
  full life and mana, and the expedition stops at the first failure.
- Loot from enemies killed before a failure is kept. This is a "respect player time" choice;
  revisit it in Milestone 4 when tuning offline rewards.
- `ExpeditionRunner.Run` is pure: it doesn't change the character. `ExpeditionReport.ClaimLoot`
  moves loot into the bag, reports anything left behind, and refuses a second claim. That
  refusal is the seed of the idempotent claims needed in Milestone 5.
- `ReportText` produces short, phone-sized English lines:
  - the expedition report, with ✓/✗ per encounter, "Why it failed" and "Try" lines;
  - item tooltips;
  - comparisons, where "▲ better" / "▼ worse" carry the meaning in words rather than colour.

### Content
- Two expeditions: **Road Patrol** (area level 3, one trash encounter) and
  **The Ashen Trail** (area level 8, two trash encounters then the Ash Warden).
- `StarterCharacters.NewGuardian()` creates a fresh Guardian with a normal mace and a normal
  buckler. `BulwarkPath` is a suggested allocation for the first 12 passive points.

## How the gate is checked

- `Milestone2GateTests` checks three things:
  - a starter Guardian completes Road Patrol in at least 27 of 30 seeds;
  - every expedition report, completed or failed, has a status headline, per-encounter
    lines, a reason and advice when it fails, and short lines;
  - every death or timeout, across all builds and encounters, has at least one diagnosis.
- `dotnet run --project src/ChaosIdle.Cli -- expedition 1` plays the loop as text:
  1. a starter Guardian tries The Ashen Trail and fails, with a reason;
  2. it farms Road Patrol, compares loot, and equips upgrades;
  3. it tries again, gets further, and fails for a different, explained reason.

## Not done: the Unity client

This needs a machine with Unity (2022 LTS or newer, with the Android module):

1. **Bring in the domain.** Run `dotnet build -c Release`, then copy
   `ChaosIdle.Domain.dll`, `ChaosIdle.Simulation.dll` and `ChaosIdle.Content.dll` from
   `src/*/bin/Release/netstandard2.1/` into `Assets/Plugins/ChaosIdle/`. The libraries
   target `netstandard2.1` with C# 9 for this reason; Unity needs nothing else.
2. **Screens**, each a thin view over existing APIs:
   - Character: `BuildSummary.Compute(character.ToBuild())`.
   - Passive tree: `PassiveAllocation.CanAllocate` / `Allocate` / `CanRefund`. Show the
     `AllocationError` reason text when a node can't be taken.
   - Inventory and equipment: `Inventory`, `EquipFromInventory`, `ReportText.ItemTooltip`.
   - Item comparison: `ItemComparer.Compare` and `ReportText.Comparison`.
   - Expedition: `ExpeditionRunner.Run`, then `ReportText.Expedition` and `ClaimLoot`.
3. **Combat visuals.** Replay a finished `CombatReport` rather than simulating in the
   scene, so visuals can never change outcomes. The report needs a per-event timeline
   first. That is a small addition: `CombatSimulator` already has every event at the point
   it records totals.
4. **Saves** aren't part of Milestone 2's gate. Persistent IDs already exist on every
   definition, and item instances carry IDs for their base and affixes, ready for a
   versioned save format.

## Known limitations

- No character levels or item requirements yet.
- No unique items yet; they belong to Milestone 3 ("special items").
- Single-target combat only.
- The Ash Warden is out of reach for a freshly geared starter. Expect failures there to
  report the reason (mitigation or resistances) until the character improves. That's
  intended, but how far out of reach it should be is open for tuning.
