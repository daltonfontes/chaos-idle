# Milestone 3 — buildcraft

**Gate:** at least two viable builds have distinct strengths and weaknesses.

## What exists

### Support modifiers — `Domain/Skills/SupportDefinition.cs`, `Content/Supports.cs`
- A support changes only the skill it's linked to. Its modifiers go into that skill's stat
  block, never into the character sheet.
- Compatibility is explicit:
  - `RequiredTags`: the skill must have all of them.
  - `ExcludedTags`: the skill must have none of them.
  - At most 3 supports per skill, and no duplicates.
- Every refusal has a typed reason (`SupportCompatibility`) the UI can show.
- There are 8 supports. Each one either has a cost (more mana, or less damage or speed) or
  is restricted to certain skills:

| Support | Needs | Effect | Cost |
|---|---|---|---|
| Heavy Impact | melee attack | 35% more physical damage | 10% less attack speed, 30% more mana |
| Searing Edge | attack | 50% phys→fire, 15% more fire damage | 20% more mana |
| Focused Intent | spell | 30% more spell damage | 40% more mana |
| Keen Edge | any | +100% crit chance, +30% crit multiplier | 20% more mana |
| Siphon | any | 2% leech for this skill | 10% less damage |
| Ready Retort | triggered | 60% faster cooldown, 20% more damage | — |
| Frugal Casting | not triggered | 35% less mana cost | 10% less damage |
| Sundering Focus | any | 15% elemental penetration | 20% more mana |

### Unique items — `Domain/Items/UniqueDefinition.cs`, `Content/Uniques.cs`
- A unique is a fixed set of modifiers on a fixed base. It reuses item, tooltip, comparison
  and loot-filter code unchanged.
- Uniques drop with weight 1 against 70/25/5 for normal, magic and rare. Bosses get ×4.
- Each unique enables a strategy and has a drawback the tests check for:

| Unique | Enables | Costs |
|---|---|---|
| The Kindled Oath (mace) | 60% phys→fire, so fire passives and penetration apply to melee | −15% fire resistance |
| Mirrorglass Ward (shield) | block applies to spells without the keystone | −60 maximum life |
| Bloodbound Pendant (amulet) | 2–3% leech on every skill | 50% less life regeneration |
| Stillwater Helm (helmet) | skills cost 30% less mana | −10% fire resistance |

### Loot filter — `Domain/Loot/LootFilter.cs`, `Simulation/Analysis/BuildRelevance.cs`
- The filter is an ordered list of rules: the first matching rule decides, otherwise a
  default action applies.
- A rule can match on rarity range, item class, minimum item level, and the number of
  affixes on stats the player cares about.
- **Actions:**
  - `Hide`: never picked up.
  - `Highlight`: picked up first (so a nearly full bag keeps what matters) and marked ★ in
    reports.
  - `Show`.
- **Build preset** (`BuildRelevance.FilterFor(build)`): it works out the stats the build
  uses from its skill, damage types, defences and triggers. It highlights uniques and items
  with two or more wanted affixes, and hides magic items with none.
  - `strict: true` also hides normal items, for characters whose gear is already full.

### Build comparison — `Simulation/Analysis/BuildComparer.cs`
- `Profile` runs a build through named scenarios over many seeds. `IsViable(rate)` checks
  the win rate in every scenario.
- `Compare` lists each meaningful difference separately, never as a single score:
  - success rate (a gap of 20 percentage points or more);
  - clear time (1.25× or more);
  - damage taken per second (1.25× or more);
  - life left (15 percentage points or more);
  - mana starvation.
- `ReportText.BuildComparison` lists the strengths of each build.

## The gate

`ReferenceBuilds` contains two Guardians with the same investment: 18 passive points, 8 gear
slots at roughly tier 2, and 3 support sockets per skill. Both are built only from real
systems.

**Iron Bastion** — Iron Cleave with Heavy Impact, Siphon and Keen Edge; Bulwark Retort with
Ready Retort and Heavy Impact; the block, armour and melee passives.

**Ember Lancer** — Cinder Lance with Focused Intent, Keen Edge and Sundering Focus; the
spell branch with the Overcharge keystone; Stillwater Helm.

Result over 100 seeds (`dotnet run --project src/ChaosIdle.Cli -- builds`):

| | Iron Bastion | Ember Lancer |
|---|---|---|
| Ashen Road (trash) | 100%, 13.0 s | 100%, **7.4 s** (1.8× faster) |
| Warden's Hearth (boss) | 100%, 39 s, **90% life left** | 99%, 44 s, 56% life left |
| Mana | sustainable | runs dry in every boss fight |

Both builds are viable. Each one's strengths are the other's weaknesses: the Lancer trades
safety and mana for speed. `Milestone3GateTests` checks all of this.

## Known limitations

- **Unity:** no client yet (see `docs/milestone-2.md`).
- **Scenarios:** only one trash pack and one boss. Map modifiers (Milestone 4) will test
  builds in more ways, for example elemental-heavy or fast-attacking enemies.
- **Uniques:** the drop rate (1 in about 101 drops) is a placeholder for economy tuning.
- **Saves:** support links and loot filters aren't persisted yet, because there's no save
  format yet.
