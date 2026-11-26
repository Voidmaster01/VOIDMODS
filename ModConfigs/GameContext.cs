// GameContext.cs - part of ModConfigs (new in 1.1.0): knows which ROLE the player has right now, and whether they
// are playing their laptop MINIGAME, so the key binds (KeyBinds.cs) can be different per role and per minigame.
//
// WHAT IT DOES
//   Role:     Security Office, Parts & Service, Backstage, Janitor, Purple Guy, or none (home screen, lobby, dead or
//             spectating: then the game's keys are never changed).
//   Minigame: true while the player looks at the laptop's Games tab (Midnight Motorist for Parts & Service, Freddy in
//             Space for the Janitor).
//   Monitor:  true while a crew role's monitor / laptop is up (Backstage: while looking at its always-on screen),
//             so every role can have a "monitor down" and a "monitor up" set of keys.
//   Every change is written to the log ("[ModConfigs] Key binds now: Janitor's Closet + minigame") for bug reports.
//   It also opens Purple Guy's left/right screens for the keys ModConfigs adds for him (see the end of this file),
//   and tells when the player is typing in a text box.
//
// HOW IT WORKS
//   The game has one PlayerRoleManager per match. A Harmony postfix on its Awake/Start remembers it, and its OnDisable
//   forgets it again. Ten times a second Tick() asks it:
//     IsLocalPlayerAlive()                        - dead players spectate; their keys are left alone
//     IsRole(PlayerRoles.X)                       - which role this player has
//     GetLocalPlayerBehaviour().playerComputer
//       .IsWatchingSystem(ComputerScreen.Games)   - is the laptop on the Games tab?
//   (Found by reading the names in the game's code: MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll.)
//   Asking only 10 times a second keeps it cheap; the key questions (asked many times per frame) just read the answer
//   saved here.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ModConfigs
{
    internal static class GameContext
    {
        private static PlayerRoleManager manager;   // the match's role manager, or null outside a match
        private static float nextCheck;

        public static PlayerRoles Role { get; private set; } = PlayerRoles.None;
        public static bool InMinigame { get; private set; }
        // Which set of keys applies inside the role (KeySection.State in KeyBinds.cs):
        //   "down" / "up"  the monitor / laptop is down or up (Purple Guy is always "down": he has no such monitor)
        //   Backstage, whose screen is always on, by what it is LOOKING at: "door", "shock", "maintenance", or "up"
        //   (the computer screen). Backstage can only use the door or the shock while looking straight at it.
        public static string State { get; private set; } = "down";
        public static bool MonitorUp { get { return State == "up"; } }
        private static PlayerComputer computer; // the local player's laptop / monitor, for the tab keys

        // Raised (from Tick, on the main thread) when Role or InMinigame changed.
        public static event Action Changed;

        internal static void SetManager(PlayerRoleManager m) { manager = m; }
        internal static void ClearManager(PlayerRoleManager m) { if (manager == m) manager = null; }

        private static readonly PlayerRoles[] Roles =
            { PlayerRoles.SecurityOffice, PlayerRoles.PartsAndService, PlayerRoles.Backstage, PlayerRoles.Janitor, PlayerRoles.PurpleGuy };

        // Called every frame by ModConfigsMod (before Controller.Tick, so the controller uses the newest role).
        public static void Tick()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 0.1f;
            PlayerRoles role = PlayerRoles.None;
            bool minigame = false;
            string state = "down";
            PlayerComputer pc = null;
            // "manager == null" is also true once Unity destroyed it (the match ended), see UnityEngine.Object.
            if (manager != null)
            {
                try
                {
                    if (manager.IsLocalPlayerAlive())
                    {
                        foreach (PlayerRoles r in Roles)
                            if (manager.IsRole(r)) { role = r; break; }
                        if (role != PlayerRoles.None && role != PlayerRoles.PurpleGuy)
                        {
                            PlayerBehaviour me = manager.GetLocalPlayerBehaviour();
                            pc = me != null ? me.playerComputer : null;
                            if (pc != null)
                            {
                                // Monitor up: the computer's own isMonitorUp flag. Backstage's screen is always on
                                // (isMonitorAlwaysUp), so for Backstage the state is the camera view it looks at.
                                if (pc.isMonitorAlwaysUp)
                                {
                                    BackstagePlayerBehaviour bs = me.TryCast<BackstagePlayerBehaviour>();
                                    if (bs != null && bs.backstageCameraController != null)
                                        state = BackstageState(bs.backstageCameraController.currentView.Value);
                                }
                                else
                                    state = pc.isMonitorUp.Value ? "up" : "down";
                                minigame = state == "up" && (role == PlayerRoles.PartsAndService || role == PlayerRoles.Janitor) && pc.IsWatchingSystem(ComputerScreen.Games);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // Between rounds the manager can be half torn down; act as "no role" and try again next time.
                    if (!warned) { warned = true; MelonLogger.Warning("[ModConfigs] Couldn't read the role (keys stay as the game has them): " + e.Message); }
                    role = PlayerRoles.None;
                    minigame = false;
                    state = "down";
                    pc = null;
                }
            }
            computer = pc;
            if (role == Role && minigame == InMinigame && state == State)
                return;
            Role = role;
            InMinigame = minigame;
            State = state;
            MelonLogger.Msg("[ModConfigs] Key binds now: " + (role == PlayerRoles.None ? "none (the game's own keys)"
                : role + " (" + state + ")" + (minigame ? " + minigame" : "")));
            if (Changed != null)
                Changed();
        }

        private static bool warned;

        private static string BackstageState(BackstageCameraController_View view)
        {
            switch (view)
            {
                case BackstageCameraController_View.MonitorView: return "up";
                case BackstageCameraController_View.DoorView: return "door";
                case BackstageCameraController_View.ShockView: return "shock";
                default: return "maintenance";
            }
        }

        // ================= Purple Guy's screens (keys the game doesn't have) =================
        // Purple Guy opens his left and right screens only by touching the bars at the screen edges with the mouse.
        // Those bars call PurpleGuyUI.MonitorFlip0 (left) and MonitorFlip1 (right), cooldown included, so the new
        // keys call exactly the same functions: a key press does what touching the bar does.

        private static PurpleGuyUI purpleUi;
        internal static void SetPurpleUi(PurpleGuyUI ui) { purpleUi = ui; }

        public static void FlipPurpleLeft() { FlipPurple(0); }
        public static void FlipPurpleRight() { FlipPurple(1); }

        private static void FlipPurple(int monitor)
        {
            if (Role != PlayerRoles.PurpleGuy || purpleUi == null)
                return;
            try
            {
                if (monitor == 0) purpleUi.MonitorFlip0();
                else purpleUi.MonitorFlip1();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't flip Purple Guy's screen: " + e.Message);
            }
        }

        // ================= laptop / monitor tabs (keys the game doesn't have) =================
        // The tab bar at the top of the laptop (Cameras, Comms, Games, Manual, Wordle...) is mouse-only in the game.
        // PlayerComputer.NextScreen / PreviousScreen are the game's own "go to the next / previous tab" functions.

        public static void NextTab() { Tab(true); }
        public static void PreviousTab() { Tab(false); }

        private static void Tab(bool next)
        {
            if (!MonitorUp || computer == null)
                return;
            try
            {
                if (next) computer.NextScreen();
                else computer.PreviousScreen();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't change the laptop tab: " + e.Message);
            }
        }

        // ================= items on the current screen: cameras, trackers, lures (keys the game doesn't have) ========
        // Voidmaster01: LT / RT go through the buttons of the open tab "top to bottom, left to right, from the top-left
        // one to the bottom-right one". The buttons are found by their kind (the game's own components):
        //   Cameras          CameraSelectionButton (One..Nine): in NUMBER order, and stepping SWITCHES the camera (Select()).
        //   Motion Detection TrackerButton, Audio Lure LureButton: stepping only MOVES THE CURSOR onto the button and
        //                    A (or a click) uses it, because using them costs power / lure uses.
        // Reading order: higher on screen first; buttons at about the same height (within 4% of the screen) count as
        // one row and go left to right. "Which one am I on" = the button nearest the cursor (within 40 pixels).

        private sealed class Item { public Component Thing; public Vector2 Point; }

        private static int ReadingOrder(Item a, Item b)
        {
            float row = Screen.height * 0.04f;
            if (Math.Abs(a.Point.y - b.Point.y) > row)
                return b.Point.y.CompareTo(a.Point.y); // Unity's y grows upward: bigger y = higher = first
            return a.Point.x.CompareTo(b.Point.x);
        }

        // The cameras only the Security Office may see, read from the match's CameraData objects (one per camera).
        // FindObjectsOfType walks the whole scene, so the answer is kept until the match changes.
        private static HashSet<CameraName> securityOnly;
        private static PlayerRoleManager securityOnlyFor;

        private static HashSet<CameraName> SecurityOnlyCameras()
        {
            if (securityOnly != null && securityOnlyFor == manager)
                return securityOnly;
            securityOnly = new HashSet<CameraName>();
            securityOnlyFor = manager;
            foreach (CameraData d in UnityEngine.Object.FindObjectsOfType<CameraData>())
                if (d != null && d.isSecurityOfficeOnly)
                    securityOnly.Add(d.cameraName);
            return securityOnly;
        }

        private static int ByCameraNumber(Item a, Item b)
        {
            return ((int)a.Thing.Cast<CameraSelectionButton>().targetCameraName).CompareTo((int)b.Thing.Cast<CameraSelectionButton>().targetCameraName);
        }

        public static void NextItem() { StepItem(1); }
        public static void PreviousItem() { StepItem(-1); }

        private static void StepItem(int dir)
        {
            if (!MonitorUp || computer == null)
                return;
            try
            {
                ComputerScreen screen = computer.currentComputerScreen.Value;
                var items = new List<Item>();
                if (screen == ComputerScreen.Cameras && computer.playerCameraSystem != null)
                {
                    // Some cameras are only for the Security Office (CameraData.isSecurityOfficeOnly); other roles skip them.
                    HashSet<CameraName> hidden = Role == PlayerRoles.SecurityOffice ? null : SecurityOnlyCameras();
                    foreach (CameraSelectionButton b in computer.playerCameraSystem.GetComponentsInChildren<CameraSelectionButton>(false))
                        if (hidden == null || !hidden.Contains(b.targetCameraName))
                            AddItem(items, b);
                }
                else if (screen == ComputerScreen.MotionDetection && computer.playerMotionDetectionSystem != null)
                {
                    foreach (TrackerButton b in computer.playerMotionDetectionSystem.GetComponentsInChildren<TrackerButton>(false))
                        AddItem(items, b);
                }
                else if (screen == ComputerScreen.AudioLure && computer.playerAudioLureSystem != null)
                {
                    foreach (LureButton b in computer.playerAudioLureSystem.GetComponentsInChildren<LureButton>(false))
                        AddItem(items, b);
                }
                if (items.Count == 0)
                    return;
                // Cameras go by their number (One, Two ... Nine, Voidmaster01: "cameras always start from one"); the
                // Janitor's trackers and lures go in reading order.
                if (screen == ComputerScreen.Cameras) items.Sort(new Comparison<Item>(ByCameraNumber));
                else items.Sort(new Comparison<Item>(ReadingOrder));
                // Where are we now? Cameras: the camera being watched (the Cameras tab always opens on camera One, so
                // RT goes to Two and LT to Nine). Trackers / lures: the button the cursor is on. -1 = none: then RT
                // starts at the first, LT at the last.
                int current = -1;
                if (screen == ComputerScreen.Cameras)
                {
                    for (int i = 0; i < items.Count; i++)
                        if (computer.playerCameraSystem.IsWatchingCamera(items[i].Thing.Cast<CameraSelectionButton>().targetCameraName)) { current = i; break; }
                }
                else
                {
                    Vector2 cursor;
                    if (Controller.CursorScreenPoint(out cursor))
                        for (int i = 0; i < items.Count; i++)
                            if ((items[i].Point - cursor).magnitude < 40f) { current = i; break; }
                }
                int next = current < 0 ? (dir > 0 ? 0 : items.Count - 1) : (current + dir + items.Count) % items.Count; // % wraps around
                Item pick = items[next];
                if (screen == ComputerScreen.Cameras)
                    pick.Thing.Cast<CameraSelectionButton>().Select();
                Controller.CursorToScreenPoint(pick.Point);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't step to the next item: " + e.Message);
            }
        }

        private static void AddItem(List<Item> items, Component c)
        {
            Vector2 p;
            if (c != null && c.gameObject.activeInHierarchy && Controller.ScreenPoint(c.transform, out p))
                items.Add(new Item { Thing = c, Point = p });
        }

        // Number keys 1-9 (top row or number pad) pick that camera while the Cameras tab is open (Voidmaster01: "cameras
        // usually use the numpad / number keys"). Security's keypad also listens to digits for its security codes, so
        // while a code is being asked for (KeypadSystem.isCalling) the digits are left to the keypad alone.
        private static KeypadSystem keypad;

        public static void SelectCameraNumber(int n)
        {
            if (!MonitorUp || computer == null || computer.playerCameraSystem == null)
                return;
            try
            {
                if (computer.currentComputerScreen.Value != ComputerScreen.Cameras)
                    return;
                if (Role == PlayerRoles.SecurityOffice)
                {
                    if (keypad == null) keypad = UnityEngine.Object.FindObjectOfType<KeypadSystem>();
                    if (keypad != null && keypad.isCalling.Value)
                        return;
                }
                CameraName want = (CameraName)(n - 1); // One = 0, Two = 1, ... (the enum's own order)
                if (Role != PlayerRoles.SecurityOffice && SecurityOnlyCameras().Contains(want))
                    return; // a Security-only camera: not for this role
                foreach (CameraSelectionButton b in computer.playerCameraSystem.GetComponentsInChildren<CameraSelectionButton>(false))
                    if (b.targetCameraName == want)
                    {
                        b.Select();
                        return;
                    }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't pick camera " + n + ": " + e.Message);
            }
        }

        // Esc / Start: puts the monitor or laptop down if it is up (EscapeKey.cs). True when it did something.
        // Backstage's screen is always on (isMonitorAlwaysUp), so it is left alone. Purple Guy: his open left or right
        // screen goes down (the same functions as his A / D keys).
        public static bool LowerMonitorForEscape()
        {
            if (Role == PlayerRoles.PurpleGuy && purpleUi != null)
            {
                if (purpleUi.isMonitorUp) { purpleUi.MonitorFlip0(); return true; }
                if (purpleUi.isMonitor1Up) { purpleUi.MonitorFlip1(); return true; }
                return false;
            }
            if (Role == PlayerRoles.None || computer == null || computer.isMonitorAlwaysUp)
                return false;
            if (!computer.isMonitorUp.Value)
                return false;
            computer.ToggleMonitorFlip(); // the game's own up/down switch, so its animation and rules apply
            return true;
        }

        // True while the player types in a text box (Purple Guy's Wordle "input guesses by text", the chat...), so
        // keys that ModConfigs ADDS (like Purple Guy's A/D) don't fire while typing letters.
        public static bool Typing()
        {
            try
            {
                UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                GameObject sel = es != null ? es.currentSelectedGameObject : null;
                if (sel == null)
                    return false;
                Il2CppTMPro.TMP_InputField tmp = sel.GetComponent<Il2CppTMPro.TMP_InputField>();
                if (tmp != null && tmp.isFocused)
                    return true;
                UnityEngine.UI.InputField old = sel.GetComponent<UnityEngine.UI.InputField>();
                return old != null && old.isFocused;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(PurpleGuyUI), nameof(PurpleGuyUI.Awake))]
    internal static class PurpleGuyUiAwakePatch
    {
        private static void Postfix(PurpleGuyUI __instance) { GameContext.SetPurpleUi(__instance); }
    }

    // Remember / forget the match's PlayerRoleManager. Postfix = runs right after the game's own method.
    [HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.Awake))]
    internal static class RoleManagerAwakePatch
    {
        private static void Postfix(PlayerRoleManager __instance) { GameContext.SetManager(__instance); }
    }

    [HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.Start))]
    internal static class RoleManagerStartPatch
    {
        private static void Postfix(PlayerRoleManager __instance) { GameContext.SetManager(__instance); }
    }

    [HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.OnDisable))]
    internal static class RoleManagerDisablePatch
    {
        private static void Postfix(PlayerRoleManager __instance) { GameContext.ClearManager(__instance); }
    }
}
