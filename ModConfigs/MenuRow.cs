// MenuRow.cs - part of ModConfigs: adds a "MOD CONFIGS" row to the game's own home screen menu.
//
// WHAT IT DOES
//   When the home screen starts, it finds the game's QUIT row, makes a copy of it, and puts the copy just above
//   QUIT with the text "MOD CONFIGS" and the VOIDMODS logo as its icon. Clicking it opens the MOD CONFIGS panel.
//
// WHY A COPY OF THE GAME'S ROW
//   A copy looks exactly like Brian's rows (frame, colors, pixel font, the ">" arrow, hover effect) without us
//   having to rebuild the style. But a copy also copies what the row DOES, so before anything else, every click
//   handler and every game script on the copy is removed. Only pure looks (images, text, layout) are kept, and
//   our own click handler is added. That way the copy can never act like QUIT.
//
// HOW IT IS PLACED
//   If the rows sit in a Unity "layout group" (a component that stacks children automatically), the copy is just
//   inserted above QUIT and the layout group moves everything. If not, the copy takes QUIT's place and QUIT moves
//   down by one row distance. The first time, the menu's structure is written to the log, to help fix placement.
//   In FNAF Online 0.9.7 the rows sit in "Buttons" (a VerticalLayoutGroup) inside the purple frame "Inputs",
//   which has a fixed size. One more row would stick out below the frame, so the "USERNAME" title is hidden and
//   everything below it inside the frame moves up by the space it took (MakeRoom).

using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class MenuRow
    {
        private const string RowName = "ModConfigsRow";
        private static bool loggedLayout;

        // The component types that only affect looks. Everything else on the copy (game scripts, click handlers,
        // event triggers) is removed. A HashSet answers "is this name in the set?" quickly.
        private static readonly HashSet<string> KeepTypes = new HashSet<string>
        {
            "Transform", "RectTransform", "CanvasRenderer", "Image", "RawImage", "Button", "TextMeshProUGUI",
            "LayoutElement", "Outline", "Shadow", "Mask", "RectMask2D", "Animator", "CanvasGroup",
            "HorizontalLayoutGroup", "VerticalLayoutGroup", "ContentSizeFitter",
        };

        public static void Add(MainMenuUI menu)
        {
            // 1. Find the QUIT text in the home screen, and the button row it belongs to.
            TMP_Text quitText = null;
            foreach (Component c in menu.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text t = c.TryCast<TMP_Text>();
                if (t != null && t.text != null && t.text.Trim().Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    quitText = t;
                    break;
                }
            }
            if (quitText == null)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't find the QUIT button; MOD CONFIGS row not added.");
                return;
            }
            UiKit.Font = quitText.font; // the game's pixel font, for every VOIDMODS panel
            Component buttonComponent = quitText.GetComponentInParent(Il2CppType.Of<Button>());
            if (buttonComponent == null)
                return;
            GameObject quitRow = buttonComponent.gameObject;
            Transform list = quitRow.transform.parent;
            if (list.Find(RowName) != null)
                return; // already added (the home screen can start more than once)

            if (!loggedLayout)
            {
                loggedLayout = true;
                LogLayout(quitRow);
            }

            // 2. Copy the row. Instantiate makes a full copy of a GameObject and its children.
            GameObject row = UnityEngine.Object.Instantiate(quitRow, list).TryCast<GameObject>();
            row.name = RowName;
            Strip(row);

            // 3. Our own click handler. A new, empty ButtonClickedEvent also drops the click targets that were set
            //    up in Unity's editor for QUIT (those are copied too and would otherwise still run).
            Button button = row.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(new Action(HubPanel.Open));

            // 4. Text and icon.
            foreach (Component c in row.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text t = c.TryCast<TMP_Text>();
                if (t != null && t.text != null && t.text.Trim().Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                    t.text = "MOD CONFIGS";
            }
            SetIcon(row, button);

            // 5. Place it above QUIT.
            int index = quitRow.transform.GetSiblingIndex();
            row.transform.SetSiblingIndex(index); // QUIT moves one place down in the list
            if (list.GetComponent(Il2CppType.Of<LayoutGroup>()) == null)
                PlaceByHand(quitRow, row, list, index);
            MakeRoom(list.parent, list);
            MelonLogger.Msg("[ModConfigs] MOD CONFIGS row added to the home screen.");
        }

        // Hides the "USERNAME" title inside the menu frame and moves the frame's other contents up by the space the
        // title used (from the title's top edge to the top edge of the next thing below it), so the extra row fits
        // inside the frame again. GetWorldCorners gives an element's 4 corners on screen (index 1 = top-left);
        // dividing by lossyScale turns screen distances back into the frame's own units.
        private static void MakeRoom(Transform frame, Transform list)
        {
            if (frame == null)
                return;
            // Unity lays the menu out a little after it starts, so measuring right now could give wrong numbers.
            // Remember what to do; Tick (called every frame by ModConfigsMod) does it two frames later.
            pendingFrame = frame;
            pendingList = list;
            pendingAt = Time.frameCount + 2;
        }

        private static Transform pendingFrame, pendingList;
        private static int pendingAt;

        // Called every frame. Runs the waiting MakeRoom once its frame has come.
        public static void Tick()
        {
            if (pendingFrame == null || Time.frameCount < pendingAt)
                return;
            Transform frame = pendingFrame, list = pendingList;
            pendingFrame = pendingList = null;
            try
            {
                Canvas.ForceUpdateCanvases(); // make Unity finish any layout work right now
                DoMakeRoom(frame, list);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Could not make room in the menu: " + e.Message);
            }
        }

        // The real work. In FNAF Online 0.9.7 the "Username" title is a text INSIDE the name box element ("Player Name
        // Input Field"), sitting above the box. It is hidden, and ONLY the name box and the list of rows move up, by the
        // distance from the title's top edge to the name box's top edge. The frame's background pieces ("Image",
        // "Vignette") stay put: the dark "Image" is drawn over the frame's border, so moving it would cover the top line.
        private static void DoMakeRoom(Transform frame, Transform list)
        {
            LogFrame(frame);
            TMP_Text title = FindTitle(frame, list);
            if (title == null || !title.gameObject.activeSelf)
            {
                MelonLogger.Warning("[ModConfigs] Couldn't find the USERNAME title to make room (see the frame list above).");
                return;
            }
            // The frame's direct child that contains the title (the name box element).
            Transform holder = title.transform;
            while (holder.parent != null && holder.parent != frame)
                holder = holder.parent;

            float titleTop = Top(title.transform);
            float boxTop = Top(holder);
            if (holder == title.transform || boxTop >= titleTop - 0.5f)
            {
                // The title is not above its holder's box; then measure to the highest other part below the title.
                boxTop = float.MinValue;
                for (int i = 0; i < frame.childCount; i++)
                {
                    Transform child = frame.GetChild(i);
                    float top = Top(child);
                    if (child != holder && child.gameObject.activeSelf && top < titleTop - 0.5f && top > boxTop)
                        boxTop = top;
                }
                if (boxTop == float.MinValue)
                    return;
            }
            float shift = (titleTop - boxTop) / Math.Max(0.0001f, frame.lossyScale.y);

            title.gameObject.SetActive(false);
            foreach (Transform part in new[] { holder, list })
            {
                RectTransform rt = part.TryCast<RectTransform>();
                if (rt != null)
                    rt.anchoredPosition += new Vector2(0, shift);
            }
            MelonLogger.Msg($"[ModConfigs] Hid the USERNAME title and moved the menu up {shift:0} units to fit MOD CONFIGS.");
        }

        // Finds the title text: a text in the frame (not in the list of rows) that contains "USER" (the game writes
        // "Username" and shows it in capitals), and that is not the name box's own typed text or its
        // "Enter Your Name..." placeholder.
        private static TMP_Text FindTitle(Transform frame, Transform list)
        {
            var skip = new HashSet<IntPtr>(); // the input fields' own texts, by their memory address
            foreach (Component c in frame.GetComponentsInChildren(Il2CppType.Of<TMP_InputField>(), true))
            {
                TMP_InputField field = c.TryCast<TMP_InputField>();
                if (field == null)
                    continue;
                if (field.textComponent != null)
                    skip.Add(field.textComponent.Pointer);
                if (field.placeholder != null)
                    skip.Add(field.placeholder.Pointer);
            }
            foreach (Component c in frame.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text t = c.TryCast<TMP_Text>();
                if (t == null || t.text == null || skip.Contains(t.Pointer) || t.transform.IsChildOf(list))
                    continue;
                if (t.text.ToUpperInvariant().Contains("USER"))
                    return t;
            }
            return null;
        }

        // All texts inside an element, joined (empty if none).
        private static string TextOf(Transform t)
        {
            var sb = new StringBuilder();
            foreach (Component c in t.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true))
            {
                TMP_Text tt = c.TryCast<TMP_Text>();
                if (tt != null && tt.text != null)
                    sb.Append(tt.text).Append(' ');
            }
            return sb.ToString().Trim();
        }

        // Writes the frame's direct children to the log: name, height, top edge and texts. If the title can't be
        // found in some future game version, this line shows why.
        private static void LogFrame(Transform frame)
        {
            var sb = new StringBuilder("[ModConfigs] Menu frame '" + frame.name + "' children:");
            for (int i = 0; i < frame.childCount; i++)
            {
                Transform child = frame.GetChild(i);
                RectTransform rt = child.TryCast<RectTransform>();
                string text = TextOf(child);
                if (text.Length > 40)
                    text = text.Substring(0, 40) + "...";
                sb.Append("\n  ").Append(i).Append(": ").Append(child.name)
                  .Append(child.gameObject.activeSelf ? "" : " (hidden)")
                  .Append(" height=").Append(rt != null ? rt.rect.height.ToString("0") : "?")
                  .Append(" top=").Append(Top(child).ToString("0"))
                  .Append(" text='").Append(text).Append('\'');
            }
            MelonLogger.Msg(sb.ToString());
        }

        // The top edge of a UI element on screen (y of its top-left corner).
        // The array must be the game-side type (Il2CppStructArray): a plain C# Vector3[] would be COPIED into a
        // temporary game array, Unity would fill that copy, and our array would stay all zeros.
        private static float Top(Transform t)
        {
            RectTransform rt = t.TryCast<RectTransform>();
            if (rt == null)
                return t.position.y;
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(corners);
            return corners[1].y;
        }

        // Removes every component that is not pure looks (see KeepTypes). DestroyImmediate removes it right now,
        // so nothing of it runs even once. GetIl2CppType().Name is the game-side class name of a component.
        private static void Strip(GameObject row)
        {
            foreach (Component c in row.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
            {
                if (c == null)
                    continue;
                string type = c.GetIl2CppType().Name;
                if (!KeepTypes.Contains(type))
                    UnityEngine.Object.DestroyImmediate(c);
            }
        }

        // The row's icon is the Image furthest to the left (the ">" arrow is furthest to the right, and the row's
        // own background is the Button's image). It gets the VOIDMODS logo.
        private static void SetIcon(GameObject row, Button button)
        {
            Sprite logo = UiKit.VoidModLogo;
            if (logo == null)
                return;
            Image icon = null;
            float leftmost = float.MaxValue;
            foreach (Component c in row.GetComponentsInChildren(Il2CppType.Of<Image>(), true))
            {
                Image img = c.TryCast<Image>();
                if (img == null || img.gameObject == row || img == button.targetGraphic)
                    continue;
                float x = img.rectTransform.position.x;
                if (x < leftmost)
                {
                    leftmost = x;
                    icon = img;
                }
            }
            if (icon != null)
            {
                icon.sprite = logo;
                icon.color = Color.white;     // the logo has its own colors
                icon.preserveAspect = true;
            }
        }

        // Without a layout group: the copy takes QUIT's spot and QUIT moves down by the distance between two rows
        // (the distance from the row above QUIT to QUIT).
        private static void PlaceByHand(GameObject quitRow, GameObject row, Transform list, int quitIndex)
        {
            RectTransform quit = quitRow.GetComponent<RectTransform>();
            RectTransform copy = row.GetComponent<RectTransform>();
            float step = -(quit.rect.height + 8); // fallback distance if there is no row above
            if (quitIndex > 0)
            {
                RectTransform above = list.GetChild(quitIndex - 1).GetComponent<RectTransform>();
                if (above != null && above.gameObject != row)
                    step = quit.anchoredPosition.y - above.anchoredPosition.y;
            }
            copy.anchoredPosition = quit.anchoredPosition;
            quit.anchoredPosition = quit.anchoredPosition + new Vector2(0, step);
        }

        // Writes the QUIT row's parents and their components to the log once, so placement can be fixed later.
        private static void LogLayout(GameObject quitRow)
        {
            var sb = new StringBuilder("[ModConfigs] Menu layout (for placing the row):");
            Transform t = quitRow.transform;
            for (int depth = 0; t != null && depth < 5; depth++, t = t.parent)
            {
                sb.Append("\n  ").Append(depth).Append(": ").Append(t.name).Append(" [");
                foreach (Component c in t.GetComponents(Il2CppType.Of<Component>()))
                    sb.Append(c.GetIl2CppType().Name).Append(' ');
                RectTransform rt = t.TryCast<RectTransform>();
                if (rt != null)
                    sb.Append("] pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.rect.size).Append(" children=").Append(t.childCount);
                else
                    sb.Append(']');
            }
            MelonLogger.Msg(sb.ToString());
        }
    }

    // When the game's home screen starts: add the MOD CONFIGS row.
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Start))]
    internal static class MainMenuRowPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            try { MenuRow.Add(__instance); }
            catch (Exception e) { MelonLogger.Warning("[ModConfigs] Could not add the MOD CONFIGS row: " + e); }
        }
    }
}
