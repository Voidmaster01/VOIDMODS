// SoundPacks.cs - part of ModConfigs (new in 1.1.0): SOUND PACKS. Players replace any game sound with their own WAV
// file, and share their sounds as a folder (Voidmaster01's request: "an easy resource pack set up").
//
// HOW A PLAYER USES IT  (MOD CONFIGS -> Sounds -> "Sound packs")
//   - A pack is a folder in <game>\UserData\ModConfigs\SoundPacks\. Inside: WAV files named after the sound they
//     replace, e.g. "foxy taunt.wav" (the game's own sound name) or "Foxy taunt.wav" (the name on the Sounds page).
//     An optional pack.txt gives the pack a name, an author and a description.
//   - MAKE A NEW PACK creates such a folder with a SOUND LIST.txt: every sound, sorted by role and group, with the
//     exact file name that replaces it. EXPORT THE GAME'S ORIGINAL SOUNDS saves every original sound as a WAV file
//     (in SoundPacks\_Original game sounds) to edit or to listen to.
//   - Each pack has an on/off switch on the page. When two packs replace the same sound, the one lower in the list
//     wins (folders are sorted by name, so "1 ...", "2 ..." choose the order).
//   - Sharing: zip the pack folder; others unzip it into their SoundPacks folder.
//   - The volume sliders work on replaced sounds too.
//
// HOW IT WORKS
//   The game plays its sounds through the sound manager's list (Sounds.cs explains it). Each list entry (a "Sound")
//   points at a sound file in memory (an AudioClip). To replace a sound, ModConfigs reads the WAV file, makes a new
//   AudioClip with the same name (AudioClip.Create + SetData, both part of the game's own code), and points the
//   list entries at it. Speakers in the world that hold the old sound directly (AudioSource.clip) are switched too,
//   every 2 seconds while a pack is on. Switching a pack off points everything back at the game's own sound.
//   WAV is read by this file itself: a WAV file is a small header ("fmt ": how many channels, how many samples per
//   second, how many bits per sample) followed by the raw samples ("data"). Other formats (MP3, OGG) would need a
//   decoder the game doesn't include, so packs use WAV, which every audio program can save.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ModConfigs
{
    internal static class SoundPacks
    {
        public const string OriginalsFolder = "_Original game sounds"; // folders starting with _ are not packs

        // One pack folder.
        private sealed class Pack
        {
            public string Folder;   // the folder's name (also its key in sound-packs.txt)
            public string Name;     // from pack.txt, or the folder name
            public string Author = "";
            public string Description = "";
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // sound name -> WAV path
            public readonly List<string> Unmatched = new List<string>(); // WAV files whose name matches no sound
        }

        internal static bool NeedRefresh = true; // set when packs or the game's sounds changed
        private static readonly List<Pack> packs = new List<Pack>();
        private static readonly Dictionary<string, bool> enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> status = new List<string>(); // the last few messages, shown on the page
        private static bool loaded;
        private static float nextSourceSwap;
        private static readonly List<string> exportQueue = new List<string>(); // sound names still to export
        private static int exportTotal, exportDone;

        private static string Root { get { return Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs", "SoundPacks"); } }
        private static string StateFile { get { return Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs", "sound-packs.txt"); } }

        // ================= every frame (from Sounds.Tick) =================
        public static void Tick()
        {
            if (!loaded)
            {
                loaded = true;
                LoadState();
                ScanPacks();
            }
            if (NeedRefresh && Sounds.clips.Count > 0)
            {
                NeedRefresh = false;
                try { Refresh(); } catch (Exception e) { Say("Couldn't apply the sound packs: " + e.Message); }
            }
            if (Time.unscaledTime >= nextSourceSwap)
            {
                nextSourceSwap = Time.unscaledTime + 2f;
                try { SwapSources(); } catch (Exception e) { MelonLogger.Warning("[ModConfigs] Sound packs: " + e.Message); }
            }
            if (exportQueue.Count > 0)
                ExportStep();
        }

        // ================= finding the packs =================
        private static void ScanPacks()
        {
            packs.Clear();
            try
            {
                Directory.CreateDirectory(Root);
                var dirs = new List<string>(Directory.GetDirectories(Root));
                dirs.Sort(StringComparer.OrdinalIgnoreCase); // name order = pack order (lower wins)
                foreach (string dir in dirs)
                {
                    string folder = Path.GetFileName(dir);
                    if (folder.StartsWith("_"))
                        continue;
                    var p = new Pack { Folder = folder, Name = folder };
                    ReadPackInfo(p, Path.Combine(dir, "pack.txt"));
                    foreach (string wav in Directory.GetFiles(dir, "*.wav"))
                    {
                        string sound = SoundForFile(Path.GetFileNameWithoutExtension(wav));
                        if (sound != null) p.Files[sound] = wav;
                        else p.Unmatched.Add(Path.GetFileName(wav));
                    }
                    packs.Add(p);
                }
            }
            catch (Exception e)
            {
                Say("Couldn't read the SoundPacks folder: " + e.Message);
            }
            NeedRefresh = true;
        }

        // pack.txt: "name=...", "author=...", "description=..." (lines starting with # are notes).
        private static void ReadPackInfo(Pack p, string file)
        {
            if (!File.Exists(file))
                return;
            foreach (string line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
                if (key == "name" && value.Length > 0) p.Name = value;
                else if (key == "author") p.Author = value;
                else if (key == "description") p.Description = value;
            }
        }

        // Which sound a file name means: the game's sound name, or the name shown on the Sounds page (both ignoring
        // upper / lower case), or the game's name with characters Windows doesn't allow in file names swapped for _.
        private static string SoundForFile(string stem)
        {
            foreach (Sounds.Entry e in Sounds.entries.Values)
                if (string.Equals(e.Clip, stem, StringComparison.OrdinalIgnoreCase) || string.Equals(SafeName(e.Clip), stem, StringComparison.OrdinalIgnoreCase))
                    return e.Clip;
            foreach (Sounds.Entry e in Sounds.entries.Values)
                if (string.Equals(e.Label, stem, StringComparison.OrdinalIgnoreCase) || string.Equals(SafeName(e.Label), stem, StringComparison.OrdinalIgnoreCase))
                    return e.Clip;
            return null;
        }

        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // ================= on / off state (sound-packs.txt: "folder=on" or "folder=off") =================
        private static void LoadState()
        {
            try
            {
                if (File.Exists(StateFile))
                    foreach (string line in File.ReadAllLines(StateFile))
                    {
                        int eq = line.LastIndexOf('=');
                        if (!line.StartsWith("#") && eq > 0)
                            enabled[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim() != "off";
                    }
            }
            catch (Exception e) { Say("Couldn't read sound-packs.txt: " + e.Message); }
        }

        private static void SaveState()
        {
            try
            {
                var lines = new List<string> { "# ModConfigs sound packs: folder name=on or off. Change them in MOD CONFIGS -> Sounds." };
                foreach (KeyValuePair<string, bool> kv in enabled)
                    lines.Add(kv.Key + "=" + (kv.Value ? "on" : "off"));
                File.WriteAllLines(StateFile, lines);
            }
            catch (Exception e) { Say("Couldn't save sound-packs.txt: " + e.Message); }
        }

        private static bool IsOn(Pack p)
        {
            bool on;
            return !enabled.TryGetValue(p.Folder, out on) || on; // a new pack starts switched on
        }

        // ================= putting the pack sounds in =================
        // Works out which file should replace each sound (packs in order, later ones win), loads files that changed,
        // and points the sound manager's entries at the right sound.
        private static void Refresh()
        {
            var want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Pack p in packs)
                if (IsOn(p))
                    foreach (KeyValuePair<string, string> kv in p.Files)
                        want[kv.Key] = kv.Value;

            int replaced = 0;
            foreach (KeyValuePair<string, Sounds.ClipState> kv in Sounds.clips)
            {
                Sounds.ClipState st = kv.Value;
                string path;
                string id = want.TryGetValue(kv.Key, out path) ? path + "|" + File.GetLastWriteTimeUtc(path).Ticks : null;
                if (id != st.PackFile)
                {
                    AudioClip old = st.PackClip;
                    st.PackClip = null; st.PackSamples = null; st.PackFile = null; st.PackApplied = -1f;
                    if (id != null)
                        Load(st, kv.Key, path, id);
                    Point(st, old, st.PackClip != null ? st.PackClip : st.Clip);
                    if (old != null)
                        UnityEngine.Object.Destroy(old); // the old pack sound isn't used any more
                }
                else
                    Point(st, null, st.PackClip != null ? st.PackClip : st.Clip); // new list entries from the game
                if (st.PackClip != null)
                    replaced++;
            }
            MelonLogger.Msg("[ModConfigs] Sound packs: " + replaced + " sound(s) replaced.");
            Sounds.RequestApply(); // the volume sliders apply to the new sounds too
        }

        // Reads one WAV file into a new sound with the same name as the one it replaces.
        private static void Load(Sounds.ClipState st, string name, string path, string id)
        {
            try
            {
                int channels, rate;
                Il2CppStructArray<float> samples = ReadWav(path, out channels, out rate);
                AudioClip clip = AudioClip.Create(name, samples.Length / channels, channels, rate, false);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset; // Unity's clean-up must not throw it away
                st.PackClip = clip;
                st.PackSamples = samples;
                st.PackFile = id;
            }
            catch (Exception e)
            {
                Say(Path.GetFileName(Path.GetDirectoryName(path)) + "\\" + Path.GetFileName(path) + ": " + e.Message);
            }
        }

        // Points the sound manager's entries for this sound at "now", and restarts speakers that were playing "old".
        private static void Point(Sounds.ClipState st, AudioClip old, AudioClip now)
        {
            if (now == null)
                return;
            foreach (Sound s in st.Owners)
                if (s.audioClip == null || s.audioClip.Pointer != now.Pointer)
                    s.audioClip = now;
            if (old != null)
                SwapOn(old.Pointer, now);
        }

        // Every 2 s while a pack is on (and once after switching one off): speakers holding a replaced sound directly
        // get the pack's sound, and speakers holding a pack sound that is no longer wanted get the game's sound back.
        private static void SwapSources()
        {
            var to = new Dictionary<IntPtr, AudioClip>();
            foreach (Sounds.ClipState st in Sounds.clips.Values)
                if (st.PackClip != null && st.Clip != null)
                    to[st.Clip.Pointer] = st.PackClip;
            if (to.Count == 0)
                return;
            foreach (AudioSource src in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (src == null || src.clip == null || src.gameObject.scene.name == null)
                    continue;
                AudioClip pack;
                if (to.TryGetValue(src.clip.Pointer, out pack))
                    Restart(src, pack);
            }
        }

        private static void SwapOn(IntPtr from, AudioClip now)
        {
            foreach (AudioSource src in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (src != null && src.clip != null && src.clip.Pointer == from)
                    Restart(src, now);
        }

        // Gives a speaker its new sound; a speaker that was playing starts the new sound (music keeps going that way).
        private static void Restart(AudioSource src, AudioClip clip)
        {
            bool playing = src.isPlaying;
            src.clip = clip;
            if (playing)
                src.Play();
        }

        // ================= reading a WAV file =================
        // Returns the samples as numbers between -1 and 1, channels interleaved (left, right, left, right...), which
        // is what AudioClip.SetData wants. Throws with a plain-words message when the file can't be used.
        private static unsafe Il2CppStructArray<float> ReadWav(string path, out int channels, out int rate)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                throw new Exception("not a WAV file (save it as WAV, e.g. Audacity: File > Export > Export as WAV)");
            int format = 0, bits = 0, dataStart = -1, dataLength = 0;
            channels = 0; rate = 0;
            // A WAV file is a list of "chunks": a 4-letter name, a length, then that many bytes.
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string chunk = Encoding.ASCII.GetString(b, pos, 4);
                int length = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (chunk == "fmt " && body + 16 <= b.Length)
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && length >= 26 && body + 26 <= b.Length)
                        format = BitConverter.ToUInt16(b, body + 24); // "extensible" WAV: the real format is further in
                }
                else if (chunk == "data")
                {
                    dataStart = body;
                    dataLength = length < 0 || body + length > b.Length ? b.Length - body : length;
                    break;
                }
                if (length < 0)
                    break;
                pos = body + length + (length & 1); // chunks are padded to an even length
            }
            bool pcm = format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32);
            bool floats = format == 3 && bits == 32;
            if (!pcm && !floats)
                throw new Exception("this kind of WAV isn't supported (format " + format + ", " + bits + "-bit); use 16-bit PCM WAV");
            if (dataStart < 0 || channels < 1 || channels > 8 || rate < 8000 || rate > 192000)
                throw new Exception("the WAV file is damaged or empty");
            int bytes = bits / 8;
            int count = dataLength / bytes;
            count -= count % channels;
            if (count <= 0)
                throw new Exception("the WAV file has no sound in it");
            var samples = new Il2CppStructArray<float>(count);
            float* dst = (float*)IntPtr.Add(samples.Pointer, 4 * IntPtr.Size); // see Sounds.WriteScaled about this
            fixed (byte* start = b)                                         // "fixed" keeps b in place while we read it
            {
                byte* p = start + dataStart;
                for (int i = 0; i < count; i++, p += bytes)
                {
                    if (floats) dst[i] = *(float*)p;
                    else if (bits == 16) dst[i] = *(short*)p / 32768f;
                    else if (bits == 8) dst[i] = (*p - 128) / 128f;                          // 8-bit WAV is 0..255, middle 128
                    else if (bits == 24) dst[i] = ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f;
                    else dst[i] = *(int*)p / 2147483648f;
                }
            }
            return samples;
        }

        // ================= exporting the game's original sounds as WAV =================
        private static void StartExport()
        {
            if (exportQueue.Count > 0)
                return;
            exportQueue.AddRange(Sounds.clips.Keys);
            exportTotal = exportQueue.Count;
            exportDone = 0;
            if (exportTotal == 0)
                Say("The game's sounds haven't loaded yet; try again in a moment.");
        }

        // One sound per frame, so the game doesn't freeze. A sound that isn't loaded yet is asked to load and waits.
        private static void ExportStep()
        {
            string name = exportQueue[0];
            Sounds.ClipState st;
            if (!Sounds.clips.TryGetValue(name, out st) || st.Clip == null)
            {
                exportQueue.RemoveAt(0);
                return;
            }
            AudioClip c = st.Clip;
            if (c.loadState != AudioDataLoadState.Loaded)
            {
                if (c.loadState != AudioDataLoadState.Loading)
                    c.LoadAudioData();
                if (c.loadState == AudioDataLoadState.Failed)
                    exportQueue.RemoveAt(0);
                return;
            }
            exportQueue.RemoveAt(0);
            try
            {
                int count = c.samples * c.channels;
                // Untouched samples: the kept original if the volume was changed, else the sound as it is now.
                Il2CppStructArray<float> data = st.Original;
                if (data == null)
                {
                    data = new Il2CppStructArray<float>(count);
                    if (!Sounds.ReadSamples(c, data, count))
                        throw new Exception("couldn't read it");
                }
                string dir = Path.Combine(Root, OriginalsFolder);
                Directory.CreateDirectory(dir);
                WriteWav(Path.Combine(dir, SafeName(name) + ".wav"), data, c.channels, c.frequency);
                exportDone++;
            }
            catch (Exception e)
            {
                Say("Couldn't export '" + name + "': " + e.Message);
            }
            if (exportQueue.Count == 0)
            {
                WriteSoundList(Path.Combine(Root, OriginalsFolder, "SOUND LIST.txt"));
                Say("Exported " + exportDone + " of " + exportTotal + " sounds to SoundPacks\\" + OriginalsFolder + ".");
                OpenFolder(Path.Combine(Root, OriginalsFolder));
                ConfigHub.Refresh();
            }
        }

        // A 16-bit PCM WAV: the 44-byte header (RIFF / fmt / data), then each sample as a whole number -32767..32767.
        private static unsafe void WriteWav(string path, Il2CppStructArray<float> data, int channels, int rate)
        {
            int count = data.Length;
            float* src = (float*)IntPtr.Add(data.Pointer, 4 * IntPtr.Size);
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
                var buffer = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    float v = src[i];
                    short s = (short)Math.Round((v > 1f ? 1f : (v < -1f ? -1f : v)) * 32767f);
                    buffer[i * 2] = (byte)s;
                    buffer[i * 2 + 1] = (byte)(s >> 8);
                }
                w.Write(buffer);
            }
        }

        // ================= a new pack template and the sound list =================
        private static void MakeTemplate()
        {
            try
            {
                string dir = Path.Combine(Root, "My Sound Pack");
                for (int n = 2; Directory.Exists(dir); n++)
                    dir = Path.Combine(Root, "My Sound Pack " + n);
                Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "pack.txt"), new[]
                {
                    "# About this sound pack (shown in MOD CONFIGS -> Sounds). Lines starting with # are notes.",
                    "name=" + Path.GetFileName(dir),
                    "author=",
                    "description=",
                });
                WriteSoundList(Path.Combine(dir, "SOUND LIST.txt"));
                Say("Made " + Path.GetFileName(dir) + ". Put WAV files in it (see SOUND LIST.txt), then RELOAD PACKS.");
                OpenFolder(dir);
                ScanPacks();
                ConfigHub.Refresh();
            }
            catch (Exception e)
            {
                Say("Couldn't make the pack folder: " + e.Message);
            }
        }

        // Every sound by role and group, with the file name that replaces it, plus how to make a pack.
        private static void WriteSoundList(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SOUND LIST - which WAV file name replaces which sound (FNAF Online " + Application.version + ", ModConfigs)");
            sb.AppendLine();
            sb.AppendLine("HOW TO MAKE A SOUND PACK");
            sb.AppendLine(" 1. Put WAV files in your pack folder, named after the sound they replace: the FILE column below.");
            sb.AppendLine("    The name shown on the Sounds page works too (e.g. \"Foxy running.wav\"). Upper / lower case doesn't matter.");
            sb.AppendLine(" 2. WAV only (8 / 16 / 24 / 32-bit or 32-bit float, mono or stereo, any sample rate).");
            sb.AppendLine("    Audacity: File > Export > Export as WAV. MOD CONFIGS > Sounds > EXPORT THE GAME'S ORIGINAL SOUNDS");
            sb.AppendLine("    saves every original sound as WAV (in SoundPacks\\" + OriginalsFolder + ") to start from.");
            sb.AppendLine(" 3. Fill in pack.txt (name, author, description).");
            sb.AppendLine(" 4. In the game: MOD CONFIGS > Sounds > RELOAD PACKS. Your pack shows up with an on / off switch.");
            sb.AppendLine(" 5. To share it, zip the pack folder. Others unzip it into <game>\\UserData\\ModConfigs\\SoundPacks.");
            sb.AppendLine(" When two packs replace the same sound, the one lower in the list wins (packs are sorted by folder");
            sb.AppendLine(" name, so start the names with 1, 2, 3... to choose the order). Volume sliders work on pack sounds too.");
            foreach (string section in Sounds.SectionIds)
            {
                bool header = false;
                string group = null;
                foreach (Sounds.Entry e in Sounds.entries.Values)
                {
                    if (e.Section != section)
                        continue;
                    if (!header) { sb.AppendLine(); sb.AppendLine("== " + Sounds.SectionTitle(section) + " =="); header = true; }
                    if (e.Group != group) { group = e.Group; sb.AppendLine("  -- " + group + " --"); }
                    sb.AppendLine("    " + (SafeName(e.Clip) + ".wav").PadRight(48) + e.Label);
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ================= the page part =================
        public static void BuildSection(ConfigPage page)
        {
            page.Space(10);
            page.Label("Sound packs", 26).color = UiKit.Purple;
            page.Label("Replace any sound with your own WAV file. A pack is a folder in UserData\\ModConfigs\\SoundPacks with WAV files "
                + "named after the sounds they replace. MAKE A NEW PACK creates one with a list of every file name. Share a pack by "
                + "zipping its folder. A lower pack wins when two replace the same sound.", 17).color = UiKit.DimText;
            page.Button("OPEN SOUND PACKS FOLDER", new Action(OpenRoot));
            page.Button("MAKE A NEW PACK (folder + list of every sound's file name)", new Action(MakeTemplate));
            page.Button(exportQueue.Count > 0 ? "EXPORTING... " + exportDone + " / " + exportTotal : "EXPORT THE GAME'S ORIGINAL SOUNDS AS WAV", new Action(StartExport));
            page.Button("RELOAD PACKS (after adding or changing files)", new Action(Reload));
            if (packs.Count == 0)
                page.Label("No sound packs yet.", 18).color = UiKit.DimText;
            foreach (Pack p in packs)
            {
                string text = p.Name + (p.Author.Length > 0 ? " by " + p.Author : "") + "  -  " + p.Files.Count + " sound(s)";
                if (p.Unmatched.Count > 0)
                    text += ", " + p.Unmatched.Count + " file(s) not matched: " + string.Join(", ", p.Unmatched.ToArray());
                var t = new PackToggle(p.Folder);
                page.Toggle(text, new Func<bool>(t.Get), new Action<bool>(t.Set));
                if (p.Description.Length > 0)
                    page.Label(p.Description, 16).color = UiKit.DimText;
            }
            foreach (string s in status)
                page.Label(s, 16).color = UiKit.DimText;
        }

        // One pack's on / off switch (a small class instead of a lambda, see ConfigHub.cs).
        private sealed class PackToggle
        {
            private readonly string folder;
            public PackToggle(string f) { folder = f; }
            public bool Get() { foreach (Pack p in packs) if (p.Folder == folder) return IsOn(p); return false; }
            public void Set(bool on) { enabled[folder] = on; SaveState(); NeedRefresh = true; ConfigHub.Refresh(); }
        }

        private static void Reload()
        {
            ScanPacks();
            Say("Reloaded: " + packs.Count + " pack(s) found.");
            ConfigHub.Refresh();
        }

        private static void OpenRoot()
        {
            Directory.CreateDirectory(Root);
            OpenFolder(Root);
        }

        // Opens a folder in Explorer through Unity (a file:/// link), like FileConfigs does: no program is started.
        private static void OpenFolder(string dir)
        {
            try { Application.OpenURL(new Uri(dir + Path.DirectorySeparatorChar).AbsoluteUri); }
            catch (Exception e) { Say("Couldn't open the folder: " + e.Message); }
        }

        // The pack name for the page (from a pack file id "path|time").
        public static string PackNameOf(string id)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(id.Split('|')[0]));
            foreach (Pack p in packs)
                if (p.Folder == folder)
                    return p.Name;
            return folder;
        }

        // A message for the page (the last 5 are kept) and the log.
        private static void Say(string text)
        {
            MelonLogger.Msg("[ModConfigs] Sound packs: " + text);
            status.Add(text);
            if (status.Count > 5)
                status.RemoveAt(0);
        }
    }
}
