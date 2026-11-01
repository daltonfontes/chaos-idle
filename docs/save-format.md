# Save format

Local, versioned saves with migrations. This is the first part of Milestone 5: cloud saves
and server-side claims will build on the same data.

## File layout

One slot is `<slot>.json` plus `<slot>.json.bak`, the previous good save.

```json
{
  "format": "chaos-idle-save",
  "schemaVersion": 1,
  "revision": 12,
  "savedAtUtc": "2026-11-01T09:57:24Z",
  "checksum": "sha256:…",
  "payload": { "character": { … }, "mapProgress": { … }, "lootFilter": { … },
               "offline": { … }, "claimedOfflineSessions": [ … ] }
}
```

- **`checksum`** is SHA-256 of the exact `payload` text. It detects damaged or truncated
  files before anything is read. It isn't anti-cheat: anyone can recompute it.
- **`revision`** goes up by one on every save. Milestone 5 sync will use it to detect
  conflicts between devices.
- **The payload stores IDs and rolled values only**, for example
  `{ "affixId": "affix.life", "tier": 2, "value": 30 }`. It never stores names, stat text or
  content numbers, so content balance changes apply to existing saves automatically.
- **Offline seeds are stored as strings**, because JSON numbers lose precision above 2^53.

## What is saved (`SaveData`)

| Section | Contents |
|---|---|
| `character` | id, name, bag capacity, bag items, and the **loadout** |
| `character.loadout` | class, passive tree, points, allocated nodes, equipment by slot, primary and triggered skills, supports by skill |
| `mapProgress` | highest unlocked tier, bosses defeated |
| `lootFilter` | name, default action, ordered rules (enums stored by name) |
| `offline` | the running session: id, map, modifiers, UTC start, seed, and the **frozen loadout** from when it started |
| `claimedOfflineSessions` | the last 100 claimed session ids; restoring an older save can't make a session claimable again |

The domain side is `Domain/Profile/GameState`: the character, progress, loot filter, the
running offline session, claimed sessions, and the revision. `OfflineCheckpoint.Start` now
keeps a copy of the loadout (`PlayerCharacter.CloneLoadout`), so later gear changes never
leak into a session, even across saves.

## Loading rules

| Situation | Result |
|---|---|
| Not JSON, truncated, wrong `format`, missing fields | `Corrupt` |
| Payload doesn't match the checksum | `ChecksumMismatch` |
| `schemaVersion` newer than the game | `NewerVersion`: refused, and the backup is **not** used, so a newer save is never silently replaced by an older one |
| Older `schemaVersion` | upgraded one step at a time by `ISaveMigration`s, on the raw JSON, before it is read; a missing step is `NoMigration` |
| Unknown class, passive tree or primary skill | `MissingContent`: fatal, because the save can't be played without them |
| Unknown item base, unique, affix, support, triggered skill, map modifier, equipment slot | dropped, unlinked, or moved to the bag, **with a warning**; the save still loads |
| Passives that are unknown, unreachable, or over budget | refunded, with a warning |

`SaveStore.Load` tries the main file and then the backup. It returns the game, which file it
came from, and every problem it found. The client should show the warnings once, for
example "An item from an older version was removed."

## Writing

`SaveStore.Save` writes `<slot>.json.tmp`, flushes it to disk, then swaps it in with
`File.Replace` (old main → `.bak`). A crash mid-write leaves either the old save or the new
one, never half of one.

## Adding a schema change

1. Bump `SaveSerializer.CurrentVersion`.
2. Add an `ISaveMigration` whose `FromVersion` is the old version. It edits the `JsonObject`
   payload in place (rename, move, default a new field).
3. Register it where the serializer is created.
4. Add a test that loads a payload in the old shape.

## Unity notes

- The save objects are plain classes with public get/set, so Newtonsoft.Json
  (`com.unity.nuget.newtonsoft-json`) can read them if `System.Text.Json` causes trouble
  under IL2CPP code stripping. Keep the property names identical (camelCase).
- Use `Application.persistentDataPath` as the save directory.
- Save when the app is paused or backgrounded (`OnApplicationPause(true)`), and start the
  offline checkpoint at the same moment.

## Not covered yet

- Cloud sync and conflict resolution (the next part of Milestone 5).
- Encryption. Not planned: local saves aren't trusted anyway, and the server will own
  anything valuable.
