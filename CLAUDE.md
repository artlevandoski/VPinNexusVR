# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

VPin Nexus VR: a WPF (.NET 10) full-screen front end for picking and launching Visual Pinball X (VPX) tables while wearing a VR headset. It is driven only by a pinball stub controller whose buttons send keyboard keys (VPX default keys). User-facing docs are in `README.md` (developer) and `Packaging/README.txt` (shipped in the release zip).

Planned features are in `docs/ROADMAP.md`. Work through them in order, one item at a time.

## Commands

```
dotnet build -c Debug                 # bin\Debug\net10.0-windows\VPinNexusVR.exe
publish.cmd                           # framework-dependent single exe -> publish\VPinNexusVR.exe (owner's day-to-day copy)
release.cmd                           # self-contained single exe + README/LICENSE/OFL -> release\VPinNexusVR-<Version>-win-x64.zip
VPinNexusVR.exe --settings <path>     # run against another settings.json (and its data folder) for testing
```

- The project file is `VRPLauncher.csproj` but `AssemblyName` is `VPinNexusVR`. `<Version>` in the csproj is shown in the settings title and names the release zip.
- There are no unit tests. Verify by building, then running the exe with `--settings` pointing at a scratch folder and a stand-in "VPX" (`cmd.exe /c timeout ...` or a PowerShell script) in `VpxExePath`/`LaunchArguments`. Capture windows with `PrintWindow`; drive the settings window through UI Automation. Only send keystrokes after confirming the launcher is the foreground window: the owner is usually using the desktop. Windows PowerShell 5's `Add-Type` compiles C# 5 (no `out var`).

## Owner's environment

- VPX 10.7.4 at `I:\vPinball\VisualPinball` (`VPinballX_GL64.exe`, tables in `Tables\`), PinUP wheel art in `I:\vPinball\PinUPSystem\POPMedia\Visual Pinball X\Wheel`.
- VR path: VPX → OpenComposite (`openvr_api64.dll` in the VPX folder) → Virtual Desktop's OpenXR runtime → Quest 3. No SteamVR. OpenComposite log: `%LOCALAPPDATA%\OpenComposite\logs\opencomposite.log`.
- The owner's live settings are `publish\settings.json` (with `I:\` paths). Code defaults are generic (`C:\VisualPinball\...`) for public releases.
- Git is done by the owner in Sourcetree: don't commit or push; list the changed files and suggest a commit message.

## Architecture

All app code is in the repo root; `MainWindow.xaml.cs` is the hub.

- **Data folder** = the folder holding `settings.json` (normally next to the exe). Everything runtime lives there: `settings.json` (`Settings`), `state.json` (`LauncherState`: last table, plus per-table `TableStats` — favourite, last run, start count, run time, broken flag and reason — keyed case-insensitively by file path; saved via a `.tmp` file and swap), `Media\{Wheels,Rules,Music}` (user-droppable assets, created on start), `vps\` (VPS database + downloaded images), `cache\` (built-in music extracted for playback). All are git-ignored.
- **Settings flow**: `Settings.LoadOrCreate` → `MainWindow.ApplySettings()` rebuilds everything (window mode, `KeyMap`, `MusicPlayer.Configure`, `ThumbnailFinder`, table scan, VPS matching, filters). `SettingsWindow` edits a `Settings.Clone()`; on Save, `Settings.Save` rewrites `settings.json` (comments are lost) and `ApplySettings` runs again. The settings window opens by itself when the VPX exe or tables folder is missing.
- **Tables** (`Tables.cs`): `TableScanner` parses file names `Name (Maker Year) details` into `TableEntry` (`Title`, `Name`, `Manufacturer`, `Year`, `Details`). `ImageIndex` matches images by exact file stem, then by `Title` prefix; `_default.*` is a per-folder fallback. `ThumbnailFinder` order: sidecar image next to the .vpx → `Media\Wheels` + `ImageFolders` → same-named `.directb2s` backglass (base64 in XML) → VPS backglass. Rules cards: rules folders → `_default` → built-in `Assets\Defaults\rules_default.png`.
- **VPS** (`Vps.cs`): `VpsClient` loads the cached `vpsdb.json`, then re-downloads only when `lastUpdated.json` changes; images are cached under `vps\img`. `VpsIndex.Match` finds the game by normalized name + year (loose contains-match fallback) and flags `UpdateVersion` only when a newer VPX release's *first* author appears in the file name.
- **Browsing**: `_allTables` (scan) → `_filters` (`BuildFilters`: all, VPS updates, type, decade, makers with ≥3 tables) → `_tables` (current filter) shown in a 5-slot carousel built in code (`CreateSlots`/`UpdateView`). The UI is laid out at a fixed 1920×1080 inside a `Viewbox`.
- **Input**: `LauncherAction` (in `KeyMap.cs`) ← `KeyBindings` (key-name lists in `settings.json`) → `KeyMap` → `OnKeyDown`. Adding an action touches: the enum, a `KeyBindings` property plus `For`/`Set`, the `KeyMap` constructor, `ControlRows` in `SettingsWindow.xaml.cs`, and the `OnKeyDown` switch. The footer names cabinet buttons via `ButtonName()`, not keys.
- **Launch cycle** (`Launch()`): start VPX with `LaunchArguments` (`{table}` placeholder) → LOADING panel until VPX broadcasts the registered window message `VPTableStart` (hooked in `OnSourceInitialized`; 30 s fallback) → `OnTableStarted` pauses music and minimizes → poll once a second while waiting for exit, where `VrErrorWatcher` closes OpenComposite error boxes and the process is killed if it doesn't quit → `BringToFront` forces focus back (`ForegroundWindow.Force`: AttachThreadInput plus an Alt-tap to beat the foreground lock, re-checked for 4 s) and input is ignored for `InputCooldownMs`.
- **Music** (`MusicPlayer.cs`): WPF `MediaPlayer` with timer-driven fades (start, pause around VPX, end-of-track loop). `Media\Music` files are shuffled; otherwise the embedded track is extracted to `cache\` because `MediaPlayer` only plays files.

## Gotchas

- WPF drops `System.IO` from implicit usings; the csproj adds it globally.
- `Window.Foreground` (a Brush) shadows anything named `Foreground`, hence the `ForegroundWindow` class name.
- Assets: fonts, `Logo.png` and `AppIcon.ico` are WPF `Resource`s (pack URIs, `App.xaml` defines the brand fonts/brushes); `Assets\Defaults\*.png` and `Assets\Audio\*.mp3` are `EmbeddedResource`s with logical names `Defaults.<file>` / `Audio.<file>`. Brand colours come from `Assets\Brand\Colour Chart.png` and are defined once in `App.xaml`.
- `.gitignore`'s `Media/` matches a folder of that name anywhere, so shipped assets go under `Assets\`, never a `Media` folder.
- Licensing: code is MIT; `Assets\Brand`, `Assets\Audio` and `Assets\Defaults` are excluded (see `LICENSE`); fonts are OFL.
