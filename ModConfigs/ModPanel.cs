// ModPanel.cs - part of ModConfigs (new in 1.1.0): a ready-made panel any mod can open, in the VOIDMODS look.
//
// WHAT IT DOES
//   A full-screen dimmed panel with a big purple title, a BACK button and one scrolling page that a mod fills with the
//   same ConfigPage rows as MOD CONFIGS (Header, Toggle, Button, TextBox...). The Mod Menu's MODS panel uses it.
//   Only one VOIDMODS panel is open at a time: opening this one closes MOD CONFIGS, and the other way round.
//
// HOW ANOTHER MOD USES IT
//   ModPanel.Open("MODS", BuildMyPanel);     // BuildMyPanel(ConfigPage page) adds the rows
//   ModPanel.Refresh();                       // rebuilds it (on the next frame, so it is safe inside a click)
//   ModPanel.Close();
//   Like MOD CONFIGS, it lives on the home screen and disappears when the home screen is left.

using System;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    public static class ModPanel
    {
        private static GameObject root;
        private static RectTransform content;
        private static TextMeshProUGUI titleText;
        private static Action<ConfigPage> build;
        private static string title;
        private static bool rebuildLater;

        private const float PageWidth = 1180f;

        public static bool IsOpen
        {
            get { return root != null && root.activeSelf; }
        }

        // Opens the panel (building it the first time) and fills the page.
        public static void Open(string panelTitle, Action<ConfigPage> buildPage)
        {
            try
            {
                HubPanel.CloseIfOpen(); // one panel at a time
                title = panelTitle ?? "";
                build = buildPage;
                if (root == null)
                    Build();
                root.SetActive(true);
                titleText.text = title.ToUpperInvariant();
                Rebuild();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] ModPanel failed to open: " + e);
            }
        }

        public static void Close()
        {
            ConfigPage.OpenRow = null;
            if (root != null)
                root.SetActive(false);
        }

        // Asks for a rebuild on the next frame (see HubPanel.Later for why not right away).
        public static void Refresh()
        {
            rebuildLater = true;
        }

        // Called every frame by ModConfigsMod.
        internal static void Tick()
        {
            if (!rebuildLater)
                return;
            rebuildLater = false;
            if (IsOpen)
                Rebuild(true);
        }

        // ---- building ----

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopLeft = new Vector2(0, 1);
        private static readonly Vector2 TopRight = new Vector2(1, 1);

        // The parts that never change: its own Canvas on top of everything, the dimming layer, the box, title, BACK and
        // the scrolling page (the same pieces HubPanel uses; see the comments there).
        private static void Build()
        {
            root = new GameObject("VoidmodsModPanel");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 501;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            RectTransform dim = UiKit.Fill(root.transform, "Dim");
            UiKit.Box(dim, new Color(0, 0, 0, 0.7f), false);
            RectTransform panel = UiKit.Element(dim, "Panel", Center, Center, Center, Vector2.zero, new Vector2(1280, 880));
            UiKit.Box(panel, UiKit.Background, true);

            RectTransform t = UiKit.Element(panel, "Title", TopLeft, TopLeft, TopLeft, new Vector2(40, -24), new Vector2(800, 64));
            titleText = UiKit.Text(t, "", 44, UiKit.Purple, TextAlignmentOptions.MidlineLeft);
            RectTransform back = UiKit.Element(panel, "Back", TopRight, TopRight, TopRight, new Vector2(-40, -26), new Vector2(200, 60));
            UiKit.Button(back, "BACK", new Action(Close), false);

            RectTransform viewport = UiKit.Element(panel, "Viewport", TopLeft, TopLeft, TopLeft, new Vector2(50, -110), new Vector2(PageWidth + 10, 740));
            viewport.gameObject.AddComponent<RectMask2D>();
            UiKit.Box(viewport, new Color(0, 0, 0, 0.001f), false);
            content = UiKit.Element(viewport, "Content", TopLeft, TopRight, TopLeft, Vector2.zero, Vector2.zero);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;
        }

        private static void Rebuild(bool keepScroll = false)
        {
            Vector2 scrollPos = content.anchoredPosition;
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
            content.anchoredPosition = keepScroll ? scrollPos : Vector2.zero;
            var page = new ConfigPage(content, PageWidth);
            page.Owner = "ModPanel:" + title;
            try
            {
                if (build != null)
                    build(page);
            }
            catch (Exception e)
            {
                page.Label("This panel failed to build: " + e.Message);
                MelonLogger.Warning("[ModConfigs] ModPanel '" + title + "' failed: " + e);
            }
            page.Finish();
        }
    }
}
