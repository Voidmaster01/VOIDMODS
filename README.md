<p align="center"><img src="branding/voidmod.png" width="96" alt="VOIDMODS logo"></p>

# VOIDMODS

Mods for **FNAF Online Multiplayer** by Brian_mpz, made by Voidmaster01, for [MelonLoader](https://melonwiki.xyz) 0.7.

| Mod | What it does |
|---|---|
| **ModConfigs** | Adds a **MOD CONFIGS** row to the home screen menu: one in-game place for the settings of all your mods. Other mods can add their own page, every mod's MelonPreferences settings get an automatic page, and mods that keep their settings in their own file (.ini .cfg .json .toml .yaml .xml ...) get one too. Search box and RESET TO DEFAULTS. Built-in **Key Binds** page: change the game's keys, play with an **Xbox / PlayStation / other controller**, and a **DANGER ZONE** to clear your save on purpose (typed phrase, countdown, automatic backup). It also turns off the game's easy-to-trigger *Hold D to clear Save Data*. |
| **Voidcons** | Your own **player icon**, shown in the backer-icon spot on the character select screen. Pick any picture in the game, move and crop it into a round HD icon, and other Voidcons players see it. Needs ModConfigs. |
| **ModMenu** | A **MODS** button on the home screen (under WHAT'S NEW): switch mods on and off, and **install and update approved mods** in game. Needs ModConfigs 1.1.0. |

## Installing (players): pick your mods

Download from the **[Releases](../../releases)** page. **FULL** (every mod) is always the release marked
**"Latest"**; every mod also has its own release, so you can take only what you want.

| You want | Download | Needs |
|---|---|---|
| The MOD CONFIGS settings hub | `ModConfigs.dll` | nothing else |
| Your own player icon | `Voidcons.dll` | `ModConfigs.dll` |
| Install, update and switch mods in game | `ModMenu.dll` | `ModConfigs.dll` (1.1.0 or newer) |

1. Install [MelonLoader](https://melonwiki.xyz) 0.7 into the game folder and start the game once.
2. Put the `.dll` files you picked in the game's `Mods` folder.
3. Start the game. With ModConfigs, **MOD CONFIGS** is in the home screen menu (key binds and controller: its
   **Key Binds** page). With ModMenu, the **MODS** button sits under WHAT'S NEW.

**Already using ModMenu?** It installs and updates the other mods for you: **MODS -> GET MODS**.

To update, replace the `.dll` files with the ones from the newest release.

**Or the one-click way:** download an installer `.bat` and double-click it. It finds FNAF Online on your PC
(or asks you to pick the folder), checks MelonLoader is installed, backs up any mod file it replaces into
`VOIDMODS-backup\` in the game folder, and installs the mods. Close the game first.

- **`VOIDMODS-FULL.bat` (everything) is always the release marked "Latest"**, so it's the first one you see.
- **Only want or need one mod?** Look through the **[Releases](../../releases)** list for its own installer
  (`VOIDMODS-Voidcons.bat`, `VOIDMODS-ModConfigs.bat`, ...).

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
    page.Slider("Volume", () => volume, v => volume = v, 0, 100, "0", true);
    page.TextBox("Nickname", () => nick, v => nick = v);
    page.Choice("Mode", new[] { "Easy", "Hard" }, () => mode, v => mode = v);
    page.ColorPicker("Tint", () => tint, v => tint = v);
    page.Button("Reset", ResetAll);
}
```

Give your page a **RESET TO DEFAULTS** button with `ConfigHub.SetReset("MyMod", ResetAll);`. Need a whole panel of your
own? `ModPanel.Open("My panel", BuildPage)` opens one in the same look (the Mod Menu uses it).

No code at all? Settings made with `MelonPreferences` show up automatically, with the fitting row: text box for text,
dropdown for an enum, color picker for a Color, slider for a value with a range. A settings file of your own
(`UserData\...` or next to your mod) gets a page too, and ModConfigs keeps its comments and layout when it saves.

## Getting your mod into the Mod Menu

Open **[Issues -> New issue -> Submit a mod](../../issues/new?template=submit-mod.yml)** with a direct link to your
release `.dll`. Voidmaster01 checks every mod by hand (public source code makes that much faster) and then adds it to
`registry/mods.json`. The Mod Menu installs only the exact approved file, checked by its SHA-256 fingerprint, so each new
version needs a new submission. How the list works: [registry/README.md](registry/README.md).

## Repo layout

```
ModConfigs/   the MOD CONFIGS hub (ConfigHub API, UiKit look, PngReader, menu row, panel, config files, key binds,
              controller, danger zone + save backup, ModPanel)
Voidcons/     player icons (networking, move-and-crop page, image compression, file picker)
ModMenu/      the MODS button: installed mods on/off, GET MODS browser + updater
registry/     mods.json, the approved mod list the Mod Menu reads (tools\add-mod.ps1 adds mods)
branding/     the VOIDMODS logo (.png embedded into ModConfigs, .ico for Windows)
tools/        helper scripts: make-release.ps1 (release files for GitHub), add-mod.ps1 (approve a mod), make-patch.ps1 + patch_core.ps1
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
