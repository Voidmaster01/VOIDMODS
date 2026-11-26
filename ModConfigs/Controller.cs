// Controller.cs - part of ModConfigs (new in 1.1.0): play FNAF Online with an Xbox, PlayStation or any other
// controller Windows supports. The game itself only knows keyboard and mouse.
//
// WHAT IT DOES
//   - Game keys on buttons: every key bind row (per role and per minigame, see KeyBinds.cs) also has a controller
//     button. Defaults (chosen by Voidmaster01): Security LB/RB doors, Y monitor; Parts & Service and Backstage
//     D-pad left/right to look, Y laptop/shock, X door; Janitor X mask, Y monitor; Midnight Motorist drives with the
//     D-pad; Freddy in Space flies with A; Purple Guy LB/RB screens. All can be changed, separately for every role and
//     minigame. Only the rows of the role
//     (and minigame) being played count, so outside a night the buttons press no game keys at all.
//   - The mouse on the controller (cameras, buttons and Purple Guy need it): the left stick moves the cursor,
//     A/Cross is a left click, the right stick scrolls the list under the cursor. MENU CONTROL STYLE (Voidmaster01's idea): "Mouse (slippy)"
//     moves the cursor freely; "Controller (sticky)" makes each push of the stick jump the cursor onto the next
//     button on screen in that direction (see StickyMove).
//   - Menu buttons: Start/Options = go back / Settings (like Esc, see EscapeKey.cs); B/Circle does the same unless a
//     key bind row of the current role uses B; View/Share = lobby chat (like Tab).
//   The settings are on ModConfigs' Key Binds page (CONTROLLER at the top, the buttons in each key bind row), saved in
//   MelonPreferences (category KeyBinds).
//
// HOW IT WORKS
//   Unity's Input System (which the game uses) already understands Xbox controllers (XInput) and PlayStation
//   controllers (DualShock / DualSense) on Windows; Gamepad.current is whichever one was used last.
//   - Everything the controller does goes into the game as Unity INPUT EVENTS, the same kind the real keyboard and
//     mouse produce: held keys (the key chosen in the row, while its button is held), clicks and scrolling are queued
//     with InputSystem.QueueEvent, and the cursor is moved with Mouse.WarpCursorPosition. The game's menus also read
//     Unity's Input System (tested: InputSystemUIInputModule), so they react exactly as to a mouse. These events only
//     ever reach this game, never another program, and nothing happens while the game window isn't in front.
//     (Version 1.1.0's first builds used Windows' keybd_event / mouse_event instead. Antivirus programs flag DLLs that
//     use those, as keyloggers do, so ModConfigs no longer touches Windows' input functions at all.)
//   - Start and View can't be sent as Esc / Tab that way (the game reads those two keys through Unity's OLD input
//     system), so they call the game's own functions: EscapeKey.StartButton and the chat's SetTextChatVisibility.
using System;
using System.Collections.Generic;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class Controller
    {
        // ================= settings =================

        private static MelonPreferences_Entry<bool> enabled;
        private static MelonPreferences_Entry<float> cursorSpeed;
        // Menu control style: "Mouse" = the left stick moves the cursor freely (slippy), "Sticky" = the left stick
        // jumps the cursor from button to button on screen (Voidmaster01's idea). See StickyMove below.
        private static MelonPreferences_Entry<string> menuStyle;
        private static readonly string[] StyleNames = { "Mouse", "Sticky" };
        private static readonly string[] StyleLabels = { "Mouse (slippy): cursor moves freely", "Controller (sticky): cursor jumps between buttons" };
        // (Each key bind row's controller button is saved with the row itself: Bind.PadEntry in KeyBinds.cs.)

        // Called by KeyBinds.Load, after the keyboard settings, so both live in the same category.
        public static void Load(MelonPreferences_Category cat)
        {
            enabled = cat.CreateEntry("Pad_Enabled", true, "Controller support", null, true);
            cursorSpeed = cat.CreateEntry("Pad_CursorSpeed", 1.0f, "Cursor speed", null, true);
            menuStyle = cat.CreateEntry("Pad_MenuStyle", "Mouse", "Menu control style (Mouse or Sticky)", null, true);
        }

        private static bool Sticky { get { return menuStyle != null && menuStyle.Value == "Sticky"; } }

        public static bool Enabled { get { return enabled != null && enabled.Value; } }

        // The button saved in a row; false when none (or the saved name is broken).
        private static bool ButtonFor(Bind b, out GamepadButton button)
        {
            button = GamepadButton.South;
            return b.PadEntry.Value != "None" && Enum.TryParse(b.PadEntry.Value, out button);
        }

        // Called by KeyBinds.ResetAll (which resets the rows' buttons itself).
        public static void ResetAll()
        {
            enabled.Value = true;
            cursorSpeed.Value = 1.0f;
            menuStyle.Value = "Mouse";
        }

        // ================= game keys: key events =================
        // While a row's controller button is held, the keyboard key chosen in that row is held down through Unity key
        // EVENTS (SendKey); when the button is let go, so is the key. Tested: the game only looks at its keys after a
        // keyboard event, so answering from inside (like the key binds do) isn't enough for a controller.
        // "held" remembers WHICH key was actually pressed for each game key, because the role, the minigame or the
        // chosen key may change while the button is still down; the release must let go of the same key.

        private static readonly Dictionary<Key, Key> held = new Dictionary<Key, Key>(); // game key -> key we hold down
        private static readonly Dictionary<Key, Key> wanted = new Dictionary<Key, Key>(); // reused every frame

        private static void UpdateGameKeys(Gamepad pad)
        {
            // 1. Which game keys should be down now, and as which keyboard key (only the rows that apply right now).
            wanted.Clear();
            // 0. A real key press refreshes Unity's whole keyboard state, which can drop a key we hold: press it again.
            Keyboard kb = Keyboard.current;
            if (kb != null)
                foreach (Key k in held.Values)
                    if (!kb[k].isPressed)
                        SendKey(k, true);
            foreach (Bind b in KeyBinds.ActiveBinds)
            {
                GamepadButton btn;
                if (ButtonFor(b, out btn) && Snapshot(pad, btn).Now)
                    wanted[b.GameKey] = KeyBinds.Chosen(b);
            }
            // 2. Let go of keys that are no longer wanted (or that changed). The loop walks a copy (new List(held)),
            //    because a Dictionary can't be changed while a foreach walks through it.
            foreach (KeyValuePair<Key, Key> h in new List<KeyValuePair<Key, Key>>(held))
            {
                Key want;
                if (!wanted.TryGetValue(h.Key, out want) || want != h.Value)
                {
                    SendKey(h.Value, false);
                    held.Remove(h.Key);
                }
            }
            // 3. Press the new ones.
            foreach (KeyValuePair<Key, Key> w in wanted)
                if (!held.ContainsKey(w.Key) && SendKey(w.Value, true))
                    held[w.Key] = w.Value;
        }

        // Lets go of every key we hold (when the game loses focus or controller support is switched off), so no key
        // ever stays stuck down in Windows.
        private static void ReleaseKeys()
        {
            foreach (Key k in held.Values)
                SendKey(k, false);
            held.Clear();
        }

        // Presses (down = true) or lets go of one key, as an input EVENT for Unity's keyboard (see QueueValue).
        // Returns false for a key this keyboard doesn't have.
        private static bool SendKey(Key key, bool down)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return false;
            KeyControl k;
            try { k = kb[key]; } catch { return false; }
            if (k == null)
                return false;
            QueueValue(kb, k, down ? 1f : 0f);
            return true;
        }

        // Sends one value into the game as an input event for a device: a key or button (1 = down, 0 = up), or a
        // scroll amount. StateEvent.From copies the device's CURRENT state into a new event (so every other key or
        // button stays as it really is), WriteValueIntoEvent changes just our control in that copy, and QueueEvent
        // hands the event to Unity, which applies it in its next input update. The event lives in a block of memory
        // Unity lends us (a NativeArray); Dispose gives it back. "finally" makes sure that happens even after an error.
        private static void QueueValue(InputDevice device, AxisControl control, float value)
        {
            InputEventPtr ev;
            var buffer = StateEvent.From(device, out ev);
            try
            {
                control.WriteValueIntoEvent(value, ev);
                InputSystem.QueueEvent(ev);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        // The mouse's left button, the same way.
        private static void MouseButton(bool down)
        {
            Mouse m = Mouse.current;
            if (m != null)
                QueueValue(m, m.leftButton, down ? 1f : 0f);
        }

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

        private static Bind captureBind;        // the key bind row waiting for "press a button...", or null
        private static float captureStarted;

        // Called every frame by ModConfigsMod.
        public static void Tick()
        {
            if (releaseClickNextFrame) // the second half of ClickAtCursor's click
            {
                releaseClickNextFrame = false;
                MouseButton(false);
            }
            Gamepad pad = Gamepad.current;
            if (pad == null)
                return;
            KeyBinds.Bypass = true; // our own reads of the controller must not be remapped
            try
            {
                if (captureBind != null)
                {
                    Capture(pad);
                    return;
                }
                // Look at every button that counts right now once per frame, so "last frame" is really the last frame.
                if (Enabled)
                {
                    foreach (Bind row in KeyBinds.ActiveBinds)
                    {
                        GamepadButton b;
                        if (ButtonFor(row, out b))
                            Snapshot(pad, b);
                    }
                    foreach (Bind row in KeyBinds.ActiveActions)
                    {
                        GamepadButton b;
                        if (ButtonFor(row, out b))
                            Snapshot(pad, b);
                    }
                }
                if (!Enabled || !GameHasFocus())
                {
                    ReleaseClick();
                    ReleaseKeys();
                    return;
                }
                UpdateGameKeys(pad);
                // Added keys (Purple Guy's screens): run once when the button goes down (Now, and not Before).
                foreach (Bind row in KeyBinds.ActiveActions)
                {
                    GamepadButton b;
                    if (ButtonFor(row, out b))
                    {
                        Snap s = Snapshot(pad, b);
                        if (s.Now && !s.Before)
                            KeyBinds.Press(row);
                    }
                }
                if (Sticky) StickyMove(pad);
                else MoveCursor(pad);
                // Left click on A / Cross: press when the button goes down, release when it comes up.
                bool south = pad.buttonSouth.isPressed;
                if (south && !clickDown) { MouseButton(true); clickDown = true; }
                else if (!south) ReleaseClick();
                // Scroll the list under the cursor with the right stick (up/down); see ScrollUnderCursor.
                float y = pad.rightStick.ReadValue().y;
                if (Math.Abs(y) > 0.2f)
                    ScrollUnderCursor(y);
                // Menu buttons: once when the button goes down (Down() remembers last frame, see below).
                if (Down(GamepadButton.Start, pad.startButton.isPressed)) EscapeKey.StartButton();
                // B / Circle = back, like Esc (Voidmaster01), unless a key bind row of the current role / minigame uses B.
                if (Down(GamepadButton.East, pad.buttonEast.isPressed) && !InUse("East")) EscapeKey.StartButton();
                if (Down(GamepadButton.Select, pad.selectButton.isPressed)) ToggleChat();
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
            MouseButton(false);
            clickDown = false;
        }

        // Is this controller button (saved name, e.g. "East") used by a key bind row that applies right now?
        private static bool InUse(string button)
        {
            foreach (Bind b in KeyBinds.ActiveBinds)
                if (b.PadEntry.Value == button) return true;
            foreach (Bind b in KeyBinds.ActiveActions)
                if (b.PadEntry.Value == button) return true;
            return false;
        }

        // View / Share: shows or hides the lobby's text chat (what Tab does), with the chat's own function.
        // FindObjectOfType finds nothing outside the lobby, and then nothing happens.
        private static void ToggleChat()
        {
            Il2Cpp.TextChatUI chat = UnityEngine.Object.FindObjectOfType<Il2Cpp.TextChatUI>();
            if (chat != null && chat.textChatCanvas != null)
                chat.SetTextChatVisibility(!chat.textChatCanvas.enabled);
        }

        // Moves the cursor with the left stick, kept inside the game window. The speed grows with how far the stick
        // is pushed (squared, so small pushes give fine control). Full tilt crosses the window in about 1.5 s.
        // Positions are Unity's (bottom-left = 0,0, the game's own pixels). "aim" keeps the fractions of a pixel a
        // slow push moves per frame, so slow movement doesn't get lost; when the real mouse moved, aim starts over.
        private static Vector2 aim;

        private static void MoveCursor(Gamepad pad)
        {
            Vector2 s = pad.leftStick.ReadValue();
            float len = s.magnitude;
            if (len < 0.15f) // dead zone: sticks never rest exactly at 0
                return;
            Mouse m = Mouse.current;
            if (m == null)
                return;
            Vector2 now = m.position.ReadValue();
            if ((now - aim).magnitude > 3f)
                aim = now;
            // pixels per second at this push; times s (which already includes how far it's pushed) = squared feel
            float speed = cursorSpeed.Value * Math.Max(Screen.width, Screen.height) / 1.5f * len;
            aim += s * speed * Time.unscaledDeltaTime; // stick up = up (Unity's y grows upward too)
            aim.x = Mathf.Clamp(aim.x, 0, Screen.width - 1);
            aim.y = Mathf.Clamp(aim.y, 0, Screen.height - 1);
            PlaceCursor(aim);
        }

        // Puts the cursor on a Unity screen point. WarpCursorPosition moves the real cursor and tells the game.
        private static void PlaceCursor(Vector2 point)
        {
            Mouse m = Mouse.current;
            if (m != null)
                m.WarpCursorPosition(new Vector2(Mathf.Round(point.x), Mathf.Round(point.y)));
        }
        // ================= sticky menu control (Pad_MenuStyle = "Sticky") =================
        // Pushing the left stick jumps the cursor onto the nearest button in that direction, so A always lands on
        // something clickable. Holding the stick repeats: the first repeat after 0.35 s, then every 0.15 s.
        //
        // HOW IT FINDS "THE NEXT BUTTON"
        //   Unity keeps a list of every clickable UI element (Selectable.allSelectablesArray: buttons, toggles,
        //   sliders, dropdowns, text boxes). For each one that is switched on and clickable we work out where its
        //   middle is on screen (RectTransformUtility.WorldToScreenPoint, which also handles the game's 3D monitor
        //   screens, whose UI sits in the 3D world). We keep the ones inside a 60 degree cone around the stick's
        //   direction and score them by distance, with a penalty for being off to the side. Then, best score first,
        //   we ask Unity's EventSystem what is really under that point (RaycastAll, the same test a mouse click
        //   uses): only an element that would really get the click counts, so buttons hidden behind a panel are
        //   skipped.

        private static bool stickOut;   // was the stick pushed last frame?
        private static float nextJump;  // when holding the stick may jump again

        private static void StickyMove(Gamepad pad)
        {
            Vector2 s = pad.leftStick.ReadValue();
            if (s.magnitude < 0.5f)
            {
                stickOut = false;
                return;
            }
            float now = Time.unscaledTime;
            if (stickOut && now < nextJump)
                return;
            nextJump = now + (stickOut ? 0.15f : 0.35f);
            stickOut = true;
            try { Jump(s.normalized); }
            catch (Exception e) { MelonLogger.Warning("[ModConfigs] Sticky cursor: " + e.Message); }
        }

        private sealed class Candidate { public Selectable Item; public Vector2 Point; public float Score; }

        private static int ByScore(Candidate a, Candidate b) { return a.Score.CompareTo(b.Score); }

        private static void Jump(Vector2 dir)
        {
            Vector2 cursor;
            if (!CursorScreenPoint(out cursor))
                return;

            var list = new List<Candidate>();
            foreach (Selectable sel in Selectable.allSelectablesArray)
            {
                if (sel == null || !sel.isActiveAndEnabled || !sel.IsInteractable())
                    continue;
                Vector2 c;
                if (!ScreenCenter(sel, out c))
                    continue;
                // Off the screen only counts for a button in a scrolling list (it can be scrolled into view).
                bool onScreen = c.x >= 0 && c.y >= 0 && c.x <= Screen.width && c.y <= Screen.height;
                if (!onScreen && sel.GetComponentInParent<ScrollRect>() == null)
                    continue;
                Vector2 d = c - cursor;
                float dist = d.magnitude;
                if (dist < 8f)
                    continue; // the one the cursor is already on
                float cos = Vector2.Dot(d / dist, dir); // 1 = straight ahead, 0.5 = 60 degrees off
                if (cos < 0.5f)
                    continue;
                list.Add(new Candidate { Item = sel, Point = c, Score = dist * (1f + 2f * (1f - cos)) });
            }
            list.Sort(new Comparison<Candidate>(ByScore));
            for (int i = 0; i < list.Count && i < 12; i++)
            {
                Vector2 point = list[i].Point;
                if (!Clickable(list[i].Item, point))
                {
                    // Hidden because its list is scrolled away from it? Then scroll the list to show it (Voidmaster01:
                    // sticky mode scrolls down by itself as the next button is chosen) and look again.
                    if (!ScrollIntoView(list[i].Item) || !ScreenCenter(list[i].Item, out point) || !Clickable(list[i].Item, point))
                        continue;
                }
                PlaceCursor(point);
                return;
            }
        }

        // ================= scrolling lists (ScrollRect) =================
        // Unity's scrolling lists (MOD CONFIGS' pages, the MODS panel, the game's own lists) are ScrollRects. Instead
        // of faking the mouse wheel (which made pages jump from the top straight to the bottom), the right stick moves
        // the list itself: at full tilt about one list-height per second, whatever the cursor speed is.

        private static void ScrollUnderCursor(float stickY)
        {
            Vector2 cursor;
            if (!CursorScreenPoint(out cursor))
                return;
            GameObject top = TopHit(cursor);
            Scrollbar used;
            if (top == null)
            {
                // Nothing that catches clicks under the cursor: look for a visible scrollbar anywhere instead.
                used = ScrollBarFallback(null, stickY);
            }
            else
            {
                ScrollRect sr = top.GetComponentInParent<ScrollRect>();
                // On a scrollbar that sits BESIDE its list (ACHIEVEMENTS; also where sticky style parks the cursor):
                // scroll the list next to it, not the bar. The game keeps such a bar in line with its list every
                // frame, so moving only the bar would be undone at once.
                if (sr == null)
                {
                    Scrollbar onBar = top.GetComponentInParent<Scrollbar>();
                    if (onBar != null && onBar.transform.parent != null)
                        foreach (ScrollRect near in onBar.transform.parent.GetComponentsInChildren<ScrollRect>(false))
                            if (near != null && near.isActiveAndEnabled && near.vertical) { sr = near; break; }
                }
                if (sr == null || sr.content == null || !sr.vertical)
                    used = ScrollBarFallback(top, stickY);
                else
                {
                    used = sr.verticalScrollbar;
                    // Some lists (ACHIEVEMENTS) have their scrollbar beside them without linking it to the list;
                    // then use the visible up/down scrollbar next to the list (under the same parent).
                    if (used == null && sr.transform.parent != null)
                        used = FirstVerticalBar(sr.transform.parent.GetComponentsInChildren<Scrollbar>(false));
                    RectTransform view = sr.viewport != null ? sr.viewport : sr.transform.TryCast<RectTransform>();
                    float room = sr.content.rect.height - view.rect.height; // how far the list can move at all
                    if (room > 0)
                    {
                        // verticalNormalizedPosition: 1 = top, 0 = bottom. Stick up (positive) = towards the top.
                        float step = stickY * view.rect.height * Time.unscaledDeltaTime / room;
                        sr.velocity = Vector2.zero; // stop any leftover "flick" movement
                        // Keep our own exact position: one frame's step can be under one unit, and some lists
                        // (ACHIEVEMENTS) round their position back every frame, which ate every tiny step (tested).
                        // Start over from the list's real position when it is another list or was moved otherwise.
                        float now = sr.verticalNormalizedPosition;
                        if (sr != scrollList || Math.Abs(now - scrollPos) > 0.02f)
                            scrollPos = now;
                        scrollList = sr;
                        scrollPos = Mathf.Clamp01(scrollPos + step);
                        sr.verticalNormalizedPosition = scrollPos;
                    }
                }
            }
            // Sticky style (Voidmaster01): the cursor rides on the scrollbar's handle while the right stick scrolls,
            // so it is plain to see what is scrolling, and the next push keeps scrolling the same list.
            if (Sticky && used != null && used.handleRect != null)
            {
                Vector2 handle;
                if (ScreenPoint(used.handleRect.transform, out handle))
                    PlaceCursor(handle);
            }
        }

        // Some of the game's screens (the ACHIEVEMENTS list) scroll with a plain Scrollbar that moves the list itself,
        // not with a ScrollRect. Then the stick moves that scrollbar: the one under the cursor, or else the first
        // switched-on vertical scrollbar on the same screen (canvas). Scrollbar.size = how much of the list is visible
        // (0..1), so moving "size" per second at full tilt is one screenful per second, like the lists above.
        // top = what is under the cursor, or null when nothing there catches clicks (then every visible scrollbar on
        // screen is a candidate; a scrollbar on a switched-off screen doesn't count, isActiveAndEnabled checks that).
        // Returns the scrollbar it moved (or null).
        private static Scrollbar ScrollBarFallback(GameObject top, float stickY)
        {
            Scrollbar bar = top != null ? top.GetComponentInParent<Scrollbar>() : null;
            if (bar == null && top != null)
            {
                Canvas canvas = top.GetComponentInParent<Canvas>();
                if (canvas != null)
                    bar = FirstVerticalBar(canvas.rootCanvas.GetComponentsInChildren<Scrollbar>(false));
            }
            if (bar == null)
                bar = FirstVerticalBar(UnityEngine.Object.FindObjectsOfType<Scrollbar>());
            if (bar == null)
                return null;
            float step = stickY * Mathf.Max(bar.size, 0.05f) * Time.unscaledDeltaTime;
            // BottomToTop: value 1 = the top of the list; TopToBottom: value 0 = the top. Stick up = towards the top.
            if (bar.direction == Scrollbar.Direction.TopToBottom)
                step = -step;
            bar.value = Mathf.Clamp01(bar.value + step); // setting value moves the list, like dragging the bar
            return bar;
        }

        // The first switched-on, up/down scrollbar in a list (on a screen whose canvas is showing), or null.
        private static Scrollbar FirstVerticalBar(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Scrollbar> bars)
        {
            foreach (Scrollbar b in bars)
            {
                if (b == null || !b.isActiveAndEnabled)
                    continue;
                if (b.direction != Scrollbar.Direction.BottomToTop && b.direction != Scrollbar.Direction.TopToBottom)
                    continue;
                Canvas c = b.GetComponentInParent<Canvas>();
                if (c != null && c.rootCanvas.enabled)
                    return b;
            }
            return null;
        }

        // If the element sits in a scrolling list that is visible but scrolled away from it, scrolls the list just
        // enough to show it (with a small margin). True when it scrolled.
        private static bool ScrollIntoView(Selectable sel)
        {
            ScrollRect sr = sel.GetComponentInParent<ScrollRect>();
            if (sr == null || sr.content == null)
                return false;
            RectTransform view = sr.viewport != null ? sr.viewport : sr.transform.TryCast<RectTransform>();
            RectTransform item = sel.transform.TryCast<RectTransform>();
            if (view == null || item == null)
                return false;
            // The list itself must be the thing on top where it is (not hidden behind another panel).
            Vector2 viewMid;
            if (!ScreenPoint(view.transform, out viewMid))
                return false;
            GameObject hit = TopHit(viewMid);
            if (hit == null || !hit.transform.IsChildOf(sr.transform))
                return false;
            // Compare heights in world space (works for flat and 3D canvases): corners 0 = bottom-left, 1 = top-left.
            view.GetWorldCorners(corners);
            float viewBottom = corners[0].y, viewTop = corners[1].y;
            item.GetWorldCorners(corners);
            float itemBottom = corners[0].y, itemTop = corners[1].y;
            float margin = (itemTop - itemBottom) * 0.25f;
            float move = 0f;
            if (itemBottom < viewBottom) move = viewBottom - itemBottom + margin;  // below: move the content up
            else if (itemTop > viewTop) move = viewTop - itemTop - margin;        // above: move the content down
            else return false;                                                    // already in view
            sr.velocity = Vector2.zero;
            sr.content.position += new Vector3(0f, move, 0f);
            sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition); // stay inside the list
            Canvas.ForceUpdateCanvases(); // let Unity place everything at the new scroll position right now
            return true;
        }

        private static ScrollRect scrollList; // the list the right stick scrolled last
        private static float scrollPos;       // our exact position in it (1 = top, 0 = bottom)

        // The topmost UI element under a screen point (what a click there would hit), or null.
        private static GameObject TopHit(Vector2 point)
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return null;
            var data = new PointerEventData(es);
            data.position = point;
            var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(data, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        // The middle of an element in Unity screen pixels. Overlay canvases are already in screen pixels (camera
        // null); canvases drawn by a camera (the 3D monitors) need that camera to turn 3D into screen positions.
        private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> corners =
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);

        private static bool ScreenCenter(Selectable sel, out Vector2 point)
        {
            return ScreenPoint(sel.transform, out point);
        }

        // The middle of any UI element (anything with a RectTransform) in Unity screen pixels. Also used by
        // GameContext to sort the camera / tracker / lure buttons in reading order.
        internal static bool ScreenPoint(Transform t, out Vector2 point)
        {
            point = Vector2.zero;
            RectTransform rt = t != null ? t.TryCast<RectTransform>() : null;
            if (rt == null)
                return false;
            rt.GetWorldCorners(corners);
            Vector3 mid = (corners[0] + corners[2]) * 0.5f; // halfway between bottom-left and top-right
            Camera cam = null;
            Canvas canvas = t.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas = canvas.rootCanvas;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = canvas.worldCamera;
                    if (cam == null) cam = Camera.main;
                }
            }
            point = RectTransformUtility.WorldToScreenPoint(cam, mid);
            return true;
        }

        // Puts the cursor on a Unity screen point (bottom-left origin, the game's own pixels). Used by the "next item
        // on screen" keys (GameContext.StepItem). Only while the game is in front.
        internal static void CursorToScreenPoint(Vector2 point)
        {
            if (GameHasFocus())
                PlaceCursor(point);
        }

        // One left click where the cursor is (the Janitor's "use the selected lure / tracker" key): button down now,
        // up on the next frame (Tick), like a real click. The game does exactly what a mouse click does there, so its
        // own limits apply (a lure's 2 uses, a tracker switching on/off).
        private static bool releaseClickNextFrame;

        internal static void ClickAtCursor()
        {
            if (!GameHasFocus() || clickDown || releaseClickNextFrame)
                return;
            MouseButton(true);
            releaseClickNextFrame = true;
        }

        // Where the cursor is now, in Unity screen points. False when it can't tell (no mouse, or not in front).
        internal static bool CursorScreenPoint(out Vector2 point)
        {
            point = Vector2.zero;
            Mouse m = Mouse.current;
            if (m == null || !GameHasFocus())
                return false;
            point = m.position.ReadValue();
            return true;
        }
        // Would a click at this point really reach this element? (Nothing else on top of it.)
        private static bool Clickable(Selectable sel, Vector2 point)
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return true; // can't tell: allow it
            var data = new PointerEventData(es);
            data.position = point;
            var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(data, hits);
            if (hits.Count == 0)
                return false;
            GameObject top = hits[0].gameObject;
            return top != null && (top == sel.gameObject || top.transform.IsChildOf(sel.transform));
        }

        // True only while the game's window is the one in front (Unity knows this itself).
        private static bool GameHasFocus()
        {
            return Application.isFocused;
        }
        // ================= "press a button..." =================

        internal static bool IsCapturing(Bind b) { return captureBind == b; }
        internal static bool Capturing { get { return captureBind != null; } }

        // Called by a key bind row's controller button (KeyBinds.cs).
        internal static void StartCapture(Bind row)
        {
            captureBind = captureBind == row ? null : row; // clicking again cancels
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
                Bind row = captureBind;
                captureBind = null;
                if (b == GamepadButton.Select)
                    KeyBinds.SavePad(row, "None");
                else if (b != GamepadButton.Start)
                    KeyBinds.SavePad(row, NameOf(b));
                ConfigHub.Refresh();
                return;
            }
        }

        // GamepadButton gives the same button several names with the SAME number (A = South = Cross), so ToString() could
        // print any of them. NameOf always saves the position name (South, East, West, North) for those four.
        internal static string NameOf(GamepadButton b)
        {
            if (b == GamepadButton.South) return "South";
            if (b == GamepadButton.East) return "East";
            if (b == GamepadButton.West) return "West";
            if (b == GamepadButton.North) return "North";
            return b.ToString();
        }

        // Names for people, with the Xbox and the PlayStation name.
        internal static string Pretty(string saved)
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

        // The general controller settings at the top of the Key Binds page. Each key bind row below has its own
        // controller button (KeyBinds.Row).
        public static void BuildSection(ConfigPage page)
        {
            page.Header("Controller");
            Gamepad pad = Gamepad.current;
            page.Label(pad != null ? "Connected: " + pad.displayName : "No controller found. Plug one in (Xbox, PlayStation or any Windows controller) and reopen this page.", 18)
                .color = pad != null ? UiKit.Purple : UiKit.DimText;
            page.Toggle("Controller support", new Func<bool>(GetEnabled), new Action<bool>(SetEnabled));
            page.Choice("Menu control style", StyleLabels, new Func<int>(GetStyle), new Action<int>(SetStyle));
            page.Slider("Cursor speed (left stick, Mouse style)", new Func<double>(GetSpeed), new Action<double>(SetSpeed), 0.3, 3.0, "0.0");
            page.Label("Always: left stick = cursor (free, or jumping between buttons in Sticky style), A / Cross = click, right stick = scroll, "
                + "Start / Options and B / Circle = back like Esc (B only when no row below uses it), View / Share = Tab (lobby chat). The game's keys are on buttons per role, in the rows below.", 18).color = UiKit.DimText;
        }

        private static int GetStyle() { return Sticky ? 1 : 0; }
        private static void SetStyle(int i) { menuStyle.Value = StyleNames[Math.Max(0, Math.Min(1, i))]; Save(); }

        private static bool GetEnabled() { return enabled.Value; }
        private static void SetEnabled(bool v) { enabled.Value = v; Save(); }
        private static double GetSpeed() { return cursorSpeed.Value; }
        private static void SetSpeed(double v) { cursorSpeed.Value = (float)v; Save(); }
        private static void Save() { MelonPreferences.GetCategory(KeyBinds.Category).SaveToFile(false); }

    }
}
