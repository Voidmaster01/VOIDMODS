// ModConfigsMod.cs - ModConfigs (v1.1.0), a VOIDMOD for FNAF Online Multiplayer: the shared MOD CONFIGS hub.
//
// WHAT IT DOES
//   Adds a "MOD CONFIGS" row to the game's home screen menu. It opens one panel where players change the
//   settings of all their mods inside the game, instead of editing files:
//     - mods that use ModConfigs as a requirement register their own page (see ConfigHub.cs),
//     - every other mod's MelonPreferences settings get an automatic page,
//     - (1.1.0) mods that keep their settings in their own file (.ini .cfg .json .toml .yaml .xml ...) get a page
//       too (see FileConfigs.cs).
//   1.1.0 also added: text boxes, sliders, dropdowns and color pickers, a search box, RESET TO DEFAULTS, a
//   GET MODS button that leads to the Mod Menu mod, it turns off the game's "Hold D to clear Save Data", and it
//   has a built-in "Key Binds" page: change the game's keys per role and per minigame (keyboard and controller),
//   plus a DANGER ZONE to clear the save on purpose.
//   It also gives VOIDMODS a shared look and picture tools (UiKit.cs, PngReader.cs).
//
// HOW TO BUILD AND RUN
//   From the repo folder:  pwsh -NoProfile -File build.ps1 -Mod ModConfigs   (or build-all.ps1)
//   It goes into the game's Mods folder like any MelonLoader mod. Mods that need it say so with
//   [assembly: MelonAdditionalDependencies("ModConfigs")], and MelonLoader loads it before them.
//
// WHAT IT NEEDS
//   MelonLoader 0.7 (Harmony and Il2CppInterop come with it).
//
// THE FILES
//   ModConfigsMod.cs (this file)   ConfigHub.cs: the API for other mods   UiKit.cs: VOIDMODS look + UI helpers
//   PngReader.cs: reads PNG files  MenuRow.cs: the menu row               HubPanel.cs: the panel itself
//   FileConfigs.cs: other mods' own config files              NoClearData.cs: turns off hold-D clear save data
//   GameSave.cs: clear the save on purpose (no backup)         KeyBinds.cs + DangerZone.cs: the built-in Key Binds page
//   Controller.cs: Xbox / PlayStation / other controllers (also on the Key Binds page)
//   GameContext.cs: which role / minigame is being played, so key binds can differ per role
//   EscapeKey.cs: Esc / Start goes back (closes a panel, puts the monitor down) before opening Settings
//   ModPanel.cs: a ready-made panel other mods can open (the Mod Menu's MODS panel)
//   Sounds.cs: the built-in "Sounds" page, one volume slider for every sound in the game
//   SoundPacks.cs: sound packs (folders of WAV files that replace game sounds), on the Sounds page

using MelonLoader;

[assembly: MelonInfo(typeof(ModConfigs.ModConfigsMod), "ModConfigs", "1.1.0", "Voidmaster01")]
[assembly: MelonGame("Brian_mpz", "FNAF Online Multiplayer")]

namespace ModConfigs
{
    // MelonLoader creates this class when the game starts. The Harmony patch in MenuRow.cs (applied automatically
    // by MelonLoader) does the actual work when the home screen appears, so there is little to do here.
    public class ModConfigsMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("MOD CONFIGS is ready. Mods can add pages with ConfigHub.Register.");
            // HarmonyInstance is this mod's own Harmony (MelonLoader makes it); the countdown needs patching by hand.
            NoClearData.PatchCountdown(HarmonyInstance);
            // ModConfigs' own built-in page: Key Binds + DANGER ZONE (KeyBinds.cs, DangerZone.cs). The VOIDMODS logo
            // is shown because it is part of an official VOIDMOD. RESET TO DEFAULTS there only resets the keys.
            KeyBinds.Load();
            ConfigHub.Register(KeyBinds.PageName, "built in", new System.Action<ConfigPage>(KeyBinds.BuildPage), true, KeyBinds.Category);
            ConfigHub.SetReset(KeyBinds.PageName, new System.Action(KeyBinds.ResetAll));
            // Built-in page 2: Sounds, one volume slider per sound (Sounds.cs).
            Sounds.Load();
            ConfigHub.Register(Sounds.PageName, "built in", new System.Action<ConfigPage>(Sounds.BuildPage), true);
            ConfigHub.SetReset(Sounds.PageName, new System.Action(Sounds.ResetAll));
        }

        // Every frame: lets MenuRow finish tidying the menu once Unity has laid it out (see MenuRow.Tick), and
        // saves config files that were changed a moment ago (see FileConfigs.Tick).
        public override void OnUpdate()
        {
            MenuRow.Tick();
            HubPanel.Tick();   // rebuilds the panel page when a click asked for it (on the next frame, see HubPanel.Later)
            ModPanel.Tick();   // the same for a ModPanel (like the Mod Menu's MODS panel)
            FileConfigs.Tick();
            GameContext.Tick(); // which role / minigame is being played (key binds are per role), checked 10x a second
            KeyBinds.Tick();   // waits for a key while a key bind shows "PRESS A KEY..."
            Controller.Tick(); // controller: cursor, click, scroll, Esc/Tab, and "PRESS A BUTTON..."
            DangerZone.Tick(); // the DANGER ZONE button's countdown
            EscapeKey.Tick();  // Esc / Start: close a panel or put the monitor down instead of opening Settings
            Sounds.Tick();     // per-sound volumes: finds the game's sounds, rewrites changed ones, hides Music / SFX sliders
        }

        // When the game closes: save config-file changes that are still waiting, so nothing is lost.
        public override void OnApplicationQuit()
        {
            FileConfigs.Flush();
        }
    }
}
