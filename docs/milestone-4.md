# Milestone 4 — idle loop and progression

**Gate:** returning after a meaningful absence feels rewarding, without making active play
irrelevant.

## What exists

### Maps and modifiers — `Domain/Maps`, `Content/Maps.cs`

| Tier | Map | Area level | Packs | Boss |
|---|---|---|---|---|
| 1 | Sunken Causeway | 6 | hounds and brutes | **The Bog Tyrant** (new): slow, armoured, telegraphed crushes |
| 2 | Ashen Trail | 9 | Ashen Road ×2 | The Ash Warden |
| 3 | Rimewood Hollow | 12 | cold casters and fast lightning runners (both new) | **The Rimeheart Matron** (new): cold spells and mixed lashes |

- A `MapInstance` is a map plus up to 3 modifiers. Applying it is pure: it returns modified
  copies of the enemies, adds the player-side modifiers to the build, and passes the loot
  bonuses to the loot generator.
- Each of the 8 modifiers tests a different weakness and pays for it in loot (a test checks
  both):
  - Hardened (life), Savage (damage), Frenzied (speed);
  - Frostbitten (extra cold damage on every hit);
  - Searing Air (−25% fire resistance for you), Withering (60% less life regeneration for you);
  - Warded (enemy elemental resistance), Ironclad (enemy armour).
- **Loot bonuses:**
  - Quantity scales each enemy's drop chance; above 100% it adds whole drops.
  - Rarity scales the magic, rare and unique weights.
- **Progression** (`MapProgress`): only an **active** kill of the highest unlocked tier's
  boss unlocks the next tier.

### Offline expeditions — `Domain/OfflineProgression`, `Simulation/Offline`

1. **Start.** `OfflineCheckpoint.Start` freezes a snapshot of the build, the map, the start
   time (UTC) and a seed. Changing gear afterwards doesn't affect the session.
2. **Resolve.** `OfflineCalculator.Resolve(checkpoint, now)`:
   - **Time credited:**
     - The time away is capped at **8 h**.
     - Under **1 minute** away earns nothing.
     - A clock that **went backwards** credits nothing, and the report says so.
   - **Efficiency:** offline time counts at **60%**.
   - **No boss:** offline runs are the map's packs only; the boss is never fought offline.
   - **Efficient simulation:**
     - Run 24 detailed, seeded simulations of the packs.
     - Play the session as a timeline: each run draws one of those outcomes, then pays its
       duration plus 15 s of travel, plus 60 s if it failed.
     - Completed runs roll real loot. Failed runs bring nothing back.
     - A test checks the run count and item count against a brute-force simulation of every
       run (within 10% and 20%).
   - **Deterministic:** the same checkpoint resolved at the same time always gives the same
     result.
3. **Claim.** `OfflineResult.Claim(inventory, filter, settings)`:
   - Applies the loot filter, takes highlighted items first, and stops at the **storage
     cap (60)** and at the bag's free space.
   - Reports what was hidden, over the cap, or had no bag space.
   - A second claim throws.
4. **Report.** `ReportText.Return` is the "welcome back" text: time away and time counted;
   runs and success rate; why failed runs failed, with advice; loot found; what was brought
   home.

All settings live in `OfflineSettings` and are meant for tuning.

### Measuring "rewarding" vs "active matters" — `Simulation/Offline/RewardRates.cs`

- `LootValue` is a placeholder value for tests and telemetry only (there's no economy yet):
  normal 1, magic 3, rare 10, unique 40.
- `RewardRates.BestActive` compares offline with the **better** of the two active options:
  packs only, or packs plus boss. A real player skips a boss they can't beat, so comparing
  against "always fight the boss" would flatter offline. Measuring this way showed offline
  at 166% of active on Rimewood Hollow before the fix.

Results for 10 h away (8 h counted), seed 1 (`dotnet run --project src/ChaosIdle.Cli -- offline`):

| Build | Map | Offline runs | Found | Offline vs best active, value per hour |
|---|---|---|---|---|
| Iron Bastion | Sunken Causeway | 511 (100%) | 885 items, 6 unique | 28% |
| Iron Bastion | Rimewood Hollow (Savage, Frostbitten) | 558 (100%) | 1235 items, 15 unique | 49% |
| Ember Lancer | Ashen Trail (Hardened) | 427 (100%) | 1206 items, 9 unique | 66% |
| Ember Lancer | Rimewood Hollow (Frostbitten, Frenzied) | 831 (100%) | 1850 items, 19 unique | 46% |

## The gate

`Milestone4GateTests` checks, for four build-and-map pairs:

- **Rewarding:** 8 h away gives a success rate of at least 90%, at least 100 items, and at
  least 10 rares or uniques.
- **Active still matters:**
  - offline items per hour are at most 75% of the best active option, and loot value per
    hour at most 85% (efficiency is 60%; the margin absorbs loot noise);
  - bosses, their guaranteed drops, and tier unlocks are active-only.
- **Bounded:** 7 days away earns exactly what 8 h does.

## Not cheat-proof

This is local-only progression:
- the clock is the device clock;
- the checkpoint lives in memory and nothing is saved yet.

The protections (the cap, rejecting a backwards clock, claiming once, freezing the build) stop
accidents and casual abuse, not a determined player who edits saves. Before shared economies,
trading or paid rewards, Milestone 5 must move checkpoints and claims to the server
(server time, operation IDs, claims that can't be paid out twice).

## Known limitations and tuning notes

- **Too many drops.** Sessions find hundreds of items, mostly normal. The strict loot filter
  and the storage cap keep the bag sane, but base drop rates will need economy tuning
  (`ItemDropChance`, `LootSettings`) once there's crafting or currency to spend loot on.
- **Rimewood Hollow's boss is out of reach** for both reference builds; its packs aren't.
  That's fine for the gate (the active comparison skips the boss), but it means tier 3
  can't be unlocked yet.
- **No saving yet.** Checkpoints and map progress aren't persisted; the save format is
  Milestone 5 work.
- **Unity:** no client yet. App lifecycle (pause, resume, kill) should create the checkpoint
  when the app goes to the background and resolve it when the app returns.
