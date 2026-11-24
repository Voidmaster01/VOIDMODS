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
            MelonLogger.Msg("[ModConfigs] MOD CONFIGS row added to the home screen.");
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
