// Controller.cs - part of ModConfigs (new in 1.1.0): play FNAF Online with an Xbox, PlayStation or any other
// controller Windows supports. The game itself only knows keyboard and mouse.
//
// WHAT IT DOES
//   - Game keys on buttons: each of the game's keys (W, A, S, D, Space; see KeyBinds.cs) also listens to a controller
//     button. Defaults: LB/L1 = A, RB/R1 = D, Y/Triangle = S, B/Circle = W, X/Square = Space. All can be changed.
//   - The mouse on the controller (cameras, buttons and Purple Guy need it): the left stick moves the cursor,
//     A/Cross is a left click, the right stick scrolls.
//   - Menu buttons: Start/Options = Esc (settings), View/Share = Tab (chat).
//   The settings are on ModConfigs' Key Binds page, section CONTROLLER, saved in MelonPreferences (category KeyBinds).
//
// HOW IT WORKS
//   Unity's Input System (which the game uses) already understands Xbox controllers (XInput) and PlayStation
//   controllers (DualShock / DualSense) on Windows; Gamepad.current is whichever one was used last.
//   - Everything the controller does is turned into REAL Windows input: held keys (the keyboard key chosen in Key
//     Binds, while its button is held), cursor moves, clicks, scrolling and the Esc/Tab taps, with Windows'
//     mouse_event / keybd_event. That is the only way the game reliably notices (tested: it only looks at its keys
//     after a real keyboard event). It only happens while the game's own window is in front (GameHasFocus), so it
//     can never type into other programs, and every held key is let go when the game loses focus.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigs
{
    internal static class Controller
    {
        // ================= settings =================

        private static MelonPreferences_Entry<bool> enabled;
        private static MelonPreferences_Entry<float> cursorSpeed;
        // Game key -> its controller button setting (the GamepadButton's name, e.g. "LeftShoulder"; "None" = no button).
        private static readonly Dictionary<Key, MelonPreferences_Entry<string>> buttons = new Dictionary<Key, MelonPreferences_Entry<string>>();

        // Called by KeyBinds.Load, after the keyboard settings, so both live in the same category.
        public static void Load(MelonPreferences_Category cat)
        {
            enabled = cat.CreateEntry("Pad_Enabled", true, "Controller support", null, true);
            cursorSpeed = cat.CreateEntry("Pad_CursorSpeed", 1.0f, "Cursor speed", null, true);
            AddButton(cat, Key.A, GamepadButton.LeftShoulder);
            AddButton(cat, Key.D, GamepadButton.RightShoulder);
            AddButton(cat, Key.S, GamepadButton.North);   // Y on Xbox, Triangle on PlayStation
            AddButton(cat, Key.W, GamepadButton.East);    // B / Circle
            AddButton(cat, Key.Space, GamepadButton.West); // X / Square
        }

        private static void AddButton(MelonPreferences_Category cat, Key key, GamepadButton def)
        {
            buttons[key] = cat.CreateEntry("Pad_" + key, NameOf(def), "Controller button for " + key, null, true);
        }

        public static bool Enabled { get { return enabled != null && enabled.Value; } }

        // The button chosen for a game key; false when none (or the saved name is broken).
        private static bool ButtonFor(Key key, out GamepadButton button)
        {
            button = GamepadButton.South;
            MelonPreferences_Entry<string> e;
            return buttons.TryGetValue(key, out e) && e.Value != "None" && Enum.TryParse(e.Value, out button);
        }

        public static void ResetAll()
        {
            enabled.Value = true;
            cursorSpeed.Value = 1.0f;
            buttons[Key.A].Value = NameOf(GamepadButton.LeftShoulder);
            buttons[Key.D].Value = NameOf(GamepadButton.RightShoulder);
            buttons[Key.S].Value = NameOf(GamepadButton.North);
            buttons[Key.W].Value = NameOf(GamepadButton.East);
            buttons[Key.Space].Value = NameOf(GamepadButton.West);
        }

        // ================= game keys: real key presses =================
        // While a bound controller button is held, the chosen keyboard key (KeyBinds.ChosenKey) is held down for real
        // through Windows; when the button is let go, so is the key. Tested: the game only looks at its keys after a
        // real keyboard event, so answering from inside (like the key binds do) isn't enough for a controller.

        private static readonly HashSet<Key> held = new HashSet<Key>(); // game keys we are holding down right now

        private static void UpdateGameKeys(Gamepad pad)
        {
            foreach (Key gameKey in buttons.Keys)
            {
                GamepadButton b;
                bool now = ButtonFor(gameKey, out b) && Snapshot(pad, b).Now;
                if (now && !held.Contains(gameKey))
                {
                    if (SendKey(KeyBinds.ChosenKey(gameKey), true))
                        held.Add(gameKey);
                }
                else if (!now && held.Remove(gameKey)) // Remove returns true if it was held
                    SendKey(KeyBinds.ChosenKey(gameKey), false);
            }
        }

        // Lets go of every key we hold (when the game loses focus or controller support is switched off), so no key
        // ever stays stuck down in Windows.
        private static void ReleaseKeys()
        {
            foreach (Key k in held)
                SendKey(KeyBinds.ChosenKey(k), false);
            held.Clear();
        }

        // Presses (down = true) or lets go of one key. Returns false for keys this table doesn't know.
        // Windows wants a "virtual key code" (VK) and the hardware "scan code" (games using the new Input System read
        // that one); MapVirtualKey turns the first into the second. Arrows and the Insert/Delete/Home/End/Page block
        // are "extended" keys and need flag 1, or Windows reports them as number pad keys.
        private static bool SendKey(Key key, bool down)
        {
            bool extended;
            byte vk = VkOf(key, out extended);
            if (vk == 0)
                return false;
            byte scan = (byte)MapVirtualKey(vk, 0);
            uint flags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
            keybd_event(vk, scan, flags, UIntPtr.Zero);
            return true;
        }

        // The Input System's key name -> Windows' key code. Letters, digits, F-keys and the number pad follow a
        // pattern; the rest is a small table.
        private static byte VkOf(Key key, out bool extended)
        {
            extended = false;
            string n = key.ToString();
            if (n.Length == 1 && n[0] >= 'A' && n[0] <= 'Z') return (byte)n[0];                    // A..Z = 0x41..0x5A
            if (n.StartsWith("Digit") && n.Length == 6) return (byte)('0' + (n[5] - '0'));        // 0..9 = 0x30..0x39
            if (n.StartsWith("Numpad") && n.Length == 7 && char.IsDigit(n[6])) return (byte)(0x60 + (n[6] - '0'));
            int f;
            if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 12) return (byte)(0x6F + f); // F1 = 0x70
            byte vk;
            if (VkTable.TryGetValue(n, out vk))
            {
                extended = Extended.Contains(n);
                return vk;
            }
            return 0;
        }

        private static readonly Dictionary<string, byte> VkTable = new Dictionary<string, byte>
        {
            { "Space", 0x20 }, { "Enter", 0x0D }, { "Tab", 0x09 }, { "Backspace", 0x08 }, { "Escape", 0x1B },
            { "LeftArrow", 0x25 }, { "UpArrow", 0x26 }, { "RightArrow", 0x27 }, { "DownArrow", 0x28 },
            { "LeftShift", 0xA0 }, { "RightShift", 0xA1 }, { "LeftCtrl", 0xA2 }, { "RightCtrl", 0xA3 },
            { "LeftAlt", 0xA4 }, { "RightAlt", 0xA5 }, { "Insert", 0x2D }, { "Delete", 0x2E }, { "Home", 0x24 },
            { "End", 0x23 }, { "PageUp", 0x21 }, { "PageDown", 0x22 }, { "Semicolon", 0xBA }, { "Equals", 0xBB },
            { "Comma", 0xBC }, { "Minus", 0xBD }, { "Period", 0xBE }, { "Slash", 0xBF }, { "Backquote", 0xC0 },
            { "LeftBracket", 0xDB }, { "Backslash", 0xDC }, { "RightBracket", 0xDD }, { "Quote", 0xDE },
        };
        private static readonly HashSet<string> Extended = new HashSet<string>
        {
            "LeftArrow", "UpArrow", "RightArrow", "DownArrow", "RightCtrl", "RightAlt", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        };

        // Each button's state this frame (Now) and last frame (Before), read once per frame. "Just went down" =
        // Now and not Before. (Unity's own wasPressedThisFrame only works when the controller sends input events;
        // this works for every controller.)
        private sealed class Snap { public int Frame = -1; public bool Now, Before; }
        private static readonly Dictionary<GamepadButton, Snap> snaps = new Dictionary<GamepadButton, Snap>();

        private static Snap Snapshot(Gamepad pad, GamepadButton b)
        {
            Snap s;
            if (!snaps.TryGetValue(b, out s))
                snaps[b] = s = new Snap();
            if (s.Frame != Time.frameCount) // first look at this button in this frame
            {
                s.Before = s.Now;
                s.Now = pad[b].isPressed;
                s.Frame = Time.frameCount;
            }
            return s;
        }

        // ================= mouse and menu keys (every frame) =================

        private static bool clickDown;          // is our left click held right now?
        private static float scrollLeft;        // scroll that wasn't sent yet (wheel steps are whole notches)
        private static Key captureKey = Key.None; // the game key waiting for "press a button...", or None
        private static float captureStarted;

        // Called every frame by ModConfigsMod.
        public static void Tick()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
                return;
            KeyBinds.Bypass = true; // our own reads of the controller must not be remapped
            try
            {
                if (captureKey != Key.None)
                {
                    Capture(pad);
                    return;
                }
                // Look at every bound button once per frame, so "last frame" is always really the last frame.
                if (Enabled)
                    foreach (Key k in buttons.Keys)
                    {
                        GamepadButton b;
                        if (ButtonFor(k, out b))
                            Snapshot(pad, b);
                    }
                if (!Enabled || !GameHasFocus())
                {
                    ReleaseClick();
                    ReleaseKeys();
                    return;
                }
                UpdateGameKeys(pad);
                MoveCursor(pad);
                // Left click on A / Cross: press when the button goes down, release when it comes up.
                bool south = pad.buttonSouth.isPressed;
                if (south && !clickDown) { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero); clickDown = true; }
                else if (!south) ReleaseClick();
                // Scroll with the right stick (up/down). 120 = one wheel notch; it builds up until a whole notch.
                float y = pad.rightStick.ReadValue().y;
                if (Math.Abs(y) > 0.3f)
                {
                    scrollLeft += y * Time.unscaledDeltaTime * 8f; // about 8 notches per second at full tilt
                    int notches = (int)scrollLeft;
                    if (notches != 0) { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, notches * 120, UIntPtr.Zero); scrollLeft -= notches; }
                }
                // Menu keys: one key tap when the button goes down (Down() remembers last frame, see below).
                if (Down(GamepadButton.Start, pad.startButton.isPressed)) TapKey(VK_ESCAPE);
                if (Down(GamepadButton.Select, pad.selectButton.isPressed)) TapKey(VK_TAB);
            }
            finally
            {
                KeyBinds.Bypass = false;
            }
        }

        // "Did this button just go down?" worked out by ourselves: true when it is pressed now but was not pressed the
        // last time we looked. (Unity's own wasPressedThisFrame depends on its frame timing; this doesn't.)
        private static readonly HashSet<GamepadButton> wasDown = new HashSet<GamepadButton>();
        private static bool Down(GamepadButton b, bool pressedNow)
        {
            if (!pressedNow)
            {
                wasDown.Remove(b);
                return false;
            }
            return wasDown.Add(b); // Add returns true only if it wasn't in the set yet = it just went down
        }

        private static void ReleaseClick()
        {
            if (!clickDown)
                return;
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            clickDown = false;
        }

        // Moves the real cursor with the left stick, kept inside the game window. The speed grows with how far the
        // stick is pushed (squared, so small pushes give fine control). Full tilt crosses the window in about 1.5 s.
        // Games read "raw" mouse movement, so besides SetCursorPos an ABSOLUTE move event is sent too (see below).
        private static void MoveCursor(Gamepad pad)
        {
            Vector2 s = pad.leftStick.ReadValue();
            float len = s.magnitude;
            if (len < 0.15f) // dead zone: sticks never rest exactly at 0
                return;
            RECT r;
            POINT p;
            IntPtr win = GetForegroundWindow();
            if (!GetClientRect(win, out r) || !GetCursorPos(out p))
                return;
            POINT topLeft = new POINT();
            ClientToScreen(win, ref topLeft);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            // pixels per second at this push; times s (which already includes how far it's pushed) = squared feel
            float speed = cursorSpeed.Value * Math.Max(w, h) / 1.5f * len;
            float dx = s.x * speed * Time.unscaledDeltaTime;
            float dy = -s.y * speed * Time.unscaledDeltaTime; // stick up = screen up (screen y grows downward)
            int x = Clamp(p.X + (int)Math.Round(dx), topLeft.X, topLeft.X + w - 1);
            int y = Clamp(p.Y + (int)Math.Round(dy), topLeft.Y, topLeft.Y + h - 1);
            SetCursorPos(x, y);
            // ABSOLUTE positions count 0..65535 across the whole desktop (all monitors, VIRTUALDESK), so convert.
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                (int)((x - vx) * 65535L / Math.Max(1, vw - 1)), (int)((y - vy) * 65535L / Math.Max(1, vh - 1)), 0, UIntPtr.Zero);
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private static void TapKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // True only when the window in front belongs to this game (our own process).
        private static bool GameHasFocus()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        // ================= "press a button..." =================

        private static void StartCapture(Key key)
        {
            captureKey = captureKey == key ? Key.None : key; // clicking again cancels
            captureStarted = Time.unscaledTime;
            // Buttons already held when capture starts (like A / Cross used to click this button) only count once
            // they are let go and pressed again.
            wasDown.Clear();
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                KeyBinds.Bypass = true;
                try
                {
                    foreach (GamepadButton b in Enum.GetValues(typeof(GamepadButton)))
                    {
                        try { if (pad[b].isPressed) wasDown.Add(b); } catch { }
                    }
                }
                finally { KeyBinds.Bypass = false; }
            }
            ConfigHub.Refresh();
        }

        // Waits for a controller button. Start cancels; Select (View/Share) clears the button ("None").
        private static void Capture(Gamepad pad)
        {
            if (Time.unscaledTime - captureStarted < 0.15f)
                return;
            foreach (GamepadButton b in Enum.GetValues(typeof(GamepadButton)))
            {
                ButtonControl c;
                try { c = pad[b]; } catch { continue; }
                if (c == null || !Down(b, c.isPressed))
                    continue;
                if (b == GamepadButton.Select)
                    buttons[captureKey].Value = "None";
                else if (b != GamepadButton.Start)
                    buttons[captureKey].Value = NameOf(b);
                MelonPreferences.GetCategory(KeyBinds.Category).SaveToFile(false);
                captureKey = Key.None;
                ConfigHub.Refresh();
                return;
            }
        }

        // GamepadButton gives the same button several names with the SAME number (A = South = Cross), so ToString() could
        // print any of them. NameOf always saves the position name (South, East, West, North) for those four.
        private static string NameOf(GamepadButton b)
        {
            if (b == GamepadButton.South) return "South";
            if (b == GamepadButton.East) return "East";
            if (b == GamepadButton.West) return "West";
            if (b == GamepadButton.North) return "North";
            return b.ToString();
        }

        // Names for people, with the Xbox and the PlayStation name.
        private static string Pretty(string saved)
        {
            GamepadButton b;
            if (saved == "None" || !Enum.TryParse(saved, out b))
                return "-";
            switch (b)
            {
                case GamepadButton.South: return "A / Cross";
                case GamepadButton.East: return "B / Circle";
                case GamepadButton.West: return "X / Square";
                case GamepadButton.North: return "Y / Triangle";
                case GamepadButton.LeftShoulder: return "LB / L1";
                case GamepadButton.RightShoulder: return "RB / R1";
                case GamepadButton.LeftTrigger: return "LT / L2";
                case GamepadButton.RightTrigger: return "RT / R2";
                case GamepadButton.LeftStick: return "L3 (left stick)";
                case GamepadButton.RightStick: return "R3 (right stick)";
                case GamepadButton.DpadUp: return "D-pad Up";
                case GamepadButton.DpadDown: return "D-pad Down";
                case GamepadButton.DpadLeft: return "D-pad Left";
                case GamepadButton.DpadRight: return "D-pad Right";
                default: return b.ToString();
            }
        }

        // ================= the page section =================

        // keys: the game keys in page order, with what they do (from KeyBinds).
        public static void BuildSection(ConfigPage page, IList<KeyValuePair<Key, string>> keys)
        {
            page.Header("Controller");
            Gamepad pad = Gamepad.current;
            page.Label(pad != null ? "Connected: " + pad.displayName : "No controller found. Plug one in (Xbox, PlayStation or any Windows controller) and reopen this page.", 18)
                .color = pad != null ? UiKit.Purple : UiKit.DimText;
            page.Toggle("Controller support", new Func<bool>(GetEnabled), new Action<bool>(SetEnabled));
            page.Slider("Cursor speed (left stick)", new Func<double>(GetSpeed), new Action<double>(SetSpeed), 0.3, 3.0, "0.0");
            page.Label("Fixed: left stick = cursor, A / Cross = click, right stick = scroll, Start / Options = Esc, View / Share = Tab. "
                + "Click a button below, then press the controller button for it (Start cancels, View / Share clears it).", 18).color = UiKit.DimText;
            foreach (KeyValuePair<Key, string> k in keys)
                Row(page, k.Key, k.Value);
        }

        private static void Row(ConfigPage page, Key key, string what)
        {
            RectTransform row = page.Custom(70);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform left = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-300, -10));
            TextMeshProUGUI t = UiKit.Text(left, KeyBinds.Pretty(key) + " key\n<size=15><color=#8c859e>" + what + "</color></size>", 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            t.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform btn = UiKit.Element(row, "Button", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(260, 52));
            var click = new CaptureClick(key);
            TextMeshProUGUI label = UiKit.Button(btn, captureKey == key ? "PRESS A BUTTON..." : Pretty(buttons[key].Value), new Action(click.Click), false);
            label.fontSize = 20;
            if (captureKey == key) label.color = UiKit.Purple;
        }

        private static bool GetEnabled() { return enabled.Value; }
        private static void SetEnabled(bool v) { enabled.Value = v; Save(); }
        private static double GetSpeed() { return cursorSpeed.Value; }
        private static void SetSpeed(double v) { cursorSpeed.Value = (float)v; Save(); }
        private static void Save() { MelonPreferences.GetCategory(KeyBinds.Category).SaveToFile(false); }

        private sealed class CaptureClick
        {
            private readonly Key key;
            public CaptureClick(Key k) { key = k; }
            public void Click() { StartCapture(key); }
        }

        // ================= Windows functions (user32.dll) =================
        // [DllImport] tells .NET "this function is inside user32.dll, a part of Windows". The numbers are the flags
        // Windows defines for each function.
        private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
            MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_TAB = 0x09, VK_ESCAPE = 0x1B;
        private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}
