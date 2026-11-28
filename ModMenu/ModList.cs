// ModList.cs - part of ModMenu: the list of APPROVED mods that GET MODS shows.
//
// WHAT IT DOES
//   Downloads registry/mods.json from the VOIDMODS repo on GitHub and turns it into a list of ListedMod objects.
//   Only Voidmaster01 can change that file (he adds a mod with tools\add-mod.ps1 after checking it), so the list
//   only ever holds mods he approved. Each entry carries the SHA-256 "fingerprint" of the exact approved file:
//   Installer.cs refuses any download whose fingerprint is different (a changed or swapped file).
//
// THE FILE (registry/mods.json; registry/README.md explains every field)
//   { "format": 1,
//     "mods": [ { "id": "Voidcons", "name": "Voidcons", "author": "Voidmaster01", "version": "1.0.0",
//                 "about": "one line", "file": "Voidcons.dll", "url": "https://github.com/.../Voidcons.dll",
//                 "sha256": "64 hex characters", "requires": ["ModConfigs"], "page": "https://..." } ] }
//   "id" is the mod's name in its [MelonInfo] line: that is how ModMenu finds the installed copy and its version.
//
// TESTING WITHOUT GITHUB
//   If the file <game>\UserData\ModMenu\registry-test.json exists, it is used INSTEAD of the GitHub list, and the
//   page says so in purple. Only a test list may use file:// download links (files on this PC).
//
// THREADS
//   Downloading takes time, so Download() runs on a background thread (see Browser.cs). It must not touch Unity
//   (Unity objects may only be used from the game's main thread); it only reads files and the network.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelonLoader;
using MelonLoader.Utils;

namespace ModMenu
{
    // One approved mod from the list.
    internal sealed class ListedMod
    {
        public string Id;        // the [MelonInfo] name, e.g. "Voidcons"
        public string Name;      // shown on the page
        public string Author;
        public string Version;   // the approved version, e.g. "1.0.0"
        public string About;     // one line about the mod
        public string File;      // file name in the Mods folder, e.g. "Voidcons.dll"
        public string Url;       // where to download that exact file
        public string Sha256;    // fingerprint of the approved file (lower case hex)
        public string Page;      // a web page about the mod (may be empty)
        public string[] Requires = new string[0]; // ids of mods it needs
    }

    // (Called "Registry" in earlier versions; renamed so it is never mistaken for the Windows registry.)
    internal static class ModList
    {
        public const string ListUrl = "https://raw.githubusercontent.com/Voidmaster01/VOIDMODS/main/registry/mods.json";

        public static string Folder { get { return Path.Combine(MelonEnvironment.UserDataDirectory, "ModMenu"); } }
        public static string TestListPath { get { return Path.Combine(Folder, "registry-test.json"); } }
        public static bool UsingTestList { get { return System.IO.File.Exists(TestListPath); } }

        // Downloads one file into memory. https:// always; file:// only for a test list.
        // The download itself is done by the game's own web code (WebFetch.cs explains why); this runs on a
        // background thread and simply waits for the answer.
        public static byte[] Fetch(string url, bool test)
        {
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return WebFetch.Get(url);
            if (test && url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return System.IO.File.ReadAllBytes(new Uri(url).LocalPath);
            throw new InvalidOperationException("Only https:// download links are allowed: " + url);
        }

        // Background thread: gets the list (test file or GitHub) and reads it. On failure returns null and says why.
        public static List<ListedMod> Download(out string error)
        {
            error = null;
            bool test = UsingTestList;
            try
            {
                byte[] bytes = test ? System.IO.File.ReadAllBytes(TestListPath) : Fetch(ListUrl, false);
                return Parse(bytes, test, out error);
            }
            catch (WebFetchException e)
            {
                // StatusCode is GitHub's answer number (404 = the list isn't on GitHub yet; 0 = no answer at all).
                error = e.StatusCode == 404
                    ? "The mod list isn't on GitHub yet (registry/mods.json, 404)."
                    : "Couldn't reach GitHub: " + e.Message;
            }
            catch (Exception e)
            {
                error = "Couldn't get the mod list: " + e.Message;
            }
            return null;
        }

        // A file name is just letters, digits, . _ - and ends in .dll (no folders: a list entry can't write
        // anywhere except the Mods folder). A fingerprint is exactly 64 hex digits.
        // Regex: ^ = start, [A-Za-z0-9._-]+ = one or more of those characters, \.dll = ".dll", $ = end.
        private static readonly Regex SafeFile = new Regex(@"^[A-Za-z0-9._-]+\.dll$", RegexOptions.IgnoreCase);
        private static readonly Regex Hex64 = new Regex("^[0-9a-fA-F]{64}$");

        // Reads the JSON. Broken entries are skipped (and logged) so one bad entry can't hide the whole list.
        public static List<ListedMod> Parse(byte[] json, bool test, out string error)
        {
            error = null;
            var list = new List<ListedMod>();
            // JsonDocument reads the whole file into a tree we can walk. "using" frees it when the block ends.
            // AllowTrailingCommas / Skip comments: a hand-edited list with a stray comma or a // note still loads.
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            using (JsonDocument doc = JsonDocument.Parse(json, options))
            {
                JsonElement root = doc.RootElement;
                JsonElement format;
                if (root.TryGetProperty("format", out format) && format.ValueKind == JsonValueKind.Number && format.GetInt32() > 1)
                {
                    error = "The mod list is newer than this Mod Menu. Update the Mod Menu (download it again from GitHub).";
                    return null;
                }
                JsonElement mods;
                if (!root.TryGetProperty("mods", out mods) || mods.ValueKind != JsonValueKind.Array)
                {
                    error = "The mod list has no \"mods\" list in it.";
                    return null;
                }
                foreach (JsonElement e in mods.EnumerateArray())
                {
                    ListedMod m = Read(e);
                    string problem = Check(m, test);
                    if (problem != null)
                    {
                        MelonLogger.Warning("[ModMenu] Skipped a mod list entry (" + (m.Id ?? "no id") + "): " + problem);
                        continue;
                    }
                    list.Add(m);
                }
            }
            return list;
        }

        private static ListedMod Read(JsonElement e)
        {
            var m = new ListedMod
            {
                Id = Text(e, "id"), Name = Text(e, "name"), Author = Text(e, "author"), Version = Text(e, "version"),
                About = Text(e, "about"), File = Text(e, "file"), Url = Text(e, "url"), Page = Text(e, "page"),
                Sha256 = (Text(e, "sha256") ?? "").ToLowerInvariant(),
            };
            if (string.IsNullOrEmpty(m.Name))
                m.Name = m.Id;
            JsonElement req;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("requires", out req) && req.ValueKind == JsonValueKind.Array)
            {
                var r = new List<string>();
                foreach (JsonElement x in req.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String)
                        r.Add(x.GetString());
                m.Requires = r.ToArray();
            }
            return m;
        }

        // A text field, or null when it is missing or not text.
        private static string Text(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        // null = the entry is usable; otherwise what is wrong with it.
        private static string Check(ListedMod m, bool test)
        {
            if (string.IsNullOrEmpty(m.Id)) return "no id";
            if (string.IsNullOrEmpty(m.Version)) return "no version";
            if (m.File == null || !SafeFile.IsMatch(m.File)) return "the file name must be a plain name ending in .dll";
            if (m.Url == null) return "no download link";
            bool https = m.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            bool file = test && m.Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
            if (!https && !file) return "the download link must start with https://";
            if (!Hex64.IsMatch(m.Sha256)) return "the sha256 fingerprint must be 64 hex characters";
            return null;
        }
    }
}
