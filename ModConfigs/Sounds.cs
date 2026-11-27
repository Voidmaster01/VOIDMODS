// Sounds.cs - part of ModConfigs (new in 1.1.0): the built-in "Sounds" page, where EVERY sound in the game has its own
// volume slider (Voidmaster01's request: the game's own Music / SFX sliders lump everything together).
//
// WHAT IT DOES
//   - MOD CONFIGS -> Sounds lists every sound the game has, sorted by role (Everyone, Security, Parts & Service,
//     Backstage, Janitor, Purple Guy) and then by plain groups (Music, Office & cameras, Animatronics...).
//     Each slider goes from 0 % (silent) to 200 % (twice as loud); 100 % is how the game made it.
//   - It hides the game's own Music and SFX sliders on the Settings page and sets them to full, because the
//     per-sound sliders replace them (Master and Voice Chat stay). A toggle on the page brings them back.
//   - Saved in UserData\ModConfigs\sound-volumes.txt, one "sound name=percent" per line (only changed sounds).
//   - The page also holds the "Sound packs" part (SoundPacks.cs): replace any sound with your own WAV file.
//
// HOW IT WORKS
//   The game plays all its sounds from one list in its sound manager (GameAudioManager: 132 effects + 4 music
//   tracks, tested on 0.9.71). Every sound file (an AudioClip) is stored "decompress on load", which means its raw
//   samples sit in memory and can be read and rewritten (AudioClip.GetData / SetData). A sample is one number
//   between -1 and 1 saying where the speaker should be at that instant; multiplying every sample by 0.5 makes
//   the sound half as loud. So instead of chasing every speaker the sound might play on (doors, Foxy, the phone,
//   one-shot sounds, fades the game does itself), ModConfigs changes the sound itself, once. Wherever and however
//   the game plays it afterwards, it is already at the chosen volume.
//   The untouched samples are kept the first time a sound is changed, so going back to 100 % (or to any other
//   value) always starts from the original and never loses quality.
//   Boosting above 100 % can push samples past -1 / 1, which would crackle, so those are held at the limit.
//   Dragging a slider only remembers the new value; the sound is rewritten a moment after the slider stops, so a
//   long music track isn't rewritten 60 times a second.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class Sounds
    {
        public const string PageName = "Sounds";

        // ================= the list of sounds: where each one goes on the page =================
        // "sound file name|role section|group|name shown on the page". The sound file name is the AudioClip's name
        // in the game. A sound that isn't in this list (a new one from a game update) still gets a slider: it shows
        // under Everyone -> New / unknown with its file name, so nothing is ever missing from the page.
        private static readonly string[] Table =
        {
            // ---- Everyone: music ----
            "GamingTypo Menu Music 2|Everyone|Music|Menu music",
            "Watch your 6|Everyone|Music|Watch Your 6",
            "Last Breath|Everyone|Music|Last Breath",
            "Credits Theme|Everyone|Music|Credits theme",
            "GamingTypo Win Music|Everyone|Music|Win music",
            "game win|Everyone|Music|Win jingle",
            "kids cheering|Everyone|Music|Kids cheering (6 AM)",
            "heavens choir|Everyone|Music|Heaven's choir",
            "King Nasir Theme Song|Everyone|Music|King Nasir theme",
            "foxy song|Everyone|Music|Foxy's song",
            // ---- Everyone: menus and alerts ----
            "fnaf blip|Everyone|Menus & alerts|Menu blip",
            "select 1|Everyone|Menus & alerts|Select 1",
            "select 2|Everyone|Menus & alerts|Select 2",
            "button error|Everyone|Menus & alerts|Button error",
            "achievement unlock|Everyone|Menus & alerts|Achievement unlocked",
            "message received|Everyone|Menus & alerts|Message received",
            "new message|Everyone|Menus & alerts|New chat message",
            "ping|Everyone|Menus & alerts|Ping",
            "bell chime|Everyone|Menus & alerts|Bell chime",
            "Tyler1 Scream|Everyone|Menus & alerts|Kicked from the game (scream)",
            "manual|Everyone|Menus & alerts|Manual open",
            "page flip|Everyone|Menus & alerts|Manual page flip",
            // ---- Everyone: office and cameras ----
            "camera flip|Everyone|Office & cameras|Monitor / laptop up and down",
            "camera boot up|Everyone|Office & cameras|Cameras starting",
            "camera blip|Everyone|Office & cameras|Camera switch",
            "static audio|Everyone|Office & cameras|Camera static",
            "camera disturbance|Everyone|Office & cameras|Camera disturbance",
            "death static|Everyone|Office & cameras|Static after a death",
            "door toggle|Everyone|Office & cameras|Door open / close",
            "door knock|Everyone|Office & cameras|Door knock",
            "light hum|Everyone|Office & cameras|Door light hum",
            "power down|Everyone|Office & cameras|Power out",
            "ambiance 1|Everyone|Office & cameras|Background ambience",
            "eerie ambiance 0|Everyone|Office & cameras|Eerie ambience 1",
            "eerie ambiance 1|Everyone|Office & cameras|Eerie ambience 2",
            "eerie ambiance 2|Everyone|Office & cameras|Eerie ambience 3",
            "eerie ambiance 3|Everyone|Office & cameras|Eerie ambience 4",
            "eerie ambiance 4|Everyone|Office & cameras|Eerie ambience 5",
            "eerie ambiance 5|Everyone|Office & cameras|Eerie ambience 6",
            "window scare|Everyone|Office & cameras|Window scare",
            "stomps|Everyone|Office & cameras|Stomps",
            "moan|Everyone|Office & cameras|Moan",
            // ---- Everyone: phone and calls ----
            "calling|Everyone|Phone & calls|Calling (ringing)",
            "call pick up|Everyone|Phone & calls|Call picked up",
            "call failed|Everyone|Phone & calls|Call failed",
            "radio static|Everyone|Phone & calls|Call static",
            "radio beep|Everyone|Phone & calls|Call beep",
            // ---- Everyone: animatronics ----
            "jumpscare scream|Everyone|Animatronics|Jumpscare scream",
            "gf jumpscare scream|Everyone|Animatronics|Golden Freddy jumpscare",
            "Foxy Run1|Everyone|Animatronics|Foxy running",
            "foxy taunt|Everyone|Animatronics|Foxy taunt",
            "thunk|Everyone|Animatronics|Foxy hits the door",
            "foxy normal thunk|Everyone|Animatronics|Foxy hits the door (normal)",
            "foxy manic thunk|Everyone|Animatronics|Foxy hits the door (manic)",
            "manic foxy|Everyone|Animatronics|Manic Foxy",
            "walk0|Everyone|Animatronics|Footsteps 1",
            "walk1|Everyone|Animatronics|Footsteps 2",
            "walk2|Everyone|Animatronics|Footsteps 3",
            "walk3|Everyone|Animatronics|Footsteps 4",
            "walk4|Everyone|Animatronics|Footsteps 5",
            "endo footsteps|Everyone|Animatronics|Endo footsteps",
            "freddy laugh normal speed|Everyone|Animatronics|Freddy laugh",
            "freddy laugh 1|Everyone|Animatronics|Freddy laugh 1",
            "freddy laugh 2|Everyone|Animatronics|Freddy laugh 2",
            "freddy laugh 3|Everyone|Animatronics|Freddy laugh 3",
            "freddy approaching|Everyone|Animatronics|Freddy approaching",
            "Freddy Music Box 2|Everyone|Animatronics|Freddy's music box",
            "Freddy Music Box|Everyone|Animatronics|Freddy's Toreador march",
            "freddy nose honk|Everyone|Animatronics|Freddy nose honk",
            "wind up|Everyone|Animatronics|Wind-up",
            "ferocious banging|Everyone|Animatronics|Ferocious banging",
            "springlocked|Everyone|Animatronics|Springlocked",
            "sister location scream|Everyone|Animatronics|Sister Location scream 1",
            "sister location scream 2|Everyone|Animatronics|Sister Location scream 2",
            // ---- Everyone: Wordle and minigames ----
            "guess the wordle|Everyone|Wordle & minigames|Wordle guess",
            "wordle win|Everyone|Wordle & minigames|Wordle solved",
            "midnight motorist theme|Everyone|Wordle & minigames|Midnight Motorist music",
            "mm hit|Everyone|Wordle & minigames|Midnight Motorist hit",
            "gravity flip|Everyone|Wordle & minigames|Gravity flip",
            "next level|Everyone|Wordle & minigames|Next level",
            "next teir|Everyone|Wordle & minigames|Next tier",
            "failed sfx|Everyone|Wordle & minigames|Failed",
            // ---- Security ----
            "ElevenLabs Hand Unit Security Office|Security|Phone|Phone call (Security)",
            "keypad button press|Security|Keypad|Keypad button",
            "number 01|Security|Keypad|Keypad voice 1",
            "number 02|Security|Keypad|Keypad voice 2",
            "number 03|Security|Keypad|Keypad voice 3",
            "number 04|Security|Keypad|Keypad voice 4",
            "number 05|Security|Keypad|Keypad voice 5",
            "number 06|Security|Keypad|Keypad voice 6",
            "number 07|Security|Keypad|Keypad voice 7",
            "number 08|Security|Keypad|Keypad voice 8",
            "number 09|Security|Keypad|Keypad voice 9",
            "keypad alarm|Security|Keypad|Keypad alarm",
            "fan|Security|Office|Office fan",
            // ---- Parts & Service ----
            "ElevenLabs Hand Unit Parts And Service|PartsAndService|Phone|Phone call (Parts & Service)",
            "charging|PartsAndService|Generator|Generator charging",
            "power generator warning|PartsAndService|Generator|Generator warning",
            "generator shut down|PartsAndService|Generator|Generator shut down",
            "warning alarm|PartsAndService|Generator|Warning alarm",
            // ---- Backstage ----
            "ElevenLabs Hand Unit Backstage|Backstage|Phone|Phone call (Backstage)",
            "controlled shock|Backstage|Shock|Controlled shock",
            "sizzle|Backstage|Shock|Shock sizzle",
            // ---- Janitor ----
            "ElevenLabs Hand Unit Janitor|Janitor|Phone|Phone call (Janitor)",
            "mask up|Janitor|Mask|Mask up",
            "mask down|Janitor|Mask|Mask down",
            "mask open|Janitor|Mask|Mask open",
            "mask shut|Janitor|Mask|Mask shut",
            "deep breaths|Janitor|Mask|Breathing in the mask",
            "GainingExposure|Janitor|Mask|Gaining exposure",
            "janitor door open|Janitor|Doors|Janitor door open",
            "janitor door close|Janitor|Doors|Janitor door close",
            "janitor enter 1|Janitor|Doors|Something enters 1",
            "janitor enter 2|Janitor|Doors|Something enters 2",
            "janitor enter 3|Janitor|Doors|Something enters 3",
            "janitor enter 4|Janitor|Doors|Something enters 4",
            "lure result|Janitor|Lures & trackers|Audio lure",
            "radar ping|Janitor|Lures & trackers|Motion detector ping",
            // ---- Purple Guy ----
            "sabotage system|PurpleGuy|Sabotage|Sabotage",
        };

        // The role sections in page order, with the title shown for each.
        internal static readonly string[] SectionIds = { "Everyone", "Security","PartsAndService", "Backstage", "Janitor", "PurpleGuy" };
        internal static string SectionTitle(string id)
        {
            switch (id)
            {
                case "Everyone": return "Everyone (all roles)";
                case "Security": return "Security Guard";
                case "PartsAndService": return "Parts & Service";
                case "PurpleGuy": return "Purple Guy";
                default: return id;
            }
        }

        // One sound on the page.
        internal sealed class Entry
        {
            public string Clip;     // the sound file's name (the key)
            public string Section;  // role section id
            public string Group;    // plain group inside the section
            public string Label;    // the name shown
        }

        // What ModConfigs knows about one sound file in memory.
        internal sealed class ClipState
        {
            public AudioClip Clip;
            public IntPtr Native;                  // the game object's address: changes if the game loads the sound again
            public Il2CppStructArray<float> Original; // untouched samples, kept from the first change on
            public float Applied = 1f;             // the volume the samples are at now (1 = as the game made it)
            public readonly List<Sound> Owners = new List<Sound>(); // the sound manager's entries that play this sound
            // Set while a sound pack replaces this sound (SoundPacks.cs): the pack's own sound, its untouched samples
            // (read from the WAV file), the volume they're at now, and which file it came from.
            public AudioClip PackClip;
            public Il2CppStructArray<float> PackSamples;
            public float PackApplied = -1f;        // -1 = not written yet
            public string PackFile;
        }

        // StringComparer.OrdinalIgnoreCase: "Foxy Run1" and "foxy run1" count as the same name.
        internal static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase); // only changed ones
        internal static readonly Dictionary<string, ClipState> clips = new Dictionary<string, ClipState>(StringComparer.OrdinalIgnoreCase);

        // Asks for every sound to be brought to its chosen volume on the next frame (used after a pack swap).
        internal static void RequestApply() { applyAt = 0f; }
        private static string file;
        private static float nextScan;       // when to look at the sound manager again
        private static float applyAt = -1f;  // when to rewrite changed sounds (a moment after the last slider move)
        private static float saveAt = -1f;
        private static bool hideGameSliders = true;

        // ================= start-up: read the table and the saved volumes =================
        public static void Load()
        {
            foreach (string row in Table)
            {
                string[] p = row.Split('|');
                entries[p[0]] = new Entry { Clip = p[0], Section = p[1], Group = p[2], Label = p[3] };
            }
            file = Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs", "sound-volumes.txt");
            try
            {
                if (File.Exists(file))
                    foreach (string line in File.ReadAllLines(file))
                    {
                        // "name=percent"; lines starting with # are notes. LastIndexOf: a name could contain "=".
                        int eq = line.LastIndexOf('=');
                        if (line.StartsWith("#") || eq <= 0)
                            continue;
                        string key = line.Substring(0, eq).Trim();
                        float pct;
                        if (key == "hide game music and sfx sliders")
                            hideGameSliders = line.Substring(eq + 1).Trim() != "false";
                        else if (float.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                            volumes[key] = Mathf.Clamp(pct, 0f, 200f) / 100f;
                    }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't read " + file + ": " + e.Message);
            }
            applyAt = 0f; // apply saved volumes as soon as the sounds are found
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var lines = new List<string>();
                lines.Add("# ModConfigs sound volumes: sound name=percent (100 = as the game made it). Change them in MOD CONFIGS -> Sounds.");
                lines.Add("hide game music and sfx sliders=" + (hideGameSliders ? "true" : "false"));
                foreach (KeyValuePair<string, float> kv in volumes)
                    lines.Add(kv.Key + "=" + Math.Round(kv.Value * 100f).ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(file, lines);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't save " + file + ": " + e.Message);
            }
        }

        public static float Volume(string clip)
        {
            float v;
            return volumes.TryGetValue(clip, out v) ? v : 1f;
        }

        private static void SetVolume(string clip, float v)
        {
            v = Mathf.Clamp(v, 0f, 2f);
            if (Mathf.Abs(v - 1f) < 0.005f) volumes.Remove(clip); // back at 100 %: nothing to remember
            else volumes[clip] = v;
            applyAt = Time.unscaledTime + 0.4f;
            saveAt = Time.unscaledTime + 0.5f;
        }

        // ================= every frame (ModConfigsMod.OnUpdate) =================
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= nextScan)
            {
                nextScan = now + 2f;
                try { FindSounds(); } catch (Exception e) { MelonLogger.Warning("[ModConfigs] Sounds: " + e.Message); }
                try { GameSliders(); } catch (Exception e) { MelonLogger.Warning("[ModConfigs] Settings sliders: " + e.Message); }
            }
            SoundPacks.Tick(); // swaps in pack sounds when something changed, runs the WAV export a little each frame
            if (applyAt >= 0f && now >= applyAt)
            {
                applyAt = -1f;
                ApplyAll();
            }
            if (saveAt >= 0f && now >= saveAt)
            {
                saveAt = -1f;
                Save();
            }
        }

        // Reads the sound manager's list. A sound seen for the first time (or loaded again by the game, so a new object)
        // gets its saved volume applied.
        private static void FindSounds()
        {
            GameAudioManager gam = UnityEngine.Object.FindObjectOfType<GameAudioManager>();
            if (gam == null)
                return;
            bool changed = false;
            changed |= AddSounds(gam.sfxSounds);
            changed |= AddSounds(gam.musicSounds);
            if (changed)
                applyAt = 0f;
        }

        private static bool AddSounds(Il2CppReferenceArray<Sound> list)
        {
            if (list == null)
                return false;
            bool changed = false;
            foreach (Sound s in list)
            {
                if (s == null || s.audioClip == null)
                    continue;
                AudioClip c = s.audioClip;
                string name = c.name;
                ClipState st;
                // A pack's sound has the same name as the one it replaces, so it counts as "already known".
                bool ours = clips.TryGetValue(name, out st) && (st.Native == c.Pointer || (st.PackClip != null && st.PackClip.Pointer == c.Pointer));
                if (!ours)
                {
                    st = new ClipState { Clip = c, Native = c.Pointer };
                    clips[name] = st;
                    changed = true;
                    SoundPacks.NeedRefresh = true; // a fresh sound from the game: a pack may need to replace it again
                }
                bool owned = false;
                foreach (Sound o in st.Owners)
                    if (o.Pointer == s.Pointer) { owned = true; break; }
                if (!owned)
                {
                    st.Owners.Add(s);
                    SoundPacks.NeedRefresh = true; // a new entry may still point at the game's sound
                }
                if (!entries.ContainsKey(name))
                    entries[name] = new Entry { Clip = name, Section = "Everyone", Group = "New / unknown", Label = name };
            }
            return changed;
        }

        // Brings every known sound to its chosen volume.
        // A sound that is still loading sets "pending", and everything is tried again half a second later.
        private static bool pending;
        private static void ApplyAll()
        {
            pending = false;
            foreach (KeyValuePair<string, ClipState> kv in clips)
            {
                try { Apply(kv.Value, Volume(kv.Key)); }
                catch (Exception e) { MelonLogger.Warning("[ModConfigs] Couldn't change the volume of '" + kv.Key + "': " + e.Message); }
            }
            if (pending)
                applyAt = Time.unscaledTime + 0.5f;
        }

        // Rewrites one sound's samples at the given volume (see HOW IT WORKS at the top).
        private static void Apply(ClipState st, float volume)
        {
            // A sound pack replaces this sound: its samples came from the WAV file, so they are always at hand.
            if (st.PackClip != null)
            {
                if (Mathf.Abs(st.PackApplied - volume) >= 0.001f && WriteScaled(st.PackSamples, st.PackClip, volume))
                    st.PackApplied = volume;
                return;
            }
            if (st.Clip == null || Mathf.Abs(st.Applied - volume) < 0.001f)
                return;
            int count = st.Clip.samples * st.Clip.channels; // samples = per channel, so stereo has twice as many numbers
            if (count <= 0)
                return;
            // The game loads a sound's samples only when it first plays it; reading them before that freezes the game.
            // So: ask Unity to load it (it does that in the background) and come back once it says "Loaded".
            if (st.Clip.loadState != AudioDataLoadState.Loaded)
            {
                if (st.Clip.loadState != AudioDataLoadState.Loading)
                    st.Clip.LoadAudioData();
                pending = true;
                return;
            }
            if (st.Original == null)
            {
                st.Original = new Il2CppStructArray<float>(count);
                if (!ReadSamples(st.Clip, st.Original, count))
                {
                    st.Original = null;
                    MelonLogger.Warning("[ModConfigs] Couldn't read the sound '" + st.Clip.name + "' (" + st.Clip.loadType + "); its volume stays as the game made it.");
                    st.Applied = volume; // don't try again every half second
                    return;
                }
            }
            if (WriteScaled(st.Original, st.Clip, volume))
                MelonLogger.Msg("[ModConfigs] Sound '" + st.Clip.name + "' is now at " + Math.Round(volume * 100f) + " %.");
            st.Applied = volume;
            if (Mathf.Abs(volume - 1f) < 0.001f)
                st.Original = null; // back to the original: no need to keep a second copy in memory
        }

        // Writes "source samples x volume" into a sound. Used for the game's sounds and for sound-pack sounds.
        internal static unsafe bool WriteScaled(Il2CppStructArray<float> source, AudioClip clip, float volume)
        {
            int count = source.Length;
            var scaled = new Il2CppStructArray<float>(count);
            // The numbers of a game array sit right after a small header of 4 pointer-sized fields (type, lock,
            // bounds, length). A pointer (float*) walks them directly, which is far faster than going through the
            // array's indexer for millions of samples. "unsafe" is C#'s word for code that uses pointers.
            float* src = (float*)IntPtr.Add(source.Pointer, 4 * IntPtr.Size);
            float* dst = (float*)IntPtr.Add(scaled.Pointer, 4 * IntPtr.Size);
            for (int i = 0; i < count; i++)
            {
                float v = src[i] * volume;
                dst[i] = v > 1f ? 1f : (v < -1f ? -1f : v); // held at the limit instead of crackling
            }
            // SetData is in the game's own code (the game uses it), so the normal call is safe, unlike GetData.
            if (clip.SetData(scaled, 0))
                return true;
            MelonLogger.Warning("[ModConfigs] Couldn't change the volume of '" + clip.name + "'.");
            return false;
        }

        // ---- reading a sound's samples ----
        // Why not just clip.GetData(...)? The game itself never reads samples, so Unity's GetData was left out of the
        // game's code when it was built, and the stand-in copy that MelonLoader's Il2CppInterop rebuilds hands Unity a
        // bad memory block: the game freezes or crashes (tested on 0.9.71). So this calls Unity's own internal
        // function directly, the one GetData would have called: AudioClip::GetData_Injected(clip, block, offset).
        //   - the clip is passed as its native address (m_CachedPtr, what Unity's engine side knows it by);
        //   - the block is a small struct { where the numbers start, how many } (Unity calls it ManagedSpanWrapper),
        //     pointing straight into the game-side array, which the game's memory manager never moves.
        // IL2CPP.ResolveICall finds that internal function by name inside the game and gives it back as a C# delegate
        // (a variable that holds a function). It is not a Windows import, so antivirus tools have nothing to flag.
        [StructLayout(LayoutKind.Sequential)]
        private struct SpanBlock
        {
            public IntPtr Begin; // address of the first number
            public int Length;   // how many numbers
        }

        // The internal function's shape: (clip address, ref block, offset) -> true when it worked.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool GetDataFn(IntPtr clip, ref SpanBlock data, int samplesOffset);
        private static GetDataFn getData;

        internal static bool ReadSamples(AudioClip clip, Il2CppStructArray<float> into, int count)
        {
            if (getData == null)
                getData = Il2CppInterop.Runtime.IL2CPP.ResolveICall<GetDataFn>("UnityEngine.AudioClip::GetData_Injected");
            IntPtr self = clip.m_CachedPtr;
            if (getData == null || self == IntPtr.Zero)
                return false;
            var block = new SpanBlock { Begin = IntPtr.Add(into.Pointer, 4 * IntPtr.Size), Length = count };
            return getData(self, ref block, 0);
        }

        // ================= the game's own Music / SFX sliders on the Settings page =================
        // The per-sound sliders replace them, so (while the toggle is on) they are hidden and set to full, so an old
        // low setting can't quietly turn everything down where no one can see it. Master and Voice Chat stay.
        private static void GameSliders()
        {
            foreach (SettingsUI s in Resources.FindObjectsOfTypeAll<SettingsUI>())
            {
                if (s == null || s.gameObject.scene.name == null)
                    continue; // a template, not the real Settings window
                Hide(s.musicVolumeSlider);
                Hide(s.sfxVolumeSlider);
            }
        }

        private static void Hide(Slider slider)
        {
            if (slider == null)
                return;
            if (hideGameSliders && slider.value < slider.maxValue)
                slider.value = slider.maxValue; // runs the game's own SetMusicVolume / SetSFXVolume, which also saves it
            GameObject row = RowOf(slider);
            if (row.activeSelf == hideGameSliders)
                row.SetActive(!hideGameSliders);
        }

        // The slider's row: its parent when that parent holds only this one slider (slider + its label), else the
        // slider itself.
        private static GameObject RowOf(Slider slider)
        {
            Transform parent = slider.transform.parent;
            if (parent != null && parent.GetComponentsInChildren<Slider>(true).Length == 1)
                return parent.gameObject;
            return slider.gameObject;
        }

        // ================= the page =================
        public static void BuildPage(ConfigPage page)
        {
            page.Header("Sounds");
            page.Label("Every sound has its own volume: 0 % is silent, 100 % is how the game made it, 200 % is twice as loud. "
                + "Changes apply right away, also to sounds that are playing. The game's Master and Voice Chat sliders (Settings) "
                + "still work on top of these. RESET TO DEFAULTS puts every sound back to 100 %.", 18).color = UiKit.DimText;
            page.Toggle("Hide the game's Music and SFX sliders (Settings) and keep them at full", new Func<bool>(GetHide), new Action<bool>(SetHide));
            SoundPacks.BuildSection(page); // the "Sound packs" part: packs on/off and the pack tools
            if (clips.Count == 0)
                page.Label("(The sound list appears once the game's sound manager has loaded; open this page again in a moment.)", 18).color = UiKit.DimText;

            foreach (string section in SectionIds)
            {
                // Collect this section's sounds by group, keeping the order of the table.
                var groups = new List<string>();
                var bySound = new List<Entry>();
                foreach (Entry e in entries.Values)
                    if (e.Section == section && (clips.ContainsKey(e.Clip) || clips.Count == 0))
                    {
                        bySound.Add(e);
                        if (!groups.Contains(e.Group)) groups.Add(e.Group);
                    }
                if (bySound.Count == 0)
                    continue;
                page.Space(10);
                page.Label(SectionTitle(section), 26).color = UiKit.Purple;
                foreach (string g in groups)
                {
                    page.Label(g, 20).color = UiKit.TextColor;
                    foreach (Entry e in bySound)
                        if (e.Group == g)
                        {
                            var row = new Row(e.Clip);
                            // A sound a pack replaces says which pack, in small purple text after its name.
                            string label = e.Label;
                            ClipState cs;
                            if (clips.TryGetValue(e.Clip, out cs) && cs.PackFile != null)
                                label += " <size=15><color=#b98cff>(" + SoundPacks.PackNameOf(cs.PackFile) + ")</color></size>";
                            page.Slider(label, new Func<double>(row.Get), new Action<double>(row.Set), 0, 200, "0'%'", true);
                        }
                }
            }
        }

        // One slider's link to its sound (a small class instead of a lambda, see ConfigHub.cs "WHY binding CLASSES").
        private sealed class Row
        {
            private readonly string clip;
            public Row(string c) { clip = c; }
            public double Get() { return Math.Round(Volume(clip) * 100f); }
            public void Set(double v) { SetVolume(clip, (float)(v / 100.0)); }
        }

        private static bool GetHide() { return hideGameSliders; }
        private static void SetHide(bool v)
        {
            hideGameSliders = v;
            saveAt = Time.unscaledTime + 0.1f;
            nextScan = 0f; // show / hide the game's sliders right away
        }

        // RESET TO DEFAULTS: every sound back to 100 %.
        public static void ResetAll()
        {
            volumes.Clear();
            applyAt = 0f;
            saveAt = 0f;
        }
    }
}
