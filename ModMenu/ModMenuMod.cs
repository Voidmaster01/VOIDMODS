// ModMenuMod.cs - ModMenu (v0.1.0), a VOIDMOD for FNAF Online Multiplayer: switching mods on and off, and (coming)
// finding, installing and updating approved mods.
//
// WHAT IT DOES
//   Adds a MODS button to the home screen, right under the game's WHAT'S NEW button (HomeButton.cs). It opens the
//   MODS panel (a ModPanel from ModConfigs) with:
//     - INSTALLED MODS: an ON/OFF switch for each mod (InstalledMods.cs: Mods <-> Mods\Disabled),
//     - a shortcut to ModConfigs' Key Binds page (key binds and the DANGER ZONE live in ModConfigs, so they work
//       even without the Mod Menu; Voidmaster01's decision),
//     - (coming next) browse and install approved mods, and update installed ones.
//   ModConfigs' GET MODS button opens the same panel.
//   The Mod Menu is deliberately NOT a page inside MOD CONFIGS: it has its own button (Voidmaster01's decision).
//
// HOW TO BUILD AND RUN
//   From the repo folder:  pwsh -NoProfile -File build.ps1 -Mod ModMenu   (ModConfigs must be built first)
//   Goes into the game's Mods folder next to ModConfigs.dll.
//
// WHAT IT NEEDS
//   MelonLoader 0.7 and ModConfigs 1.1.0 or newer (it uses ModPanel, UiKit.CopyLooks, ConfigHub.Show/SetGetMods).

using System;
using MelonLoader;
using ModConfigs;

[assembly: MelonInfo(typeof(ModMenu.ModMenuMod), "ModMenu", "0.1.0", "Voidmaster01")]
[assembly: MelonGame("Brian_mpz", "FNAF Online Multiplayer")]
// MelonLoader loads ModConfigs first, and tells the player if it is missing.
[assembly: MelonAdditionalDependencies("ModConfigs")]

namespace ModMenu
{
    public class ModMenuMod : MelonMod
    {
        private const string KeyBindsPage = "Key Binds"; // ModConfigs' built-in page (KeyBinds.PageName there)

        public override void OnInitializeMelon()
        {
            // GET MODS in MOD CONFIGS opens our panel too.
            ConfigHub.SetGetMods(new Action(OpenPanel));
            LoggerInstance.Msg("Mod Menu is ready (the MODS button on the home screen, under WHAT'S NEW).");
        }

        // Opens the MODS panel (from the home screen button or GET MODS).
        public static void OpenPanel()
        {
            ModPanel.Open("Mods", new Action<ConfigPage>(BuildPanel));
        }

        private static void OpenKeyBinds()
        {
            ModPanel.Close();
            ConfigHub.Show(KeyBindsPage);
        }

        private static void BuildPanel(ConfigPage page)
        {
            InstalledMods.BuildSection(page);
            page.Space(20);
            page.Header("Get mods");
            page.Label("Browsing, installing and updating approved mods is coming in the next version.", 20).color = UiKit.DimText;
            page.Space(20);
            page.Header("Controls");
            page.Button("KEY BINDS & DANGER ZONE (IN MOD CONFIGS)", new Action(OpenKeyBinds));
        }
    }
}
