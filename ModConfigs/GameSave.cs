// GameSave.cs - part of ModConfigs (new in 1.1.0): PUBLIC helpers to back up and (on purpose) clear the game's save.
//
// WHAT IT DOES
//   FNAF Online keeps its save in two places:
//     - Unity "PlayerPrefs", which on Windows live in the registry under HKEY_CURRENT_USER\Software\<company>\<game>
//       (here: Software\Brian_mpz\FNAF Online Multiplayer),
//     - a few files in the game's "persistent data" folder (AppData\LocalLow\Brian_mpz\FNAF Online Multiplayer).
//   Backup() copies both into UserData\ModConfigs\SaveBackups\<date-time>\ (the registry part as a .reg file, the
//   same format Windows' own Registry Editor exports; double-clicking it puts the values back).
//   ClearAll() runs the game's OWN "clear save data" (MainMenuUI.ClearData), which ModConfigs otherwise blocks
//   (see NoClearData.cs). It is meant for the DANGER ZONE on the Key Binds page, behind its typed confirmation.
//
// HOW ANOTHER MOD USES IT
//   string folder, error;
//   if (GameSave.Backup(out folder, out error) && GameSave.ClearAll(out error)) { ... }
//   ClearAll only works on the home screen (the game's clear method belongs to the home screen's menu).

using System;
using System.Diagnostics;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ModConfigs
{
    public static class GameSave
    {
        // Where backups go: UserData\ModConfigs\SaveBackups
        public static string BackupRoot
        {
            get { return Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs", "SaveBackups"); }
        }

        // The registry key Unity uses for this game's PlayerPrefs, built from the game's own company and name.
        public static string RegistryKey
        {
            get { return @"HKCU\Software\" + Application.companyName + @"\" + Application.productName; }
        }

        // True while the home screen is open, so ClearAll can work.
        public static bool CanClear
        {
            get { return NoClearData.Menu != null; }
        }

        // Copies the whole save into a new folder named after the date and time. Returns false (and why) on failure.
        public static bool Backup(out string folder, out string error)
        {
            folder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            error = null;
            try
            {
                Directory.CreateDirectory(folder);
                // 1. The registry part, with Windows' own reg.exe: "reg export <key> <file> /y" (/y = overwrite).
                //    ProcessStartInfo describes the program to run; CreateNoWindow keeps a console from flashing up.
                var info = new ProcessStartInfo("reg.exe", "export \"" + RegistryKey + "\" \"" + Path.Combine(folder, "playerprefs.reg") + "\" /y")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                using (Process p = Process.Start(info))
                {
                    if (!p.WaitForExit(15000) || p.ExitCode != 0)
                    {
                        error = "Windows could not export the save from the registry (reg.exe exit code " + (p.HasExited ? p.ExitCode.ToString() : "timeout") + ").";
                        return false;
                    }
                }
                // 2. The files in the game's persistent data folder (skipping Unity's own log files).
                string data = Application.persistentDataPath;
                if (Directory.Exists(data))
                {
                    string target = Path.Combine(folder, "files");
                    Directory.CreateDirectory(target);
                    foreach (string f in Directory.GetFiles(data))
                        if (!Path.GetFileName(f).StartsWith("Player", StringComparison.OrdinalIgnoreCase) || !f.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                            File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
                }
                MelonLogger.Msg("[ModConfigs] Save data backed up to " + folder);
                return true;
            }
            catch (Exception e)
            {
                error = "Backup failed: " + e.Message;
                return false;
            }
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
