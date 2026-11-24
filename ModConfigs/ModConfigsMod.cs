// ModConfigsMod.cs - ModConfigs (v1.0.0), a VOIDMOD for FNAF Online Multiplayer: the shared MOD CONFIGS hub.
//
// WHAT IT DOES
//   Adds a "MOD CONFIGS" row to the game's home screen menu. It opens one panel where players change the
//   settings of all their mods inside the game, instead of editing files:
//     - mods that use ModConfigs as a requirement register their own page (see ConfigHub.cs),
//     - every other mod's MelonPreferences settings get an automatic page.
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
//   ModConfigsMod.cs (this file)   ConfigHub.cs: the API for other mods   UiKit.cs: VOIDMODS look + picture helpers
//   PngReader.cs: reads PNG files  MenuRow.cs: the menu row               HubPanel.cs: the panel itself

using MelonLoader;

[assembly: MelonInfo(typeof(ModConfigs.ModConfigsMod), "ModConfigs", "1.0.0", "Voidmaster01")]
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
        }

        // Every frame: lets MenuRow finish tidying the menu once Unity has laid it out (see MenuRow.Tick).
        public override void OnUpdate()
        {
            MenuRow.Tick();
        }
    }
}
