# VOIDMODS beta (test builds)

These are **test builds** of the next VOIDMODS versions, straight from the `modconfigs-1.1-modmenu` branch. They are
updated as work goes on, so things may be unfinished or change. For the stable versions, use the
[Releases](../../../releases) page instead.

**Built for: FNAF Online Multiplayer 0.9.71-alpha (test)** with MelonLoader 0.7.3.
(ModConfigs reads Purple Guy's screens the way 0.9.71 does, so on 0.9.7 that one part logs a warning instead of working.)

## What's in here

| File | Version | What's new |
|------|---------|------------|
| `ModConfigs.dll` | 1.1.0 beta | **Sounds** page: every sound has its own volume slider (0-200 %), the game's Music / SFX sliders are replaced. **Sound packs**: replace any sound with your own WAV files. Per-role **key binds** for keyboard and controller, controller menu styles, keyboard layouts (QWERTZ, AZERTY...), Esc / Start goes back. |
| `ModMenu.dll` | 0.2.0 beta | The **MODS** button: installed mods on / off, GET MODS browser and updater. Needs ModConfigs. |
| `Voidcons.dll` | 1.0.0 | Player icons, rebuilt for 0.9.71. Needs ModConfigs. |

## How to install

1. Install MelonLoader 0.7.3 into the game folder if it isn't there yet, and start the game once.
2. Close the game. Copy the DLLs you want into the game's `Mods` folder (replace the old ones).
   ModMenu and Voidcons need `ModConfigs.dll` too.
3. Start the game. **MOD CONFIGS** is in the home screen menu (Key Binds and Sounds are built-in pages there).

If Windows Defender or another antivirus blocks a file, please tell us which file and what it said. ModConfigs and
ModMenu use no Windows functions at all. Voidcons uses the Windows "open file" window and Windows' picture reader for
its icon picker, which some antivirus tools are wary of.

## Check you got the right files (SHA-256)

In PowerShell: `Get-FileHash .\ModConfigs.dll`

| File | SHA-256 |
|------|---------|
| ModConfigs.dll | `21B6C8C78F597073499F9250DB04FAE21E2278FB147102370A1BA43098B237E9` |
| ModMenu.dll | `221F9AF9CE8C0432546277621BE7B1A213EA7B4FD52E77E99E7601B17CE127F3` |
| Voidcons.dll | `F0764419FD74436FB8FE4494000255A5C95088FC4A9FA367B592D420CE45063A` |

## Found a bug?

Open an [issue](../../../issues) and add:
- what you did and what happened,
- your game version (bottom left of the home screen) and keyboard layout,
- the file `MelonLoader\Latest.log` from the game folder (it never contains passwords; it does show your Windows
  user name in folder paths, so blank that out if you like).
