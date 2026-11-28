// DangerZone.cs - part of ModConfigs (new in 1.1.0): the one deliberate way to clear all FNAF Online save data.
//
// WHAT IT DOES
//   ModConfigs turns off the game's "Hold D to clear Save Data" completely (too easy by accident). Voidmaster01 wanted
//   a GitHub-style DANGER ZONE instead, at the bottom of ModConfigs' Key Binds page (KeyBinds.cs):
//     1. Type the phrase DELETE MY SAVE DATA exactly (capitals and spaces count). Pasting is blocked: text that
//        arrives faster than anyone can type (several letters in one frame) is removed again, see PasteGuard.
//     2. When the phrase matches, the red button counts down 5 seconds before it unlocks.
//     3. Clicking it runs the game's own clear (GameSave.ClearAll, home screen only). A FULL wipe with no backup
//        and no undo (Voidmaster01, 2026-11-28): getting through steps 1 and 2 means the player really wants it.
//   Changing the phrase back to anything else locks the button again.
//   (Why a phrase and not the username: the save has no username in it, only a cached one, so there's nothing
//   reliable to compare with.)

using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigs
{
    internal static class DangerZone
    {
        public const string Phrase = "DELETE MY SAVE DATA";
        private const float Wait = 5f;

        private static readonly Color Red = new Color(0.86f, 0.22f, 0.27f, 1f);

        private static string typed = "";      // what is in the box right now
        private static float unlockAt = -1f;   // when the button unlocks (Time.unscaledTime); -1 = phrase not typed
        private static string status;          // the last result ("Backed up to ... and cleared." or an error)
        private static TextMeshProUGUI buttonLabel; // the red button's text, updated every frame by Tick

        public static void BuildSection(ConfigPage page)
        {
            page.Header("Danger zone").color = Red;
            // A red frame around the explanation, like GitHub's danger zone box.
            RectTransform box = page.Custom(118);
            Image frame = box.gameObject.AddComponent<Image>();
            frame.color = Red;
            RectTransform inside = UiKit.Fill(box, "Inside");
            inside.sizeDelta = new Vector2(-6, -6);
            UiKit.Box(inside, UiKit.RowColor, false);
            RectTransform words = UiKit.Fill(inside, "Text");
            words.sizeDelta = new Vector2(-40, -16);
            UiKit.Text(words, "<b>Clear all save data</b>\nDeletes ALL your FNAF Online progress on this PC: stats, achievements, XP and settings. "
                + "There is no backup and no undo.", 18, UiKit.TextColor, TextAlignmentOptions.MidlineLeft);

            if (!GameSave.CanClear)
            {
                page.Label("Open MOD CONFIGS from the home screen to use this.", 18).color = UiKit.DimText;
                return;
            }
            // The phrase box. Its "set" runs when typing ends; PasteGuard watches every single change.
            TMP_InputField input = page.TextBox("Type " + Phrase + " to confirm", new Func<string>(GetTyped), new Action<string>(SetTyped));
            var guard = new PasteGuard(input);
            input.onValueChanged.AddListener(new Action<string>(guard.Changed));

            RectTransform btn = page.Custom(ConfigPage.RowHeight);
            buttonLabel = UiKit.Button(btn, "", new Action(Click), false);
            btn.GetComponent<Image>().color = Red; // the button's border in red
            if (status != null)
                page.Label(status, 18).color = status.StartsWith("Cleared") ? UiKit.Purple : Red;
            Tick();
        }

        private static string GetTyped() { return typed; }
        private static void SetTyped(string t) { Typed(t); }

        // Every change of the text: start the countdown when the phrase matches exactly, stop it otherwise.
        private static void Typed(string t)
        {
            typed = t ?? "";
            if (typed == Phrase)
            {
                if (unlockAt < 0)
                    unlockAt = Time.unscaledTime + Wait;
            }
            else
                unlockAt = -1f;
        }

        // Called every frame by ModConfigsMod: keeps the button text up to date (locked / countdown / ready).
        public static void Tick()
        {
            if (guard != null)
                guard.Revert(); // undoes a paste seen last frame (does nothing otherwise)
            if (buttonLabel == null) // Unity's == null is also true once the page was rebuilt and the label destroyed
                return;
            if (unlockAt < 0)
            {
                buttonLabel.text = "TYPE THE PHRASE ABOVE FIRST";
                buttonLabel.color = UiKit.DimText;
            }
            else if (Time.unscaledTime < unlockAt)
            {
                buttonLabel.text = "WAIT " + Math.Ceiling(unlockAt - Time.unscaledTime) + "...";
                buttonLabel.color = UiKit.DimText;
            }
            else
            {
                buttonLabel.text = "CLEAR ALL SAVE DATA";
                buttonLabel.color = Red;
            }
        }

        // The red button: only does something when the phrase is typed and the countdown is over.
        private static void Click()
        {
            if (unlockAt < 0 || Time.unscaledTime < unlockAt)
                return;
            string error;
            // "? :" picks the first text when ClearAll worked, the second when it didn't.
            status = GameSave.ClearAll(out error) ? "Cleared. All save data is gone." : "Nothing was cleared: " + error;
            typed = "";
            unlockAt = -1f;
            ConfigHub.Refresh();
        }

        // Blocks pasting. TextMeshPro pastes by inserting the text ONE CHARACTER AT A TIME (each one reports a change),
        // so "how much was added" can't tell a paste from typing. Speed can: a paste adds many characters in the same
        // frame (Time.frameCount = the number of the frame being drawn), while a person types at most one per frame.
        // So: two or more changes in one frame = a paste. The box is not touched while TextMeshPro is still inserting
        // (that could break it); instead Tick puts the text from before that frame back on the next frame.
        private static PasteGuard guard; // the guard of the box on screen now (for Tick)

        private sealed class PasteGuard
        {
            private readonly TMP_InputField input;
            private string last = "";       // the text after the last accepted change
            private string beforeFrame = ""; // the text at the start of the current frame
            private int frame = -1, changes;
            private bool revert;            // a paste was seen: put beforeFrame back next frame
            private bool restoring;         // true while Revert itself changes the text

            public PasteGuard(TMP_InputField i) { input = i; last = i.text ?? ""; guard = this; }

            public void Changed(string now)
            {
                if (restoring)
                    return;
                if (Time.frameCount != frame)
                {
                    frame = Time.frameCount;
                    beforeFrame = last;
                    changes = 0;
                }
                changes++;
                if (changes > 1 || revert)
                {
                    revert = true;
                    Typed(beforeFrame); // the countdown sees the text from before the paste (so it stays locked)
                    return;
                }
                last = now ?? "";
                Typed(last);
            }

            // Called by Tick on the next frame: removes the pasted text and puts the cursor at the end.
            public void Revert()
            {
                if (!revert || input == null)
                    return;
                revert = false;
                restoring = true;
                input.text = beforeFrame;
                input.caretPosition = beforeFrame.Length;
                restoring = false;
                last = beforeFrame;
                Typed(beforeFrame);
            }
        }
    }
}
