# Combat pipeline (Milestone 1)

This is the single, authoritative order in which hits are calculated. The code lives in
`src/ChaosIdle.Simulation/Damage/DamagePipeline.cs`; formulas are in
`src/ChaosIdle.Domain/Combat/CombatFormulas.cs`. Changing the order or a constant is a
balance change: update this document and the tests in `DamagePipelineTests` /
`CombatFormulasTests` together.

## Stats and modifiers

Every stat value is `Flat × (1 + ΣIncreased/100) × Π(1 + More/100)`.

- **Flat** values add together.
- **Increased** percentages add together, then apply once.
- **More** percentages each multiply separately.

On damage stats (`PhysicalDamage`, `FireDamage`, …), **Flat means added damage**.

Resistances, block chance, life leech, life on block, penetration, and crit multiplier
use only their Flat value.

## Outgoing hit (attacker side)

1. **Base damage**
   - Attacks: weapon physical roll × skill damage effectiveness.
   - Spells: the skill's own base damage roll. Effectiveness does **not** scale this.
2. **Added damage**: Flat damage-type stats × skill damage effectiveness (attacks and spells).
3. **Conversion**: each conversion moves a percentage of the *pre-conversion* amount.
   Conversions do not chain (phys→fire→cold moves nothing to cold), and at most 100% of a
   type may be converted (validated).
4. **Scaling** per *final* damage type. Increased values are summed across the relevant
   stats, then More multipliers are applied. The relevant stats are:
   - `Damage` and the type's own stat (`PhysicalDamage`, `FireDamage`, …);
   - `ElementalDamage` if the type is fire, cold, or lightning;
   - `AttackDamage` or `SpellDamage`, depending on the skill.

   Converted damage benefits only from the modifiers of its final type. This is a
   deliberate simplification for now.
5. **Critical strike**
   - Chance = (base + flat) × crit scaling, capped at 100%. The base comes from the weapon
     for attacks and from the skill for spells.
   - A crit multiplies all damage by (150% + flat crit multiplier).

## Defender side

6. **Evasion** (attacks only): hit chance = `acc / (acc + evasion × 0.25)`, clamped to [5%, 100%].
7. **Block**: applies to attacks, and also to spells if the defender has
   `BlockAppliesToSpells` (granted by the Unyielding Bastion keystone). The chance is capped
   at 75%. A blocked hit deals 0 damage and fires `OnBlock` triggers and life-on-block.
8. **Armour** (physical only): reduction = `armour / (armour + 8 × hit)`, capped at 85%.
   It is strong against many small hits and weak against one big hit.
9. **Resistance** (elemental): `min(res, 75) − penetration`, floored at −100%.
   Negative resistance increases damage taken.
10. **After the hit**: damage is subtracted from life. The attacker leeches
    `LifeLeechPercent` of the damage dealt, instantly and capped by missing life.
    Overkill is not counted as damage dealt.

Spells can never be evaded, and are blocked only with `BlockAppliesToSpells`. Otherwise,
resistances and the life pool are the answer to spells.

## Randomness

- All randomness goes through `IRandom`. The production implementation is `SplitMix64Random`.
  It is fully specified, so a seed gives the same result on .NET, Unity, and a future
  server. A test pins its output sequence.
- Each roll consumes a fixed number of random values whatever the outcome:
  - an attack hit uses 2 (weapon roll, crit);
  - a spell uses 1 per base damage range, plus 1 for crit;
  - defence resolution always uses 2 (evade, block), even for spells.

  Because of this, comparing two builds on the same seed compares like with like
  (common random numbers).

## Combat loop

`CombatSimulator` is event-driven:

- **Turn order**: the next action is whichever of the player or the current enemy is due
  first. On ties, the player acts first.
- **Regeneration**: life and mana regenerate continuously between events.
- **Mana cost** per use is `(skill cost + flat ManaCost) × ManaCost scaling`, and never
  negative.
- **Mana**: if the primary skill can't be afforded, the character waits exactly until it
  can. This waiting is reported as time out of mana. Triggered skills cost no mana and use
  their own cooldowns.
- **Enemies**: they are fought one at a time, in encounter order. Each enemy uses the first
  attack that is off cooldown. Attacks with a cooldown *start* on cooldown, so big attacks
  have a wind-up.
- **End of the fight**: the fight ends on victory, death, or the time limit (300 s by
  default; this is reported as `Timeout`).

## Failure diagnosis

`FailureAnalyzer` turns a report into player-facing reasons:

| Code | Rule |
|---|---|
| `InsufficientDamage` | The fight timed out. |
| `LowResistance` | Died; ≥50% of the last 5 s of damage was one element, and that resistance was below the cap. |
| `LowPhysicalMitigation` | Died; ≥50% of the last 5 s of damage was physical, and armour prevented <30% of physical damage. |
| `LargeHitSpike` | The killing blow was ≥40% of max life. |
| `LowSustain` | Died after recovering <15% of the damage taken. |
| `ResourceStarvation` | ≥15% of the fight was spent waiting for mana. |
| `Overwhelmed` | Died, and no other death rule matched. It reports damage per second at the end and the main attack, so every death has a reason. |

## Expected values (tooltips and comparison)

`DamagePipeline.ExpectedHit` runs steps 1–4 with every base roll at its midpoint, then
multiplies by `1 + critChance × (critMultiplier − 1)`. `BuildSummary` and `ItemComparer`
use it; the simulator never does. Because both share the same step functions, tooltips
can't drift from combat.

## Known simplifications (to revisit)

- Single target only. There is no area damage or multi-enemy packs yet.
- No damage over time, ailments, or chaos damage.
- Added damage is a fixed average, not a min–max range.
- Leech is instant.
- Enemies don't crit, regenerate, or have block.
