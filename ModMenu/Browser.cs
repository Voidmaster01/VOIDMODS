// Browser.cs - part of ModMenu: the GET MODS section of the MODS panel (browse, install, update).
//
// WHAT IT DOES
//   Shows every approved mod from the list (Registry.cs) with what it is, and one button that fits:
//     INSTALL                      not installed yet (mods it requires are installed with it)
//     UPDATE TO x                  the list has a newer version than the one that is loaded
//     (no button)                  up to date / you have a newer version / switched off / installed, restart
//   UPDATE ALL appears when more than one mod can be updated. CHECK AGAIN downloads the list again.
//   The MODS button on the home screen says "MODS (2)" when 2 updates are waiting (HomeButton.SetCount).
//   SUBMIT A MOD opens the GitHub form where modders ask Voidmaster01 to add their mod to the list.
//
// HOW THE BACKGROUND WORK REACHES THE SCREEN
//   Downloads run on a background thread so the game doesn't freeze. That thread may NOT touch Unity objects, so it
//   only changes the plain fields below (inside "lock (Sync)", so the two threads never change them at the same time)
//   and sets "changed". Tick(), on the game's main thread every frame, sees "changed" and rebuilds the page.
//   Background work is started with Task.Run(new Action(job.Run)): a small job object instead of a lambda (the
//   VOIDMODS style, see ModConfigs' ConfigHub.cs).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MelonLoader;
using ModConfigs;
using UnityEngine;

namespace ModMenu
{
    internal static class Browser
    {
        public const string SubmitUrl = "https://github.com/Voidmaster01/VOIDMODS/issues/new?template=submit-mod.yml";

        private static readonly object Sync = new object();
        // ---- shared between the threads (only changed inside lock (Sync)) ----
        private static List<ListedMod> list;        // null until the first check finished
        private static string listError;            // why the last check failed, or null
        private static DateTime checkedAt;
        private static bool listFromTest;           // the last list came from registry-test.json
        private static bool busy;                   // a check or install is running (one thing at a time)
        private static string busyText = "";
        private static string busyId;               // the mod being downloaded now (its row shows busyText)
        private static string result;               // what the last install did (or why it failed)
        private static bool resultIsError;
        private static string resultId;             // the row that shows "result": the mod the player clicked
        private static readonly HashSet<string> installedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // ids, this session
        private static volatile bool changed;       // "volatile": the main thread always sees the newest value

        // ================= starting background work =================

        public static void StartCheck()
        {
            lock (Sync)
            {
                if (busy) return;
                busy = true;
                busyText = "Checking the mod list...";
            }
            changed = true;
            Task.Run(new Action(CheckJob));
        }

        // Background thread.
        private static void CheckJob()
        {
            string error;
            bool test = Registry.UsingTestList;
            List<ListedMod> got = Registry.Download(out error);
            lock (Sync)
            {
                // A failed check keeps the last list (GitHub may just be slow), unless that list came from the other
                // source (test file <-> GitHub): then it no longer applies and is dropped.
                if (got != null) list = got;
                else if (listFromTest != test) list = null;
                listError = error;
                listFromTest = test;
                checkedAt = DateTime.Now;
                busy = false;
            }
            if (error != null) MelonLogger.Warning("[ModMenu] " + error);
            changed = true;
        }

        // One install run: a list of mods (requirements first) and where each one goes.
        private sealed class InstallJob
        {
            public readonly List<ListedMod> Mods = new List<ListedMod>();
            public readonly List<string> Targets = new List<string>(); // null = new install into Mods
            public bool Test;

            // Background thread. Stops at the first failure (a mod without its requirement would be no use).
            public void Run()
            {
                int done = 0;
                string error = null;
                for (int i = 0; i < Mods.Count; i++)
                {
                    lock (Sync)
                    {
                        busyId = Mods[i].Id;
                        busyText = "Downloading " + Mods[i].Name + " " + Mods[i].Version + "... (" + (i + 1) + " of " + Mods.Count + ")";
                    }
                    changed = true;
                    try
                    {
                        Installer.Install(Mods[i], Targets[i], Test);
                        lock (Sync) installedNow.Add(Mods[i].Id);
                        done++;
                    }
                    catch (Exception e)
                    {
                        // AggregateException wraps errors from async downloads; GetBaseException finds the real one.
                        error = Mods[i].Name + ": " + e.GetBaseException().Message;
                        MelonLogger.Warning("[ModMenu] Install failed: " + error);
                        break;
                    }
                }
                lock (Sync)
                {
                    busy = false;
                    busyId = null;
                    resultIsError = error != null;
                    result = error != null
                        ? "Stopped. " + error + (done > 0 ? " (" + done + " installed before that.)" : "")
                        : done + (done == 1 ? " mod" : " mods") + " installed. Restart the game to use " + (done == 1 ? "it." : "them.");
                }
                changed = true;
            }
        }

        // Main thread: works out the order (missing requirements first) and starts the job.
        // where = the row whose button was clicked (null = UPDATE ALL): messages appear there, next to that button.
        private static void StartInstall(List<ListedMod> wanted, string where)
        {
            List<ListedMod> all;
            bool test;
            lock (Sync)
            {
                if (busy || list == null) return;
                all = list;
                test = listFromTest;
                resultId = where;
            }
            var job = new InstallJob { Test = test };
            string problem = null;
            foreach (ListedMod m in wanted)
                problem = problem ?? AddWithRequirements(m, all, job, 0);
            if (problem != null)
            {
                lock (Sync) { result = problem; resultIsError = true; }
                changed = true;
                return;
            }
            if (job.Mods.Count == 0) return;
            lock (Sync) { busy = true; busyText = "Starting..."; result = null; }
            changed = true;
            Task.Run(new Action(job.Run));
        }

        // Adds m's missing requirements (and theirs, and so on), then m itself. depth stops a loop where two mods
        // require each other. Returns a problem text, or null.
        private static string AddWithRequirements(ListedMod m, List<ListedMod> all, InstallJob job, int depth)
        {
            if (depth > 10) return "The requirements of " + m.Name + " go round in a circle.";
            if (job.Mods.Contains(m)) return null;
            foreach (string id in m.Requires)
            {
                ListedMod req = Find(all, id);
                bool have;
                lock (Sync) have = installedNow.Contains(id);
                if (have) continue;
                if (req == null)
                {
                    var probe = new ListedMod { Id = id, File = id + ".dll" };
                    if (Installer.Where(probe).Path != null) continue; // installed already, just not in the list
                    return m.Name + " needs " + id + ", which isn't installed and isn't in the mod list.";
                }
                Found f = Installer.Where(req);
                if (f.Path != null && !f.On) return m.Name + " needs " + req.Name + ": switch it ON under Installed mods first.";
                if (f.Path == null)
                {
                    string p = AddWithRequirements(req, all, job, depth + 1);
                    if (p != null) return p;
                }
            }
            Found mine = Installer.Where(m);
            job.Mods.Add(m);
            job.Targets.Add(mine.On ? mine.Path : null);
            return null;
        }

        private static ListedMod Find(List<ListedMod> all, string id)
        {
            foreach (ListedMod m in all)
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                    return m;
            return null;
        }

        // ================= what each mod's state is (main thread) =================

        private enum State { NotInstalled, Update, UpToDate, Newer, Off, Restart }

        private static State StateOf(ListedMod m, out Found f)
        {
            f = Installer.Where(m);
            lock (Sync)
                if (installedNow.Contains(m.Id))
                    return State.Restart;
            if (f.Path == null) return State.NotInstalled;
            if (!f.On) return State.Off;
            if (f.LoadedVersion == null)
                // In Mods but not loaded (added after the game started): the fingerprint tells if it is this version.
                return Installer.Sha256OfFile(f.Path) == m.Sha256 ? State.UpToDate : State.Update;
            int c = Installer.CompareVersions(m.Version, f.LoadedVersion);
            return c > 0 ? State.Update : c < 0 ? State.Newer : State.UpToDate;
        }

        public static int UpdateCount()
        {
            List<ListedMod> l;
            lock (Sync) l = list;
            if (l == null) return 0;
            int n = 0;
            Found f;
            foreach (ListedMod m in l)
                if (StateOf(m, out f) == State.Update)
                    n++;
            return n;
        }

        // Every frame (from ModMenuMod.OnUpdate): show what the background thread finished.
        public static void Tick()
        {
            if (!changed) return;
            changed = false;
            ConfigHub.Refresh(); // rebuilds the MODS panel on the next frame, if it is open
            HomeButton.SetCount(UpdateCount());
        }

        // ================= the page section =================

        // A copy of the shared fields, taken in one go inside the lock, so one page build sees one consistent state.
        private sealed class Snapshot
        {
            public List<ListedMod> List; public string Error; public DateTime At; public bool Test, Busy, ResultIsError;
            public string BusyText, BusyId, Result, ResultId;
        }

        public static void BuildSection(ConfigPage page)
        {
            var st = new Snapshot();
            lock (Sync)
            {
                st.List = list; st.Error = listError; st.At = checkedAt; st.Test = listFromTest; st.Busy = busy;
                st.BusyText = busyText; st.BusyId = busyId; st.Result = result; st.ResultIsError = resultIsError; st.ResultId = resultId;
            }

            page.Header("Get mods");
            page.Label("Only mods Voidmaster01 has checked are listed. Every download is checked against the approved " +
                "file's fingerprint, and older copies are kept in UserData\\ModMenu\\Backups.", 18).color = UiKit.DimText;
            if (st.Test)
                page.Label("TEST LIST in use: UserData\\ModMenu\\registry-test.json (delete it to use the real list).", 18).color = UiKit.Purple;
            var updates = new List<ListedMod>();
            if (st.List != null)
            {
                if (st.List.Count == 0)
                    page.Label("The list is empty for now.", 20).color = UiKit.DimText;
                foreach (ListedMod m in st.List)
                {
                    Found f;
                    State s = StateOf(m, out f);
                    if (s == State.Update) updates.Add(m);
                    Row(page, m, s, f, st);
                }
            }
            // UPDATE ALL's own messages (resultId null) appear right above its button.
            if (st.ResultId == null && st.Result != null)
                page.Label(st.Result, 20).color = st.ResultIsError ? UiKit.Purple : UiKit.DimText;
            if (updates.Count > 1)
                page.Button(st.Busy ? "WORKING..." : "UPDATE ALL (" + updates.Count + ")", new Action(new InstallBinding(updates, null).Click));
            // The list's own status sits right above CHECK AGAIN, so pressing it visibly does something.
            page.Space(8);
            if (st.Busy && st.BusyId == null)
                page.Label(st.BusyText, 20).color = UiKit.Purple;   // checking the list (no mod row to show it on)
            else if (st.Error != null)
                page.Label(st.Error, 20).color = UiKit.Purple;
            else if (st.List != null)
                page.Label("List checked at " + st.At.ToString("HH:mm") + ".", 18).color = UiKit.DimText;
            page.Button(st.Busy ? "WORKING..." : "CHECK AGAIN", new Action(CheckAgain));
            page.Button("SUBMIT A MOD (GITHUB)", new Action(OpenSubmit));
        }

        private static void Row(ConfigPage page, ListedMod m, State s, Found f, Snapshot st)
        {
            bool isBusy = st.Busy;
            page.Space(8);
            page.Label(m.Name + "  " + m.Version + (string.IsNullOrEmpty(m.Author) ? "" : "  by " + m.Author), 24);
            if (!string.IsNullOrEmpty(m.About))
                page.Label(m.About, 18).color = UiKit.DimText;
            if (m.Requires.Length > 0)
            {
                // Show the listed names ("Test Helper") rather than the ids ("MMTestHelper") where the list has them.
                var names = new List<string>();
                foreach (string id in m.Requires)
                {
                    ListedMod r = Find(st.List, id);
                    names.Add(r != null ? r.Name : id);
                }
                page.Label("Needs: " + string.Join(", ", names), 18).color = UiKit.DimText;
            }
            // Progress and results appear on the row they belong to, right where the player clicked.
            if (st.Busy && st.BusyId == m.Id)
                page.Label(st.BusyText, 20).color = UiKit.Purple;
            if (!st.Busy && st.Result != null && st.ResultId == m.Id)
                page.Label(st.Result, 20).color = st.ResultIsError ? UiKit.Purple : UiKit.DimText;
            string status;
            switch (s)
            {
                case State.NotInstalled: status = null; break;
                case State.Update: status = "Installed: " + (f.LoadedVersion ?? "another build") + ". A newer version is ready."; break;
                case State.UpToDate: status = "Installed, up to date."; break;
                case State.Newer: status = "Installed: " + f.LoadedVersion + " (newer than the list)."; break;
                case State.Off: status = "Installed but switched off (see Installed mods)."; break;
                default: status = "Installed. Restart the game to load it."; break;
            }
            if (status != null)
                page.Label(status, 18).color = s == State.Update ? UiKit.Purple : UiKit.DimText;
            var one = new List<ListedMod> { m };
            if (s == State.NotInstalled)
                page.Button(isBusy ? "WORKING..." : "INSTALL " + m.Name.ToUpperInvariant(), new Action(new InstallBinding(one, m.Id).Click));
            else if (s == State.Update)
                page.Button(isBusy ? "WORKING..." : "UPDATE " + m.Name.ToUpperInvariant() + " TO " + m.Version, new Action(new InstallBinding(one, m.Id).Click));
        }

        private static void CheckAgain()
        {
            lock (Sync) result = null;
            StartCheck();
        }

        private static void OpenSubmit()
        {
            Application.OpenURL(SubmitUrl);
        }

        // Connects one INSTALL / UPDATE button to its mods (instead of a lambda).
        private sealed class InstallBinding
        {
            private readonly List<ListedMod> mods;
            private readonly string where;
            public InstallBinding(List<ListedMod> m, string row) { mods = m; where = row; }
            public void Click() { StartInstall(mods, where); }
        }
    }
}
