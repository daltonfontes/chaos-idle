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
| `src/ChaosIdle.App` (`GameSession`) | Everything the screens do, with no Godot types: load or create the save, the character sheet, expeditions (active, boss included, tier unlocks), the bag (describe and compare, equip, discard), local offline progress on pause/resume, and cloud sync (`GameSession.Cloud.cs`). Unit-tested in `GameSessionTests`; cloud sync is tested against the real server in `CloudSyncTests`. |
| `client/Main.cs` + `Main.tscn` | The main screen, built in code (portrait 720×1280, larger font), with five tabs: **Hero** (the fight replay, character sheet, report, bag), **Maps**, **Tree**, **Skills**, **Loot**. |
| `client/CombatView.cs` | **Fight replay** at the top of Hero. After each map run it plays the run back from the beats the simulator recorded (`SimulationOptions.RecordTimeline`, kept in `GameSession.LastFight`): the hero and the current enemy, life bars, skill bolts coloured by element (triggered skills arc), enemy lunges, damage numbers (crits bigger, in gold), miss / evade / block, kills, and the result. Play/Pause, speed 1×/2×/4×, and "Skip to boss". It only shows what the simulation decided; recording does not change the fight (tested). All art is plain shapes drawn in code. |
| `client/MapsLootTabs.cs` | **Maps:** pick a map and up to three modifiers; the preview lists packs, boss, each modifier's risk and reward, the total loot bonus, and an estimate from 20 quick simulations with your build ("boss beaten 60%", main danger). "Run map" runs it and shows the report on Hero; "Farm offline here" sets the map offline sessions use. **Loot:** preset (show everything / focused on your build / strict), a minimum rarity to pick up (uniques always kept), the rules in words in the order they're checked, and your bag as the filter would treat it. |
| `client/TreeCanvas.cs` | Draws the shared passive tree: drag to move, pinch / mouse wheel / − + buttons to zoom. Filled = taken, thick ring = available, blue = the selected path, X = another class's start, double ring = keystone. |
| `client/BuildTabs.cs` | **Passives:** the drawn tree; tapping any node shows the shortest path to it, its cost and the combined character-sheet change, with "Take path (N points)" and "Refund". **Skills:** choose the main skill, see each skill's supports and free sockets; tapping a support previews its effect or explains why it doesn't fit. |
| `client/Screenshots.cs` + `Screenshots.tscn` | CI only: opens the real main screen, plays an expedition, and saves a PNG of each tab. |
| `client/Smoke.cs` + `Smoke.tscn` | A headless check run by CI **inside the Godot runtime**: new game, an expedition, background the app, relaunch, come back three hours later with offline loot. |
| `client/CloudPanel.cs` | **Cloud save** row at the top of Hero: the status in words ("Saved in the cloud (#12)", "No connection; will sync when back online"), "Save to cloud" or "Sync now", and after a conflict the two saves side by side with "Keep this phone's save" / "Use the cloud save". The rules are in `GameSession.Cloud.cs`. |
| `client/export_presets.cfg` | The Android preset: arm64, debug-signed, package `com.chaosidle.game`. |

**App lifecycle:**
- `NotificationApplicationPaused` and `NotificationWMCloseRequest` save the game and start an
  offline session on the highest unlocked map.
- `NotificationApplicationResumed`, and the next launch, resolve it and print the
  "welcome back" report. Under a minute away keeps the session running.
- If a save can't be read, nothing is overwritten. The screen offers "Start over", which
  renames the damaged files aside.

## Cloud save

`GameSession.Cloud.cs` (in `ChaosIdle.App`, so it's tested against the real server) syncs the
save with the game server (`docs/backend.md`).

- **Account.** "Save to cloud" creates a guest account for this phone. Its token is kept in
  `cloud.json` next to the save, in the app's private storage. Linking Google comes with the
  account screen.
- **Sync.** It runs after player actions (batched, 3 s), on resume, and on "Sync now". The
  phone remembers the cloud revision it last matched and a checksum of the game at that point.
  - Only this phone changed: it uploads.
  - Only the cloud changed: it downloads.
  - Both changed: **nothing is overwritten**. The player sees both saves (map tier, bag,
    passives, when saved) and picks one.
  - No connection: the status says so, and it syncs later.
  - The token was signed out elsewhere: the phone forgets it and keeps its save.
- **Offline progress with a cloud account is the server's.**
  - On pause, a local session starts at once, so no progress is lost. The phone then uploads
    and asks the server to start the session, and takes the server's session in place of the
    local one.
  - On resume, the server pays it out (claim) and the phone downloads the result.
  - If the server can't be reached on pause, the local session is the fallback and pays out
    on the phone.
  - A server session is never paid on the phone: if another phone already collected it, this
    one just takes the cloud save.
- **Server address.** It's read from the project setting `chaos_idle/cloud/server_url` (set it
  before exporting the APK; empty means no cloud), or from `CHAOSIDLE_SERVER_URL` on desktop
  and in CI. Android blocks plain `http://` by default, so use `https://` for phones. The APK
  now asks for the Internet permission.
- **CI** starts the real server, and the screenshot run connects from inside the Godot runtime:
  `CLOUD SYNCED` must appear in the log.

Without a cloud account, offline progress stays local (Milestone 4 rules).

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

- Editing single loot filter rules (today: presets plus a minimum rarity).
- Fight replay: sprites and sounds instead of shapes; replays of offline runs.
- The account screen: link Google (Credential Manager plugin), switch account, sign out.
