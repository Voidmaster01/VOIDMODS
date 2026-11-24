// VoidconsPage.cs - part of the Voidcons mod: the Voidcons page inside MOD CONFIGS (the ModConfigs hub).
//
// WHAT IT DOES
//   Main view: your current icon, Choose picture..., Remove icon, the BackerOverride switch, Forget remembered
//   backers, and Reload Voidcons.
//   Move-and-crop view (after choosing a picture): the picture with a round frame, like the circle crop tool Brian
//   uses for the game's backer icons. Drag the picture to move it, use the mouse wheel or the Zoom buttons to zoom,
//   and watch the live result on the right. "Use this" makes a round 256 x 256 (HD) icon.png; "Cancel" goes back
//   without changing anything.
//
// HOW TO RUN / WHAT IT NEEDS
//   Compiled into Voidcons.dll. Voidcons.cs registers Build with ConfigHub; the hub calls it every time the page
//   is shown, so it always shows the current state. Tick is called every frame from VoidconsMod.OnUpdate.
//
// HOW THE CROP MATH WORKS
//   The stage is a 420 x 420 window; the crop circle in its middle is 300 across. The math works with the square
//   around that circle; the corners outside the circle become transparent at the end (ImageTools.ApplyCircle).
//   The picture is drawn at scale s = cover * zoom, where "cover" is the scale at which the picture just covers
//   the square. offX/offY = how far the picture's center is moved from the stage's center (y counts upward).
//   To know which part of the picture is inside the square, go backwards from screen to picture pixels:
//     picture x = width/2  + (screen x - offX) / s        picture y = height/2 - (screen y - offY) / s
//   (picture rows count downward, screen y upward, hence the minus). The square's corners give the crop rectangle.

using System;
using Il2CppTMPro;
using ModConfigs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Voidcons
{
    internal static class VoidconsPage
    {
        private const float StageSize = 420f;
        private const float CropSide = 300f;
        private const float MaxZoom = 8f;

        private static string status = "";
        private static Sprite currentIcon;   // the sprite for the main view's preview

        // ---- crop editor state (src == null means the editor is closed) ----
        private static byte[] src;           // the chosen picture's pixels (at most 1024 px on a side)
        private static int srcW, srcH;
        private static Sprite srcSprite;     // the chosen picture as a sprite, shown on the stage
        private static float zoom = 1f, offX, offY;
        private static byte[] cropped;       // the current 64 x 64 result (RGBA)
        private static Sprite previewSprite;
        private static RectTransform pictureRT;
        private static Image previewImage;

        // ================= every frame =================

        // Collects a finished pick from the file picker (it runs on another thread) and opens the crop editor.
        public static void Tick()
        {
            FilePicker.Result result = FilePicker.TryTakeResult();
            if (result == null)
                return;
            if (result.Rgba != null)
                StartCrop(result);
            else
                status = result.Message;
            ConfigHub.Refresh();
        }

        // ================= page building =================

        public static void Build(ConfigPage page)
        {
            if (src != null)
                BuildCropEditor(page);
            else
                BuildMain(page);
        }

        private static void BuildMain(ConfigPage page)
        {
            page.Header("Your icon");
            RectTransform area = page.Custom(170);
            RectTransform frame = UiKit.Element(area, "Frame", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(160, 160));
            UiKit.Box(frame, UiKit.RowColor, true);
            RectTransform pic = UiKit.Element(frame, "Pic", Mid, Mid, Mid, Vector2.zero, new Vector2(128, 128));
            UiKit.DestroySprite(currentIcon);
            byte[] own = VoidconsMod.OwnPng;
            currentIcon = own != null ? VoidconsMod.MakeSprite(own) : null;
            UiKit.Picture(pic, currentIcon);
            RectTransform info = UiKit.Element(area, "Info", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(190, 0), new Vector2(-190, 0));
            UiKit.Text(info, own != null
                ? $"Round HD icon, {own.Length / 1024.0:0.0} KB\nOther Voidcons players see it on the character select screen (the lobby's host needs Voidcons too)."
                : "No icon yet. Choose a picture below.", 22, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);

            page.Button("Choose picture...", new Action(ChoosePicture));
            page.Button("Remove icon", new Action(RemoveIcon));
            page.Toggle("Backers: show my picture instead of my backer icon", new Func<bool>(GetOverride), new Action<bool>(BackerOverride.Set));
            page.Button($"Forget remembered backers ({BackerGuard.Count})", new Action(ForgetBackers));
            page.Button("Reload Voidcons", new Action(ReloadMod));
            if (status.Length > 0)
                page.Label(status, 20).color = UiKit.Purple;
        }

        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopLeft = new Vector2(0, 1);

        private static void BuildCropEditor(ConfigPage page)
        {
            page.Header("Move and crop");
            page.Label("Drag the picture to move it. Mouse wheel or Zoom buttons to zoom. The circle becomes your icon.", 20);
            RectTransform area = page.Custom(StageSize);

            // The stage: a dark box that cuts off the picture at its edges (RectMask2D).
            RectTransform stage = UiKit.Element(area, "Stage", TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(StageSize, StageSize));
            UiKit.Box(stage, new Color(0.02f, 0.01f, 0.03f, 1f), true);
            stage.gameObject.AddComponent<RectMask2D>();
            pictureRT = UiKit.Element(stage, "Picture", Mid, Mid, Mid, Vector2.zero, Vector2.one);
            UiKit.Picture(pictureRT, srcSprite).preserveAspect = false; // pictureRT itself gets the exact size below

            // Darken everything outside the crop circle and draw a purple ring around it: one picture made in code.
            RectTransform overlay = UiKit.Fill(stage, "CircleOverlay");
            UiKit.Picture(overlay, CircleOverlay());

            // An invisible layer on top that receives the mouse: an EventTrigger calls our methods on drag and scroll.
            RectTransform catcher = UiKit.Element(stage, "Catcher", Vector2.zero, Vector2.one, Mid, Vector2.zero, Vector2.zero);
            UiKit.Box(catcher, new Color(0, 0, 0, 0.001f), false);
            EventTrigger trigger = catcher.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.Drag, new Action<BaseEventData>(OnDrag));
            AddTrigger(trigger, EventTriggerType.EndDrag, new Action<BaseEventData>(OnEndDrag));
            AddTrigger(trigger, EventTriggerType.Scroll, new Action<BaseEventData>(OnScroll));

            // Right side: the live result, then the buttons.
            float x = StageSize + 40;
            RectTransform frame = UiKit.Element(area, "ResultFrame", TopLeft, TopLeft, TopLeft, new Vector2(x, 0), new Vector2(150, 150));
            UiKit.Box(frame, UiKit.RowColor, true);
            RectTransform pic = UiKit.Element(frame, "Result", Mid, Mid, Mid, Vector2.zero, new Vector2(128, 128));
            previewImage = UiKit.Picture(pic, null);
            RectTransform caption = UiKit.Element(area, "Caption", TopLeft, TopLeft, TopLeft, new Vector2(x + 165, 0), new Vector2(200, 150));
            UiKit.Text(caption, "Your icon\n256 x 256 (HD)", 20, UiKit.DimText, TextAlignmentOptions.MidlineLeft);
            SideButton(area, x, 170, "Zoom +", new Action(ZoomIn));
            SideButton(area, x + 190, 170, "Zoom -", new Action(ZoomOut));
            SideButton(area, x, 230, "Whole picture", new Action(FitWhole), 370);
            SideButton(area, x, 300, "Use this", new Action(UseThis), 370);
            SideButton(area, x, 360, "Cancel", new Action(Cancel), 370);

            ApplyTransform();
            UpdatePreview();
            if (status.Length > 0)
                page.Label(status, 20).color = UiKit.DimText;
        }

        // The overlay picture, made once: transparent inside the circle, dark (60% black) outside it, and a 3-pixel
        // purple ring on the edge. Each pixel's distance from the center decides which of the three it is; the
        // "+ 0.5 / clamp" trick (as in ImageTools.ApplyCircle) smooths the edges.
        private static Sprite overlaySprite;
        private static Sprite CircleOverlay()
        {
            if (overlaySprite != null)
                return overlaySprite;
            int size = (int)StageSize;
            double c = size / 2.0, r = CropSide / 2.0;
            var rgba = new byte[size * size * 4];
            byte pr = (byte)(UiKit.Purple.r * 255), pg = (byte)(UiKit.Purple.g * 255), pb = (byte)(UiKit.Purple.b * 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double d = Math.Sqrt((x + 0.5 - c) * (x + 0.5 - c) + (y + 0.5 - c) * (y + 0.5 - c));
                    double ring = Math.Max(0, Math.Min(1, 1.5 - Math.Abs(d - r) + 0.5)); // ~3 px wide band around the edge
                    double outside = Math.Max(0, Math.Min(1, d - r + 0.5));               // 0 inside, 1 outside
                    int o = (y * size + x) * 4;
                    // Opacity: the ring on top of the black shade. Color: the ring is purple, the shade is black,
                    // so the pixel's color is purple times "how much of what you see is ring".
                    double a = ring + 0.6 * outside * (1 - ring);
                    double purpleShare = a > 0 ? ring / a : 0;
                    rgba[o] = (byte)(pr * purpleShare);
                    rgba[o + 1] = (byte)(pg * purpleShare);
                    rgba[o + 2] = (byte)(pb * purpleShare);
                    rgba[o + 3] = (byte)Math.Round(a * 255);
                }
            overlaySprite = UiKit.SpriteFromRgba(rgba, size, size);
            return overlaySprite;
        }

        private static void SideButton(RectTransform area, float x, float y, string text, Action onClick, float width = 180)
        {
            RectTransform rt = UiKit.Element(area, "Button", TopLeft, TopLeft, TopLeft, new Vector2(x, -y), new Vector2(width, 50));
            UiKit.Button(rt, text, onClick, false);
        }

        // An EventTrigger "entry" says: when this kind of event happens, call this method.
        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> handler)
        {
            var entry = new EventTrigger.Entry();
            entry.eventID = type;
            entry.callback.AddListener(handler); // System.Action<T> turns into Unity's UnityAction<T> by itself
            trigger.triggers.Add(entry);
        }

        // ================= crop editor =================

        private static void StartCrop(FilePicker.Result result)
        {
            EndCrop();
            src = result.Rgba;
            srcW = result.Width;
            srcH = result.Height;
            srcSprite = UiKit.SpriteFromRgba(src, srcW, srcH);
            zoom = 1f;
            offX = offY = 0f;
            status = result.Message;
        }

        private static void EndCrop()
        {
            UiKit.DestroySprite(srcSprite);
            UiKit.DestroySprite(previewSprite);
            srcSprite = previewSprite = null;
            src = cropped = null;
        }

        // The scale at which the picture just covers the square, and the smallest zoom (whole picture inside).
        private static float Cover
        {
            get { return CropSide / Math.Min(srcW, srcH); }
        }

        private static float MinZoom
        {
            get { return (float)Math.Min(srcW, srcH) / Math.Max(srcW, srcH); }
        }

        // Puts the picture at its current size and position, keeping the square's center on the picture.
        private static void ApplyTransform()
        {
            if (pictureRT == null || src == null)
                return;
            zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            float s = Cover * zoom;
            offX = Mathf.Clamp(offX, -srcW * s / 2, srcW * s / 2);
            offY = Mathf.Clamp(offY, -srcH * s / 2, srcH * s / 2);
            pictureRT.sizeDelta = new Vector2(srcW * s, srcH * s);
            pictureRT.anchoredPosition = new Vector2(offX, offY);
        }

        // Works out which part of the picture is inside the square (see the math in the header) and makes the
        // 64 x 64 result from it with ImageTools.Resample.
        private static void UpdatePreview()
        {
            if (src == null || previewImage == null)
                return;
            float s = Cover * zoom;
            double left = srcW / 2.0 + (-CropSide / 2 - offX) / s;
            double top = srcH / 2.0 - (CropSide / 2 - offY) / s;
            double side = CropSide / s;
            cropped = ImageTools.Resample(src, srcW, srcH, left, top, side, side, ImageTools.IconSize, ImageTools.IconSize);
            ImageTools.ApplyCircle(cropped, ImageTools.IconSize); // round, like the game's backer icons
            UiKit.DestroySprite(previewSprite);
            previewSprite = UiKit.SpriteFromRgba(cropped, ImageTools.IconSize, ImageTools.IconSize);
            previewImage.sprite = previewSprite;
            previewImage.color = Color.white;
        }

        // Dragging: the mouse moved by "delta" screen pixels. lossyScale.x is how many screen pixels one UI unit is
        // on this screen, so dividing turns the mouse movement into UI units. TryCast turns the general event data
        // into pointer (mouse) data, which has the movement in it.
        private static void OnDrag(BaseEventData data)
        {
            PointerEventData pointer = data.TryCast<PointerEventData>();
            if (pointer == null || pictureRT == null)
                return;
            float k = Math.Max(0.0001f, pictureRT.lossyScale.x);
            offX += pointer.delta.x / k;
            offY += pointer.delta.y / k;
            ApplyTransform(); // the result is only recalculated when the drag ends (it's the slower part)
        }

        private static void OnEndDrag(BaseEventData data)
        {
            UpdatePreview();
        }

        private static void OnScroll(BaseEventData data)
        {
            PointerEventData pointer = data.TryCast<PointerEventData>();
            if (pointer == null)
                return;
            ZoomBy(pointer.scrollDelta.y > 0 ? 1.1f : 1 / 1.1f);
        }

        // Zooming keeps the same spot of the picture in the square's middle: the offset grows with the scale.
        private static void ZoomBy(float factor)
        {
            float before = zoom;
            zoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
            float ratio = zoom / before;
            offX *= ratio;
            offY *= ratio;
            ApplyTransform();
            UpdatePreview();
        }

        private static void ZoomIn() { ZoomBy(1.25f); }
        private static void ZoomOut() { ZoomBy(1 / 1.25f); }

        private static void FitWhole()
        {
            zoom = MinZoom;
            offX = offY = 0;
            ApplyTransform();
            UpdatePreview();
        }

        // Compresses the square into a PNG and saves it as the new icon.
        private static void UseThis()
        {
            try
            {
                if (cropped == null)
                    UpdatePreview();
                byte[] png = ImageTools.EncodePng(cropped, ImageTools.IconSize, ImageTools.IconSize);
                status = VoidconsMod.SaveOwnIcon(png) + $" ({png.Length / 1024.0:0.0} KB)";
            }
            catch (Exception e)
            {
                status = "Could not save: " + e.Message;
            }
            EndCrop();
            ConfigHub.Refresh();
        }

        private static void Cancel()
        {
            EndCrop();
            status = "Cancelled; your icon is unchanged.";
            ConfigHub.Refresh();
        }

        // ================= main view buttons =================
        // Each try/catch turns a problem (for example a file that can't be written) into a status line.

        private static bool GetOverride()
        {
            return BackerOverride.IsOn;
        }

        private static void ChoosePicture()
        {
            if (FilePicker.Busy)
                status = "The file window is already open (it may be behind the game window).";
            else
            {
                FilePicker.Start();
                status = "Pick a picture in the file window (if you don't see it, it may be behind the game: Alt+Tab).";
            }
            ConfigHub.Refresh();
        }

        private static void RemoveIcon()
        {
            try { status = VoidconsMod.RemoveOwnIcon(); }
            catch (Exception e) { status = "Could not remove: " + e.Message; }
            ConfigHub.Refresh();
        }

        private static void ForgetBackers()
        {
            try
            {
                BackerGuard.Reset();
                status = "Forgot all remembered backers. Voidcons starts remembering again from the next lobby.";
            }
            catch (Exception e) { status = "Could not reset: " + e.Message; }
            ConfigHub.Refresh();
        }

        private static void ReloadMod()
        {
            try { status = VoidconsMod.Reload(); }
            catch (Exception e) { status = "Reload failed: " + e.Message; }
            ConfigHub.Refresh();
        }
    }
}
