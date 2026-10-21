# VRPLauncher

A 2D, keyboard-only front end for picking and launching Visual Pinball X tables while wearing a VR headset.
It is built to be driven entirely from a VR pinball stub's buttons and to read well on the desktop view mirrored into the headset.

- Scans a tables folder for `.vpx` files and shows them in a big carousel.
- Thumbnails, in order: an image next to the `.vpx` with the same name, the backglass inside a same-named `.directb2s`, then images in the configured image folders (PinUP wheel images by default).
- Launches VPX with configurable arguments, then brings itself back to the foreground when VPX exits.
- Remembers the last table you played.

## Build

Needs the .NET 10 SDK.

```
publish.cmd
```

This writes `publish\VRPLauncher.exe`. Run it from anywhere; on first run it creates `settings.json` next to the exe.

## Default controls (VPX default keys)

| Action | Keys | Stub button |
|---|---|---|
| Previous / next table | Left Shift / Right Shift, arrow Left / Right | Flippers |
| Jump to previous / next letter | Left Ctrl / Right Ctrl, arrow Up / Down, PageUp / PageDown | Magna-save |
| Play | 1, Enter | Start, plunger |
| Quit the launcher | hold Esc for 1.5 s | Exit |

The footer shows the name of the last key pressed, which is the name to put in `settings.json` if you want to rebind a button.

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
  "UseDirectB2SImages": true,
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
    "Exit": ["Escape"]
  }
}
```

Key names are WPF `Key` names (`LeftShift`, `D1` or just `1`, `Return`/`Enter`, `Escape`, `Space`, `F1`, ...). Comments and trailing commas are allowed.
Unknown key names are listed in red on screen at startup.

### VR mode

VPX 10.7 has no command-line switch for VR: VR is a setting of the GL build. Point `VpxExePath` at the exe you have set up for VR (`VPinballX_GL64.exe` here) and the launcher just starts it with `-Play`.
`-Minimized` keeps the VPX editor window hidden; use `-ExtMinimized` instead if you want the pause menu.
Launch VPX itself, not a wrapper such as `VPinballX_GL64_Launcher.exe` that starts VPX and exits straight away, or the launcher will think the game ended immediately.

### Testing with another settings file

```
VRPLauncher.exe --settings C:\path\to\test-settings.json
```
