// Installer.cs - part of ModMenu: finds a listed mod on disk, and installs or updates it safely.
//
// WHAT IT DOES
//   - Where(): where a listed mod's file is now (Mods = on, Mods\Disabled or .dll.dis = off), and which version
//     MelonLoader loaded this session.
//   - Install(): downloads the approved file, checks its SHA-256 fingerprint against the list, and only if it
//     matches puts it in the Mods folder. An older copy is MOVED to <game>\UserData\ModMenu\Backups first, so an
//     update can always be undone by hand (put the backup back into Mods).
//
// WHY MOVE INSTEAD OF OVERWRITE
//   While the game runs, Windows keeps the loaded mod files open, so overwriting one fails. Moving (renaming) a
//   loaded file is allowed, which the INSTALLED MODS switches already rely on. The new file is written next to it
//   first under a temporary name (.download, which MelonLoader ignores) and only renamed to .dll when complete, so a
//   half-written mod can never be loaded. MelonLoader loads mods at start-up, so updates count after a restart.
//
// SHA-256
//   A "hash": a 64-character fingerprint computed from every byte of a file. Change one byte and the fingerprint is
//   completely different, so a matching fingerprint means "exactly the file Voidmaster01 approved".

using System;
using System.IO;
using System.Security.Cryptography;
using MelonLoader;
using MelonLoader.Utils;

namespace ModMenu
{
    // Where a listed mod is on this PC right now.
    internal sealed class Found
    {
        public string Path;           // the file, or null when it isn't installed
        public bool On;               // in Mods\ as .dll (MelonLoader loads it at start-up)
        public string LoadedVersion;  // the version MelonLoader loaded this session, or null
    }

    internal static class Installer
    {
        private static string Mods { get { return MelonEnvironment.ModsDirectory; } }
        public static string BackupsDir { get { return Path.Combine(Registry.Folder, "Backups"); } }

        // MAIN THREAD (it asks MelonLoader which mods are loaded).
        public static Found Where(ListedMod m)
        {
            var f = new Found();
            // 1. Loaded this session: MelonLoader knows the real file, even if the player renamed it.
            foreach (MelonMod mod in MelonMod.RegisteredMelons)
            {
                if (!string.Equals(mod.Info.Name, m.Id, StringComparison.OrdinalIgnoreCase))
                    continue;
                f.LoadedVersion = mod.Info.Version;
                try
                {
                    string loc = mod.MelonAssembly.Location;
                    if (File.Exists(loc))
                    {
                        f.Path = loc;
                        f.On = true;
                        return f;
                    }
                }
                catch { } // no file location: fall through to the file name checks
            }
            // 2. By file name: on (Mods\X.dll), or switched off (Mods\Disabled\X.dll, Mods\X.dll.dis/.disabled).
            string on = Path.Combine(Mods, m.File);
            if (File.Exists(on)) { f.Path = on; f.On = true; return f; }
            foreach (string off in new[] { Path.Combine(Mods, "Disabled", m.File), on + ".dis", on + ".disabled" })
                if (File.Exists(off)) { f.Path = off; return f; }
            return f;
        }

        public static string Sha256Of(byte[] data)
        {
            // SHA256.HashData gives 32 bytes; Convert.ToHexString writes them as 64 hex characters.
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        public static string Sha256OfFile(string path)
        {
            try { return Sha256Of(File.ReadAllBytes(path)); }
            catch { return ""; }
        }

        // BACKGROUND THREAD (no Unity here). target = the file to replace, or null to install into Mods\<file>.
        // Throws with a readable message when something is wrong; nothing in Mods is changed in that case.
        public static void Install(ListedMod m, string target, bool test)
        {
            // 1. Download and check the fingerprint BEFORE touching the Mods folder.
            byte[] data = Registry.Fetch(m.Url, test);
            string got = Sha256Of(data);
            if (got != m.Sha256)
                throw new InvalidOperationException("the downloaded " + m.File + " is NOT the approved file (fingerprint " +
                    got.Substring(0, 12) + "... instead of " + m.Sha256.Substring(0, 12) + "...). Nothing was installed.");

            if (target == null)
                target = Path.Combine(Mods, m.File);
            string temp = target + ".download";
            File.WriteAllBytes(temp, data);

            // 2. Move the old copy into Backups (name: file-version-date.dll), then rename the new file into place.
            string backup = null;
            try
            {
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(BackupsDir);
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    backup = Path.Combine(BackupsDir, Path.GetFileNameWithoutExtension(m.File) + "-before-" + m.Version + "-" + stamp + ".dll");
                    File.Move(target, backup);
                }
                File.Move(temp, target);
            }
            catch
            {
                // Put everything back as it was, then let the caller show the error.
                if (backup != null && File.Exists(backup) && !File.Exists(target))
                    File.Move(backup, target);
                if (File.Exists(temp))
                    File.Delete(temp); // our own unfinished download, never a player's file
                throw;
            }
            MelonLogger.Msg("[ModMenu] Installed " + m.Name + " " + m.Version + " -> " + target +
                (backup != null ? " (old copy: " + backup + ")" : "") + ". It loads after a restart.");
        }

        // Compares two version texts like "1.10.0" and "1.9". +1 = a is newer, -1 = b is newer, 0 = same.
        // System.Version understands "1.2.3"; anything after a '-' or '+' (like "1.0.0-beta") is ignored.
        public static int CompareVersions(string a, string b)
        {
            Version va, vb;
            if (Version.TryParse(Clean(a), out va) && Version.TryParse(Clean(b), out vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string v)
        {
            if (v == null) return "";
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            string s = (cut >= 0 ? v.Substring(0, cut) : v).TrimStart('v', 'V');
            return s.Contains(".") ? s : s + ".0"; // Version needs at least "major.minor"
        }
    }
}
