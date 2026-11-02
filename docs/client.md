# Godot client

`client/` is a Godot 4 project with C# (.NET 9, which Godot 4.7's Android export requires). The project first planned Unity and moved to
Godot because:
- Godot is free and light to install;
- it uses C#, so the game libraries plug in unchanged;
- its C# SDK comes from NuGet, so CI (and Claude's cloud environment) can compile the client
  and run it without the editor.

## How it is built

| Piece | What it does |
|---|---|
| `src/ChaosIdle.App` (`GameSession`) | Everything the screens do, with no Godot types: load or create the save, the character sheet, expeditions (active, boss included, tier unlocks), the bag (describe and compare, equip, discard), and local offline progress on pause/resume. Unit-tested in `GameSessionTests`. |
| `client/Main.cs` + `Main.tscn` | The main screen, built in code (portrait 720×1280, larger font), with three tabs. **Adventure:** character sheet, maps, report, bag. **Passives** and **Skills:** `client/BuildTabs.cs`. |
| `client/BuildTabs.cs` | **Passives:** the tree as a list grouped by branch, with text markers (● taken, ○ available, · locked, ★ keystone); tapping a node previews what taking or refunding it changes, or says why it can't. **Skills:** choose the main skill, see each skill's supports and free sockets; tapping a support previews its effect or explains why it doesn't fit. |
| `client/Screenshots.cs` + `Screenshots.tscn` | CI only: opens the real main screen, plays an expedition, and saves a PNG of each tab. |
| `client/Smoke.cs` + `Smoke.tscn` | A headless check run by CI **inside the Godot runtime**: new game, an expedition, background the app, relaunch, come back three hours later with offline loot. |
| `client/export_presets.cfg` | The Android preset: arm64, debug-signed, package `com.chaosidle.game`. |

**App lifecycle:**
- `NotificationApplicationPaused` and `NotificationWMCloseRequest` save the game and start an
  offline session on the highest unlocked map.
- `NotificationApplicationResumed`, and the next launch, resolve it and print the
  "welcome back" report. Under a minute away keeps the session running.
- If a save can't be read, nothing is overwritten. The screen offers "Start over", which
  renames the damaged files aside.

This is local offline progress (Milestone 4 rules). Switching to the server's offline
sessions comes with sign-in (see `docs/backend.md`).

## Getting the APK (no local install needed)

The **Godot client** workflow (`.github/workflows/android.yml`) runs on every push and PR:

1. installs Godot (.NET) and the export templates, version `GODOT_VERSION`;
2. imports the project and builds the C#;
3. runs the smoke test in the Godot runtime;
4. takes screenshots of each tab with the real renderer on a virtual display (artifact
   `screenshots`);
5. exports `chaos-idle-debug.apk`, signed with a throwaway debug key generated in CI.

Download it from the workflow run's **Artifacts** section (`chaos-idle-debug-apk`), unzip
it, copy the `.apk` to an Android phone, and open it. Android asks once to allow installs
from that source.

A debug key changes every run, so to install a newer APK over an older one Android may ask
you to uninstall first. A fixed release key, kept in GitHub secrets, comes before any store
release.

## Working on it locally (optional)

- Install Godot 4.7.2 **.NET** (the "mono" build) and open `client/project.godot`. The editor
  version must match `Godot.NET.Sdk` in `client/ChaosIdle.Client.csproj`.
- Compile without the editor: `dotnet build client/ChaosIdle.Client.csproj`.

## Next steps

- Show map modifiers, uniques' details and the loot filter on screen (the passive tree and
  supports are shown since the Passives and Skills tabs). A visual graph of the tree could
  replace the list later.
- Combat visuals by replaying a finished `CombatReport`, so visuals can't change outcomes.
  This needs a per-event timeline added to the report.
- Sign-in and cloud saves against the server.
