// ModMenuMod.cs - ModMenu (v0.1.0), a VOIDMOD for FNAF Online Multiplayer: finding, installing and updating mods.
//
// WHAT IT DOES
//   Adds a "Mod Menu" page to MOD CONFIGS and takes over ModConfigs' GET MODS button, which then opens this page
//   instead of the Mod Menu's download page.
//   The page has a shortcut to ModConfigs' built-in Key Binds page. The key binds and the DANGER ZONE themselves
//   live in ModConfigs, so they work even without the Mod Menu (Voidmaster01's decision: settings belong in
//   ModConfigs; the Mod Menu only adds a way to get there).
//   (Coming next: browse and install approved mods, and update installed ones.)
//
// HOW TO BUILD AND RUN
//   From the repo folder:  pwsh -NoProfile -File build.ps1 -Mod ModMenu   (ModConfigs must be built first)
//   Goes into the game's Mods folder next to ModConfigs.dll.
//
// WHAT IT NEEDS
//   MelonLoader 0.7 and ModConfigs 1.1.0 or newer (it uses ConfigHub.Show and ConfigHub.SetGetMods).

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
        public const string PageName = "Mod Menu";
        private const string KeyBindsPage = "Key Binds"; // ModConfigs' built-in page (KeyBinds.PageName there)

        public override void OnInitializeMelon()
        {
            // Our page in MOD CONFIGS. voidMod = true shows the VOIDMODS logo (only for Voidmaster01's official mods).
            ConfigHub.Register(PageName, "0.1.0", new Action<ConfigPage>(BuildPage), true);
            // GET MODS now opens our page.
            ConfigHub.SetGetMods(new Action(OpenPage));
            LoggerInstance.Msg("Mod Menu is ready (MOD CONFIGS -> Mod Menu, or the GET MODS button).");
        }

        private static void OpenPage()
        {
            ConfigHub.Show(PageName);
        }

        private static void OpenKeyBinds()
        {
            ConfigHub.Show(KeyBindsPage);
        }

        private static void BuildPage(ConfigPage page)
        {
            page.Header("Get mods");
            page.Label("Browsing, installing and updating approved mods is coming in the next version.", 20).color = UiKit.DimText;
            page.Space(20);
            page.Header("Settings");
            page.Button("KEY BINDS & DANGER ZONE", new Action(OpenKeyBinds));
            page.Label("Key binds live in MOD CONFIGS, so they also work without the Mod Menu.", 18).color = UiKit.DimText;
        }
    }
}
