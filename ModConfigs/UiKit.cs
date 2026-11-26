// UiKit.cs - part of ModConfigs: the shared VOIDMODS look, as small helpers any mod can use.
//
// WHAT IT DOES
//   Makes UI pieces that match FNAF Online's own menus: the game's pixel font, near-black purple boxes with a
//   purple border, white text and purple accents. Also turns pictures (PNG bytes or raw pixels) into Unity sprites,
//   and gives the VOIDMODS logo.
//
// HOW TO USE IT (from another mod)
//   RectTransform area = page.Custom(300);                          // a strip on your ConfigPage
//   UiKit.Box(area, UiKit.RowColor, true);                          // fill it with a bordered box
//   UiKit.Text(area, "Hello", 24, UiKit.TextColor, TextAlignmentOptions.Center);
//   RectTransform b = UiKit.Element(area, "Btn", ...);  UiKit.Button(b, "Click me", new Action(MyMethod), true);
//   (1.1.0) UiKit.InputField(...) makes a text box, UiKit.Slider(...) a slider, and TryParseHex/ToHex turn
//   colors into "#RRGGBB" text and back.
//
// UNITY UI IN SHORT
//   Every UI piece is a GameObject with a RectTransform. anchorMin/anchorMax say which part of the parent it is
//   attached to (0,0 = bottom-left, 1,1 = top-right; min != max means it stretches), pivot is its own "handle"
//   point, anchoredPosition moves it from the anchor, and sizeDelta is its size (or, when stretching, how much
//   bigger or smaller than the stretched area it is).

using System;
using System.IO;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    public static class UiKit
    {
        // ---- The VOIDMODS colors, taken from the game's menu (red, green, blue, alpha; each 0..1) ----
        public static readonly Color Background = new Color(0.035f, 0.02f, 0.06f, 0.98f);
        public static readonly Color RowColor = new Color(0.06f, 0.035f, 0.1f, 1f);
        public static readonly Color RowHover = new Color(0.13f, 0.07f, 0.21f, 1f);
        public static readonly Color Border = new Color(0.17f, 0.11f, 0.27f, 1f);
        public static readonly Color Purple = new Color(0.61f, 0.36f, 0.9f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f, 1f);
        public static readonly Color DimText = new Color(0.55f, 0.52f, 0.62f, 1f);

        // The game's pixel font. MenuRow sets it from the game's own QUIT button; null until the home screen loaded.
        public static TMP_FontAsset Font;

        // ---- Building blocks ----

        // An empty UI element inside "parent", positioned as described in the header above.
        public static RectTransform Element(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false); // false = keep our numbers relative to the new parent
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // An element that fills its parent completely (anchors 0..1 on both axes, no extra size).
        public static RectTransform Fill(Transform parent, string name)
        {
            return Element(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        public const float BorderWidth = 3f;

        // Fills "target" with a colored box. border = true adds the purple-grey frame the game's rows have:
        // "target" itself is filled with the border color, and a child "Fill" element, 3 units smaller on every
        // side, holds the real color. Everything stays inside the target's own rectangle, so scroll areas can't
        // cut the border off (an Outline effect would draw outside it and lose its top and left edges there).
        // Returns the Image holding the real color (the fill), e.g. for a Button to tint on hover.
        public static Image Box(Transform target, Color color, bool border)
        {
            Image img = target.gameObject.AddComponent<Image>();
            if (!border)
            {
                img.color = color;
                return img;
            }
            img.color = Border;
            RectTransform fill = Fill(target, "Fill");
            fill.sizeDelta = new Vector2(-2 * BorderWidth, -2 * BorderWidth); // stretched, minus the border on each side
            Image inner = fill.gameObject.AddComponent<Image>();
            inner.color = color;
            inner.raycastTarget = false; // clicks land on the outer image (the whole box)
            return inner;
        }

        // Text that fills "target", in the game's font. raycastTarget = false: text never blocks clicks.
        public static TextMeshProUGUI Text(Transform target, string text, float size, Color color, TextAlignmentOptions align)
        {
            RectTransform rt = Fill(target, "Text");
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null)
                t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        // Makes "target" a clickable button in the game's row style: dark box, border, lighter on hover.
        // arrow = true adds the purple ">" on the right, like the game's menu rows. Returns the label.
        // The colors live in the Button's ColorBlock (normal / hover / pressed), applied over the white fill Image,
        // so hovering changes the fill while the border stays the same.
        public static TextMeshProUGUI Button(Transform target, string text, Action onClick, bool arrow)
        {
            Image img = Box(target, Color.white, true);
            Button button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            ColorBlock colors = button.colors; // a struct: copy it, change the copy, then put it back
            colors.normalColor = RowColor;
            colors.highlightedColor = RowHover;
            colors.selectedColor = RowColor;
            colors.pressedColor = Border;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(onClick); // System.Action turns into Unity's UnityAction by itself
            TextMeshProUGUI label = Text(target, text, 24, TextColor, arrow ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            if (arrow)
            {
                label.margin = new Vector4(24, 0, 60, 0); // left, top, right, bottom padding inside the text box
                TextMeshProUGUI chevron = Text(target, ">", 30, Purple, TextAlignmentOptions.MidlineRight);
                chevron.margin = new Vector4(0, 0, 24, 0);
            }
            return label;
        }

        // An Image showing a sprite, filling "target", keeping the picture's shape.
        public static Image Picture(Transform target, Sprite sprite)
        {
            RectTransform rt = Fill(target, "Picture");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = sprite != null ? Color.white : new Color(1, 1, 1, 0); // invisible when there's no sprite
            return img;
        }

        // ---- Copying one of the game's own buttons (new in 1.1.0) ----

        // The component types that only affect looks. Everything else on a copy (game scripts, click handlers,
        // event triggers) is removed. A HashSet answers "is this name in the set?" quickly.
        private static readonly System.Collections.Generic.HashSet<string> KeepTypes = new System.Collections.Generic.HashSet<string>
        {
            "Transform", "RectTransform", "CanvasRenderer", "Image", "RawImage", "Button", "TextMeshProUGUI",
            "LayoutElement", "Outline", "Shadow", "Mask", "RectMask2D", "Animator", "CanvasGroup",
            "HorizontalLayoutGroup", "VerticalLayoutGroup", "ContentSizeFitter",
        };

        // Removes every component that is not pure looks (see KeepTypes) from "go" and its children.
        // DestroyImmediate removes it right now, so nothing of it runs even once. GetIl2CppType().Name is the
        // game-side class name of a component.
        public static void StripToLooks(GameObject go)
        {
            foreach (Component c in go.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
            {
                if (c == null)
                    continue;
                if (!KeepTypes.Contains(c.GetIl2CppType().Name))
                    UnityEngine.Object.DestroyImmediate(c);
            }
        }

        // Makes a copy of one of the game's buttons that LOOKS the same but only does what onClick says.
        // Instantiate copies the whole object with its children; StripToLooks removes the game's scripts; a new, empty
        // ButtonClickedEvent drops the click targets set up in Unity's editor (those are copied too and would still run).
        public static GameObject CopyLooks(GameObject original, Transform parent, string name, Action onClick)
        {
            GameObject copy = UnityEngine.Object.Instantiate(original, parent).TryCast<GameObject>();
            copy.name = name;
            StripToLooks(copy);
            Button button = copy.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                if (onClick != null)
                    button.onClick.AddListener(onClick);
            }
            return copy;
        }

        // ---- Text boxes and sliders (new in 1.1.0) ----

        // Makes "target" a text box in the row style: click it, type, press Enter (or click elsewhere) and
        // onDone gets the text. placeholder = grey hint shown while the box is empty.
        // A TMP_InputField needs a few parts: a "viewport" that cuts off text wider than the box (RectMask2D),
        // the text it edits (textComponent) and the hint (placeholder). Unity starts a component the moment it is
        // added, before those parts are set, so the object is switched off (SetActive(false)) while it is put
        // together and switched back on at the end.
        public static TMP_InputField InputField(Transform target, string value, string placeholder, float fontSize, Action<string> onDone)
        {
            GameObject go = target.gameObject;
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            Image img = Box(target, Color.white, true);
            RectTransform area = Fill(target, "TextArea");
            area.sizeDelta = new Vector2(-28, -8); // 14 units of padding left and right, 4 top and bottom
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI hint = Text(area, placeholder ?? "", fontSize, DimText, TextAlignmentOptions.MidlineLeft);
            TextMeshProUGUI text = Text(area, "", fontSize, TextColor, TextAlignmentOptions.MidlineLeft);
            TMP_InputField input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            ColorBlock colors = input.colors; // same hover colors as the buttons
            colors.normalColor = RowColor;
            colors.highlightedColor = RowHover;
            colors.selectedColor = RowHover;
            colors.pressedColor = Border;
            colors.colorMultiplier = 1f;
            input.colors = colors;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = hint;
            if (Font != null)
                input.fontAsset = Font;
            input.pointSize = fontSize;
            input.richText = false;          // typed "<b>" stays text instead of turning bold
            input.customCaretColor = true;   // the blinking cursor in VOIDMODS purple
            input.caretColor = Purple;
            input.selectionColor = new Color(Purple.r, Purple.g, Purple.b, 0.35f);
            input.text = value ?? "";        // set BEFORE the listener, so filling the box doesn't count as typing
            if (onDone != null)
                input.onEndEdit.AddListener(onDone);
            go.SetActive(wasActive);
            return input;
        }

        // Makes "target" a slider (a bar with a handle you drag). onChange gets each new value while dragging.
        // Unity's Slider moves two things for us: the purple "fill" grows from the left up to the value, and the
        // white "handle" sits on the value. We only have to make them and tell the Slider which is which.
        public static Slider Slider(Transform target, float min, float max, float value, bool wholeNumbers, Action<float> onChange)
        {
            Box(target, new Color(0, 0, 0, 0.001f), false); // invisible, but catches clicks anywhere on the bar
            Vector2 midLeft = new Vector2(0, 0.5f), midRight = new Vector2(1, 0.5f), center = new Vector2(0.5f, 0.5f);
            RectTransform track = Element(target, "Track", midLeft, midRight, center, Vector2.zero, new Vector2(-20, 10));
            Box(track, Border, false);
            RectTransform fillArea = Element(target, "FillArea", midLeft, midRight, center, Vector2.zero, new Vector2(-20, 10));
            RectTransform fill = Element(fillArea, "Fill", Vector2.zero, new Vector2(0, 1), midLeft, Vector2.zero, Vector2.zero);
            Box(fill, Purple, false).raycastTarget = false;
            RectTransform handleArea = Element(target, "HandleArea", Vector2.zero, Vector2.one, center, Vector2.zero, new Vector2(-20, 0));
            RectTransform handle = Element(handleArea, "Handle", Vector2.zero, new Vector2(0, 1), center, Vector2.zero, new Vector2(16, -6));
            Image knob = Box(handle, TextColor, false);
            Slider slider = target.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.SetValueWithoutNotify(value);
            ColorBlock colors = slider.colors;
            colors.normalColor = TextColor;
            colors.highlightedColor = Purple;
            colors.pressedColor = Purple;
            colors.selectedColor = TextColor;
            slider.colors = colors;
            if (onChange != null)
                slider.onValueChanged.AddListener(onChange);
            return slider;
        }

        // ---- Colors as text ----

        // Reads "#RRGGBB" or "#RRGGBBAA" (the # is optional): two hex digits (00..FF) per part.
        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;
            string s = (text ?? "").Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8)
                return false;
            var parts = new float[4] { 1, 1, 1, 1 };
            for (int i = 0; i < s.Length / 2; i++)
            {
                int v;
                // NumberStyles.HexNumber reads "FF" as 255.
                if (!int.TryParse(s.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out v))
                    return false;
                parts[i] = v / 255f;
            }
            color = new Color(parts[0], parts[1], parts[2], parts[3]);
            return true;
        }

        // The other way: a color -> "#RRGGBB", plus "AA" when it is see-through. "X2" = two hex digits.
        public static string ToHex(Color c)
        {
            string hex = "#" + Part(c.r) + Part(c.g) + Part(c.b);
            return c.a < 0.999f ? hex + Part(c.a) : hex;
        }

        private static string Part(float v)
        {
            return ((int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f)).ToString("X2");
        }

        // ---- Pictures ----

        // Raw RGBA pixels (4 bytes per pixel, TOP row first) -> a Unity sprite. Unity textures start at the BOTTOM
        // row, so the rows are flipped first. HideFlags.DontUnloadUnusedAsset keeps Unity from freeing it while used.
        public static Sprite SpriteFromRgba(byte[] rgba, int width, int height)
        {
            var flipped = new byte[rgba.Length];
            int row = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(rgba, y * row, flipped, (height - 1 - y) * row, row);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // PNG bytes -> sprite (or null if the file can't be read or is bigger than maxSize on a side).
        public static Sprite SpriteFromPng(byte[] png, int maxSize, out string error)
        {
            byte[] rgba = PngReader.Decode(png, maxSize, out int w, out int h, out error);
            return rgba == null ? null : SpriteFromRgba(rgba, w, h);
        }

        // Frees a sprite and its texture (UnityEngine.Object.Destroy removes them at the end of the frame).
        public static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
                return;
            if (sprite.texture != null)
                UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }

        // The VOIDMODS logo, embedded into ModConfigs.dll by build.ps1 (branding\voidmod.png). Made once, then reused.
        private static Sprite logo;
        private static bool logoTried;
        public static Sprite VoidModLogo
        {
            get
            {
                if (logoTried)
                    return logo;
                logoTried = true;
                // GetManifestResourceStream opens a file that was embedded into this DLL when it was built.
                using (Stream s = typeof(UiKit).Assembly.GetManifestResourceStream("voidmod.png"))
                {
                    if (s == null)
                        return null;
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    logo = SpriteFromPng(bytes, 1024, out _);
                }
                return logo;
            }
        }
    }
}
