// InstalledMods.cs - part of ModMenu: the INSTALLED MODS list, with an ON/OFF switch for each mod.
//
// WHAT IT DOES
//   Lists every mod file MelonLoader could load:
//     - <game>\Mods\*.dll                       = switched ON (MelonLoader loads these when the game starts),
//     - <game>\Mods\Disabled\*.dll              = switched OFF (MelonLoader doesn't look in subfolders),
//     - <game>\Mods\*.dll.dis / *.dll.disabled  = switched OFF too (another common way to disable a mod).
//   Switching a mod OFF moves its file into Mods\Disabled; switching it ON moves it back into Mods (a .dll.dis file
//   is renamed back to .dll). MelonLoader only loads mods when the game starts, so a change counts after a restart;
//   the page says so.
//   ModConfigs and ModMenu can't be switched off here: this very menu runs on them.
//
// WHERE THE NAMES COME FROM
//   For mods that are loaded right now, MelonLoader knows their real name, version and author (from the mod's
//   [MelonInfo] line). Switched-off files aren't loaded, so they show their file name.

using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using ModConfigs;

namespace ModMenu
{
    internal static class InstalledMods
    {
        // One mod file on disk.
        private sealed class ModFile
        {
            public string Path;      // where the file is now
            public string FileName;  // e.g. "Voidcons.dll"
            public string Title;     // "Voidcons 1.0.0 by Voidmaster01" (loaded) or the file name (not loaded)
            public bool On;          // in Mods\ as .dll = on
            public bool Protected;   // ModConfigs / ModMenu: can't be switched off here
        }

        private static string Mods { get { return MelonEnvironment.ModsDirectory; } }
        private static string Disabled { get { return System.IO.Path.Combine(Mods, "Disabled"); } }

        // The mods that were ON when the game started (file names, lower case), to tell when a restart is needed.
        private static HashSet<string> onAtStart;
        private static string lastError;

        // ================= finding the files =================

        private static List<ModFile> Scan()
        {
            var names = LoadedNames();
            var list = new List<ModFile>();
            if (Directory.Exists(Mods))
                foreach (string f in Directory.GetFiles(Mods))
                {
                    string name = System.IO.Path.GetFileName(f);
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        list.Add(Make(f, name, true, names));
                    else if (name.EndsWith(".dll.dis", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
                        list.Add(Make(f, name.Substring(0, name.IndexOf(".dll", StringComparison.OrdinalIgnoreCase) + 4), false, names));
                }
            if (Directory.Exists(Disabled))
                foreach (string f in Directory.GetFiles(Disabled, "*.dll"))
                    list.Add(Make(f, System.IO.Path.GetFileName(f), false, names));
            // Sort by name (a named method instead of a lambda; see ModConfigs' ConfigHub.cs for why).
            list.Sort(new Comparison<ModFile>(ByName));
            if (onAtStart == null)
            {
                onAtStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ModFile m in list)
                    if (m.On)
                        onAtStart.Add(m.FileName);
            }
            return list;
        }

        private static int ByName(ModFile a, ModFile b)
        {
            return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        }

        private static ModFile Make(string path, string fileName, bool on, Dictionary<string, string> loaded)
        {
            string title;
            if (!loaded.TryGetValue(fileName, out title))
                title = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string bare = System.IO.Path.GetFileNameWithoutExtension(fileName);
            return new ModFile
            {
                Path = path, FileName = fileName, Title = title, On = on,
                Protected = bare.Equals("ModConfigs", StringComparison.OrdinalIgnoreCase) || bare.Equals("ModMenu", StringComparison.OrdinalIgnoreCase),
            };
        }

        // File name -> "Name version by Author" for every mod MelonLoader loaded this session.
        private static Dictionary<string, string> LoadedNames()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (MelonMod m in MelonMod.RegisteredMelons)
            {
                try
                {
                    string file = System.IO.Path.GetFileName(m.MelonAssembly.Location);
                    d[file] = m.Info.Name + " " + m.Info.Version + (string.IsNullOrEmpty(m.Info.Author) ? "" : " by " + m.Info.Author);
                }
                catch { } // a mod without a file location: just skip it
            }
            return d;
        }

        // ================= switching =================

        // Moves the file to where its new state says. File.Move fails if a file with that name is already there, or
        // if Windows still has the file open; then nothing changes and the page shows why.
        private static void Switch(ModFile m, bool on)
        {
            lastError = null;
            try
            {
                string target;
                if (on)
                    target = System.IO.Path.Combine(Mods, m.FileName);
                else
                {
                    Directory.CreateDirectory(Disabled);
                    target = System.IO.Path.Combine(Disabled, m.FileName);
                }
                if (File.Exists(target))
                {
                    lastError = "Can't move " + m.FileName + ": a file with that name is already in " + System.IO.Path.GetDirectoryName(target) + ".";
                    return;
                }
                File.Move(m.Path, target);
                MelonLogger.Msg("[ModMenu] " + m.FileName + " switched " + (on ? "ON" : "OFF") + " (counts after a restart).");
            }
            catch (Exception e)
            {
                lastError = "Can't move " + m.FileName + ": " + e.Message;
            }
        }

        // ================= the page section =================

        public static void BuildSection(ConfigPage page)
        {
            page.Header("Installed mods");
            List<ModFile> mods = Scan();
            bool restart = false;
            foreach (ModFile m in mods)
            {
                if (m.On != onAtStart.Contains(m.FileName))
                    restart = true;
                if (m.Protected)
                {
                    page.Label(m.Title + "   (always on: this menu needs it)", 20).color = UiKit.DimText;
                    continue;
                }
                var sw = new SwitchBinding(m);
                page.Toggle(m.Title + (m.On ? "" : "  (" + m.FileName + ")"), new Func<bool>(sw.Get), new Action<bool>(sw.Set));
            }
            if (mods.Count == 0)
                page.Label("No mods found in the Mods folder.", 20).color = UiKit.DimText;
            if (restart)
                page.Label("Restart the game to apply your changes (MelonLoader loads mods when the game starts).", 20).color = UiKit.Purple;
            if (lastError != null)
                page.Label(lastError, 18).color = UiKit.Purple;
            page.Button("SHOW THE MODS FOLDER", new Action(OpenFolder));
        }

        // Opens the Mods folder in Explorer through Unity (OpenURL with a file:/// link), the way ModConfigs does it.
        // Earlier versions started explorer.exe directly; antivirus programs distrust mods that start other programs.
        // new Uri(...).AbsoluteUri turns "G:\Games\Mods\" into "file:///G:/Games/Mods/" (spaces become %20).
        private static void OpenFolder()
        {
            try { UnityEngine.Application.OpenURL(new Uri(Mods + Path.DirectorySeparatorChar).AbsoluteUri); }
            catch (Exception e) { MelonLogger.Warning("[ModMenu] Can't open the Mods folder: " + e.Message); }
        }

        // Connects one switch to one file (instead of a lambda). Set moves the file, then the page is rebuilt so it
        // shows the new state (ConfigHub.Refresh happens on the next frame, see ModConfigs).
        private sealed class SwitchBinding
        {
            private readonly ModFile mod;
            public SwitchBinding(ModFile m) { mod = m; }
            public bool Get() { return mod.On; }
            public void Set(bool on)
            {
                if (on == mod.On)
                    return;
                Switch(mod, on);
                ConfigHub.Refresh();
            }
        }
    }
}
