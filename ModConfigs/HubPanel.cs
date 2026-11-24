// HubPanel.cs - part of ModConfigs: the MOD CONFIGS panel that opens from the home screen menu.
//
// WHAT IT DOES
//   Shows a list of mods on the left and the chosen mod's settings page on the right:
//     - mods that called ConfigHub.Register get their own page (VOIDMODS show the VOIDMODS logo),
//     - every other mod with MelonPreferences settings gets an automatic page: on/off settings become switches,
//       numbers get - and + buttons, and each change is saved to UserData\MelonPreferences.cfg right away.
//   Pages scroll with the mouse wheel when they are taller than the panel.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into ModConfigs.dll. MenuRow.cs opens it (HubPanel.Open). It lives on its own Canvas in the home
//   screen scene, so leaving the home screen removes it automatically.

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class HubPanel
    {
        // One line in the mod list: a name, a version, the logo flag and the method that builds its page.
        private sealed class Item
        {
            public string Name, Version;
            public bool VoidMod;
            public Action<ConfigPage> Build;
        }

        private static GameObject root;
        private static RectTransform listContent, pageContent;
        private static TextMeshProUGUI pageTitle;
        private static List<Item> items = new List<Item>();
        private static string selected; // the name of the open page, kept while the panel is closed

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

        private static void Close()
        {
            if (root != null)
                root.SetActive(false);
        }

        // ================= building the frame =================

        // The parts that never change: dark background, the panel box, title, BACK button, list and page areas.
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

            RectTransform title = UiKit.Element(panel, "Title", TopLeft, TopLeft, TopLeft, new Vector2(40, -24), new Vector2(700, 64));
            UiKit.Text(title, "MOD CONFIGS", 44, UiKit.Purple, TextAlignmentOptions.MidlineLeft);
            RectTransform back = UiKit.Element(panel, "Back", TopRight, TopRight, TopRight, new Vector2(-40, -26), new Vector2(200, 60));
            UiKit.Button(back, "BACK", new Action(Close), false);

            listContent = MakeScroll(panel, new Vector2(40, -110), new Vector2(380, 730));

            RectTransform titleBox = UiKit.Element(panel, "PageTitle", TopLeft, TopLeft, TopLeft, new Vector2(470, -110), new Vector2(PageWidth, 50));
            pageTitle = UiKit.Text(titleBox, "", 30, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
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

        // Registered pages first (sorted by name), then automatic pages for the remaining MelonPreferences categories.
        private static void CollectItems()
        {
            items = new List<Item>();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registered = new List<ConfigHub.Entry>(ConfigHub.Entries);
            registered.Sort(new Comparison<ConfigHub.Entry>(ByName));
            foreach (ConfigHub.Entry e in registered)
            {
                items.Add(new Item { Name = e.Name, Version = e.Version, VoidMod = e.VoidMod, Build = e.Build });
                covered.Add(e.PrefsCategory);
            }
            foreach (MelonPreferences_Category category in MelonPreferences.Categories)
            {
                if (category.IsHidden || covered.Contains(category.Identifier) || !HasVisibleEntries(category))
                    continue;
                var auto = new AutoPage(category);
                items.Add(new Item { Name = category.DisplayName ?? category.Identifier, Version = "settings", Build = new Action<ConfigPage>(auto.Build) });
            }
        }

        private static int ByName(ConfigHub.Entry a, ConfigHub.Entry b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasVisibleEntries(MelonPreferences_Category category)
        {
            foreach (MelonPreferences_Entry e in category.Entries)
                if (!e.IsHidden)
                    return true;
            return false;
        }

        // Rebuilds the list buttons. The open page gets a purple bar on its left edge.
        private static void ShowList()
        {
            Clear(listContent);
            float y = 0;
            foreach (Item item in items)
            {
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
                ShowList();
                RebuildCurrent();
            }
        }

        // ================= the page =================

        // Empties the page area and lets the selected mod build its page again (so values are always current).
        public static void RebuildCurrent()
        {
            if (!IsOpen)
                return;
            Clear(pageContent);
            pageContent.anchoredPosition = Vector2.zero; // scroll back to the top
            Item item = items.Find(new Predicate<Item>(new Selector2(selected).Matches));
            if (item == null)
            {
                pageTitle.text = items.Count == 0 ? "No mods with settings found." : "";
                return;
            }
            pageTitle.text = item.Name + "  <size=20><color=#8c859e>" + item.Version + "</color></size>";
            var page = new ConfigPage(pageContent, PageWidth);
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

        // Removes all children of a UI element right away (DestroyImmediate), from the last to the first.
        private static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(rt.GetChild(i).gameObject);
        }

        // ================= automatic pages for MelonPreferences =================

        // Builds a page from one MelonPreferences category: bool -> switch, numbers -> -/+, anything else is shown
        // as text (those types can't be edited here yet). Every change is saved to the file immediately.
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
                    if (type == typeof(bool))
                        page.Toggle(label, new Func<bool>(bind.GetBool), new Action<bool>(bind.SetBool));
                    else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                        page.Number(label, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), 1, -1e9, 1e9, "0");
                    else if (type == typeof(float) || type == typeof(double))
                        page.Number(label, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), 0.1, -1e9, 1e9, "0.##");
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
                RebuildCurrent();
            }
        }

        // Reads and writes one MelonPreferences entry. BoxedValue is the value as a plain "object";
        // Convert.ChangeType turns our number back into the entry's own type (int, float...).
        private sealed class EntryBinding
        {
            private readonly MelonPreferences_Category category;
            private readonly MelonPreferences_Entry entry;
            public EntryBinding(MelonPreferences_Category c, MelonPreferences_Entry e) { category = c; entry = e; }

            public bool GetBool() { return (bool)entry.BoxedValue; }
            public void SetBool(bool v) { entry.BoxedValue = v; category.SaveToFile(false); }
            public double GetNumber() { return Convert.ToDouble(entry.BoxedValue); }
            public void SetNumber(double v)
            {
                entry.BoxedValue = Convert.ChangeType(v, entry.GetReflectedType());
                category.SaveToFile(false); // false = don't print a "saved" line to the log every click
            }
        }
    }
}
