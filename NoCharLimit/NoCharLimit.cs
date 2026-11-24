// NoCharLimit.cs - source code of the NoCharLimit mod (v1.4.0) for FNAF Online Multiplayer.
//
// WHAT IT DOES
//   1. Removes the game's length limits: 14 characters for player names, 400 for chat messages
//      (lobby names are cut at 64, the most Unity's lobby service accepts).
//   2. Lets players color text with codes like {red}text{/} or {#FF8800}text{/}. The game blocks < and >,
//      so real color tags can't be typed; the mod turns the codes into tags when the text is drawn.
//
// HOW TO BUILD AND RUN
//   Run build.ps1 (next to this file) with the game closed; it compiles this file into Mods\NoCharLimit.dll.
//   Then start the game with "Start FNAF Online.bat". MelonLoader (the mod loader) loads every DLL in Mods.
//
// WHAT IT NEEDS
//   MelonLoader 0.7 in the game folder. It brings Harmony (a library for changing game methods while the game
//   runs) and Il2CppInterop (lets C# talk to the game's compiled code). See build.ps1 for the build tools.
//
// BACKGROUND: the game is made with Unity using "IL2CPP": its C# code was turned into machine code
// (GameAssembly.dll). MelonLoader generates C# stand-ins for the game's classes (the "Il2Cpp..." names below),
// so a mod can call and patch them as if they were normal C#.

// "using" lines name the libraries (namespaces) this file uses, so their classes can be written without the full name.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using Unity.Services.Lobbies.Internal;

// Things in [square brackets] are "attributes": labels that tools read. These two tell MelonLoader the mod's
// main class, name, version and author, and which game it is for (developer "Brian_mpz", game name).
[assembly: MelonInfo(typeof(NoCharLimit.NoCharLimitMod), "NoCharLimit", "1.4.0", "Voidmaster01")]
[assembly: MelonGame("Brian_mpz", "FNAF Online Multiplayer")]

namespace NoCharLimit
{
    // The main mod class. ": MelonMod" means it builds on MelonLoader's MelonMod class ("inheritance"), so
    // MelonLoader knows to create it and call its methods, like OnInitializeMelon below, at the right moment.
    // The other classes further down (the ...Patch classes) are found by Harmony through their [HarmonyPatch] labels.
    public class NoCharLimitMod : MelonMod
    {
        // The game's 14 (name) and 400 (chat) checks are compiled constants, so they are patched in
        // GameAssembly.dll in memory. Each site is found by looking up the method by name and scanning
        // its machine code for the length check, so it survives game updates that move code around.
        // "??" matches any byte. Offset is where the patch starts inside the match.
        // A Site describes one such length check: which class and method it is in, the bytes to look for,
        // and the new bytes to write. "sealed" just means no other class may build on this one.
        // Func<IntPtr> is a small function that returns a pointer (IntPtr = a memory address); here it hands
        // back the game class, looked up only when it's needed.
        private sealed class Site
        {
            public Func<IntPtr> Class;
            public string Method;
            public string Pattern;
            public int Offset;
            public byte[] Patch;
            // Optional sites are skipped when the check is missing (newer builds moved it elsewhere).
            public bool Optional;
        }

        // The replacement machine code, as bytes (0x.. = a number written in hexadecimal, base 16).
        //   0x90       = NOP, "do nothing": overwriting a jump with NOPs removes it.
        //   0xEB       = JMP short, "always jump": turns a "jump only if ..." into an unconditional jump.
        //   MovAxR9w   = "mov ax, r9w" plus one NOP: always keep the typed character (see the 0.9.7 site below).
        // "static readonly" = one shared copy for the whole mod that never changes after it is created.
        private static readonly byte[] Nop2 = { 0x90, 0x90 };
        private static readonly byte[] Nop5 = { 0x90, 0x90, 0x90, 0x90, 0x90 };
        private static readonly byte[] Jmp = { 0xEB };
        private static readonly byte[] MovAxR9w = { 0x66, 0x41, 0x8B, 0xC1, 0x90 };

        // The list of all length checks to patch. Each line creates a Site with "new Site { Field = value, ... }".
        // "() => ..." is a lambda: a tiny function written in place. "()" means it takes no input, and the part
        // after "=>" is what it returns: the game class's address, which Il2CppInterop keeps in
        // Il2CppClassPointerStore<ClassName>.NativeClassPtr.
        // The comment above each Site is the machine instruction it matches, in assembly language:
        // "cmp ..., 14" compares the text length with 14, and "jg"/"jle" jump depending on the result.
        private static readonly Site[] Sites =
        {
            // cmp dword [reg+10h], 14 ; jg  -> never truncate
            new Site { Class = () => Il2CppClassPointerStore<MainMenuUI>.NativeClassPtr, Method = "TruncateUsername", Pattern = "83 ?? 10 0E 7F", Offset = 4, Patch = Nop2 },
            // cmp dword [reg+10h], 14 ; jle -> jmp
            new Site { Class = () => Il2CppClassPointerStore<MatchmakingUI>.NativeClassPtr, Method = "ValidatePlayerName", Pattern = "83 ?? 10 0E 7E", Offset = 4, Patch = Jmp, Optional = true },
            new Site { Class = () => Il2CppClassPointerStore<MatchmakingUI>.NativeClassPtr, Method = "CleanName", Pattern = "83 ?? 10 0E 7E", Offset = 4, Patch = Jmp },
            // cmp dword [reg+10h], 400 ; cmovge -> removed  (builds up to 0.9.7 EA3)
            new Site { Class = () => Il2CppClassPointerStore<TextChatClient>.NativeClassPtr, Method = "ValidateInput", Pattern = "81 ?? 10 90 01 00 00 66 ?? 0F 4D ??", Offset = 7, Patch = Nop5, Optional = true },
            // cmp dword [rdx+10h], 400 ; cmovl ax, r9w -> mov ax, r9w  (official 0.9.7: the char is only kept below 400)
            new Site { Class = () => Il2CppClassPointerStore<TextChatClient>.NativeClassPtr, Method = "ValidateInput", Pattern = "81 7A 10 90 01 00 00 66 41 0F 4C C1", Offset = 7, Patch = MovAxR9w, Optional = true },
            // cmp dword [reg+10h], 400 ; jle -> jmp
            new Site { Class = () => Il2CppClassPointerStore<TextChatClient>.NativeClassPtr, Method = "TruncateText", Pattern = "81 ?? 10 90 01 00 00 7E", Offset = 7, Patch = Jmp },
            new Site { Class = () => Il2CppClassPointerStore<TextChatClient>.NativeClassPtr, Method = "SendCommsMessage", Pattern = "81 ?? 10 90 01 00 00 7E", Offset = 7, Patch = Jmp },
            new Site { Class = () => Il2CppClassPointerStore<TextChatSystem>.NativeClassPtr, Method = "SendCommsMessage", Pattern = "81 ?? 10 90 01 00 00 7E", Offset = 7, Patch = Jmp },
            new Site { Class = () => Il2CppClassPointerStore<TextChatSystem>.NativeClassPtr, Method = "CleanMessage", Pattern = "81 ?? 10 90 01 00 00 7E", Offset = 7, Patch = Jmp },
        };

        // "const" values are fixed at compile time. MaxMethodBytes (4096) limits how far a scan may read.
        // PageExecuteReadWrite (0x40) is Windows' code for "this memory may be run, read AND written".
        private const int MaxMethodBytes = 0x1000;
        private const uint PageExecuteReadWrite = 0x40;

        // [DllImport("kernel32")] lets C# call functions that live in Windows itself (kernel32.dll).
        // "extern" means the body is not here; Windows provides it.
        //   VirtualProtect        changes what a piece of memory may be used for. Game code is normally read-only,
        //                         so it is made writable for a moment, patched, and set back.
        //   GetCurrentProcess     returns a handle to this program (the game).
        //   FlushInstructionCache tells the processor to forget old copies of the code it may have cached,
        //                         so it runs the patched bytes.
        // "out uint oldProtect" = the function hands back a second result (the old setting) through this variable.
        [DllImport("kernel32")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32")]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

        // MelonLoader calls this once when the mod is loaded ("override" replaces MelonMod's empty version).
        // Step 1 finds every site, step 2 patches the ones that were found, step 3 writes a summary to the log.
        public override void OnInitializeMelon()
        {
            // Each site is its own length check, so patch every one that is found and report the rest,
            // instead of giving up on all of them when one is missing.
            // "var" lets the compiler work out the type itself. (Site Site, IntPtr Address) is a "tuple":
            // two values kept together as one item, here a site and the address where it was found.
            var found = new List<(Site Site, IntPtr Address)>();
            int missing = 0;
            // Step 1: look for each site. $"..." is an interpolated string: {...} inside it is replaced by the value.
            // An Optional site that simply isn't in this game build is skipped quietly ("continue" = next item).
            foreach (var site in Sites)
            {
                string name = $"{site.Method}";
                IntPtr address = Find(site, out string error);
                if (address == IntPtr.Zero && site.Optional && error == "length check not found")
                    continue;
                if (address == IntPtr.Zero)
                {
                    missing++;
                    LoggerInstance.Warning($"{name}: {error}; this limit stays in place.");
                    DumpMethod(site);
                    continue;
                }
                found.Add((site, address));
            }

            // Step 2: patch. "var (site, address)" unpacks each tuple into two variables.
            // Make the memory writable, copy the new bytes in (Marshal.Copy), restore the old protection
            // ("out _" = throw that result away), then make the processor pick up the change.
            foreach (var (site, address) in found)
            {
                UIntPtr size = (UIntPtr)site.Patch.Length;
                VirtualProtect(address, size, PageExecuteReadWrite, out uint oldProtect);
                Marshal.Copy(site.Patch, 0, address, site.Patch.Length);
                VirtualProtect(address, size, oldProtect, out _);
                FlushInstructionCache(GetCurrentProcess(), address, size);
            }

            // Step 3: one summary line in the MelonLoader log. "a ? b : c" means "if a then b, else c".
            // In an interpolated string, {{ and }} write a real { or }, since a single one starts a value.
            LoggerInstance.Msg(missing == 0
                ? "Name and chat character limits removed. Color codes like {red}text{/} are on."
                : $"{found.Count} of {found.Count + missing} length checks removed (see warnings). Color codes like {{red}}text{{/}} are on.");
        }

        // Writes a method's machine code to the log, so a new pattern can be written for a game update.
        // It finds the method the same way as Find (below), then writes up to 2048 bytes as hex, 32 per line,
        // each line starting with "[dump <Method>]" and the position, for example "[dump CleanName] 0040: 48 8B ...".
        // ":X4" and ":X2" format a number as hexadecimal with 4 or 2 digits.
        private void DumpMethod(Site site)
        {
            IntPtr klass = site.Class();
            IntPtr methodInfo = klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_method_from_name(klass, site.Method, -1);
            IntPtr code = methodInfo == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(methodInfo);
            if (code == IntPtr.Zero)
                return;
            int length = Math.Min(MethodLength(code), 0x800);
            var line = new System.Text.StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i % 32 == 0)
                {
                    if (line.Length > 0)
                        LoggerInstance.Msg(line.ToString());
                    line.Clear().Append($"[dump {site.Method}] {i:X4}:");
                }
                line.Append($" {Marshal.ReadByte(code, i):X2}");
            }
            if (line.Length > 0)
                LoggerInstance.Msg(line.ToString());
        }

        // Find: returns the address where a site's patch should go, or IntPtr.Zero (address 0 = "nothing")
        // with a reason in "error". It asks IL2CPP for the method by name (-1 = any number of parameters).
        private static IntPtr Find(Site site, out string error)
        {
            IntPtr klass = site.Class();
            IntPtr methodInfo = klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_method_from_name(klass, site.Method, -1);
            if (methodInfo == IntPtr.Zero)
            {
                error = "method not found";
                return IntPtr.Zero;
            }

            // MethodInfo starts with the pointer to the method's compiled code.
            IntPtr code = Marshal.ReadIntPtr(methodInfo);
            if (code == IntPtr.Zero)
            {
                error = "method has no code";
                return IntPtr.Zero;
            }

            // The scan: try the pattern at every position i of the method's code. The inner loop compares byte by
            // byte and stops early when one doesn't match ("&& ok"). A null entry ("??") matches any byte.
            // It counts ALL matches: exactly one is required, because patching the wrong place could crash the game.
            // int?[] is a list of "nullable" ints: each entry is a number or null (null = "any byte").
            int?[] pattern = ParsePattern(site.Pattern);
            int length = MethodLength(code);
            IntPtr match = IntPtr.Zero;
            int matches = 0;
            for (int i = 0; i + pattern.Length <= length; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length && ok; j++)
                    ok = pattern[j] == null || Marshal.ReadByte(code, i + j) == pattern[j];
                if (ok)
                {
                    matches++;
                    match = code + i + site.Offset;
                }
            }

            if (matches != 1)
            {
                error = matches == 0 ? "length check not found" : $"length check found {matches} times";
                return IntPtr.Zero;
            }

            error = null;
            return match;
        }

        // Methods are separated by int3 (0xCC) padding; stop there so the scan stays inside this method.
        private static int MethodLength(IntPtr code)
        {
            for (int i = 0; i < MaxMethodBytes - 1; i++)
                if (Marshal.ReadByte(code, i) == 0xCC && Marshal.ReadByte(code, i + 1) == 0xCC)
                    return i;
            return MaxMethodBytes;
        }

        // Turns a pattern text like "83 ?? 10 0E 7F" into numbers: each part is read as hexadecimal
        // (Convert.ToInt32(part, 16)), and "??" becomes null. RemoveEmptyEntries ignores double spaces.
        private static int?[] ParsePattern(string pattern)
        {
            string[] parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new int?[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = parts[i] == "??" ? null : Convert.ToInt32(parts[i], 16);
            return result;
        }
    }

    // ---- Harmony patches ----
    // [HarmonyPatch(typeof(Class), nameof(Class.Method))] tells Harmony which game method to hook.
    // In the class below it, methods with special names run around the game's method:
    //   Prefix    runs BEFORE the game's method (it can change the input),
    //   Postfix   runs AFTER it (it can change the result),
    //   Finalizer runs at the very end, even if the game's method failed with an error.
    // Special parameter names: __instance = the game object the method runs on, __result = the method's
    // return value, __0 = its first parameter. "ref" means the patch can replace that value.
    // nameof(X) is simply the name X as text, checked by the compiler so a typo fails the build.

    // Sets the name box's own typing limit to 0, which means "no limit", when the main menu starts.
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Start))]
    internal static class MainMenuStartPatch
    {
        private static void Postfix(MainMenuUI __instance)
        {
            __instance.playerNameInputField.characterLimit = 0;
        }
    }

    // The same for the chat box, when the text chat starts.
    [HarmonyPatch(typeof(TextChatClient), nameof(TextChatClient.Start))]
    internal static class TextChatStartPatch
    {
        private static void Postfix(TextChatClient __instance)
        {
            __instance.chatInputField.characterLimit = 0;
        }
    }

    // The game swaps < and > for a box character (U+25A1) so names and chat can't hold rich-text tags.
    // Instead, hide them from the game during the call and put them back afterwards, with a zero-width
    // space after each < so the text still can't form a tag. The fonts have both characters.
    //
    // Color codes: since < and > can't be typed, players write {red}text{/} or {#FF8800}text{/}. Braces are
    // hidden from the game the same way as the angle brackets, so its cleaning can't remove them, and
    // ColorCodeDisplayPatch colors them when drawn. Only players with the mod see the colors; everyone
    // else sees the codes as plain text.
    // A plain [HarmonyPatch] (no method named) means: the list of methods comes from TargetMethods() below.
    [HarmonyPatch]
    internal static class KeepAngleBracketsPatch
    {
        // Stand-in characters from Unicode's "private use" area (\uE000 and up): no font or player uses them,
        // so they can safely hold the place of < > { } while the game cleans the text. '\uE000' is written as
        // an escape (a code number) because the character itself is invisible.
        private const char LessStandIn = '\uE000';
        private const char GreaterStandIn = '\uE001';
        private const char OpenBraceStandIn = '\uE002';
        private const char CloseBraceStandIn = '\uE003';

        // The color names players can use, each with its hex color code (#RRGGBB: red, green, blue, 00 to FF).
        // A Dictionary looks a value up by its key. OrdinalIgnoreCase makes {RED} and {red} the same.
        // ["red"] = "#FF0000" inside { } fills the dictionary right when it is created.
        private static readonly Dictionary<string, string> NamedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "#FF0000", ["orange"] = "#FFA500", ["yellow"] = "#FFFF00", ["gold"] = "#FFD700",
            ["lime"] = "#00FF00", ["green"] = "#008000", ["cyan"] = "#00FFFF", ["lightblue"] = "#90D5FF",
            ["blue"] = "#0000FF", ["purple"] = "#A020F0", ["magenta"] = "#FF00FF", ["pink"] = "#FF69B4",
            ["brown"] = "#8B4513", ["white"] = "#FFFFFF", ["gray"] = "#808080", ["grey"] = "#808080",
            ["black"] = "#000000",
        };

        // {red}  {#F80}  {#FF8800}  {#FF8800CC}  and {/} to go back to the normal color.
        // The regex (a search pattern) below, piece by piece. @"..." is a string where \ is just a backslash.
        //   \{ and \}            a real { and } (a bare { has a special meaning in regex, so it gets a \)
        //   (?: a | b | c )      "one of these choices"
        //   (?<hex>#...)         a NAMED group: whatever matches is kept under the name "hex"
        //   [0-9A-Fa-f]{3}       exactly 3 hex digits; the choices allow 3, 6 or 8 digits (8 = with transparency)
        //   (?<name>[A-Za-z]+)   one or more letters, kept as "name" (a color name)
        //   (?<end>/)            a slash, kept as "end" ({/} closes a color)
        // RegexOptions.Compiled makes the pattern faster to use, which matters since it runs on every label.
        private static readonly Regex ColorCode = new Regex(@"\{(?:(?<hex>#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8}))|(?<name>[A-Za-z]+)|(?<end>/))\}", RegexOptions.Compiled);

        // Tags this mod made earlier (for example in a name or message received from another player),
        // turned back into codes so they survive the game's cleaning again.
        private static readonly Regex ColorTag = new Regex(@"<color=(?<hex>#[0-9A-Fa-f]{3,8})>|(?<end></color>)", RegexOptions.Compiled);

        // These methods can call each other; only the outermost call changes the text, so an inner call
        // never hands real tags back to code that would escape them.
        // [ThreadStatic] gives every thread (a line of work the game runs in parallel) its own counter,
        // so two threads cleaning text at the same time can't mix up each other's count.
        [ThreadStatic] private static int depth;

        // Named methods instead of lambdas: build.ps1's bare compiler setup can't emit lambdas here.
        // Regex.Replace with a MatchEvaluator calls the given method once for every match and puts
        // whatever it returns in the match's place: CodeToTag turns {red} into <color=#FF0000>, and so on.
        internal static string CodesToTags(string text)
        {
            return ColorCode.Replace(text, new MatchEvaluator(CodeToTag));
        }

        private static string CodeToTag(Match m)
        {
            if (m.Groups["end"].Success)
                return "</color>";
            if (m.Groups["hex"].Success)
                return "<color=" + m.Groups["hex"].Value + ">";
            // TryGetValue: true plus the hex code if the name is known; an unknown name like {hello} stays as it was.
            string hex;
            return NamedColors.TryGetValue(m.Groups["name"].Value, out hex) ? "<color=" + hex + ">" : m.Value;
        }

        // The way back: <color=#FF0000> becomes {#FF0000} and </color> becomes {/}.
        private static string TagsToCodes(string text)
        {
            return ColorTag.Replace(text, new MatchEvaluator(TagToCode));
        }

        private static string TagToCode(Match m)
        {
            return m.Groups["end"].Success ? "{/}" : "{" + m.Groups["hex"].Value + "}";
        }

        // The game methods that clean names and messages; Harmony puts this class's Prefix/Postfix/Finalizer
        // around each of them. "yield return" hands back one item at a time, so the method produces a list
        // without building it first. AccessTools.Method finds a method by its name, even a private one.
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MatchmakingUI), "EscapeRichText");
            yield return AccessTools.Method(typeof(MatchmakingUI), "CleanName");
            yield return AccessTools.Method(typeof(TextChatSystem), "EscapeRichText");
            yield return AccessTools.Method(typeof(TextChatSystem), "CleanMessage");
            yield return AccessTools.Method(typeof(LobbyListEntryUI), "EscapeRichText");
            yield return AccessTools.Method(typeof(CharacterSelectPlayer), "EscapeRichText");
        }

        // Before the game cleans the text: count one level deeper, and only on the outermost call turn earlier
        // color tags back into codes and swap < > { } for the stand-ins, so the game's cleaning can't see them.
        private static void Prefix(ref string __0)
        {
            depth++;
            if (depth != 1 || __0 == null)
                return;
            // Drop the zero-width space a previous pass added, so it doesn't pile up.
            __0 = TagsToCodes(__0.Replace("<​", "<"))
                .Replace('<', LessStandIn).Replace('>', GreaterStandIn)
                .Replace('{', OpenBraceStandIn).Replace('}', CloseBraceStandIn);
        }

        // Codes stay as plain text here; ColorCodeDisplayPatch turns them into colors when they are drawn.
        // (0.9.7 escapes names again after these methods, which broke tags made at this point.)
        private static void Postfix(ref string __result)
        {
            if (depth != 1 || __result == null)
                return;
            __result = __result.Replace(LessStandIn.ToString(), "<​").Replace(GreaterStandIn, '>')
                .Replace(OpenBraceStandIn, '{').Replace(CloseBraceStandIn, '}');
        }

        // Runs even if the game's method throws, so the depth count never gets stuck.
        private static void Finalizer()
        {
            depth--;
        }
    }

    // Turns {red}text{/} codes into colors at the last moment: when any TextMeshPro label is given its text.
    // Nothing in the game runs after this, so its escaping can't undo the colors. Typing boxes are
    // skipped, so the codes stay editable while typing them.
    [HarmonyPatch]
    internal static class ColorCodeDisplayPatch
    {
        // Remembers, per label (by its memory address), whether it is a typing box. Finding that out is slow,
        // and this patch runs very often, so each label is checked only once.
        // loggedError makes sure an error is written to the log only once instead of on every frame.
        private static readonly Dictionary<IntPtr, bool> InputTextCache = new Dictionary<IntPtr, bool>();
        private static bool loggedError;

        // The three ways a TextMeshPro (TMP) label gets its text: setting .text, and two versions of SetText.
        // The SetText versions are only hooked if this game build has them (a null check, != null).
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertySetter(typeof(TMP_Text), nameof(TMP_Text.text));
            var setText = AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string) });
            if (setText != null)
                yield return setText;
            var setTextSync = AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) });
            if (setTextSync != null)
                yield return setTextSync;
        }

        // Before the label gets its text: quick exit when there is no { at all (most labels). Otherwise convert the
        // codes; if anything changed and this isn't a typing box, switch on rich text (so TMP reads color tags)
        // and hand the converted text on. try/catch: if anything fails, log it once and leave the text as it was,
        // because an error here must never break the game's labels.
        private static void Prefix(TMP_Text __instance, ref string __0)
        {
            if (__0 == null || __0.IndexOf('{') < 0)
                return;
            try
            {
                string converted = KeepAngleBracketsPatch.CodesToTags(__0);
                if (converted == __0 || IsTypingBox(__instance))
                    return;
                __instance.richText = true;
                __0 = converted;
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    MelonLogger.Warning($"[NoCharLimit] Color codes: {e.Message}");
                }
            }
        }

        // A label is a typing box's own text when its nearest TMP_InputField parent uses exactly this label as
        // its textComponent. Il2CppType.Of<T>() and TryCast<T>() are Il2CppInterop's way of asking for and
        // converting game types (plain C# "is"/"as" doesn't work on IL2CPP objects).
        private static bool IsTypingBox(TMP_Text text)
        {
            IntPtr key = text.Pointer;
            bool result;
            if (!InputTextCache.TryGetValue(key, out result))
            {
                Component parent = text.GetComponentInParent(Il2CppType.Of<TMP_InputField>(), true);
                TMP_InputField input = parent == null ? null : parent.TryCast<TMP_InputField>();
                result = input != null && input.textComponent != null && input.textComponent.Pointer == key;
                InputTextCache[key] = result;
            }
            return result;
        }
    }

    // Hosting names the lobby "<player>'s Lobby", and Unity's Lobby service caps names at 64 characters.
    // With long names allowed, that could make lobby creation fail, so the name is cut to 64 first (Substring).
    [HarmonyPatch(typeof(WrappedLobbyService), nameof(WrappedLobbyService.CreateLobbyAsync))]
    internal static class CreateLobbyPatch
    {
        private const int MaxLobbyNameLength = 64;

        private static void Prefix(ref string lobbyName)
        {
            if (lobbyName != null && lobbyName.Length > MaxLobbyNameLength)
                lobbyName = lobbyName.Substring(0, MaxLobbyNameLength);
        }
    }
}
