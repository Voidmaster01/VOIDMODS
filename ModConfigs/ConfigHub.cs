// ConfigHub.cs - part of ModConfigs: the PUBLIC API that other mods use to add their own page to MOD CONFIGS.
//
// WHAT IT DOES
//   A mod calls ConfigHub.Register once (usually in OnInitializeMelon). Its page then shows up in the MOD CONFIGS
//   panel on the game's home screen. Mods that don't register still get an automatic page for their
//   MelonPreferences settings and for their own config files (see HubPanel.cs and FileConfigs.cs), so registering
//   is only needed for custom pages.
//
// HOW ANOTHER MOD USES IT
//   1. Reference ModConfigs.dll when building, and add this line so MelonLoader loads ModConfigs first and tells
//      players when it's missing:     [assembly: MelonAdditionalDependencies("ModConfigs")]
//   2. Register a page:               ConfigHub.Register("MyMod", "1.0.0", BuildMyPage);
//   3. Build the page with the helpers (each one adds a row under the previous one):
//        static void BuildMyPage(ConfigPage page)
//        {
//            page.Header("Settings");
//            page.Toggle("Fast mode", GetFast, SetFast);             // Func<bool> to read, Action<bool> to save
//            page.Number("Speed", GetSpeed, SetSpeed, 0.5, 0, 10);   // - and + buttons, or type a number
//            page.Slider("Volume", GetVol, SetVol, 0, 1);            // a bar you drag            (new in 1.1.0)
//            page.TextBox("Nickname", GetNick, SetNick);             // a box you type text in    (new in 1.1.0)
//            page.Choice("Mode", new[] { "Easy", "Hard" }, GetModeIndex, SetModeIndex);   // dropdown (new in 1.1.0)
//            page.ColorPicker("Name color", GetColor, SetColor);    // swatch + hex + sliders    (new in 1.1.0)
//            page.Button("Reset", ResetAll);
//            RectTransform area = page.Custom(200);                  // free space for your own UI (see UiKit)
//        }
//   4. Optional (1.1.0): ConfigHub.SetReset("MyMod", MyResetMethod) gives your page a RESET TO DEFAULTS button.
//      Without it, the button still works if your page covers a MelonPreferences category (it resets that).
//   The page is built fresh every time it is shown, so it always shows current values.
//   Call ConfigHub.Refresh() after changing something outside the page, to rebuild it.
//
// WHY "binding" CLASSES INSTEAD OF LAMBDAS
//   A lambda is a tiny nameless function, like  () => set(!get()) . When it uses variables from around it, the
//   compiler secretly makes a class to hold them. The bare compiler setup in build.ps1 can't do that here, so
//   this file writes those small classes by hand (ToggleBinding, NumberBinding...). Each one keeps what a row
//   needs, and its methods are what the buttons call.

using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    public static class ConfigHub
    {
        // Where the GET MODS button sends players when the Mod Menu mod isn't installed: its release on GitHub.
        public const string ModMenuReleaseUrl = "https://github.com/Voidmaster01/VOIDMODS/releases/tag/ModMenu";

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

        // Reset methods given with SetReset, by page name. StringComparer.OrdinalIgnoreCase makes "MyMod" and
        // "mymod" the same key.
        internal static readonly Dictionary<string, Action> Resets = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

        // What GET MODS does when another mod (the Mod Menu) has taken it over; null = open the release page.
        internal static Action GetModsHandler;

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

        // (1.1.0) Gives the page called "name" a RESET TO DEFAULTS button that runs "reset". null removes it again.
        public static void SetReset(string name, Action reset)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (reset == null)
                Resets.Remove(name);
            else
                Resets[name] = reset;
        }

        // (1.1.0) For the Mod Menu mod: makes the GET MODS button open "open" instead of the release page.
        public static void SetGetMods(Action open)
        {
            GetModsHandler = open;
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

        // A tiny helper object for RemoveAll: "is this entry's name the same as mine?" (see the lambda note above).
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

        // Dropdowns and color pickers fold open under their row. Only one is open at a time; this remembers which
        // (page name + row label), so it stays open while the page is rebuilt. "static" = shared by all pages.
        internal static string OpenRow;
        internal string Owner = ""; // the page's name, set by HubPanel

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

        // ---------- shared row parts ----------

        // A bordered row with the setting's name on the left. rightSpace = how much room the controls on the
        // right need, so a long name gets cut off before it runs under them.
        private RectTransform Row(string label, float rightSpace)
        {
            RectTransform row = Custom(RowHeight);
            UiKit.Box(row, UiKit.RowColor, true);
            RectTransform text = UiKit.Element(row, "Label", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(-rightSpace - 30, 0));
            TextMeshProUGUI t = UiKit.Text(text, label, 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);
            t.overflowMode = TextOverflowModes.Ellipsis; // too long -> ends with "..."
            return row;
        }

        // A control on the right side of a row. right = distance from the row's right edge to the control's right edge.
        private static RectTransform RightPart(RectTransform row, string name, float right, float width)
        {
            return UiKit.Element(row, name, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-right, 0), new Vector2(width, RowHeight - 14));
        }

        // ---------- the setting rows ----------

        // A setting with an ON/OFF switch on the right. get reads the current value, set saves a new one.
        public void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            RectTransform row = Row(label, 190);
            RectTransform sw = RightPart(row, "Switch", 10, 170);
            var binding = new ToggleBinding(get, set);
            binding.Label = UiKit.Button(sw, "", new Action(binding.Click), false);
            binding.Show();
        }

        // A number with - and + buttons. step = how much one click changes it; min/max = the allowed range.
        // Since 1.1.0 the number itself is a text box too: click it, type a value, press Enter.
        public void Number(string label, Func<double> get, Action<double> set, double step, double min, double max, string format = "0.##")
        {
            RectTransform row = Row(label, 300);
            var binding = new NumberBinding(get, set, step, min, max, format);
            UiKit.Button(RightPart(row, "Minus", 250, 60), "-", new Action(binding.Down), false);
            binding.Input = UiKit.InputField(RightPart(row, "Value", 80, 160), "", "", 24, new Action<string>(binding.Typed));
            binding.Input.textComponent.alignment = TextAlignmentOptions.Center;
            UiKit.Button(RightPart(row, "Plus", 10, 60), "+", new Action(binding.Up), false);
            binding.Show();
        }

        // (1.1.0) A number on a bar you drag, with the value as a text box next to it.
        // wholeNumbers = true only allows 1, 2, 3... (no 1.5).
        public void Slider(string label, Func<double> get, Action<double> set, double min, double max, string format = "0.##", bool wholeNumbers = false)
        {
            RectTransform row = Row(label, 540);
            var binding = new SliderBinding(get, set, min, max, format);
            binding.Bar = UiKit.Slider(RightPart(row, "Bar", 150, 380), (float)min, (float)max, (float)get(), wholeNumbers, new Action<float>(binding.Slid));
            binding.Input = UiKit.InputField(RightPart(row, "Value", 10, 130), "", "", 22, new Action<string>(binding.Typed));
            binding.Input.textComponent.alignment = TextAlignmentOptions.Center;
            binding.Show();
        }

        // (1.1.0) A text setting: a box you click and type in. The new text is saved when you press Enter or click
        // somewhere else.
        public TMP_InputField TextBox(string label, Func<string> get, Action<string> set)
        {
            RectTransform row = Row(label, 480);
            var binding = new TextBinding(get, set);
            binding.Input = UiKit.InputField(RightPart(row, "Input", 10, 460), get() ?? "", "type here", 22, new Action<string>(binding.Done));
            return binding.Input;
        }

        // (1.1.0) A dropdown: the row shows the chosen option; clicking it folds out the whole list below.
        // get/set work with the option's position in the list (0 = first).
        public void Choice(string label, string[] options, Func<int> get, Action<int> set)
        {
            if (options == null || options.Length == 0)
            {
                Label(label + ": (no options)", 20);
                return;
            }
            string key = Owner + "|" + label;
            bool open = OpenRow == key;
            RectTransform row = Row(label, 400);
            int current = get();
            string shown = current >= 0 && current < options.Length ? options[current] : "?";
            var toggle = new FoldBinding(key);
            UiKit.Button(RightPart(row, "Value", 10, 380), shown + (open ? "   ^" : "   v"), new Action(toggle.Click), false);
            if (!open)
                return;
            // The folded-out list: one button per option, a bit indented, the current one in purple.
            for (int i = 0; i < options.Length; i++)
            {
                RectTransform opt = UiKit.Element(Content, "Option", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(Width - 390, -cursor), new Vector2(380, 44));
                cursor += 48;
                var pick = new PickBinding(set, i);
                TextMeshProUGUI t = UiKit.Button(opt, options[i], new Action(pick.Click), false);
                t.fontSize = 20;
                if (i == current)
                    t.color = UiKit.Purple;
            }
            cursor += Gap;
        }

        // (1.1.0) A color setting: a swatch (a little square of the color), its hex code (#RRGGBB or #RRGGBBAA) as
        // a text box, and an EDIT button that folds out red / green / blue / alpha (see-through) sliders.
        public void ColorPicker(string label, Func<Color> get, Action<Color> set)
        {
            string key = Owner + "|" + label;
            bool open = OpenRow == key;
            RectTransform row = Row(label, 420);
            var binding = new ColorBinding(get, set);
            RectTransform swatch = RightPart(row, "Swatch", 330, 70);
            binding.Swatch = UiKit.Box(swatch, Color.white, true);
            binding.Hex = UiKit.InputField(RightPart(row, "Hex", 140, 180), "", "#RRGGBB", 22, new Action<string>(binding.Typed));
            var toggle = new FoldBinding(key);
            UiKit.Button(RightPart(row, "Edit", 10, 120), open ? "DONE" : "EDIT", new Action(toggle.Click), false);
            if (open)
            {
                string[] names = { "Red", "Green", "Blue", "Alpha" };
                binding.Bars = new UnityEngine.UI.Slider[4];
                for (int c = 0; c < 4; c++)
                {
                    RectTransform sub = Custom(44);
                    RectTransform name = UiKit.Element(sub, "Name", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(140, 0));
                    UiKit.Text(name, names[c], 20, UiKit.DimText, TextAlignmentOptions.MidlineLeft);
                    RectTransform bar = UiKit.Element(sub, "Bar", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(210, 0), new Vector2(-230, 36));
                    var channel = new ChannelBinding(binding, c);
                    binding.Bars[c] = UiKit.Slider(bar, 0, 255, 0, true, new Action<float>(channel.Slid));
                }
            }
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

        // Reads a number someone typed. Both "1.5" and "1,5" work (some countries write a comma), and
        // CultureInfo.InvariantCulture makes the reading the same on every PC. Returns false for "abc".
        internal static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // ---------- the binding objects (they do the job a lambda would do; see the note at the top) ----------

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
            public TMP_InputField Input;
            public NumberBinding(Func<double> g, Action<double> s, double st, double mn, double mx, string f)
            {
                get = g; set = s; step = st; min = mn; max = mx; format = f;
            }
            public void Up() { Change(get() + step); }
            public void Down() { Change(get() - step); }
            // Called when someone finished typing in the value box. Not a number -> just show the old value again.
            public void Typed(string text)
            {
                double v;
                if (TryParseNumber(text, out v))
                    Change(v);
                else
                    Show();
            }
            private void Change(double v)
            {
                v = Math.Round(v, 6);                  // Math.Round removes tiny errors like 0.30000000000000004
                set(Math.Max(min, Math.Min(max, v)));  // keep it inside min..max
                Show();
            }
            public void Show() { Input.text = get().ToString(format, CultureInfo.InvariantCulture); }
        }

        private sealed class SliderBinding
        {
            private readonly Func<double> get;
            private readonly Action<double> set;
            private readonly double min, max;
            private readonly string format;
            public UnityEngine.UI.Slider Bar;
            public TMP_InputField Input;
            public SliderBinding(Func<double> g, Action<double> s, double mn, double mx, string f) { get = g; set = s; min = mn; max = mx; format = f; }
            // The bar moved: save the new value and update the box (not the bar: it's already where the mouse put it).
            public void Slid(float v)
            {
                set(Math.Round(v, 6));
                Input.text = get().ToString(format, CultureInfo.InvariantCulture);
            }
            public void Typed(string text)
            {
                double v;
                if (TryParseNumber(text, out v))
                    set(Math.Max(min, Math.Min(max, Math.Round(v, 6))));
                Show();
            }
            // SetValueWithoutNotify moves the bar without calling Slid again (that would save twice).
            public void Show()
            {
                double v = get();
                Bar.SetValueWithoutNotify((float)v);
                Input.text = v.ToString(format, CultureInfo.InvariantCulture);
            }
        }

        private sealed class TextBinding
        {
            private readonly Func<string> get;
            private readonly Action<string> set;
            public TMP_InputField Input;
            public TextBinding(Func<string> g, Action<string> s) { get = g; set = s; }
            // Save, then show what was really saved (a mod may refuse or change the text).
            public void Done(string text)
            {
                set(text ?? "");
                Input.text = get() ?? "";
            }
        }

        // Folds a dropdown or color picker open or shut, then rebuilds the page where it is (same scroll spot).
        private sealed class FoldBinding
        {
            private readonly string key;
            public FoldBinding(string k) { key = k; }
            public void Click()
            {
                OpenRow = OpenRow == key ? null : key;
                HubPanel.RebuildKeepScroll();
            }
        }

        // One option in a folded-out dropdown: saves its number and folds the list shut.
        private sealed class PickBinding
        {
            private readonly Action<int> set;
            private readonly int index;
            public PickBinding(Action<int> s, int i) { set = s; index = i; }
            public void Click()
            {
                set(index);
                OpenRow = null;
                HubPanel.RebuildKeepScroll();
            }
        }

        internal sealed class ColorBinding
        {
            private readonly Func<Color> get;
            private readonly Action<Color> set;
            public Image Swatch;
            public TMP_InputField Hex;
            public UnityEngine.UI.Slider[] Bars; // null while the sliders are folded shut
            public ColorBinding(Func<Color> g, Action<Color> s) { get = g; set = s; }

            public void Typed(string text)
            {
                Color c;
                if (UiKit.TryParseHex(text, out c))
                    set(c);
                Show();
            }
            // One of the four sliders moved: change just that part of the color (0 = red ... 3 = alpha).
            public void SetChannel(int channel, float value255)
            {
                Color c = get();
                float v = value255 / 255f;
                if (channel == 0) c.r = v; else if (channel == 1) c.g = v; else if (channel == 2) c.b = v; else c.a = v;
                set(c);
                Show();
            }
            public void Show()
            {
                Color c = get();
                Swatch.color = new Color(c.r, c.g, c.b, 1f); // the swatch ignores alpha so you can always see it
                Hex.text = UiKit.ToHex(c);
                if (Bars != null)
                {
                    Bars[0].SetValueWithoutNotify(c.r * 255f);
                    Bars[1].SetValueWithoutNotify(c.g * 255f);
                    Bars[2].SetValueWithoutNotify(c.b * 255f);
                    Bars[3].SetValueWithoutNotify(c.a * 255f);
                }
            }
        }

        // Tells the ColorBinding which of its four sliders moved.
        private sealed class ChannelBinding
        {
            private readonly ColorBinding owner;
            private readonly int channel;
            public ChannelBinding(ColorBinding o, int c) { owner = o; channel = c; }
            public void Slid(float v) { owner.SetChannel(channel, v); }
        }
    }
}
