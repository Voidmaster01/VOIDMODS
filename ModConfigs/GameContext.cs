// GameContext.cs - part of ModConfigs (new in 1.1.0): knows which ROLE the player has right now, and whether they
// are playing their laptop MINIGAME, so the key binds (KeyBinds.cs) can be different per role and per minigame.
//
// WHAT IT DOES
//   Role:     Security Office, Parts & Service, Backstage, Janitor, Purple Guy, or none (home screen, lobby, dead or
//             spectating: then the game's keys are never changed).
//   Minigame: true while the player looks at the laptop's Games tab (Midnight Motorist for Parts & Service, Freddy in
//             Space for the Janitor).
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
            // "manager == null" is also true once Unity destroyed it (the match ended), see UnityEngine.Object.
            if (manager != null)
            {
                try
                {
                    if (manager.IsLocalPlayerAlive())
                    {
                        foreach (PlayerRoles r in Roles)
                            if (manager.IsRole(r)) { role = r; break; }
                        if (role == PlayerRoles.PartsAndService || role == PlayerRoles.Janitor)
                        {
                            PlayerBehaviour me = manager.GetLocalPlayerBehaviour();
                            PlayerComputer pc = me != null ? me.playerComputer : null;
                            minigame = pc != null && pc.IsWatchingSystem(ComputerScreen.Games);
                        }
                    }
                }
                catch (Exception e)
                {
                    // Between rounds the manager can be half torn down; act as "no role" and try again next time.
                    if (!warned) { warned = true; MelonLogger.Warning("[ModConfigs] Couldn't read the role (keys stay as the game has them): " + e.Message); }
                    role = PlayerRoles.None;
                    minigame = false;
                }
            }
            if (role == Role && minigame == InMinigame)
                return;
            Role = role;
            InMinigame = minigame;
            MelonLogger.Msg("[ModConfigs] Key binds now: " + (role == PlayerRoles.None ? "none (the game's own keys)" : role + (minigame ? " + minigame" : "")));
            if (Changed != null)
                Changed();
        }

        private static bool warned;

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
