// KeyBinds.cs - part of ModConfigs (new in 1.1.0): lets players change the keys FNAF Online uses (the game has no
// key binds of its own), SEPARATELY FOR EACH ROLE AND EACH MINIGAME, on the keyboard and on a controller. It is
// ModConfigs' own built-in "Key Binds" page, together with the CONTROLLER settings (Controller.cs) and the DANGER ZONE
// (DangerZone.cs). The Mod Menu mod only has a button that opens this page (Voidmaster01's decision: settings live in
// ModConfigs). Binding is done here on the home screen, never during a night (Voidmaster01's decision).
//
// WHAT IT DOES
//   Every crew role has TWO sections (Voidmaster01's design): MONITOR DOWN and MONITOR UP, each listing the keys that
//   role uses (from the game's own TIPS screens and role cards, collected by Voidmaster01). The monitor-up section
//   also has Q / E (LB / RB) for the previous / next laptop tab: keys the game doesn't have, ADDED by ModConfigs.
//     Security Office        A close left door, D close right door, S flip monitor
//     Parts & Service        A look left, D look right, W open/close door (laptop closed only), Space laptop
//       Midnight Motorist    W A S D drive (the laptop's Games tab; the arrow keys always work too)
//     Backstage              A look left, D look right; W door only while looking at the door, S shock only while
//                            looking at the shock panel. Its screen is always on, so it has one section per VIEW:
//                            door, shock panel, maintenance, computer screen (the "monitor up" one, with the tabs)
//     Janitor's Closet       A open/close mask (monitor down only), D flip monitor
//       Freddy in Space      Space fly (the laptop's Games tab)
//     Purple Guy             A open/close left screen, D right screen: keys the game DOESN'T have (it only has the
//                            mouse on the screen-edge bars), ADDED by ModConfigs (Voidmaster01's defaults, LB/RB too)
//   Every row has a keyboard key and a controller button. GameContext.cs tells which section applies right now:
//   the player's role and whether the monitor is up, plus the minigame's section ON TOP while its Games tab is open
//   (the minigame's keys win, the role's other keys keep working, so the Janitor's mask still works while flying).
//   Outside a night, and while dead or spectating, nothing is changed. Saved in UserData\MelonPreferences.cfg,
//   category KeyBinds, as Key_<section>_<key> and Pad_<section>_<key> (the monitor-down sections kept their old ids).
//
// HOW THE KEYBOARD REMAPPING WORKS
//   The game reads these keys with Unity's new Input System, by asking a key object "isPressed?" (also
//   "wasPressedThisFrame?" and "wasReleasedThisFrame?"). Those questions are property reads on ButtonControl
//   (every key is a ButtonControl), so three Harmony prefixes sit in front of them. When the game asks about a key
//   that was moved in the CURRENT section, the prefix asks the NEW key instead and returns that answer.
//   Example: Security's A moved to Left Arrow -> the game asks "is A pressed?" -> we answer "is Left Arrow pressed?".
//   We found this by testing: a probe mod showed the game asking /Keyboard/w, a, s, d through isPressed.
//
// LIMITS
//   Esc (settings), Tab (chat), N (what's new) and ? (how to play) are read another way (Unity's old input system,
//   which crashes the game when patched), so they can't be moved here. The game's own Settings has the mute key.
//
// CONTROLLERS
//   Controller.cs presses the REAL keyboard key (the one chosen here for the current section) while the row's
//   controller button is held, because the game only looks at its keys after a real keyboard event (tested).
//   ActiveBinds tells it which rows apply right now.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigs
{
    // One row on the page: a game key in one section, what it does there, and the two saved choices.
    internal sealed class Bind
    {
        public KeySection Section;
        public Key GameKey;                                // the key the game asks about
        public string What;                                // what it does in this section
        public string DefaultPad;                          // default controller button (GamepadButton name)
        public MelonPreferences_Entry<string> KeyEntry;    // the keyboard key chosen (Key name, e.g. "UpArrow")
        public MelonPreferences_Entry<string> PadEntry;    // the controller button chosen ("None" = no button)
        // Set for keys the game DOESN'T have and ModConfigs adds (Purple Guy's screens): pressing the key or button
        // runs this instead of answering the game's key questions. GameKey is then only the default key.
        public Action OnPress;
    }

    // One section: a role, or a role's minigame.
    internal sealed class KeySection
    {
        public string Id;            // used in the setting names, e.g. "Janitor"
        public string Title;         // shown on the page
        public string Note;          // a dim line under the title (may be null)
        public PlayerRoles Role;
        public bool Minigame;        // true = applies on top of the role's section while the Games tab is open
        public string State;         // when it applies inside the role: "down" / "up" (monitor), Backstage: "door", "shock", "maintenance", "up"
        public readonly List<Bind> Binds = new List<Bind>();
    }

    internal static class KeyBinds
    {
        public const string Category = "KeyBinds";
        public const string PageName = "Key Binds";

        private static readonly List<KeySection> sections = new List<KeySection>();

        // What applies RIGHT NOW (rebuilt when the role, the minigame or a choice changes):
        //   active: game key -> its row (role rows, minigame rows replacing the role's for the same key)
        //   remap:  only the moved keys, game key -> chosen key. Looked up on every key question, so a Dictionary.
        private static readonly Dictionary<Key, Bind> active = new Dictionary<Key, Bind>();
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

        // Creates the sections and settings (or reads them back from MelonPreferences.cfg).
        public static void Load()
        {
            MelonPreferences_Category cat = MelonPreferences.CreateCategory(Category, PageName);
            KeySection s;

            // Every crew role has TWO sections (Voidmaster01's design): monitor down, and monitor up. The monitor-up one
            // also has the laptop tab keys. The ids of the monitor-down sections are the ones used before, so earlier
            // choices carry over. Monitor-up defaults: like monitor down, but LB/RB go to the tabs (doors on the D-pad).
            s = Section("Security", "Security Office: monitor down", PlayerRoles.SecurityOffice, false, "down", null);
            Add(cat, s, Key.A, "Close left door", GamepadButton.LeftShoulder);
            Add(cat, s, Key.D, "Close right door", GamepadButton.RightShoulder);
            Add(cat, s, Key.S, "Flip monitor up/down", GamepadButton.North);
            s = Section("SecurityMonitor", "Security Office: monitor up", PlayerRoles.SecurityOffice, false, "up", null);
            Add(cat, s, Key.A, "Close left door", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Close right door", GamepadButton.DpadRight);
            Add(cat, s, Key.S, "Flip monitor up/down", GamepadButton.North);
            Tabs(cat, s);

            s = Section("PartsService", "Parts & Service: laptop closed", PlayerRoles.PartsAndService, false, "down", null);
            Add(cat, s, Key.A, "Look left", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Look right", GamepadButton.DpadRight);
            Add(cat, s, Key.Space, "Open/close laptop", GamepadButton.North);
            Add(cat, s, Key.W, "Open/close door", GamepadButton.West);
            // No looking left/right and no door while the laptop is open (Voidmaster01 tested it): only the laptop key.
            s = Section("PartsServiceMonitor", "Parts & Service: laptop open", PlayerRoles.PartsAndService, false, "up", null);
            Add(cat, s, Key.Space, "Open/close laptop", GamepadButton.North);
            Tabs(cat, s);

            s = Section("Motorist", "Parts & Service: Midnight Motorist (minigame)", PlayerRoles.PartsAndService, true, "up",
                "On top of \"laptop open\" while the Games tab is open. The arrow keys always drive too.");
            Add(cat, s, Key.W, "Drive up", GamepadButton.DpadUp);
            Add(cat, s, Key.A, "Drive left", GamepadButton.DpadLeft);
            Add(cat, s, Key.S, "Drive down", GamepadButton.DpadDown);
            Add(cat, s, Key.D, "Drive right", GamepadButton.DpadRight);

            // Backstage's computer screen is always on, and it can only use the door or the shock while LOOKING straight
            // at it (Voidmaster01 tested it). So Backstage has one section per view it can look at (A/D turn between them).
            s = Section("BackstageDoor", "Backstage: at the door", PlayerRoles.Backstage, false, "door", null);
            Add(cat, s, Key.A, "Look left", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Look right", GamepadButton.DpadRight);
            Add(cat, s, Key.W, "Open/close door", GamepadButton.West);
            s = Section("BackstageShock", "Backstage: at the shock panel", PlayerRoles.Backstage, false, "shock", null);
            Add(cat, s, Key.A, "Look left", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Look right", GamepadButton.DpadRight);
            Add(cat, s, Key.S, "Shock", GamepadButton.North);
            s = Section("BackstageMaintenance", "Backstage: at maintenance", PlayerRoles.Backstage, false, "maintenance",
                "The repair menu itself is used with the mouse / cursor.");
            Add(cat, s, Key.A, "Look left", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Look right", GamepadButton.DpadRight);
            s = Section("BackstageMonitor", "Backstage: at the computer screen", PlayerRoles.Backstage, false, "up",
                "Backstage's screen is always on: this applies while you are looking at it.");
            Add(cat, s, Key.A, "Look left", GamepadButton.DpadLeft);
            Add(cat, s, Key.D, "Look right", GamepadButton.DpadRight);
            Tabs(cat, s);

            s = Section("Janitor", "Janitor's Closet: monitor down", PlayerRoles.Janitor, false, "down", null);
            Add(cat, s, Key.A, "Open/close mask", GamepadButton.West);
            Add(cat, s, Key.D, "Flip monitor up/down", GamepadButton.North);
            // No mask while the monitor is up (Voidmaster01 tested it).
            s = Section("JanitorMonitor", "Janitor's Closet: monitor up", PlayerRoles.Janitor, false, "up", null);
            Add(cat, s, Key.D, "Flip monitor up/down", GamepadButton.North);
            Tabs(cat, s);

            s = Section("FreddySpace", "Janitor: Freddy in Space (minigame)", PlayerRoles.Janitor, true, "up",
                "On top of \"monitor up\" while the Games tab is open. Your monitor and tab keys keep working.");
            Add(cat, s, Key.Space, "Fly (hold)", GamepadButton.South);
            // Purple Guy has no keys in the game: his screens open only with the mouse on the bars at the screen edges.
            // These two keys are ADDED by ModConfigs (Voidmaster01's defaults: A/D and LB/RB); see GameContext.cs.
            s = Section("PurpleGuy", "Purple Guy", PlayerRoles.PurpleGuy, false, "down",
                "The game only has the mouse for these; ModConfigs adds the keys. They don't fire while you type in a text box.");
            Add(cat, s, Key.A, "Open/close left screen", GamepadButton.LeftShoulder).OnPress = new Action(GameContext.FlipPurpleLeft);
            Add(cat, s, Key.D, "Open/close right screen", GamepadButton.RightShoulder).OnPress = new Action(GameContext.FlipPurpleRight);

            Controller.Load(cat); // the controller's general settings live in the same category (Controller.cs)
            GameContext.Changed += Rebuild; // "+=" subscribes: Rebuild runs every time the role or minigame changes
            Rebuild();
        }

        // state: when the section applies inside the role (GameContext.State): "down" / "up" for the monitor, and for
        // Backstage the view it looks at. Minigames always sit on "up".
        private static KeySection Section(string id, string title, PlayerRoles role, bool minigame, string state, string note)
        {
            var s = new KeySection { Id = id, Title = title, Role = role, Minigame = minigame, State = state, Note = note };
            sections.Add(s);
            return s;
        }

        // The laptop / monitor tab bar is mouse-only in the game; these keys are ADDED (Voidmaster01: LB/RB). They
        // call the game's own next / previous tab functions (GameContext.NextTab / PreviousTab).
        private static void Tabs(MelonPreferences_Category cat, KeySection s)
        {
            Add(cat, s, Key.Q, "Previous tab (added by ModConfigs)", GamepadButton.LeftShoulder).OnPress = new Action(GameContext.PreviousTab);
            Add(cat, s, Key.E, "Next tab (added by ModConfigs)", GamepadButton.RightShoulder).OnPress = new Action(GameContext.NextTab);
        }

        // is_hidden = true: the entries don't get their own automatic rows in MOD CONFIGS; our page shows them instead.
        // Returns the new row, so a caller can also set its OnPress.
        private static Bind Add(MelonPreferences_Category cat, KeySection s, Key key, string what, GamepadButton pad)
        {
            var b = new Bind { Section = s, GameKey = key, What = what, DefaultPad = Controller.NameOf(pad) };
            b.KeyEntry = cat.CreateEntry("Key_" + s.Id + "_" + key, key.ToString(), s.Title + ": key for " + what, null, true);
            b.PadEntry = cat.CreateEntry("Pad_" + s.Id + "_" + key, b.DefaultPad, s.Title + ": controller button for " + what, null, true);
            s.Binds.Add(b);
            return b;
        }

        // The keyboard key chosen in a row. Enum.TryParse reads a name like "UpArrow" back into Key.UpArrow; a broken
        // name falls back to the game key itself.
        internal static Key Chosen(Bind b)
        {
            Key chosen;
            if (!Enum.TryParse(b.KeyEntry.Value, out chosen) || chosen == Key.None)
                chosen = b.GameKey;
            return chosen;
        }

        // Works out what applies now: the role's section for the monitor's state (down / up), then the minigame
        // section on top (same key = minigame wins).
        private static void Rebuild()
        {
            active.Clear();
            remap.Clear();
            actions.Clear();
            PlayerRoles role = GameContext.Role;
            if (role == PlayerRoles.None)
                return;
            string state = GameContext.State;
            foreach (KeySection s in sections)
                if (s.Role == role && !s.Minigame && s.State == state)
                    foreach (Bind b in s.Binds)
                    {
                        if (b.OnPress != null) actions.Add(b); // added keys never change the game's own key answers
                        else active[b.GameKey] = b;
                    }
            if (GameContext.InMinigame)
                foreach (KeySection s in sections)
                    if (s.Role == role && s.Minigame)
                        foreach (Bind b in s.Binds)
                            active[b.GameKey] = b;
            foreach (Bind b in active.Values)
            {
                Key k = Chosen(b);
                if (k != b.GameKey)
                    remap[b.GameKey] = k;
            }
        }

        // The rows that apply right now (Controller.cs holds their keys while their buttons are held).
        internal static ICollection<Bind> ActiveBinds { get { return active.Values; } }

        // The added-key rows that apply right now (Purple Guy's screens); Tick and Controller.Tick run their OnPress.
        private static readonly List<Bind> actions = new List<Bind>();
        internal static List<Bind> ActiveActions { get { return actions; } }

        // Runs an added key's action, unless the player is typing in a text box.
        internal static void Press(Bind b)
        {
            if (!GameContext.Typing())
                b.OnPress();
        }

        private static void Save()
        {
            MelonPreferences.GetCategory(Category).SaveToFile(false); // false = no "saved" line in the log
            Rebuild();
        }

        internal static void SavePad(Bind b, string button)
        {
            b.PadEntry.Value = button;
            Save();
        }

        // RESET TO DEFAULTS on the Key Binds page: every key and button back to its default (never touches the save).
        public static void ResetAll()
        {
            capturing = null;
            foreach (KeySection s in sections)
                foreach (Bind b in s.Binds)
                {
                    b.KeyEntry.Value = b.GameKey.ToString();
                    b.PadEntry.Value = b.DefaultPad;
                }
            Controller.ResetAll(); // controller on/off and cursor speed back to their defaults too
            Save();
        }

        // ================= the remapping (called from the Harmony patches) =================

        // Answers a key question for the game. Returns false when the key wasn't moved in the current section (then
        // the game's own answer is used). which: 0 = isPressed, 1 = wasPressedThisFrame, 2 = wasReleasedThisFrame.
        internal static bool TryAnswer(ButtonControl control, int which, out bool answer)
        {
            answer = false;
            if (Bypass || remap.Count == 0)
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
            // One log line per moved key per section per game session, the first time the game uses it (for bug reports).
            Bind b;
            if (active.TryGetValue(gameKey, out b) && logged.Add(b.Section.Id + "_" + gameKey))
                MelonLogger.Msg("[ModConfigs] Key binds (" + b.Section.Title + "): the game asked for " + Pretty(gameKey) + ", answered with " + Pretty(to) + ".");
            return true;
        }

        private static readonly HashSet<string> logged = new HashSet<string>(); // section_key already logged

        // ================= "press a key..." =================

        // Called every frame by ModConfigsMod: while a row waits for a key, look at every key on the keyboard and take
        // the first one pressed this frame. Esc cancels.
        // Also runs the added keys (Purple Guy's screens) when their keyboard key goes down.
        public static void Tick()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;
            if (actions.Count > 0)
            {
                Bypass = true;
                try
                {
                    foreach (Bind b in actions)
                    {
                        KeyControl c = kb[Chosen(b)];
                        if (c != null && c.wasPressedThisFrame)
                            Press(b);
                    }
                }
                catch { }
                finally { Bypass = false; }
            }
            if (capturing == null || Time.unscaledTime - captureStarted < 0.1f)
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
                    {
                        b.KeyEntry.Value = k.ToString();
                        Save();
                    }
                    ConfigHub.Refresh();
                    return;
                }
            }
            finally
            {
                Bypass = false;
            }
        }

        // ================= the page =================

        // The whole built-in page: the controller's general settings, the key binds per section, the DANGER ZONE last
        // (far from everything else, like on GitHub).
        public static void BuildPage(ConfigPage page)
        {
            Controller.BuildSection(page);
            page.Space(20);
            page.Header("Key binds");
            page.Label("Each role has its own keys; they switch by themselves when your night starts. Click a key, then press the new "
                + "key (Esc cancels). Click a controller button, then press the button on your controller (Start cancels, View / Share "
                + "clears it). Purple = changed. RESET TO DEFAULTS puts everything back.", 18).color = UiKit.DimText;
            foreach (KeySection s in sections)
                BuildSection(page, s);
            page.Space(30);
            DangerZone.BuildSection(page);
        }

        private static void BuildSection(ConfigPage page, KeySection s)
        {
            page.Space(10);
            page.Label(s.Title, 26).color = s.Minigame ? UiKit.TextColor : UiKit.Purple;
            if (s.Note != null)
                page.Label(s.Note, 17).color = UiKit.DimText;
            foreach (Bind b in s.Binds)
                Row(page, b);
            Warnings(page, s);
        }

        // One row: the game key and what it does on the left; the keyboard key and the controller button on the right.
        private static void Row(ConfigPage page, Bind b)
        {
            RectTransform row = page.Custom(70);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform left = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-500, -10));
            string sub = b.OnPress != null ? "added by ModConfigs (the game has no key for this)" : "game key: " + Pretty(b.GameKey);
            TextMeshProUGUI text = UiKit.Text(left, b.What + "\n<size=15><color=#8c859e>" + sub + "</color></size>", 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            text.overflowMode = TextOverflowModes.Ellipsis;

            // Keyboard key button.
            RectTransform kbtn = UiKit.Element(row, "Key", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, 0), new Vector2(220, 52));
            TextMeshProUGUI klabel = UiKit.Button(kbtn, "", new Action(new StartCapture(b).Click), false);
            klabel.fontSize = 20;
            if (capturing == b)
            {
                klabel.text = "PRESS A KEY...";
                klabel.color = UiKit.Purple;
            }
            else
            {
                Key chosen = Chosen(b);
                klabel.text = Pretty(chosen);
                klabel.color = chosen == b.GameKey ? UiKit.TextColor : UiKit.Purple; // purple = changed from default
            }

            // Controller button (Controller.cs waits for the button).
            RectTransform pbtn = UiKit.Element(row, "Pad", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(230, 52));
            bool waiting = Controller.IsCapturing(b);
            TextMeshProUGUI plabel = UiKit.Button(pbtn, waiting ? "PRESS A BUTTON..." : Controller.Pretty(b.PadEntry.Value), new Action(new StartPadCapture(b).Click), false);
            plabel.fontSize = 18;
            plabel.color = waiting || b.PadEntry.Value != b.DefaultPad ? UiKit.Purple : UiKit.TextColor;
        }

        // Warns about two actions of one section on the same key or button, and about keys that also open a menu.
        // (A minigame section also counts its role's rows that it doesn't replace, because those work at the same time.)
        private static void Warnings(ConfigPage page, KeySection s)
        {
            var rows = new List<Bind>(s.Binds);
            if (s.Minigame)
                foreach (KeySection r in sections)
                    if (r.Role == s.Role && !r.Minigame && r.State == "up") // the monitor-up section it sits on
                        foreach (Bind b in r.Binds)
                            if (s.Binds.Find(new Predicate<Bind>(new SameGameKey(b.GameKey).Matches)) == null)
                                rows.Add(b);
            var keys = new Dictionary<Key, string>();
            var pads = new Dictionary<string, string>();
            foreach (Bind b in rows)
            {
                Key k = Chosen(b);
                string other;
                if (keys.TryGetValue(k, out other))
                    page.Label("Note: \"" + b.What + "\" and \"" + other + "\" are both on " + Pretty(k) + ", so that key does both.", 17).color = UiKit.Purple;
                else
                    keys[k] = b.What;
                if (b.PadEntry.Value != "None")
                {
                    if (pads.TryGetValue(b.PadEntry.Value, out other))
                        page.Label("Note: \"" + b.What + "\" and \"" + other + "\" are both on " + Controller.Pretty(b.PadEntry.Value) + ".", 17).color = UiKit.Purple;
                    else
                        pads[b.PadEntry.Value] = b.What;
                }
                if (Array.IndexOf(MenuKeys, k) >= 0 && k != b.GameKey)
                    page.Label("Note: " + Pretty(k) + " also opens one of the game's menus (Esc, Tab, N and ? can't be changed).", 17).color = UiKit.Purple;
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

        // Small named classes instead of lambdas (see ConfigHub.cs for why).
        private sealed class SameGameKey
        {
            private readonly Key key;
            public SameGameKey(Key k) { key = k; }
            public bool Matches(Bind b) { return b.GameKey == key; }
        }

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

        private sealed class StartPadCapture
        {
            private readonly Bind bind;
            public StartPadCapture(Bind b) { bind = b; }
            public void Click() { capturing = null; Controller.StartCapture(bind); }
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
