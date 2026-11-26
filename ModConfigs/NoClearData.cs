// NoClearData.cs - part of ModConfigs (new in 1.1.0): turns off the game's "Hold D to clear Save Data".
//
// WHAT IT DOES
//   On the home screen, FNAF Online wipes ALL your save data when you hold the D key. That is easy to do by
//   accident, for example while typing in a MOD CONFIGS text box, so ModConfigs switches it off completely
//   (Voidmaster01's decision: always off, no setting):
//     1. The game's ClearData method (the one that really deletes the save) is blocked, so it never runs.
//     2. The countdown before it (ClearDataCountdown) is stopped on its first step, so no countdown shows.
//     3. The "Hold "D" to clear Save Data" hint is hidden from the home screen.
//   Step 1 alone already keeps the save safe; 2 and 3 only stop the game from showing things that do nothing now.
//
// HOW IT WORKS: HARMONY PATCHES
//   Harmony (it comes with MelonLoader) can put our own small method in front of one of the game's methods: a
//   "prefix". If the prefix returns false, the game's method is skipped. A "postfix" runs after the game's method.
//   [HarmonyPatch(...)] above a class tells MelonLoader which game method to patch; it applies these by itself.
//
// WHY THE COUNTDOWN IS PATCHED BY HAND
//   ClearDataCountdown is a "coroutine": a method Unity runs a bit every frame. The compiler turns it into a
//   hidden helper class with a made-up name like "_ClearDataCountdown_d__38", and Unity calls that class's
//   MoveNext() once per frame ("do the next bit; return false when done"). The number in the name can change in a
//   game update, so the class is searched by the part of its name that stays the same, and patched in code.

using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace ModConfigs
{
    internal static class NoClearData
    {
        // Called once from ModConfigsMod.OnInitializeMelon: finds the countdown's helper class and patches its
        // MoveNext with StopCountdown below. BindingFlags.NonPublic finds it even though the game hides it.
        public static void PatchCountdown(HarmonyLib.Harmony harmony)
        {
            try
            {
                foreach (Type nested in typeof(MainMenuUI).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nested.Name.IndexOf("ClearDataCountdown", StringComparison.Ordinal) < 0)
                        continue;
                    MethodInfo moveNext = nested.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (moveNext == null)
                        continue;
                    harmony.Patch(moveNext, new HarmonyMethod(typeof(NoClearData).GetMethod(nameof(StopCountdown), BindingFlags.NonPublic | BindingFlags.Static)));
                    return;
                }
                MelonLogger.Msg("[ModConfigs] The clear-data countdown was not found (fine: ClearData itself is still blocked).");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Could not stop the clear-data countdown (ClearData itself is still blocked): " + e.Message);
            }
        }

        // The prefix for the countdown's MoveNext. "ref bool __result" is Harmony's way to set what the skipped
        // method would have returned: false means "the coroutine is finished", so Unity stops running it.
        private static bool StopCountdown(ref bool __result)
        {
            __result = false;
            return false; // skip the game's MoveNext
        }

        // Hides the "Hold "D" to clear Save Data" text: any text on the home screen that mentions clearing save data.
        // (true in GetComponentsInChildren = also look in objects that are switched off right now.)
        public static void HideHint(MainMenuUI menu)
        {
            foreach (Component c in menu.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text t = c.TryCast<TMP_Text>();
                if (t == null || t.text == null)
                    continue;
                string text = t.text.ToLowerInvariant();
                if (text.Contains("clear save data") || text.Contains("clear savedata"))
                {
                    t.gameObject.SetActive(false);
                    MelonLogger.Msg("[ModConfigs] Hold-D clear save data is turned off (hint hidden).");
                }
            }
        }
    }

    // Blocks the game's ClearData: returning false from a prefix means the game's method never runs.
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.ClearData))]
    internal static class BlockClearDataPatch
    {
        private static bool Prefix()
        {
            MelonLogger.Msg("[ModConfigs] Blocked the game's 'clear save data' (hold D). Your save is untouched.");
            return false;
        }
    }

    // After the home screen starts: hide the hint text.
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Start))]
    internal static class HideClearDataHintPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            try { NoClearData.HideHint(__instance); }
            catch (Exception e) { MelonLogger.Warning("[ModConfigs] Could not hide the clear-data hint: " + e.Message); }
        }
    }
}
