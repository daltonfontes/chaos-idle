# Passive tree — the Wayfinder's Wheel

One round tree shared by every class, in the style of large ARPG passive trees, with
original content: every layout, node, name and number here is our own.

## Layout

- **Three class starts** near the centre, 120° apart: Guardian (210°), Arcanist (90°),
  Hunter (330°). A class can never take or walk through another class's start.
- **An inner ring** of 12 road nodes. Each start touches the three ring nodes in front of it.
- **12 spokes** lead from the ring to **12 themed clusters**. Each cluster is four minor nodes
  around a notable, and taking two minors closes a small loop.
- **12 bridges** join neighbouring clusters, so there is more than one way around.
- **6 keystones** sit on the rim, each behind one cluster's outward minor.
- **Road nodes** give small stats that follow the region: Vigour (+10 life) near the Guardian,
  Focus (mana and mana regeneration) near the Arcanist, Poise (accuracy and attack speed) near
  the Hunter.

That makes 111 nodes. Positions are in `PassiveNode.X/Y` (origin at the centre), and
`PassiveTreeDefinition.Links` lists each connection once, for drawing.

| Angle | Cluster | Notable |
|---|---|---|
| 0° | Precision | Deadeye Calm: accuracy, crit chance |
| 30° | Storm | Stormcaller: lightning damage, penetration |
| 60° | Insight | Searing Focus: crit chance and multiplier |
| 90° | Spellcraft | Kindled Mind: spell damage, cast speed |
| 120° | Hearth | Hearthfire: fire and elemental damage |
| 150° | Wards | Warded Soul: all resistances |
| 180° | Forge | Forge Temper: phys→fire conversion, fire damage |
| 210° | Vitality | Unbroken: life, regeneration |
| 240° | Bulwark | Shieldwall: block, life on block |
| 270° | Iron | Ironclad: armour |
| 300° | Bloodletting | Answering Steel: physical damage, attack speed |
| 330° | Swiftness | Wind Step: attack speed, evasion |

| Keystone | Behind | Rule change | Cost |
|---|---|---|---|
| Steady Hand | Storm | 30% more damage | you can't crit |
| Overcharge | Spellcraft | 40% more spell damage | 50% more mana cost |
| Unhurried Mind | Wards | 60% more mana regeneration | 15% less cast speed |
| Ember Covenant | Forge | all physical damage converted to fire | 20% less armour |
| Unyielding Bastion | Bulwark | block applies to spells | 20% less life |
| Glass Focus | Bloodletting | 30% more damage | 25% less life |

## Rules

- `PassiveAllocation` starts at the class's start node.
- **Single nodes:** a node can be taken when it touches what you have and a point is free.
- **Paths:**
  - `PathTo(target)` is the shortest route (deterministic, never through another class's start).
  - `CanAllocatePath` gives its cost.
  - `AllocatePath` takes it all at once.
- **Refunds** are free for now (there's no currency yet), and are refused if they would cut
  other nodes off.
- **Content:** starter and reference allocations are written as **targets**
  (`PassiveAllocation.FromTargets`), so they follow layout changes.

## Saves

Saves from before the redesign name the old tree (`tree.guardian`). On load, the character
moves to its class's tree (`CharacterClassDefinition.PassiveTreeId`), every point is
refunded, and a warning says so. Unknown or unreachable nodes in a current save are refunded
one by one, also with warnings.

## Balance notes

- **Bog Tyrant** (tier 1 boss) is now 1600 life and 250 armour, with lighter crushes. A fresh
  Guardian (starter passives and gear) wins the first map with its boss about 99% of the time,
  and a test requires at least 85%.
- **Ember Lancer** now goes Vitality → Hearthfire → Overcharge. The Guardian start is far from
  the spell clusters, so a Guardian caster pays for the trip. It still clears trash about 3×
  faster than the Iron Bastion and runs short of mana against the boss.
