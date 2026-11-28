// GameSave.cs - part of ModConfigs (new in 1.1.0): PUBLIC helper to clear the game's save on purpose.
//
// WHAT IT DOES
//   ClearAll() runs the game's OWN "clear save data" (MainMenuUI.ClearData), which ModConfigs otherwise blocks
//   (see NoClearData.cs). It is meant for the DANGER ZONE on the Key Binds page, behind its typed confirmation.
//   There is NO backup and no undo (Voidmaster01's decision, 2026-11-28): whoever gets through the DANGER ZONE
//   wants a full wipe, and can simply play again. Earlier builds made a backup by reading the Windows registry
//   (where Unity keeps the game's settings and progress); antivirus programs distrust mods that read and export the
//   registry, so that code is gone and ModConfigs no longer touches the registry at all.
//
// HOW ANOTHER MOD USES IT
//   string error;
//   if (GameSave.ClearAll(out error)) { ... }
//   ClearAll only works on the home screen (the game's clear method belongs to the home screen's menu).

using System;
using MelonLoader;

namespace ModConfigs
{
    public static class GameSave
    {
        // True while the home screen is open, so ClearAll can work.
        public static bool CanClear
        {
            get { return NoClearData.Menu != null; }
        }

        // Runs the game's own ClearData once. Returns false (and why) when it can't.
        public static bool ClearAll(out string error)
        {
            error = null;
            Il2Cpp.MainMenuUI menu = NoClearData.Menu;
            if (menu == null) // Unity's "== null" is also true for an object that was destroyed (home screen left)
            {
                error = "Clearing only works on the home screen.";
                return false;
            }
            try
            {
                NoClearData.AllowOnce = true; // lets exactly one ClearData through the block in NoClearData.cs
                menu.ClearData();
                MelonLogger.Msg("[ModConfigs] Save data cleared on purpose (DANGER ZONE).");
                return true;
            }
            catch (Exception e)
            {
                error = "Clearing failed: " + e.Message;
                return false;
            }
            finally
            {
                NoClearData.AllowOnce = false; // never leave the door open
            }
        }
    }
}
