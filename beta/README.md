# VOIDMODS beta (test builds)

These are **test builds** of the next VOIDMODS versions, straight from the `modconfigs-1.1-modmenu` branch. They are
updated as work goes on, so things may be unfinished or change. For the stable versions, use the
[Releases](../../../releases) page instead.

**Built for: FNAF Online Multiplayer 0.9.71-alpha (test)** with MelonLoader 0.7.3.
(ModConfigs reads Purple Guy's screens the way 0.9.71 does, so on 0.9.7 that one part logs a warning instead of working.)

## What's in here

| File | Version | What's new |
|------|---------|------------|
| `ModConfigs.dll` | 1.1.0 beta | DANGER ZONE is now a full wipe with no backup, and ModConfigs no longer touches the Windows registry. **Sounds** page: every sound has its own volume slider (0-200 %), the game's Music / SFX sliders are replaced. **Sound packs**: replace any sound with your own WAV files. Per-role **key binds** for keyboard and controller, controller menu styles, keyboard layouts (QWERTZ, AZERTY...), Esc / Start goes back. |
| `ModMenu.dll` | 0.2.1 beta | The **MODS** button: installed mods on / off, GET MODS browser and updater. Needs ModConfigs. 0.2.1: downloads through the game's own web code and no longer starts Explorer itself (fewer antivirus false alarms). |
| `Voidcons.dll` | 1.1.0 beta | Player icons. Needs ModConfigs. 1.1.0: icons are 128 x 128 with up to 256 colors (about 10 KB instead of up to 100 KB), and each player keeps copies of icons they have seen, so an icon only crosses the network once. Old 256 px icons are shrunk automatically (the original is kept). Talks only to other 1.1.0 players. |

## How to install

1. Install MelonLoader 0.7.3 into the game folder if it isn't there yet, and start the game once.
2. Close the game. Copy the DLLs you want into the game's `Mods` folder (replace the old ones).
   ModMenu and Voidcons need `ModConfigs.dll` too.
3. Start the game. **MOD CONFIGS** is in the home screen menu (Key Binds and Sounds are built-in pages there).

If Windows Defender or another antivirus blocks a file, please tell us which file and what it said. ModConfigs and
ModMenu use no Windows functions at all, and ModMenu downloads only through the game's own web code. Voidcons uses the Windows "open file" window and Windows' picture reader for
its icon picker, which some antivirus tools are wary of.

## Check you got the right files (SHA-256)

In PowerShell: `Get-FileHash .\ModConfigs.dll`

| File | SHA-256 |
|------|---------|
| ModConfigs.dll | `BB77338833B44718F73C03588FA9AB204EF3CAE4713283E5F65F617E278933E6` |
| ModMenu.dll | `34CD21A21F62F3E252B0D695BE286532C7417C3C5966EAB768BB53A46AA9E458` |
| Voidcons.dll | `67130624CD542EF1312910866B4403804552583B1D33A77A7EB01CAEAC555969` |

## Found a bug?

Open an [issue](../../../issues) and add:
- what you did and what happened,
- your game version (bottom left of the home screen) and keyboard layout,
- the file `MelonLoader\Latest.log` from the game folder (it never contains passwords; it does show your Windows
  user name in folder paths, so blank that out if you like).
