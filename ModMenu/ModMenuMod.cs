// ModMenuMod.cs - ModMenu (v0.2.0), a VOIDMOD for FNAF Online Multiplayer: switching mods on and off, and
// finding, installing and updating approved mods.
//
// WHAT IT DOES
//   Adds a MODS button to the home screen, right under the game's WHAT'S NEW button (HomeButton.cs). It opens the
//   MODS panel (a ModPanel from ModConfigs) with:
//     - INSTALLED MODS: an ON/OFF switch for each mod (InstalledMods.cs: Mods <-> Mods\Disabled),
//     - GET MODS: approved mods from the VOIDMODS list on GitHub, with INSTALL / UPDATE buttons (Browser.cs,
//       Registry.cs, Installer.cs). When the game starts it checks for updates in the background (a setting,
//       "Check for updates when the game starts", on the automatic ModMenu page in MOD CONFIGS),
//     - a shortcut to ModConfigs' Key Binds page (key binds and the DANGER ZONE live in ModConfigs, so they work
//       even without the Mod Menu; Voidmaster01's decision).
//   ModConfigs' GET MODS button opens the same panel.
//   The Mod Menu is deliberately NOT a page inside MOD CONFIGS: it has its own button (Voidmaster01's decision).
//
// HOW TO BUILD AND RUN
//   From the repo folder:  pwsh -NoProfile -File build.ps1 -Mod ModMenu   (ModConfigs must be built first)
//   Goes into the game's Mods folder next to ModConfigs.dll.
//
// WHAT IT NEEDS
//   MelonLoader 0.7 and ModConfigs 1.1.0 or newer (it uses ModPanel, UiKit.CopyLooks, ConfigHub.Show/SetGetMods).
//   An internet connection for GET MODS (everything else works offline).

using System;
using MelonLoader;
using ModConfigs;

[assembly: MelonInfo(typeof(ModMenu.ModMenuMod), "ModMenu", "0.2.0", "Voidmaster01")]
[assembly: MelonGame("Brian_mpz", "FNAF Online Multiplayer")]
// MelonLoader loads ModConfigs first, and tells the player if it is missing.
[assembly: MelonAdditionalDependencies("ModConfigs")]

namespace ModMenu
{
    public class ModMenuMod : MelonMod
    {
        private const string KeyBindsPage = "Key Binds"; // ModConfigs' built-in page (KeyBinds.PageName there)

        // A MelonPreferences setting (saved in UserData\MelonPreferences.cfg). ModConfigs shows it automatically.
        private static MelonPreferences_Entry<bool> checkOnStart;

        public override void OnInitializeMelon()
        {
            MelonPreferences_Category cat = MelonPreferences.CreateCategory("ModMenu", "Mod Menu");
            checkOnStart = cat.CreateEntry("CheckForUpdatesOnStart", true, "Check for updates when the game starts",
                "Downloads the approved mod list from GitHub when the game starts and shows MODS (n) when updates are ready.");
            // GET MODS in MOD CONFIGS opens our panel too.
            ConfigHub.SetGetMods(new Action(OpenPanel));
            if (checkOnStart.Value)
                Browser.StartCheck(); // runs in the background; the game keeps loading
            LoggerInstance.Msg("Mod Menu is ready (the MODS button on the home screen, under WHAT'S NEW).");
        }

        // Every frame: shows results of background downloads (see Browser.Tick).
        public override void OnUpdate()
        {
            Browser.Tick();
        }

        // Opens the MODS panel (from the home screen button or GET MODS). The first time, if the game didn't check
        // the list at start-up (setting off), it checks now.
        public static void OpenPanel()
        {
            if (!checkOnStart.Value && !checkedOnOpen)
            {
                checkedOnOpen = true;
                Browser.StartCheck();
            }
            ModPanel.Open("Mods", new Action<ConfigPage>(BuildPanel));
        }
        private static bool checkedOnOpen;

        private static void OpenKeyBinds()
        {
            ModPanel.Close();
            ConfigHub.Show(KeyBindsPage);
        }

        private static void BuildPanel(ConfigPage page)
        {
            InstalledMods.BuildSection(page);
            page.Space(20);
            Browser.BuildSection(page);
            page.Space(20);
            page.Header("Controls");
            page.Button("KEY BINDS & DANGER ZONE (IN MOD CONFIGS)", new Action(OpenKeyBinds));
        }
    }
}