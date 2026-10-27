# VRPLauncher

A 2D, keyboard-only front end for picking and launching Visual Pinball X tables while wearing a VR headset.
It is built to be driven entirely from a VR pinball stub's buttons and to read well on the desktop view mirrored into the headset.

- Scans a tables folder for `.vpx` files and shows them in a big carousel.
- Thumbnails, in order: an image next to the `.vpx` with the same name, wheel art from the built-in `Media\Wheels` folder and then the configured wheel folders (PinUP wheel images by default), the backglass inside a same-named `.directb2s`, then the VPS backglass.
- Rules cards from the built-in `Media\Rules` folder (and any extra rules folders), shown full screen on request.
- Uses the [Virtual Pinball Spreadsheet](https://virtualpinballspreadsheet.github.io/) database for game info, backglass pictures, filters and "newer version available" notices.
- Launches VPX with configurable arguments, then brings itself back to the foreground when VPX exits.
- Remembers the last table you played, your favourites, and how often and how long you've played each table.

## Build

Needs the .NET 10 SDK.

```
publish.cmd
```

This writes `publish\VPinNexusVR.exe`. Run it from anywhere; on first run it creates `settings.json` next to the exe.

## Making a release

```
release.cmd
```

This builds a **self-contained** exe (.NET included, so users install nothing) and zips it with `Packaging\README.txt`, `LICENSE` and the font licences into `release\VPinNexusVR-<version>-win-x64.zip`. The version comes from `<Version>` in `VRPLauncher.csproj`; bump it before each release. `release/` is not committed.

To publish: on GitHub go to **Releases → Draft a new release**, create a tag such as `v0.1.0-beta`, attach the zip, tick **Set as a pre-release** and publish.

New installs default to `C:\VisualPinball` for VPX and its Tables folder; if those aren't found, the settings window opens on first run.

## Default controls (VPX default keys)

| Action | Keys | Stub button |
|---|---|---|
| Previous / next table | Left Shift / Right Shift, arrow Left / Right | Flippers |
| Jump to previous / next letter | Left Ctrl / Right Ctrl, arrow Up / Down, PageUp / PageDown | Magna-save |
| Play | 1, Enter | Start, plunger |
| Quit the launcher | hold Esc for 1.5 s | Exit |
| Show / close the rules card | 5, V | Coin |
| Next / previous filter | 2, F / G | Extra ball (2) |
| Favourite on / off | 3 | whichever button sends 3 |
| Music on / off | M | (keyboard) |
| Open settings | F2 | (keyboard) |

## Favourites and play history

Press **3** to star or unstar the selected table; favourites show an orange star on their card. Each time a table is launched the launcher counts the start and adds the time until VPX exits, and the info line under the table shows it ("Played 3 times, 1 h 20 min").

Three filters appear once there is something in them, right after All tables: **Favourites**, **Recently played** (last played first) and **Most played** (most starts first, then most play time).

All of this is kept in `state.json` next to `settings.json`, keyed by the table's file path, so renaming or moving a table file starts its history again.

## Virtual Pinball Spreadsheet (VPS)

At startup the launcher loads its cached copy of `vpsdb.json` and then checks `lastUpdated.json` on VPS in the background. It only downloads the 8 MB database again when that stamp has changed.
The cache is kept in a `vps` folder next to `settings.json`, with downloaded pictures in `vps\img`. If VPS can't be reached, the cached copy is used.

Each table is matched to a VPS game by the "Name (Maker Year)" part of its file name. A few loosely named files are matched by year and a partial name.

- **Game info:** manufacturer, year, type, players, theme and designers are shown under the selected table.
- **Rules:** press **Coin** (5) to show the table's rules card full screen and again to close it. The flippers browse tables while it is open, and Esc also closes it.
- **Updates:** if VPS lists a newer VPX release whose *main* author appears in your file name (for example "VPW" or "RobbyKingPin"), a green "Newer version on VPS" badge is shown. Releases where your author is only a contributor are ignored. Some newer releases need a newer VPX than 10.7, so check the release notes before updating.
- **Filters:** **2** cycles through All tables, Favourites / Recently played / Most played (see above), Updates on VPS, Electro-mechanical / Solid state, decades, and every manufacturer with at least 3 tables. The current filter is shown at the top left.

Untick "Use VPS data" in the settings window to turn all of this off.

## Background music

Quiet background music plays while you browse. It fades in when the launcher opens, fades out while a table is playing and fades back in when you return; each track also fades at its end so the loop is not abrupt.

- The built-in track is `Assets\Audio\vpinnexus.mp3`, compiled into the exe.
- To use your own music instead, put MP3s (or M4A, WMA, WAV) in `Media\Music`; they play in shuffled order.
- Turn it on or off and set the volume (default 15%) in the settings window, or press **M** (rebindable in Controls) to toggle it.

## Built-in image folders

The launcher creates a `Media` folder next to `settings.json` (normally next to the exe):

- `Media\Wheels`: wheel art, searched before any other wheel folder.
- `Media\Rules`: rules cards for the viewer. Save an image as `_default.png` here to show it for every table that has no rules card of its own. If there is none, the launcher shows its built-in "Rules card not available" image.

To add or replace a picture, save it as `Table Name (Maker Year).png` (`.jpg` and `.bmp` also work), e.g. `24 (Stern 2009).png`. The launcher also accepts the full table file name. Changes are picked up the next time the launcher starts or settings are saved.
The settings window has an **Open** button for each folder. You can also add more wheel or rules folders there.

## Branding

The launcher uses the VPin Nexus VR brand:

- Logo: `Assets\Brand\Logo.png` (header and "Now playing" screen).
- Colours: from `Assets\Brand\Colour Chart.png`, defined once in `App.xaml` (Midnight Navy background, Nexus Cyan accents, VR Orange update badge, Silver White text, Chrome Midtone secondary text).
- Fonts: Orbitron for titles and headings, Exo 2 for everything else, in `Assets\Fonts`. Both use the SIL Open Font License (`OFL-*.txt` alongside), which allows bundling them in the app.

The logo and fonts are compiled into the exe, so nothing needs installing. To change them, replace the file with one of the same name and run `publish.cmd`.

## Built-in default pictures

Default pictures live in `Assets\Defaults` in the repo and are compiled into `VPinNexusVR.exe` by `publish.cmd`, so the exe is self-contained.
To change one, replace the PNG with the same name (currently `rules_default.png`) and run `publish.cmd` again.
A `_default` image in a `Media` folder always takes priority over the built-in one.

## Settings window

Press **F2** to open settings. It has four tabs: **General** (VPX, tables folder, VPS, fullscreen), **Images** (wheel and rules folders), **Controls** (key bindings) and **Music**. Use it to set the VPX exe, launch arguments, tables folder and wheel/image folders with Browse buttons. It shows how many tables and images it finds as you go, and **Save** applies the changes immediately.
It also opens by itself at startup if the VPX exe or tables folder can't be found.
Saving rewrites `settings.json`, so any comments you added to that file are removed. Timings (input cooldown, exit hold) can only be changed in the file.

### Controls

The **Controls** section of the settings window lists every action with the keys bound to it. Click **Add key…** and press the button on your stub to bind it; a key can only do one thing, so it is taken off any other action. **Clear** removes an action's keys and **Reset to defaults** restores the VPX default keys. Settings and Quit must keep at least one key.
The menu shows cabinet button names (Flippers, Magna Saves, Start, Coin); the settings window shows the actual keys.

Quitting needs a hold so that the Esc press that closes VPX can't also close the launcher. Input is also ignored for a moment after VPX exits.

## settings.json

```jsonc
{
  "TablesFolder": "I:\\vPinball\\VisualPinball\\Tables",
  "IncludeSubfolders": false,
  "VpxExePath": "I:\\vPinball\\VisualPinball\\VPinballX_GL64.exe",
  "LaunchArguments": "-Minimized -Play \"{table}\"",   // {table} = full path of the .vpx
  "ImageFolders": [
    "I:\\vPinball\\PinUPSystem\\POPMedia\\Visual Pinball X\\Wheel",
    "I:\\vPinball\\VisualPinball\\vpxlauncher\\wheels"
  ],
  "RulesFolders": [],         // extra rules folders; Media\Rules is always searched first
  "UseDirectB2SImages": true,
  "UseVps": true,
  "MusicEnabled": true,
  "MusicVolume": 0.15,       // 0 to 1
  "Fullscreen": true,
  "MinimizeWhileRunning": true,
  "InputCooldownMs": 1500,   // ignore keys this long after VPX exits
  "ExitHoldMs": 1500,        // 0 = quit on a single press
  "Keys": {
    "Previous": ["LeftShift", "Left"],
    "Next": ["RightShift", "Right"],
    "PreviousLetter": ["LeftCtrl", "Up", "PageUp"],
    "NextLetter": ["RightCtrl", "Down", "PageDown"],
    "Launch": ["D1", "Return"],
    "Exit": ["Escape"],
    "Settings": ["F2"],
    "View": ["D5", "V"],
    "NextFilter": ["D2", "F"],
    "PreviousFilter": ["G"],
    "ToggleMusic": ["M"],
    "ToggleFavorite": ["D3"]
  }
}
```

Key names are WPF `Key` names (`LeftShift`, `D1` or just `1`, `Return`/`Enter`, `Escape`, `Space`, `F1`, ...). Comments and trailing commas are allowed.
Unknown key names are listed in red on screen at startup.

### VR mode

VPX 10.7 has no command-line switch for VR: VR is a setting of the GL build. Point `VpxExePath` at the exe you have set up for VR (`VPinballX_GL64.exe` here) and the launcher just starts it with `-Play`.
`-Minimized` keeps the VPX editor window hidden; use `-ExtMinimized` instead if you want the pause menu.
Launch VPX itself, not a wrapper such as `VPinballX_GL64_Launcher.exe` that starts VPX and exits straight away, or the launcher will think the game ended immediately.

### Loading screen

While a table loads, the launcher stays up showing a LOADING screen with the table's wheel art. VPX broadcasts a `VPTableStart` window message when the playfield is ready (10.7.4 and later); the launcher then switches to "Now playing", pauses the music and minimizes. If the message never arrives (older VPX, or a load that hangs), it does this after 30 seconds anyway.

### When VR can't start

On this setup VPX reaches the headset through OpenComposite and Virtual Desktop's OpenXR runtime. If the headset isn't connected when a table starts, OpenComposite shows an error box that can't be clicked from inside the headset.
The launcher watches for that box while VPX runs: it closes it, makes sure VPX quits, and returns to the menu with a message (for example "VR headset not found").

### Testing with another settings file

```
VPinNexusVR.exe --settings C:\path\to\test-settings.json
```

## Development

Built with the help of [Claude Code](https://claude.com/claude-code).

## License

The source code is released under the [MIT License](LICENSE). The VPin Nexus VR name, logo and icon (`Assets\Brand`), music (`Assets\Audio`) and default images (`Assets\Defaults`) are not covered and may not be reused. The Orbitron and Exo 2 fonts are under the SIL Open Font License (`Assets\Fonts\OFL-*.txt`).
