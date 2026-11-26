// KeyBinds.cs - part of ModConfigs (new in 1.1.0): lets players change the keys FNAF Online uses (the game has no
// key binds of its own). It is ModConfigs' own built-in "Key Binds" page, together with the DANGER ZONE
// (DangerZone.cs). The Mod Menu mod only has a button that opens this page (Voidmaster01's decision: settings
// live in ModConfigs).
//
// WHAT IT DOES
//   The game's roles use five keys (from its How to Play manual):
//     W      Open/close door (Parts & Services, Backstage)
//     A      Close left door (Security) / Look left (Parts & Services, Backstage) / Mask (Janitor)
//     S      Flip monitor (Security) / Shock (Backstage)
//     D      Close right door (Security) / Look right (Parts & Services, Backstage) / Monitor (Janitor)
//     Space  Open/close laptop (Parts & Services)
//   Each can be moved to any other key. The choices are saved in UserData\MelonPreferences.cfg (category KeyBinds).
//
// HOW THE REMAPPING WORKS
//   The game reads these keys with Unity's new Input System, by asking a key object "isPressed?" (also
//   "wasPressedThisFrame?" and "wasReleasedThisFrame?"). Those questions are property reads on ButtonControl
//   (every key is a ButtonControl), so three Harmony prefixes sit in front of them. When the game asks about a key
//   that was moved, the prefix asks the NEW key instead and returns that answer; the game never knows.
//   Example: W moved to Up Arrow -> the game asks "is W pressed?" -> we answer with "is Up Arrow pressed?".
//   The old key then does nothing for the game (unless another binding uses it), which is what players expect.
//   We found this by testing: a probe mod showed the game asking /Keyboard/w, a, s, d through isPressed.
//
// LIMITS
//   Esc (settings), Tab (chat), N (what's new) and ? (how to play) are read another way (Unity's old input system,
//   which crashes the game when patched), so they can't be moved here. The game's own Settings has the mute key.
//
// CONTROLLERS
//   Controller.cs presses the REAL keyboard key (the one chosen here) while its controller button is held, because
//   the game only looks at its keys after a real keyboard event (tested: it never asked about A while only a
//   controller button was pressed). ChosenKey tells it which key to press. The page shows the controller settings
//   between the keys and the DANGER ZONE.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModConfigs
{
    internal static class KeyBinds
    {
        public const string Category = "KeyBinds";
        public const string PageName = "Key Binds";

        // One rebindable game key: the key the game asks about, what it does, and the saved setting holding the
        // key the player chose (as the Key enum's name, e.g. "UpArrow").
        private sealed class Bind
        {
            public Key GameKey;
            public string What;
            public MelonPreferences_Entry<string> Entry;
        }

        private static readonly List<Bind> binds = new List<Bind>();

        // The moved keys only: game key -> chosen key. Looked up on every key question, so a Dictionary (fast).
        private static readonly Dictionary<Key, Key> remap = new Dictionary<Key, Key>();

        // True while our own code reads keys, so the prefixes below don't remap OUR reads (that would loop forever:
        // asking "is Up Arrow pressed?" from inside the prefix must get the real answer).
        internal static bool Bypass;

        private static Bind capturing;         // the row waiting for "press a key...", or null
        private static float captureStarted;   // when it started (Time.unscaledTime), to ignore that same frame

        // Unity's reserved menu keys in this game (read through the old input system): binding a game key to one of
        // these still works, but the key keeps opening its menu too, so the page warns about it.
        private static readonly Key[] MenuKeys = { Key.Escape, Key.Tab, Key.N, Key.Slash };

        // ================= settings =================

        // Creates the settings (or reads them back from MelonPreferences.cfg) and builds the remap table.
        public static void Load()
        {
            MelonPreferences_Category cat = MelonPreferences.CreateCategory(Category, PageName);
            Add(cat, Key.W, "Open/close door (Parts & Services, Backstage)");
            Add(cat, Key.A, "Close left door (Security) / Look left (Parts & Services, Backstage) / Mask (Janitor)");
            Add(cat, Key.S, "Flip monitor (Security) / Shock (Backstage)");
            Add(cat, Key.D, "Close right door (Security) / Look right (Parts & Services, Backstage) / Monitor (Janitor)");
            Add(cat, Key.Space, "Open/close laptop (Parts & Services)");
            Controller.Load(cat); // the controller settings live in the same category (Controller.cs)
            Rebuild();
        }

        // is_hidden = true: the entry doesn't get its own automatic row in MOD CONFIGS; our page shows it instead.
        private static void Add(MelonPreferences_Category cat, Key key, string what)
        {
            MelonPreferences_Entry<string> e = cat.CreateEntry("Key_" + key, key.ToString(), "Key for " + key, what, true);
            binds.Add(new Bind { GameKey = key, What = what, Entry = e });
        }

        // Turns the saved names back into keys and fills the remap table with the ones that differ from the default.
        // Enum.TryParse reads a name like "UpArrow" back into Key.UpArrow; a broken name falls back to the default.
        private static void Rebuild()
        {
            remap.Clear();
            foreach (Bind b in binds)
            {
                Key chosen;
                if (!Enum.TryParse(b.Entry.Value, out chosen) || chosen == Key.None)
                    chosen = b.GameKey;
                if (chosen != b.GameKey)
                    remap[b.GameKey] = chosen;
            }
        }

        private static Key Chosen(Bind b)
        {
            Key k;
            return remap.TryGetValue(b.GameKey, out k) ? k : b.GameKey;
        }

        // The keyboard key the player chose for a game key (the game key itself when it wasn't moved).
        internal static Key ChosenKey(Key gameKey)
        {
            Key k;
            return remap.TryGetValue(gameKey, out k) ? k : gameKey;
        }

        private static void Save(Bind b, Key key)
        {
            b.Entry.Value = key.ToString();
            MelonPreferences.GetCategory(Category).SaveToFile(false); // false = no "saved" line in the log
            Rebuild();
        }

        // RESET TO DEFAULTS on the Key Binds page: every key back to itself (never touches the save data).
        public static void ResetAll()
        {
            capturing = null;
            foreach (Bind b in binds)
                b.Entry.Value = b.GameKey.ToString();
            Controller.ResetAll(); // the controller buttons and speed go back to their defaults too
            MelonPreferences.GetCategory(Category).SaveToFile(false);
            Rebuild();
        }

        // ================= the remapping (called from the Harmony patches) =================

        // Answers a key question for the game. Returns false when the key wasn't moved (then the game's own answer
        // is used). which: 0 = isPressed, 1 = wasPressedThisFrame, 2 = wasReleasedThisFrame.
        internal static bool TryAnswer(ButtonControl control, int which, out bool answer)
        {
            answer = false;
            if (Bypass)
                return false;
            KeyControl key = control.TryCast<KeyControl>(); // TryCast = "is it really a key? then treat it as one"
            if (key == null)
                return false;
            Key gameKey = key.keyCode;
            Key to;
            if (!remap.TryGetValue(gameKey, out to))
                return false; // not moved: the game gets its normal answer
            Bypass = true;
            try
            {
                Keyboard kb = Keyboard.current;
                KeyControl source = kb != null ? kb[to] : null;
                answer = source != null && (which == 0 ? source.isPressed : which == 1 ? source.wasPressedThisFrame : source.wasReleasedThisFrame);
            }
            finally
            {
                Bypass = false; // finally runs even after "return" or an error, so Bypass can't get stuck on
            }
            // One log line per moved key per game session, the first time the game uses it (handy for bug reports).
            if (logged.Add(gameKey))
                MelonLogger.Msg("[ModConfigs] Key binds: the game asked for " + Pretty(gameKey) + ", answered with " + Pretty(to) + ".");
            return true;
        }

        private static readonly HashSet<Key> logged = new HashSet<Key>(); // keys already logged this session

        // ================= "press a key..." =================

        // Called every frame by ModConfigsMod: while a row waits for a key, look at every key on the keyboard and take
        // the first one pressed this frame. Esc cancels. (The click that started it is a mouse click, not a key, but
        // the first frame is skipped anyway to be safe.)
        public static void Tick()
        {
            if (capturing == null || Time.unscaledTime - captureStarted < 0.1f)
                return;
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;
            Bypass = true;
            try
            {
                foreach (Key k in Enum.GetValues(typeof(Key)))
                {
                    if (k == Key.None)
                        continue;
                    KeyControl c;
                    try { c = kb[k]; } catch { continue; } // a few enum values have no key on a real keyboard
                    if (c == null || !c.wasPressedThisFrame)
                        continue;
                    Bind b = capturing;
                    capturing = null;
                    if (k != Key.Escape)
                        Save(b, k);
                    ConfigHub.Refresh();
                    return;
                }
            }
            finally
            {
                Bypass = false;
            }
        }

        // ================= the page section =================

        // The whole built-in page: key binds first, the DANGER ZONE last (far from everything else, like on GitHub).
        public static void BuildPage(ConfigPage page)
        {
            BuildSection(page);
            page.Space(20);
            // The controller section gets the same game keys with what they do (Controller.cs).
            var keys = new List<KeyValuePair<Key, string>>();
            foreach (Bind b in binds)
                keys.Add(new KeyValuePair<Key, string>(b.GameKey, b.What));
            Controller.BuildSection(page, keys);
            page.Space(30);
            DangerZone.BuildSection(page);
        }

        public static void BuildSection(ConfigPage page)
        {
            page.Header("Key binds");
            page.Label("Click a key, then press the new key for it (Esc cancels). RESET TO DEFAULTS puts every key back.", 18).color = UiKit.DimText;
            foreach (Bind b in binds)
                Row(page, b);
            Warnings(page);
        }

        // One row: the game key and what it does on the left, the chosen key as a button on the right.
        private static void Row(ConfigPage page, Bind b)
        {
            RectTransform row = page.Custom(78);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform left = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-300, -10));
            TextMeshProUGUI text = UiKit.Text(left, Pretty(b.GameKey) + "\n<size=15><color=#8c859e>" + b.What + "</color></size>", 24, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            text.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform btn = UiKit.Element(row, "Key", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(260, 56));
            var click = new StartCapture(b);
            TextMeshProUGUI label = UiKit.Button(btn, "", new Action(click.Click), false);
            if (capturing == b)
            {
                label.text = "PRESS A KEY...";
                label.color = UiKit.Purple;
            }
            else
            {
                Key chosen = Chosen(b);
                label.text = Pretty(chosen);
                label.color = chosen == b.GameKey ? UiKit.TextColor : UiKit.Purple; // purple = changed from default
            }
        }

        // Warns about two game keys on the same key, and about keys that also open one of the game's menus.
        private static void Warnings(ConfigPage page)
        {
            var used = new Dictionary<Key, string>();
            foreach (Bind b in binds)
            {
                Key k = Chosen(b);
                string other;
                if (used.TryGetValue(k, out other))
                    page.Label("Note: " + Pretty(b.GameKey) + " and " + other + " are both on " + Pretty(k) + ", so that key does both.", 18).color = UiKit.Purple;
                else
                    used[k] = Pretty(b.GameKey);
                if (Array.IndexOf(MenuKeys, k) >= 0 && k != b.GameKey)
                    page.Label("Note: " + Pretty(k) + " also opens one of the game's menus (Esc, Tab, N and ? can't be changed).", 18).color = UiKit.Purple;
            }
        }

        // Key names for people: "UpArrow" -> "Up Arrow", "Digit1" -> "1", "Numpad5" -> "Numpad 5".
        // The loop puts a space before each capital letter or first digit that follows a small letter.
        internal static string Pretty(Key k)
        {
            string s = k.ToString();
            if (s.StartsWith("Digit"))
                return s.Substring(5);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (char.IsUpper(s[i]) || (char.IsDigit(s[i]) && !char.IsDigit(s[i - 1]))) && char.IsLower(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // The button's click: start waiting for a key for this row (a named class instead of a lambda; see ConfigHub.cs).
        private sealed class StartCapture
        {
            private readonly Bind bind;
            public StartCapture(Bind b) { bind = b; }
            public void Click()
            {
                capturing = capturing == bind ? null : bind; // clicking again cancels
                captureStarted = Time.unscaledTime;
                ConfigHub.Refresh();
            }
        }
    }

    // ================= the Harmony patches =================
    // MethodType.Getter = patch the hidden "get_" method behind a property. "ref bool __result" lets a prefix set the
    // answer; returning false skips the game's own method. Returning true lets the normal answer through.

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.isPressed), MethodType.Getter)]
    internal static class IsPressedPatch
    {
        private static bool Prefix(ButtonControl __instance, ref bool __result)
        {
            bool answer;
            if (!KeyBinds.TryAnswer(__instance, 0, out answer))
                return true;
            __result = answer;
            return false;
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame), MethodType.Getter)]
    internal static class WasPressedPatch
    {
        private static bool Prefix(ButtonControl __instance, ref bool __result)
        {
            bool answer;
            if (!KeyBinds.TryAnswer(__instance, 1, out answer))
                return true;
            __result = answer;
            return false;
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasReleasedThisFrame), MethodType.Getter)]
    internal static class WasReleasedPatch
    {
        private static bool Prefix(ButtonControl __instance, ref bool __result)
        {
            bool answer;
            if (!KeyBinds.TryAnswer(__instance, 2, out answer))
                return true;
            __result = answer;
            return false;
        }
    }
}
