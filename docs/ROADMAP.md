# VPinNexusVR — Feature Roadmap

Planned improvements for VPinNexusVR, based on a review of the current code, the Visual Pinball X source (`vpinball/vpinball`, master as of Oct 2026) and ideas from b4ast1's VPX-Launcher (vpuniverse.com).

Suggested place in the repo: `docs/ROADMAP.md`. Work through it top to bottom; each item is self-contained.

---

## Already in place (don't rebuild)

- Cabinet-button-only navigation with an on-screen key legend
- Hold-to-quit (`ExitHoldMs`) and the input cooldown after VPX exits
- Remembers the last table (`LauncherState.LastTable`)
- Filters (`BuildFilters()` in `MainWindow.xaml.cs`)
- Wheel / backglass / rules art fallbacks
- `VrErrorWatcher` closes the OpenComposite error box so VPX doesn't sit behind it

---

## Phase 1 — Quick wins

### 1. Loading screen that waits for the playfield

**Status: done.** VPX 10.7.4 also sends `VPTableStart` (the string is in its load-progress messages, between "Prerendering static parts" and "Startup done"). Confirmed with a real table in the headset.

**Problem:** `Launch()` minimizes the launcher as soon as VPX starts, so the headset shows the desktop or a black view while the table loads.

**How:** VPX broadcasts a registered window message when the playfield is ready:

```cpp
// vpinball: src/core/player.cpp (~line 388)
::PostMessage(HWND_BROADCAST, RegisterWindowMessage(_T("VPTableStart")), NULL, NULL);
```

- In `MainWindow`, P/Invoke `RegisterWindowMessage("VPTableStart")` once and add an `HwndSource.AddHook` handler.
- In `Launch()`, keep `PlayingPanel` visible (wheel art + title, optionally a `(Loading)` video, see item 7) and **don't minimize yet**.
- When the hook sees `VPTableStart`, apply `MinimizeWhileRunning` and pause the music.
- Fallback: if no message arrives within ~30 s (old VPX build, crashed load), minimize anyway.

**Done when:** launching a table shows the loading panel until the playfield appears, then the launcher gets out of the way.

### 2. Favorites, recently played, play count and play time

**Status: done.** Toggle Favorite defaults to key **3**. Run time is measured from launch to VPX exit, so it includes the loading screen. The play-history filters only appear once a table has been played or starred.

- Add `LauncherAction.ToggleFavorite` to `KeyMap.cs` with a default key, and show it in the legend and the Controls tab.
- Extend `LauncherState` with a per-table record keyed by file path: `Favorite`, `LastRun`, `StartCount`, `RunTime` (seconds).
- In `Launch()`, increment `StartCount` and set `LastRun` on start, then add the elapsed time to `RunTime` when the process exits.
- Add filters in `BuildFilters()`: **Favorites**, **Recently played** (sorted by `LastRun` desc), **Most played**.
- Show a favorite marker (star) on the selected table's card.
- **Atomic save:** `LauncherState.Save` currently overwrites the file directly. Write to `state.json.tmp`, then `File.Replace` (or `File.Move(..., overwrite: true)`) so a crash mid-write can't wipe favorites.

### 3. Broken-table detection

- If VPX exits within ~5 s of launch, or with a non-zero exit code (and it wasn't the VR error path), mark the table `Broken = true` in its state record.
- Show a warning badge on its card ("Last launch failed").
- On startup, don't auto-select a broken `LastTable`; fall back to the first table.
- Clear the flag the next time the table runs normally.

### 4. Custom collections in settings.json

Add a `Collections` list to `Settings`:

```jsonc
"Collections": [
  { "Name": "Top 10", "Folder": "--Top10--", "Include": ".*", "Exclude": "", "Sort": "Name" },
  { "Name": "Williams 90s", "Include": "Williams 199", "Sort": "YearDesc" }
]
```

- `Folder` is relative to `TablesFolder` (requires `IncludeSubfolders`).
- `Include` / `Exclude` are regexes against the file name.
- `Sort`: `Name`, `NameDesc`, `Year`, `YearDesc`.
- Append these to `BuildFilters()` after the built-in filters. Invalid regexes go in the startup error list, like unknown key names.

### 5. Startup log

On every start, write `launcher.log` next to `settings.json`: version, resolved VPX path, tables folder, table count, VPS status (cached or updated), image/rules folder hit counts, unknown key names and any exceptions. Overwrite each run (or keep the last 2). It's for bug reports from beta testers.

---

## Phase 2 — Medium effort

### 6. Highscores on the table card

- **ROM tables:** run PINemHi (`PINemHi.exe <rom>.nv`) and parse the output. Cache the result per table with the `.nv` file's timestamp, so it only reruns when the file changes. Look for the `.nv` file in:
  1. `<table folder>\pinmame\nvram\<rom>.nv` (VPX 10.8.1 folder-per-table layout)
  2. the VPinMAME `nvram` folder (legacy; make it a setting)
- The ROM name can come from VPS data, or from parsing `cGameName = "..."` in the table script (`-ExtractVBS`, or a `.vbs` next to the table).
- **Original tables:** scores live in `user\VPReg.stg` (OLE compound file, needs the OpenMcdf NuGet package). Optional, so skip it at first.
- Show the top scores under the game info; hide the section when none are found.

### 7. Adopt the VPX 10.8.1 frontend conventions

From `docs/FileLayout.md` in the vpinball repo:

- **`medias/` folder per table**, files named `(Type) <table or folder name>.<ext>`. Add these to the image search order:
  - `(Wheel)` → carousel art
  - `(GameHelp)` → rules card
  - `(Loading)` → loading screen video (item 1)
  - `(Backglass)`, `(Playfield)` → optional previews
- **`.info` JSON file next to each table** (named after the table file or its folder), shared between frontends:

```json
{
  "Info": { "Title": "", "Manufacturer": "", "Year": 0, "VPSId": "", "IPDBId": "" },
  "User": { "Rating": 0, "Favorite": 0, "LastRun": "", "StartCount": 0, "RunTime": 0, "Tags": [] },
  "VPX":  { "Version": "", "LastRun": "", "StartCount": 0, "RunTime": 0 }
}
```

  - Read `Info` as a source of game info (before or instead of VPS matching).
  - Optionally mirror the item 2 data into `User`, behind a setting, since it writes into the tables folder.
  - Treat `VPX` as read-only (reserved for VPX itself).

### 8. VR or desktop on 10.8.1, without OpenComposite

VPX 10.8.1 has native OpenXR. The setting `[PlayerVR] AskToTurnOn` (`src/core/Settings_properties.inl`) is `0 = Enabled`, `1 = Autodetect`, `2 = Disabled`.

- **Autodetect** gives VR when the headset is ready and desktop when it isn't, with no launcher work needed. Document it in the README.
- With **Enabled** and no headset, VPX shows its own dialog: title "Connection to VR headset failed", text "Retry connection ?" (`src/core/player.cpp`, ~line 199). Extend `VrErrorWatcher` to detect this box too (class `#32770`, the window title above), click **No**/close it and show the same "VR headset not found" status.
- Add an `{ini}` placeholder (or a `VrIniPath` / `DesktopIniPath` pair) so `LaunchArguments` can pass `-Ini "<file>"` to switch between VR and desktop settings files.
- Other useful command-line switches (from `docs/Command Line.md`):
  - `-TableIni <file>`
  - `-PrefPath <path>`
  - `-CaptureAttract <frames> <fps> <file>` for generating per-table preview videos.

---

## Phase 3 — Switch tables without leaving the headset

b4ast1's launcher does this by forking VPX. That's no longer needed: VPX 10.8.1's plugin API lets a plugin add its own pages to the in-game menu, which already renders in VR.

- API: `plugins/plugins/PagedUIPlugin.h` and `plugins/plugins/VPXPlugin.h` in the vpinball repo.
  - Item types include `PAGEDUI_ITEM_ENUM` (dropdown list), `PAGEDUI_ITEM_SUBPAGE` and `PAGEDUI_ITEM_ACTION` (button with a Run callback).
  - `VPXACTION_ExitGame` closes the table.
  - Events `OnGameStart` / `OnGameEnd`.

**Plan — "VPin Nexus" plugin (C++ DLL):**

1. Add a **Switch table** sheet to the pause menu, with the launcher's filters as sub-pages. The table list comes from a `tables.json` that VPinNexusVR writes on startup.
2. Picking a table writes its path to `next.txt` (next to `settings.json`) and triggers `ExitGame`.
3. When VPX exits, VPinNexusVR checks for `next.txt`, deletes it and immediately launches that table, with the item 1 loading screen covering the restart.

**Caveats:**

- The plugin must be C++ and built against the VPX plugin headers.
- The API is new in the 10.8.1 beta and may change, so pin to a VPX version and note it in the README.
- Do this last, once items 1–2 are solid.

---

## Housekeeping

The README still targets VPX 10.7. For 10.8.1:

- The BGFX build (`VPinballX_BGFX.exe`) is the default; the GL and DX9 builds are deprecated.
- Settings moved to `%APPDATA%\VPinballX\10.8\VPinballX.ini` (one folder per minor version).
- Tables are recommended in a folder per table, with companion files alongside.
- Update the first-run defaults (`C:\VisualPinball`), the default `VpxExePath` and the "VR mode" section to match.
- Bump `<Version>` in `VRPLauncher.csproj` as features land.
