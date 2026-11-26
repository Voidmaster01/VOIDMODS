// EscapeKey.cs - part of ModConfigs (new in 1.1.0): makes Esc (and the controller's Start / Options button, which
// presses Esc) "go back" instead of opening the game's Settings when there is something to go back from
// (Voidmaster01's request).
//
// WHAT IT DOES
//   Esc normally opens the game's Settings window. With ModConfigs it first goes back one step:
//     - home screen, MOD CONFIGS or the Mod Menu's MODS panel open  -> closes that panel (back to the home screen)
//     - in a night with the monitor / laptop up                       -> puts it down (Security, Parts & Service,
//                                                                         Janitor; Purple Guy's left / right screen)
//       Backstage is left alone: its computer screen is always on.
//     - while a key bind waits for a key, or while typing in a text box -> nothing else (Esc cancels that)
//   Only when there is nothing to go back from does Settings open, as before.
//
// HOW IT WORKS
//   The game reads Esc itself through Unity's old input system, which can't be patched safely (it crashed the game in
//   tests), and it opens Settings without going through a function we could stop (tested: a patch on SettingsUI.Show
//   never ran). So instead:
//     1. Tick() (every frame) sees Esc go down through the new Input System, like the game's other keys.
//     2. If there is something to go back from, it does that, and for the next few frames closes the Settings window
//        the game opened for the same Esc, with Settings' own Hide() (what its CLOSE SETTINGS button does).
//   Unity runs scripts in its own order, so the game may open Settings in the same frame or the next one; watching
//   a few frames catches both. At worst Settings flashes for a frame.

using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModConfigs
{
    internal static class EscapeKey
    {
        private static int hideSettingsFrames; // how many more frames to close the game's Settings

        // Called every frame by ModConfigsMod.
        public static void Tick()
        {
            if (hideSettingsFrames > 0)
            {
                hideSettingsFrames--;
                HideSettings();
            }
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;
            bool esc;
            KeyBinds.Bypass = true; // our own read: never remapped
            try { esc = kb.escapeKey.wasPressedThisFrame; }
            finally { KeyBinds.Bypass = false; }
            if (!esc)
                return;
            try
            {
                if (WentBack())
                {
                    hideSettingsFrames = 3;
                    HideSettings();
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Esc: " + e.Message);
            }
        }

        // Goes back one step if there is one. True = it did (or Esc belonged to a key bind / text box).
        private static bool WentBack()
        {
            if (KeyBinds.RecentlyCapturing || GameContext.Typing())
                return true;
            if (ModPanel.IsOpen)
            {
                ModPanel.Close();
                return true;
            }
            if (HubPanel.IsOpen)
            {
                HubPanel.CloseIfOpen();
                return true;
            }
            return GameContext.LowerMonitorForEscape();
        }

        // Closes the game's Settings window if it is open. FindObjectOfType only finds objects that are switched on,
        // so when Settings is closed (or hidden on a switched-off object) there is nothing to do.
        private static void HideSettings()
        {
            SettingsUI s = UnityEngine.Object.FindObjectOfType<SettingsUI>();
            if (s != null)
                s.Hide();
        }
    }
}
