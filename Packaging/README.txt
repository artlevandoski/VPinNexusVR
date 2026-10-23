VPin Nexus VR - launcher for Visual Pinball X in VR
=====================================================

A full-screen menu for picking and starting Visual Pinball X (VPX) tables while
wearing a VR headset, driven entirely by a pinball stub controller or keyboard.

This is a BETA. Please report problems on the GitHub page.


Requirements
------------
- Windows 10 or 11, 64-bit. Nothing else to install (.NET is built in).
- Visual Pinball X set up for VR (for example VPinballX_GL64.exe).


Getting started
---------------
1. Unzip this folder anywhere, for example C:\VPinNexusVR.
2. Run VPinNexusVR.exe.
   Windows may say "Windows protected your PC" because the program is not
   code-signed. Click "More info", then "Run anyway".
3. If VPX is not in C:\VisualPinball, the Settings window opens. Use the
   Browse buttons to pick your VPX exe and your Tables folder, then Save.

The launcher creates these next to the exe on first run:
- settings.json   your settings
- Media\Wheels    wheel art: save as "Table Name (Maker Year).png"
- Media\Rules     rules cards, same naming; _default.png is used when a table has none
- Media\Music     your own MP3s, played instead of the built-in track


Default controls (VPX default keys; change them in Settings > Controls)
-------------------------------------------------------------------------
Flippers (Left/Right Shift)        browse tables
Magna Saves (Left/Right Ctrl)      jump to the previous/next letter
Start (1)                          play the selected table
Coin (5)                           show the rules card
2                                  next filter
M                                  music on/off
F2                                 settings
Hold Esc                           quit the launcher


Licences
--------
The launcher's source code is MIT licensed (see LICENSE.txt). The VPin Nexus VR
name, logo, icon, music and default images are not covered by that licence.
The Orbitron and Exo 2 fonts are used under the SIL Open Font License
(see OFL-Orbitron.txt and OFL-Exo2.txt).
Table information comes from the Virtual Pinball Spreadsheet
(https://virtualpinballspreadsheet.github.io/).
