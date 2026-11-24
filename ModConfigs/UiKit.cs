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

        // Fills "target" with a colored box. border = true adds the purple-grey frame the game's rows have
        // (an Outline effect draws the box's shape again, shifted, in the border color).
        public static Image Box(Transform target, Color color, bool border)
        {
            Image img = target.gameObject.AddComponent<Image>();
            img.color = color;
            if (border)
            {
                Outline outline = target.gameObject.AddComponent<Outline>();
                outline.effectColor = Border;
                outline.effectDistance = new Vector2(3, -3);
            }
            return img;
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
        // The colors live in the Button's ColorBlock (normal / hover / pressed), applied over a white Image.
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
