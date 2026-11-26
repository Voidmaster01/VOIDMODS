// HomeButton.cs - part of ModMenu: the MODS button on the game's home screen, right under WHAT'S NEW.
//
// WHAT IT DOES
//   When the home screen starts, it finds the game's own WHAT'S NEW button (top right), makes a copy of it that
//   LOOKS exactly the same (ModConfigs' UiKit.CopyLooks removes everything the original button does), writes "MODS"
//   on it and places it just below WHAT'S NEW. Clicking it opens the MODS panel (ModMenuMod.OpenPanel).
//   When mod updates are waiting it reads "MODS (n)".
//   Voidmaster01 chose this spot: easy to see, and it doesn't cover the background art.

using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using ModConfigs;
using UnityEngine;
using UnityEngine.UI;

namespace ModMenu
{
    internal static class HomeButton
    {
        private const string Name = "VoidmodsModsButton";
        private static TMP_Text buttonLabel;

        // Writes "MODS" or "MODS (n)" when n updates are waiting (Browser.Tick calls this after a check).
        // "buttonLabel == null" is also true once Unity destroyed the label (leaving the home screen), so it's safe.
        public static void SetCount(int updates)
        {
            if (buttonLabel == null)
                return;
            buttonLabel.text = updates > 0 ? "MODS (" + updates + ")" : "MODS";
        }

        public static void Add(MainMenuUI menu)
        {
            // 1. The WHAT'S NEW text (the apostrophe can be ' or ’, so only the words are compared) and its button.
            TMP_Text whatsNew = null;
            foreach (Component c in menu.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text t = c.TryCast<TMP_Text>();
                if (t == null || t.text == null)
                    continue;
                string s = t.text.ToUpperInvariant();
                if (s.Contains("WHAT") && s.Contains("NEW") && s.Length < 16)
                {
                    whatsNew = t;
                    break;
                }
            }
            if (whatsNew == null)
            {
                MelonLogger.Warning("[ModMenu] Couldn't find WHAT'S NEW on the home screen; the MODS button is missing (use GET MODS in MOD CONFIGS).");
                return;
            }
            Component b = whatsNew.GetComponentInParent(Il2CppType.Of<Button>());
            if (b == null)
                return;
            GameObject original = b.gameObject;
            if (original.transform.parent.Find(Name) != null)
                return; // already there (the home screen can start more than once)
            // 2. The look-alike copy, next to the original in the same parent.
            //    WHAT'S NEW (FNAF Online 0.9.7) is built like this:
            //      What's New Button   [Image Button]          the white box
            //        Text (TMP)        "What's New"            its label
            //        Arrow                                     a "check out what's new!" callout with an arrow
            //    The copy keeps the box and the label (now "MODS"); every other part (the callout) is switched off.
            GameObject copy = UiKit.CopyLooks(original, original.transform.parent, Name, new Action(ModMenuMod.OpenPanel));
            for (int i = 0; i < copy.transform.childCount; i++)
            {
                Transform child = copy.transform.GetChild(i);
                TMP_Text label = child.GetComponent<TMP_Text>();
                if (label != null)
                {
                    buttonLabel = label;            // the button's own label: "MODS", or "MODS (2)" with 2 updates
                    SetCount(Browser.UpdateCount());
                }
                else
                    child.gameObject.SetActive(false); // the callout and anything else
            }

            // 3. Directly below the original, touching it (Voidmaster01: "it would look better"). The button is drawn
            //    scaled up, so its size in layout units isn't its size on screen. GetWorldCorners gives its real corners
            //    on screen (0 = bottom-left, 1 = top-left), so the copy moves down by exactly the real height.
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            original.GetComponent<RectTransform>().GetWorldCorners(corners);
            float height = corners[1].y - corners[0].y;
            copy.transform.position = original.transform.position - new Vector3(0, height, 0);
            MelonLogger.Msg("[ModMenu] MODS button added under WHAT'S NEW.");
        }
    }

    // When the game's home screen starts: add the MODS button.
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Start))]
    internal static class HomeButtonPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            try { HomeButton.Add(__instance); }
            catch (Exception e) { MelonLogger.Warning("[ModMenu] Could not add the MODS button: " + e); }
        }
    }
}
