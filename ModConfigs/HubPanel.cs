// HubPanel.cs - part of ModConfigs: the MOD CONFIGS panel that opens from the home screen menu.
//
// WHAT IT DOES
//   Shows a search box and a list of mods on the left, and the chosen mod's settings page on the right:
//     - mods that called ConfigHub.Register get their own page (VOIDMODS show the VOIDMODS logo),
//     - every other mod with MelonPreferences settings gets an automatic page: on/off settings become switches,
//       numbers get - and + (or a slider when the mod gave a min and max), text gets a text box, a list of fixed
//       choices (an "enum") gets a dropdown and colors get a color picker. Each change is saved to
//       UserData\MelonPreferences.cfg right away,
//     - (1.1.0) mods with their own config file (.ini .cfg .json .toml .yaml .xml ...) get a page too, see
//       FileConfigs.cs.
//   Each page has a RESET TO DEFAULTS button (click twice, so it can't happen by accident).
//   GET MODS opens the Mod Menu mod when it is installed, or else its download page on GitHub.
//   Pages scroll with the mouse wheel when they are taller than the panel.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into ModConfigs.dll. MenuRow.cs opens it (HubPanel.Open). It lives on its own Canvas in the home
//   screen scene, so leaving the home screen removes it automatically.

using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class HubPanel
    {
        // One line in the mod list: a name, a version, the logo flag, the method that builds its page and the
        // method that resets it (null = this page has nothing to reset).
        private sealed class Item
        {
            public string Name, Version;
            public bool VoidMod;
            public Action<ConfigPage> Build;
            public Action Reset;
        }

        private static GameObject root;
        private static RectTransform listContent, pageContent, resetArea;
        private static TextMeshProUGUI pageTitle;
        private static List<Item> items = new List<Item>();
        private static string selected; // the name of the open page, kept while the panel is closed
        private static string filter = ""; // what is typed in the search box

        private const float PageWidth = 840f;

        public static bool IsOpen
        {
            get { return root != null && root.activeSelf; }
        }

        // ================= opening and closing =================

        public static void Open()
        {
            try
            {
                ModPanel.Close(); // one VOIDMODS panel at a time
                if (root == null)
                    Build();
                root.SetActive(true);
                CollectItems();
                if (selected == null && items.Count > 0)
                    selected = items[0].Name;
                ShowList();
                RebuildCurrent();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] The panel failed to open: " + e);
            }
        }

        // Opens the panel with the page called "name" selected (ConfigHub.Show). The search box is not cleared, so
        // a page hidden by the search still opens; it is just not highlighted in the list.
        public static void OpenOn(string name)
        {
            selected = name;
            ConfigPage.OpenRow = null;
            Open();
        }

        // Closes MOD CONFIGS if it is open (ModPanel calls this: one VOIDMODS panel at a time).
        internal static void CloseIfOpen()
        {
            if (IsOpen)
                Close();
        }

        private static void Close()
        {
            FileConfigs.Flush();      // save config-file changes that are still waiting
            ConfigPage.OpenRow = null; // fold any open dropdown shut for next time
            if (root != null)
                root.SetActive(false);
        }

        // GET MODS: the Mod Menu mod takes this button over with ConfigHub.SetGetMods. Without it, the button opens
        // the Mod Menu's release page in the web browser (Application.OpenURL), where it can be downloaded.
        private static void GetMods()
        {
            if (ConfigHub.GetModsHandler != null)
            {
                try { ConfigHub.GetModsHandler(); }
                catch (Exception e) { MelonLogger.Warning("[ModConfigs] GET MODS failed: " + e); }
                return;
            }
            Application.OpenURL(ConfigHub.ModMenuReleaseUrl);
        }

        // ================= building the frame =================

        // The parts that never change: dark background, the panel box, title, buttons, search box, list and page.
        private static void Build()
        {
            root = new GameObject("ModConfigsCanvas");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // above the game's menus
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; // lay out as if the screen were 1920x1080
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>(); // lets the canvas receive clicks

            // Full-screen dimming layer; it also catches clicks so they don't reach the menu behind.
            RectTransform dim = UiKit.Fill(root.transform, "Dim");
            UiKit.Box(dim, new Color(0, 0, 0, 0.7f), false);

            RectTransform panel = UiKit.Element(dim, "Panel", Center, Center, Center, Vector2.zero, new Vector2(1360, 880));
            UiKit.Box(panel, UiKit.Background, true);

            RectTransform title = UiKit.Element(panel, "Title", TopLeft, TopLeft, TopLeft, new Vector2(40, -24), new Vector2(600, 64));
            UiKit.Text(title, "MOD CONFIGS", 44, UiKit.Purple, TextAlignmentOptions.MidlineLeft);
            RectTransform getMods = UiKit.Element(panel, "GetMods", TopRight, TopRight, TopRight, new Vector2(-260, -26), new Vector2(240, 60));
            UiKit.Button(getMods, "GET MODS", new Action(GetMods), false).color = UiKit.Purple;
            RectTransform back = UiKit.Element(panel, "Back", TopRight, TopRight, TopRight, new Vector2(-40, -26), new Vector2(200, 60));
            UiKit.Button(back, "BACK", new Action(Close), false);

            // The search box: every letter typed filters the list (onValueChanged fires on each change).
            RectTransform search = UiKit.Element(panel, "Search", TopLeft, TopLeft, TopLeft, new Vector2(40, -110), new Vector2(370, 54));
            TMP_InputField box = UiKit.InputField(search, "", "Search mods...", 22, null);
            box.onValueChanged.AddListener(new Action<string>(Search));
            listContent = MakeScroll(panel, new Vector2(40, -176), new Vector2(380, 664));

            RectTransform titleBox = UiKit.Element(panel, "PageTitle", TopLeft, TopLeft, TopLeft, new Vector2(470, -110), new Vector2(PageWidth - 320, 50));
            pageTitle = UiKit.Text(titleBox, "", 30, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            pageTitle.overflowMode = TextOverflowModes.Ellipsis;
            resetArea = UiKit.Element(panel, "Reset", TopLeft, TopLeft, TopLeft, new Vector2(470 + PageWidth - 300, -108), new Vector2(300, 50));
            pageContent = MakeScroll(panel, new Vector2(470, -170), new Vector2(PageWidth + 10, 670));
        }

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopLeft = new Vector2(0, 1);
        private static readonly Vector2 TopRight = new Vector2(1, 1);

        // A scrolling area: a "viewport" that cuts off anything outside it (RectMask2D), a "content" element inside
        // that holds the rows, and a ScrollRect that moves the content with the mouse wheel or by dragging.
        private static RectTransform MakeScroll(Transform parent, Vector2 pos, Vector2 size)
        {
            RectTransform viewport = UiKit.Element(parent, "Viewport", TopLeft, TopLeft, TopLeft, pos, size);
            viewport.gameObject.AddComponent<RectMask2D>();
            UiKit.Box(viewport, new Color(0, 0, 0, 0.001f), false); // nearly invisible, but catches the mouse wheel
            RectTransform content = UiKit.Element(viewport, "Content", TopLeft, TopRight, TopLeft, Vector2.zero, new Vector2(0, 0));
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; // no bouncing past the ends
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        // ================= the mod list =================

        // Registered pages first (sorted by name), then automatic pages for the remaining MelonPreferences
        // categories, then pages for mods' own config files.
        private static void CollectItems()
        {
            items = new List<Item>();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registered = new List<ConfigHub.Entry>(ConfigHub.Entries);
            registered.Sort(new Comparison<ConfigHub.Entry>(ByName));
            foreach (ConfigHub.Entry e in registered)
            {
                Action reset;
                if (!ConfigHub.Resets.TryGetValue(e.Name, out reset))
                {
                    // No reset method given: reset the MelonPreferences category the page covers, if there is one.
                    MelonPreferences_Category category = FindCategory(e.PrefsCategory);
                    if (category != null)
                        reset = new Action(new CategoryReset(category).Run);
                }
                items.Add(new Item { Name = e.Name, Version = e.Version, VoidMod = e.VoidMod, Build = e.Build, Reset = reset });
                covered.Add(e.PrefsCategory);
            }
            foreach (MelonPreferences_Category category in MelonPreferences.Categories)
            {
                if (category.IsHidden || covered.Contains(category.Identifier) || !HasVisibleEntries(category))
                    continue;
                var auto = new AutoPage(category);
                items.Add(new Item
                {
                    Name = category.DisplayName ?? category.Identifier, Version = "settings",
                    Build = new Action<ConfigPage>(auto.Build), Reset = new Action(new CategoryReset(category).Run)
                });
            }
            try
            {
                foreach (FileConfigs.Group group in FileConfigs.Scan())
                {
                    var page = new FilePage(group);
                    string name = items.Exists(new Predicate<Item>(new Selector2(group.Name).Matches)) ? group.Name + " (file)" : group.Name;
                    items.Add(new Item { Name = name, Version = "config file", Build = new Action<ConfigPage>(page.Build), Reset = new Action(page.Reset) });
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Looking for config files failed: " + e);
            }
        }

        private static int ByName(ConfigHub.Entry a, ConfigHub.Entry b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static MelonPreferences_Category FindCategory(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return null;
            foreach (MelonPreferences_Category c in MelonPreferences.Categories)
                if (string.Equals(c.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        private static bool HasVisibleEntries(MelonPreferences_Category category)
        {
            foreach (MelonPreferences_Entry e in category.Entries)
                if (!e.IsHidden)
                    return true;
            return false;
        }

        // Called on every change in the search box.
        private static void Search(string text)
        {
            filter = (text ?? "").Trim();
            ShowList();
        }

        // Does this list line match the search? IndexOf(..) >= 0 means "the text is somewhere in it";
        // OrdinalIgnoreCase ignores upper/lower case. An empty search matches everything.
        private static bool Matches(Item item)
        {
            return filter.Length == 0
                || item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.Version ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Rebuilds the list buttons (only the ones matching the search). The open page gets a purple bar on its left.
        private static void ShowList()
        {
            Clear(listContent);
            float y = 0;
            foreach (Item item in items)
            {
                if (!Matches(item))
                    continue;
                RectTransform rt = UiKit.Element(listContent, "Mod", TopLeft, TopLeft, TopLeft, new Vector2(0, -y), new Vector2(370, 70));
                var select = new Selector(item.Name);
                TextMeshProUGUI label = UiKit.Button(rt, item.Name + "\n<size=16><color=#8c859e>" + item.Version + "</color></size>", new Action(select.Click), false);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(item.VoidMod ? 72 : 20, 0, 10, 0);
                label.fontSize = 22;
                if (item.VoidMod && UiKit.VoidModLogo != null)
                {
                    RectTransform logo = UiKit.Element(rt, "Logo", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(46, 46));
                    UiKit.Picture(logo, UiKit.VoidModLogo);
                }
                if (item.Name == selected)
                {
                    RectTransform bar = UiKit.Element(rt, "Selected", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(6, 0));
                    UiKit.Box(bar, UiKit.Purple, false).raycastTarget = false;
                }
                y += 80;
            }
            if (y == 0 && items.Count > 0)
            {
                RectTransform none = UiKit.Element(listContent, "None", TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(370, 60));
                UiKit.Text(none, "No mods match \"" + filter + "\".", 20, UiKit.DimText, TextAlignmentOptions.MidlineLeft);
                y = 60;
            }
            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x, y);
        }

        // Remembers which list line it belongs to, so its Click can open that page (instead of a lambda).
        private sealed class Selector
        {
            private readonly string name;
            public Selector(string n) { name = n; }
            public void Click()
            {
                selected = name;
                ConfigPage.OpenRow = null;
                Later(true, false); // list + page, on the next frame
            }
        }

        // ================= the page =================

        // Empties the page area and lets the selected mod build its page again (so values are always current).
        // This one rebuilds RIGHT NOW: only for code that is not running inside a click (like Open).
        public static void RebuildCurrent()
        {
            Rebuild(false);
        }

        // ---- rebuilding on the NEXT frame ----
        // A click on a button runs inside Unity's own click handling. If that click rebuilt the page at once, it
        // would destroy the very button Unity is still busy with, and Unity's UI ends up in a broken state (we saw
        // every new button turn plain white). So clicks only ASK for a rebuild here, and Tick (called every frame by
        // ModConfigsMod) does it on the next frame, when Unity has finished with the click.
        private static bool laterPage, laterList, laterKeepScroll;

        // list = also rebuild the mod list; keepScroll = stay at the same scroll spot on the page.
        internal static void Later(bool list, bool keepScroll)
        {
            if (!laterPage)
                laterKeepScroll = keepScroll;
            else
                laterKeepScroll &= keepScroll; // if any request wants the top, go to the top
            laterPage = true;
            laterList |= list;
        }

        // The same as Later(false, true): used when a dropdown or color picker folds open or shut, and by
        // ConfigHub.Refresh, so the page stays where it was.
        internal static void RebuildKeepScroll()
        {
            Later(false, true);
        }

        // Called every frame: does a rebuild that was asked for.
        internal static void Tick()
        {
            if (!laterPage)
                return;
            bool list = laterList, keep = laterKeepScroll;
            laterPage = laterList = false;
            if (list)
                ShowList();
            Rebuild(keep);
        }

        private static void Rebuild(bool keepScroll)
        {
            if (!IsOpen)
                return;
            Vector2 scroll = pageContent.anchoredPosition;
            Clear(pageContent);
            Clear(resetArea);
            pageContent.anchoredPosition = keepScroll ? scroll : Vector2.zero;
            Item item = items.Find(new Predicate<Item>(new Selector2(selected).Matches));
            if (item == null)
            {
                pageTitle.text = items.Count == 0 ? "No mods with settings found." : "";
                return;
            }
            pageTitle.text = item.Name + "  <size=20><color=#8c859e>" + item.Version + "</color></size>";
            if (item.Reset != null)
            {
                var reset = new ResetButton(item.Reset);
                // The button is a CHILD of resetArea, so Clear(resetArea) removes it for the next page. (Making
                // resetArea itself the button would fail the second time: it would already have an Image.)
                RectTransform button = UiKit.Fill(resetArea, "ResetButton");
                reset.Label = UiKit.Button(button, "RESET TO DEFAULTS", new Action(reset.Click), false);
                reset.Label.fontSize = 18;
            }
            var page = new ConfigPage(pageContent, PageWidth);
            page.Owner = item.Name;
            try
            {
                item.Build(page);
            }
            catch (Exception e)
            {
                // A broken page from some mod must not break the panel: show the error in its place instead.
                page.Label("This page failed to build: " + e.Message);
                MelonLogger.Warning($"[ModConfigs] Page '{item.Name}' failed: {e}");
            }
            page.Finish();
        }

        private sealed class Selector2
        {
            private readonly string name;
            public Selector2(string n) { name = n; }
            public bool Matches(Item i) { return i.Name == name; }
        }

        // RESET TO DEFAULTS asks once: the first click turns it into "CLICK AGAIN TO RESET" for 4 seconds, and only
        // a second click in that time really resets. Time.unscaledTime = seconds since the game started.
        private sealed class ResetButton
        {
            private readonly Action reset;
            private float armedAt = -100f;
            public TextMeshProUGUI Label;
            public ResetButton(Action r) { reset = r; }
            public void Click()
            {
                if (Time.unscaledTime - armedAt > 4f)
                {
                    armedAt = Time.unscaledTime;
                    Label.text = "CLICK AGAIN TO RESET";
                    Label.color = UiKit.Purple;
                    return;
                }
                try { reset(); }
                catch (Exception e) { MelonLogger.Warning("[ModConfigs] Reset failed: " + e); }
                ConfigPage.OpenRow = null;
                Later(false, false); // next frame; also puts the button back to "RESET TO DEFAULTS"
            }
        }

        // Removes all children of a UI element right away (DestroyImmediate), from the last to the first.
        private static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(rt.GetChild(i).gameObject);
        }

        // ================= automatic pages for MelonPreferences =================

        // Builds a page from one MelonPreferences category, choosing a row for each setting's type (see the top of
        // this file). Types it doesn't know (lists, custom classes) are shown as text. Every change is saved at once.
        private sealed class AutoPage
        {
            private readonly MelonPreferences_Category category;
            public AutoPage(MelonPreferences_Category c) { category = c; }

            public void Build(ConfigPage page)
            {
                foreach (MelonPreferences_Entry entry in category.Entries)
                {
                    if (entry.IsHidden)
                        continue;
                    string label = string.IsNullOrEmpty(entry.DisplayName) ? entry.Identifier : entry.DisplayName;
                    Type type = entry.GetReflectedType();
                    var bind = new EntryBinding(category, entry);
                    bool whole = type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
                    bool fraction = type == typeof(float) || type == typeof(double);
                    double min, max;
                    if (type == typeof(bool))
                        page.Toggle(label, new Func<bool>(bind.GetBool), new Action<bool>(bind.SetBool));
                    else if ((whole || fraction) && TryRange(entry, out min, out max))
                        page.Slider(label, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), min, max, whole ? "0" : "0.##", whole);
                    else if (whole)
                        page.Number(label, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), 1, -1e9, 1e9, "0");
                    else if (fraction)
                        page.Number(label, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), 0.1, -1e9, 1e9, "0.##");
                    else if (type == typeof(string))
                        page.TextBox(label, new Func<string>(bind.GetText), new Action<string>(bind.SetText));
                    else if (type.IsEnum)
                        page.Choice(label, Enum.GetNames(type), new Func<int>(bind.GetEnum), new Action<int>(bind.SetEnum));
                    else if (type == typeof(Color))
                        page.ColorPicker(label, new Func<Color>(bind.GetColor), new Action<Color>(bind.SetColor));
                    else
                        page.Label($"{label}: {entry.BoxedValue}   (can't be changed here yet)", 20);
                    if (!string.IsNullOrEmpty(entry.Description))
                        page.Label(entry.Description, 18);
                }
                page.Space(10);
                page.Button("Reload settings from file", new Action(Reload));
            }

            private void Reload()
            {
                MelonPreferences.Load();
                Later(false, false); // a button click: rebuild on the next frame
            }
        }

        // Did the mod give this number a min and max (a MelonLoader "ValueRange" validator)? Reflection = asking an
        // object at runtime which properties it has, so this works without knowing the validator's exact type.
        private static bool TryRange(MelonPreferences_Entry entry, out double min, out double max)
        {
            min = max = 0;
            try
            {
                PropertyInfo prop = entry.GetType().GetProperty("Validator");
                object validator = prop != null ? prop.GetValue(entry) : null;
                if (validator == null)
                    return false;
                PropertyInfo pMin = validator.GetType().GetProperty("MinValue");
                PropertyInfo pMax = validator.GetType().GetProperty("MaxValue");
                if (pMin == null || pMax == null)
                    return false;
                min = Convert.ToDouble(pMin.GetValue(validator));
                max = Convert.ToDouble(pMax.GetValue(validator));
                return max > min;
            }
            catch
            {
                return false;
            }
        }

        // Puts every setting of a category back to the value its mod gave as the default, and saves.
        private sealed class CategoryReset
        {
            private readonly MelonPreferences_Category category;
            public CategoryReset(MelonPreferences_Category c) { category = c; }
            public void Run()
            {
                foreach (MelonPreferences_Entry e in category.Entries)
                    e.ResetToDefault();
                category.SaveToFile(false);
            }
        }

        // Reads and writes one MelonPreferences entry. BoxedValue is the value as a plain "object";
        // Convert.ChangeType turns our number back into the entry's own type (int, float...).
        private sealed class EntryBinding
        {
            private readonly MelonPreferences_Category category;
            private readonly MelonPreferences_Entry entry;
            public EntryBinding(MelonPreferences_Category c, MelonPreferences_Entry e) { category = c; entry = e; }

            private void Save(object v)
            {
                entry.BoxedValue = v;
                category.SaveToFile(false); // false = don't print a "saved" line to the log every click
            }
            public bool GetBool() { return (bool)entry.BoxedValue; }
            public void SetBool(bool v) { Save(v); }
            public double GetNumber() { return Convert.ToDouble(entry.BoxedValue); }
            public void SetNumber(double v) { Save(Convert.ChangeType(v, entry.GetReflectedType())); }
            public string GetText() { return entry.BoxedValue as string ?? ""; }
            public void SetText(string v) { Save(v); }
            // Enums: the dropdown works with positions in Enum.GetNames; Enum.Parse turns a name back into the value.
            public int GetEnum() { return Array.IndexOf(Enum.GetNames(entry.GetReflectedType()), entry.BoxedValue.ToString()); }
            public void SetEnum(int i) { Type t = entry.GetReflectedType(); Save(Enum.Parse(t, Enum.GetNames(t)[i])); }
            public Color GetColor() { return (Color)entry.BoxedValue; }
            public void SetColor(Color c) { Save(c); }
        }

        // ================= pages for config files =================

        // Connects one group of config files (one mod) to the list: Build shows it, Reset puts the backups back.
        private sealed class FilePage
        {
            private readonly FileConfigs.Group group;
            public FilePage(FileConfigs.Group g) { group = g; }
            public void Build(ConfigPage page) { FileConfigs.BuildPage(group, page); }
            public void Reset() { FileConfigs.Restore(group); }
        }
    }
}
