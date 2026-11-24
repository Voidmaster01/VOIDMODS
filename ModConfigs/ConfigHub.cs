// ConfigHub.cs - part of ModConfigs: the PUBLIC API that other mods use to add their own page to MOD CONFIGS.
//
// WHAT IT DOES
//   A mod calls ConfigHub.Register once (usually in OnInitializeMelon). Its page then shows up in the MOD CONFIGS
//   panel on the game's home screen. Mods that don't register still get an automatic page for their
//   MelonPreferences settings (see HubPanel.cs), so registering is only needed for custom pages.
//
// HOW ANOTHER MOD USES IT
//   1. Reference ModConfigs.dll when building, and add this line so MelonLoader loads ModConfigs first and tells
//      players when it's missing:     [assembly: MelonAdditionalDependencies("ModConfigs")]
//   2. Register a page:               ConfigHub.Register("MyMod", "1.0.0", BuildMyPage);
//   3. Build the page with the helpers (each one adds a row under the previous one):
//        static void BuildMyPage(ConfigPage page)
//        {
//            page.Header("Settings");
//            page.Toggle("Fast mode", GetFast, SetFast);       // Func<bool> to read, Action<bool> to save
//            page.Number("Speed", GetSpeed, SetSpeed, 0.5, 0, 10);
//            page.Button("Reset", ResetAll);
//            RectTransform area = page.Custom(200);            // free space for your own UI (see UiKit)
//        }
//   The page is built fresh every time it is shown, so it always shows current values.
//   Call ConfigHub.Refresh() after changing something outside the page, to rebuild it.

using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;

namespace ModConfigs
{
    public static class ConfigHub
    {
        // One registered page. "internal" = only ModConfigs itself can see these fields.
        internal sealed class Entry
        {
            public string Name;
            public string Version;
            public Action<ConfigPage> Build;
            public bool VoidMod;        // shows the VOIDMODS logo next to the name
            public string PrefsCategory; // a MelonPreferences category this page already covers (hidden from the automatic pages)
        }
        internal static readonly List<Entry> Entries = new List<Entry>();

        // Adds a page. name = what the list shows; version = shown under it; build = the method that fills the page.
        // voidMod = true marks one of Voidmaster01's official VOIDMODS (logo next to it). The logo is not under the
        // MIT license (see LICENSE), so other mods leave this false. prefsCategory = the identifier of a
        // MelonPreferences category this page already shows, so it doesn't appear a second time as an automatic page.
        public static void Register(string name, string version, Action<ConfigPage> build, bool voidMod = false, string prefsCategory = null)
        {
            if (string.IsNullOrEmpty(name) || build == null)
                throw new ArgumentException("ConfigHub.Register needs a name and a build method");
            Entries.RemoveAll(new Predicate<Entry>(new SameName(name).Matches)); // registering again replaces the old page
            Entries.Add(new Entry { Name = name, Version = version ?? "", Build = build, VoidMod = voidMod, PrefsCategory = prefsCategory ?? name });
        }

        // Rebuilds the page that is open right now (does nothing when the panel is closed).
        public static void Refresh()
        {
            HubPanel.RebuildCurrent();
        }

        // True while the MOD CONFIGS panel is open.
        public static bool IsOpen
        {
            get { return HubPanel.IsOpen; }
        }

        // A tiny helper object for RemoveAll: "is this entry's name the same as mine?" (a named method instead of
        // a lambda, because the bare compiler setup used by build.ps1 can't emit lambdas that capture variables).
        private sealed class SameName
        {
            private readonly string name;
            public SameName(string n) { name = n; }
            public bool Matches(Entry e) { return string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase); }
        }
    }

    // ConfigPage: what a mod's build method gets. It stacks rows from the top down, in the VOIDMODS style.
    // "cursor" is how far down the next row goes; Finish() sets the scrollable height to fit everything.
    public sealed class ConfigPage
    {
        public const float RowHeight = 56f;
        public const float Gap = 10f;

        // The scrolling area the rows go into. Its top-left corner is (0, 0); y grows DOWNWARD as negative numbers.
        public RectTransform Content { get; private set; }
        public float Width { get; private set; }
        private float cursor;

        internal ConfigPage(RectTransform content, float width)
        {
            Content = content;
            Width = width;
        }

        // Reserves a strip of the given height and returns it as an empty element the caller can fill.
        public RectTransform Custom(float height)
        {
            RectTransform rt = UiKit.Element(Content, "Custom", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, -cursor), new Vector2(Width, height));
            cursor += height + Gap;
            return rt;
        }

        public void Space(float height)
        {
            cursor += height;
        }

        // A purple heading, like the game's "USERNAME" label.
        public TextMeshProUGUI Header(string text)
        {
            RectTransform rt = Custom(44);
            return UiKit.Text(rt, text.ToUpperInvariant(), 30, UiKit.Purple, TextAlignmentOptions.MidlineLeft);
        }

        // Plain text; the height grows with the number of lines (roughly), so long texts don't overlap.
        public TextMeshProUGUI Label(string text, float fontSize = 22)
        {
            int lines = 1 + (text ?? "").Length / 70 + CountLines(text);
            RectTransform rt = Custom(Math.Max(30, lines * fontSize * 1.3f));
            return UiKit.Text(rt, text ?? "", fontSize, UiKit.TextColor, TextAlignmentOptions.TopLeft);
        }

        // A full-width button in the style of the game's menu rows. Returns its label so the text can change later.
        public TextMeshProUGUI Button(string text, Action onClick)
        {
            RectTransform rt = Custom(RowHeight);
            return UiKit.Button(rt, text, onClick, true);
        }

        // A setting with an ON/OFF switch on the right. get reads the current value, set saves a new one.
        public void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            RectTransform row = Custom(RowHeight);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform text = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-220, 0));
            UiKit.Text(text, label, 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            RectTransform sw = UiKit.Element(row, "Switch", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(170, RowHeight - 14));
            var binding = new ToggleBinding(get, set);
            binding.Label = UiKit.Button(sw, "", new Action(binding.Click), false);
            binding.Show();
        }

        // A number with - and + buttons. step = how much one click changes it; min/max = the allowed range.
        public void Number(string label, Func<double> get, Action<double> set, double step, double min, double max, string format = "0.##")
        {
            RectTransform row = Custom(RowHeight);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform text = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-330, 0));
            UiKit.Text(text, label, 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            var binding = new NumberBinding(get, set, step, min, max, format);
            RectTransform minus = UiKit.Element(row, "Minus", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, 0), new Vector2(60, RowHeight - 14));
            UiKit.Button(minus, "-", new Action(binding.Down), false);
            RectTransform value = UiKit.Element(row, "Value", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(160, RowHeight - 14));
            binding.Label = UiKit.Text(value, "", 24, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform plus = UiKit.Element(row, "Plus", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(60, RowHeight - 14));
            UiKit.Button(plus, "+", new Action(binding.Up), false);
            binding.Show();
        }

        // Called by the hub after the build method: makes the scroll area exactly as tall as the rows.
        internal void Finish()
        {
            Content.sizeDelta = new Vector2(Content.sizeDelta.x, cursor);
        }

        private static int CountLines(string text)
        {
            int n = 0;
            if (text != null)
                foreach (char c in text)
                    if (c == '\n')
                        n++;
            return n;
        }

        // The objects below remember what a switch or number row needs, so its button methods can use it later.
        // (They do the job a lambda would do; see the note in ConfigHub.SameName.)
        private sealed class ToggleBinding
        {
            private readonly Func<bool> get;
            private readonly Action<bool> set;
            public TextMeshProUGUI Label;
            public ToggleBinding(Func<bool> g, Action<bool> s) { get = g; set = s; }
            public void Click() { set(!get()); Show(); }
            public void Show()
            {
                bool on = get();
                Label.text = on ? "ON" : "OFF";
                Label.color = on ? UiKit.Purple : UiKit.DimText;
            }
        }

        private sealed class NumberBinding
        {
            private readonly Func<double> get;
            private readonly Action<double> set;
            private readonly double step, min, max;
            private readonly string format;
            public TextMeshProUGUI Label;
            public NumberBinding(Func<double> g, Action<double> s, double st, double mn, double mx, string f)
            {
                get = g; set = s; step = st; min = mn; max = mx; format = f;
            }
            public void Up() { Change(step); }
            public void Down() { Change(-step); }
            private void Change(double by)
            {
                double v = Math.Round(get() + by, 6); // Math.Round removes tiny errors like 0.30000000000000004
                set(Math.Max(min, Math.Min(max, v)));  // keep it inside min..max
                Show();
            }
            public void Show() { Label.text = get().ToString(format); }
        }
    }
}
