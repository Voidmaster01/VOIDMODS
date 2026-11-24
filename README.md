<p align="center"><img src="branding/voidmod.png" width="96" alt="VOIDMODS logo"></p>

# VOIDMODS

Mods for **FNAF Online Multiplayer** by Brian_mpz, made by Voidmaster01, for [MelonLoader](https://melonwiki.xyz) 0.7.

| Mod | What it does |
|---|---|
| **ModConfigs** | Adds a **MOD CONFIGS** row to the home screen menu: one in-game place for the settings of all your mods. Other mods can add their own page, and every mod's MelonPreferences settings get an automatic page. |
| **Voidcons** | Your own **player icon**, shown in the backer-icon spot on the character select screen. Pick any picture in the game, move and crop it into a round HD icon, and other Voidcons players see it. Needs ModConfigs. |

## Installing (players): pick your mods

Download from the **[Releases](../../releases)** page. Every mod is its own `.dll`, so take only what you want
(or `VOIDMODS-all.zip` for everything).

| You want | Download | Needs |
|---|---|---|
| The MOD CONFIGS settings hub | `ModConfigs.dll` | nothing else |
| Your own player icon | `Voidcons.dll` | `ModConfigs.dll` |

1. Install [MelonLoader](https://melonwiki.xyz) 0.7 into the game folder and start the game once.
2. Put the `.dll` files you picked in the game's `Mods` folder.
3. Start the game. With ModConfigs, **MOD CONFIGS** is in the home screen menu.

To update, replace the `.dll` files with the ones from the newest release.

**Or the one-click way:** download `VOIDMODS-patch-<n>.bat` and double-click it. It finds FNAF Online on your PC
(or asks you to pick the folder), checks MelonLoader is installed, backs up any mod file it replaces into
`VOIDMODS-backup\<n>\` in the game folder, and installs the mods. Close the game first.

Voidcons notes: icons travel only when the lobby's host has Voidcons too, and only players with Voidcons see them.
Voidcons never sends, logs or stores player IDs in readable form. (The game itself already shares every player's
ID with the lobby in its own player list; Voidcons adds nothing to that.)

## Building (modders)

Needs PowerShell 7, the .NET 6 runtime, and the game with MelonLoader started once.

1. Put your game folder path in a file `game-path.txt` next to `build.ps1` (it is git-ignored).
2. `pwsh -NoProfile -File build-all.ps1` builds everything into the game's `Mods` folder.
   One mod: `pwsh -NoProfile -File build.ps1 -Mod Voidcons`.

## Adding your mod to MOD CONFIGS

```csharp
[assembly: MelonAdditionalDependencies("ModConfigs")]   // MelonLoader loads ModConfigs first

public override void OnInitializeMelon()
{
    ConfigHub.Register("MyMod", "1.0.0", BuildPage);
}

static void BuildPage(ConfigPage page)
{
    page.Header("Settings");
    page.Toggle("Fast mode", () => fast, v => fast = v);
    page.Number("Speed", () => speed, v => speed = v, 0.5, 0, 10);
    page.Button("Reset", ResetAll);
}
```

No code at all? Settings made with `MelonPreferences` show up automatically.

## Repo layout

```
ModConfigs/   the MOD CONFIGS hub (ConfigHub API, UiKit look, PngReader, menu row, panel)
Voidcons/     player icons (networking, move-and-crop page, image compression, file picker)
branding/     the VOIDMODS logo (.png embedded into ModConfigs, .ico for Windows)
tools/        helper scripts: make-release.ps1 (release files for GitHub), make-patch.ps1 + patch_core.ps1
              (one-click installer .bat), make-ico.ps1
build.ps1     builds one mod;  build-all.ps1 builds all of them
```

## Questions, bugs and ideas

Head to **[Discussions](../../discussions)**: ask for help, report bugs, suggest ideas, show off your Voidcons icon,
or ask about adding your own mod to MOD CONFIGS.

## License

The code is under the [MIT License](LICENSE): use it, learn from it, build on it, just keep the copyright notice.
The **VOIDMODS "V" logo** is the official icon of ModConfigs and every official VOIDMOD. It is not part of the MIT
license: it ships unchanged with those mods and may not be edited or used for other mods. Third-party mods using
ConfigHub leave `voidMod` set to `false`.

Not affiliated with Brian_mpz. Game files are not included and must not be added to this repo.
