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
                // 1. The registry part, read with .NET's own registry functions and written as a .reg file.
                //    (Earlier versions started Windows' reg.exe for this; antivirus programs distrust mods that start
                //    other programs, so ModConfigs now does it itself.)
                if (!ExportRegistry(Path.Combine(folder, "playerprefs.reg"), out error))
                    return false;
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

        // Writes every value under the game's PlayerPrefs registry key into a .reg file: the text format Windows'
        // Registry Editor uses for "Export", so double-clicking the file puts the values back.
        //   Format:  Windows Registry Editor Version 5.00  /  [HKEY_CURRENT_USER\...]  /  "name"=value
        //   Values:  dword:0000000a (32-bit number), hex(b):.. (64-bit number), hex:.. (raw bytes, which is how Unity
        //            stores text and decimals), "text" (plain text); other kinds as hex(<kind number>):..
        //   The file is UTF-16 with a byte order mark, like Registry Editor's own exports.
        private static bool ExportRegistry(string file, out string error)
        {
            error = null;
            string sub = @"Software\" + Application.companyName + @"\" + Application.productName;
            // "using" closes the key again when done. OpenSubKey returns null when the key doesn't exist.
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(sub))
            {
                if (key == null)
                {
                    error = "The game has no save in the registry yet (" + sub + ").";
                    return false;
                }
                var sb = new System.Text.StringBuilder();
                sb.Append("Windows Registry Editor Version 5.00\r\n\r\n");
                sb.Append("[HKEY_CURRENT_USER\\").Append(sub).Append("]\r\n");
                foreach (string name in key.GetValueNames())
                {
                    sb.Append('"').Append(Escape(name)).Append("\"=");
                    object v = key.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                    switch (key.GetValueKind(name))
                    {
                        case Microsoft.Win32.RegistryValueKind.DWord:
                            sb.Append("dword:").Append(unchecked((uint)(int)v).ToString("x8"));
                            break;
                        case Microsoft.Win32.RegistryValueKind.QWord:
                            sb.Append("hex(b):").Append(Hex(BitConverter.GetBytes((long)v)));
                            break;
                        case Microsoft.Win32.RegistryValueKind.String:
                            sb.Append('"').Append(Escape((string)v)).Append('"');
                            break;
                        case Microsoft.Win32.RegistryValueKind.Binary:
                            sb.Append("hex:").Append(Hex((byte[])v));
                            break;
                        case Microsoft.Win32.RegistryValueKind.ExpandString:
                            sb.Append("hex(2):").Append(Hex(System.Text.Encoding.Unicode.GetBytes((string)v + "\0")));
                            break;
                        case Microsoft.Win32.RegistryValueKind.MultiString:
                            sb.Append("hex(7):").Append(Hex(System.Text.Encoding.Unicode.GetBytes(string.Join("\0", (string[])v) + "\0\0")));
                            break;
                        default:
                            sb.Append("hex:").Append(Hex(v as byte[] ?? new byte[0]));
                            break;
                    }
                    sb.Append("\r\n");
                }
                sb.Append("\r\n");
                File.WriteAllText(file, sb.ToString(), System.Text.Encoding.Unicode); // Unicode = UTF-16, with BOM
                return true;
            }
        }

        // In .reg text, \ and " inside quotes are written as \\ and \".
        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // Bytes as "0a,ff,00" (two hex digits each, separated by commas).
        private static string Hex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
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
