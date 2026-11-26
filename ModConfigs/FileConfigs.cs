// FileConfigs.cs - part of ModConfigs (new in 1.1.0): finds other mods' own config files and shows them as pages.
//
// WHAT IT DOES
//   Many mods don't use MelonPreferences; they keep their settings in a file of their own, like
//   UserData\SomeMod\config.json or Mods\SomeMod.ini. This file finds those files, reads the settings out of them,
//   and builds a MOD CONFIGS page for each mod: true/false becomes a switch, numbers get - and +, #RRGGBB colors
//   get a color picker, and everything else gets a text box.
//
//   Formats it understands:
//     .ini .cfg .conf .properties .txt   "key = value" lines, [sections], ; or # comments
//     .toml                               the same idea, with "quoted" text
//     .yaml .yml                          "key: value" lines, nested by indenting
//     .json                               { "key": value } objects and [lists]
//     .xml                                <key>value</key> and key="value" attributes
//
// HOW SAVING WORKS (and why it is careful)
//   - Only the value itself is replaced in the text; comments, blank lines, indenting and the order stay exactly
//     as they were. That includes .json files with // or /* */ comments (see ReadJson for how).
//   - Before ModConfigs changes a file for the first time, it copies the original to
//     UserData\ModConfigs\Backups\..., and RESET TO DEFAULTS puts that copy back.
//   - Saving waits until nothing changed for half a second (so dragging a slider doesn't write 60 times a second).
//   - The file keeps its text encoding and its line endings (Windows CRLF or Unix LF).
//   - Most mods read their file only when the game starts, so the page says to restart the game.
//
// WHAT IT SKIPS
//   MelonLoader's own files (Loader.cfg, MelonPreferences.cfg: that one has its own pages already), anything in
//   Mods\Disabled, ModConfigs' own backups, readme/license/log/changelog/"how to" files, files over 200 KB, and
//   .txt files that are mostly sentences instead of "key = value" lines.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ModConfigs
{
    internal static class FileConfigs
    {
        // ================= what a found file looks like =================

        internal enum Kind { Bool, Number, Color, Text }

        // One setting inside a file.
        internal sealed class Value
        {
            public string Key;       // what the page shows, e.g. "volume" or "graphics.quality"
            public string Section;   // the [section] it is in (ini/toml), or null
            public string Text;      // the value as text, without quotes
            public Kind Kind;
            // For the line-based formats: where the value sits in the file (line number, first character, length).
            // For JSON: Start and Length count characters in the whole text instead (Line is not used).
            public int Line, Start, Length;
            public bool Quoted;
            // For XML: the element or attribute.
            public XElement Element;
            public XAttribute Attribute;
        }

        // One config file.
        internal sealed class File
        {
            public string Path;      // full path on disk
            public string Shown;     // the path from the game folder, e.g. UserData\SomeMod\config.json
            public string Format;    // "ini", "properties", "toml", "yaml", "json" or "xml"
            public Encoding Encoding;
            public List<string> Lines = new List<string>();   // line-based formats: the text, line by line...
            public List<string> Endings = new List<string>(); // ...and what ended each line ("\r\n", "\n" or "")
            public string JsonText;       // JSON: the whole text; values are replaced right inside it
            public XDocument Xml;
            public string Newline = "\n"; // XML only: the file's own line break
            public List<Value> Values = new List<Value>();
            public string Error;     // set when reading or saving failed
            public bool Dirty;       // changed in memory, not saved yet
            public float ChangedAt;  // when it was last changed (Time.unscaledTime, in seconds)
        }

        // All files of one mod, shown together on one page.
        internal sealed class Group
        {
            public string Name;
            public List<File> Files = new List<File>();
        }

        private static List<Group> groups = new List<Group>();

        private const long MaxBytes = 200 * 1024;
        private const int MaxValuesPerFile = 300;

        // ================= finding files =================

        // Looks for config files again (called every time the panel opens). Unsaved changes are saved first, so
        // reading the files again can't throw them away.
        public static List<Group> Scan()
        {
            Flush();
            var found = new List<Group>();
            var byName = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            string root = MelonEnvironment.GameRootDirectory;
            foreach (string top in new[] { MelonEnvironment.UserDataDirectory, MelonEnvironment.ModsDirectory })
            {
                if (!Directory.Exists(top))
                    continue;
                foreach (string path in FilesIn(top, 0))
                {
                    File file = TryRead(path, root);
                    if (file == null)
                        continue;
                    string group = GroupName(path, top);
                    Group g;
                    if (!byName.TryGetValue(group, out g))
                    {
                        g = new Group { Name = group };
                        byName[group] = g;
                        found.Add(g);
                    }
                    g.Files.Add(file);
                }
            }
            groups = found;
            return found;
        }

        // Every file under "dir", down to 3 folders deep, minus the skipped folders. It calls itself for each
        // subfolder (that is called recursion), and "depth" counts how deep it is so it stops in time.
        private static List<string> FilesIn(string dir, int depth)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    list.Add(f);
                if (depth < 3)
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        string name = System.IO.Path.GetFileName(sub);
                        if (name.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || name.Equals("ModConfigs", StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.AddRange(FilesIn(sub, depth + 1));
                    }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModConfigs] Can't look in " + dir + ": " + e.Message);
            }
            return list;
        }

        // The mod a file belongs to: its first folder under UserData or Mods (UserData\SomeMod\x.json -> SomeMod),
        // or, for a file right in UserData or Mods, its own name without the extension (Mods\SomeMod.ini -> SomeMod).
        private static string GroupName(string path, string top)
        {
            string rel = path.Substring(top.Length).TrimStart('\\', '/');
            int slash = rel.IndexOfAny(new[] { '\\', '/' });
            return slash > 0 ? rel.Substring(0, slash) : System.IO.Path.GetFileNameWithoutExtension(rel);
        }

        // A regex ("regular expression") is a small pattern language for text. This one matches file names that
        // are documentation or logs, not settings: \b means "word edge", | means "or", and
        // RegexOptions.IgnoreCase makes README and readme the same.
        private static readonly Regex NotConfig = new Regex(@"readme|licen[cs]e|changelog|\blog\b|\.log|how ?to|credits|manifest|\.deps\.|runtimeconfig", RegexOptions.IgnoreCase);

        // Reads one file if it is a config file we understand; null otherwise.
        private static File TryRead(string path, string root)
        {
            string name = System.IO.Path.GetFileName(path);
            string format = FormatOf(System.IO.Path.GetExtension(path));
            if (format == null || NotConfig.IsMatch(name))
                return null;
            if (name.Equals("MelonPreferences.cfg", StringComparison.OrdinalIgnoreCase) || name.Equals("Loader.cfg", StringComparison.OrdinalIgnoreCase)
                || name.Equals("MelonLoader.cfg", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                if (new FileInfo(path).Length > MaxBytes)
                    return null;
                var file = new File { Path = path, Shown = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length).TrimStart('\\', '/') : path, Format = format };
                Load(file);
                // A .txt file only counts when it really is "key = value" lines, not a text with a few "=" in it.
                if (System.IO.Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase) && !MostlySettings(file))
                    return null;
                return file.Values.Count > 0 ? file : null;
            }
            catch (Exception e)
            {
                // A broken file is skipped instead of breaking the panel; the log says why.
                MelonLogger.Warning("[ModConfigs] Skipped " + path + ": " + e.Message);
                return null;
            }
        }

        private static string FormatOf(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".ini": case ".cfg": case ".conf": case ".txt": return "ini";
                case ".properties": return "properties";
                case ".toml": return "toml";
                case ".yaml": case ".yml": return "yaml";
                case ".json": return "json";
                case ".xml": return "xml";
                default: return null;
            }
        }

        // True when at least 4 of every 5 meaningful lines are settings.
        private static bool MostlySettings(File file)
        {
            int meaningful = 0;
            foreach (string line in file.Lines)
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#") || t.StartsWith("//") || t.StartsWith("["))
                    continue;
                meaningful++;
            }
            return meaningful > 0 && file.Values.Count * 5 >= meaningful * 4;
        }

        // ================= reading =================

        // Reads the file's text (keeping its encoding) and finds its values.
        private static void Load(File file)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(file.Path);
            file.Encoding = DetectEncoding(bytes);
            string text = file.Encoding.GetString(bytes);
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1); // the BOM mark itself is not part of the text
            file.Values.Clear();
            file.Error = null;
            if (file.Format == "json")
                ReadJson(file, text);
            else if (file.Format == "xml")
                ReadXml(file, text);
            else
            {
                SplitLines(text, file.Lines, file.Endings);
                ReadLines(file);
            }
        }

        // A "BOM" is an invisible mark some editors put at the very start of a file to say "this is UTF-8" (or
        // UTF-16). We remember it, so saving writes the file back the same way. No mark = UTF-8 without one.
        private static Encoding DetectEncoding(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(true);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return new UnicodeEncoding(false, true);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new UnicodeEncoding(true, true);
            return new UTF8Encoding(false);
        }

        // Cuts the text into lines and keeps each line's ending, so they can be glued back exactly.
        private static void SplitLines(string text, List<string> lines, List<string> endings)
        {
            lines.Clear();
            endings.Clear();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                bool crlf = i > start && text[i - 1] == '\r';
                lines.Add(text.Substring(start, (crlf ? i - 1 : i) - start));
                endings.Add(crlf ? "\r\n" : "\n");
                start = i + 1;
            }
            lines.Add(text.Substring(start)); // the last line (may be empty when the file ends with a line break)
            endings.Add("");
        }

        // Finds "key = value" (or "key: value" for YAML and .properties) on each line.
        // For YAML it also keeps a "stack" of the keys above the current line: a key with nothing after the colon
        // starts a nested block, and lines indented further belong to it ("graphics:" + "  quality: 2" ->
        // "graphics.quality"). A stack is a list where you add and remove at the end, like a pile of plates.
        private static void ReadLines(File file)
        {
            string section = null;
            var yamlKeys = new List<string>();
            var yamlIndents = new List<int>();
            for (int i = 0; i < file.Lines.Count && file.Values.Count < MaxValuesPerFile; i++)
            {
                string line = file.Lines[i];
                string t = line.Trim();
                if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t.StartsWith("//"))
                    continue;
                if (file.Format != "yaml" && t[0] == '[' && t.EndsWith("]"))
                {
                    section = t.Trim('[', ']').Trim(); // [[name]] (TOML) works too
                    continue;
                }
                if (file.Format == "yaml" && (t.StartsWith("- ") || t == "-" || t == "---"))
                    continue; // YAML lists are not supported here; the rest of the file still is
                int sep = FindSeparator(line, file.Format);
                if (sep <= 0)
                    continue;
                string key = line.Substring(0, sep).Trim().Trim('"', '\'');
                if (key.Length == 0 || key.Length > 60)
                    continue;

                // Where does the value start and end? Skip spaces after the separator...
                int start = sep + 1;
                while (start < line.Length && (line[start] == ' ' || line[start] == '\t'))
                    start++;
                int end = line.Length;
                bool quoted = false;
                if (start < line.Length && (line[start] == '"' || line[start] == '\''))
                {
                    // ...a "quoted" value ends at the matching quote; the quotes themselves stay in the file.
                    int close = line.IndexOf(line[start], start + 1);
                    if (close < 0)
                        continue;
                    quoted = true;
                    start++;
                    end = close;
                }
                else
                {
                    // ...an unquoted value ends where a comment starts (" ;" or " #"), minus trailing spaces.
                    int comment = CommentStart(line, start, file.Format);
                    if (comment >= 0)
                        end = comment;
                    while (end > start && (line[end - 1] == ' ' || line[end - 1] == '\t'))
                        end--;
                }
                string value = line.Substring(start, end - start);

                if (file.Format == "yaml")
                {
                    int indent = line.Length - line.TrimStart().Length;
                    while (yamlIndents.Count > 0 && yamlIndents[yamlIndents.Count - 1] >= indent)
                    {
                        yamlIndents.RemoveAt(yamlIndents.Count - 1);
                        yamlKeys.RemoveAt(yamlKeys.Count - 1);
                    }
                    if (!quoted && value.Length == 0)
                    {
                        yamlIndents.Add(indent);
                        yamlKeys.Add(key);
                        continue;
                    }
                    if (yamlKeys.Count > 0)
                        key = string.Join(".", yamlKeys) + "." + key;
                    if (!quoted && (value.StartsWith("|") || value.StartsWith(">") || value.StartsWith("&") || value.StartsWith("*")))
                        continue; // multi-line text and YAML anchors: too tricky to edit safely, so left alone
                }
                // TOML lists [..], tables {..} and """long text""" are left alone too.
                if (!quoted && (value.StartsWith("[") || value.StartsWith("{")) && file.Format != "ini")
                    continue;

                file.Values.Add(new Value
                {
                    Key = key, Section = section, Text = value, Kind = KindOf(value, quoted),
                    Line = i, Start = start, Length = end - start, Quoted = quoted
                });
            }
        }

        // The position of "=" (or ":" where that format uses it), or -1 when the line has no setting.
        private static int FindSeparator(string line, string format)
        {
            int eq = line.IndexOf('=');
            if (format == "yaml")
            {
                // "key: value" - the colon must be followed by a space or end the line (so "http://x" isn't split).
                for (int i = 0; i < line.Length; i++)
                    if (line[i] == ':' && (i + 1 == line.Length || line[i + 1] == ' '))
                        return i;
                return -1;
            }
            if (format == "properties")
            {
                int colon = line.IndexOf(':');
                if (colon >= 0 && (eq < 0 || colon < eq))
                    return colon;
            }
            return eq;
        }

        // Where an end-of-line comment starts in an unquoted value: a ; or # that has a space before it.
        private static int CommentStart(string line, int from, string format)
        {
            for (int i = from; i < line.Length; i++)
            {
                bool marker = line[i] == '#' || (line[i] == ';' && format != "yaml" && format != "toml");
                if (marker && i > from && (line[i - 1] == ' ' || line[i - 1] == '\t'))
                    return i - 1;
            }
            return -1;
        }

        private static readonly Regex NumberPattern = new Regex(@"^-?\d+(\.\d+)?$");
        private static readonly Regex HexColor = new Regex(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");

        // Decides how a value is shown. Quoted values are always text (or a color), since the mod wrote them as text.
        private static Kind KindOf(string value, bool quoted)
        {
            if (HexColor.IsMatch(value))
                return Kind.Color;
            if (quoted)
                return Kind.Text;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return Kind.Bool;
            if (NumberPattern.IsMatch(value))
                return Kind.Number;
            return Kind.Text;
        }

        // JSON: read with Utf8JsonReader, which walks the file one "token" at a time ({, a name, a value, ], ...) and
        // tells where each token starts and ends. That position is all we need to replace a value later, so the rest
        // of the file (comments, spacing) is never touched. The options let it read // and /* */ comments and a
        // comma after the last item, which many mods' files have.
        // While reading, two stacks remember where we are: "path" holds the names of the objects and lists we are
        // inside (e.g. "player"), and "counters" holds, for each list, the position of its next item (-1 = object).
        private static void ReadJson(File file, string text)
        {
            file.JsonText = text;
            byte[] bytes = Encoding.UTF8.GetBytes(text); // the reader works on UTF-8 bytes, not on a string
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var path = new List<string>();
            var counters = new List<int>();
            string name = null; // the last property name read ("volume" in "volume": 0.8)
            while (reader.Read() && file.Values.Count < MaxValuesPerFile)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        name = reader.GetString();
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        path.Add(counters.Count == 0 ? "" : ChildKey(counters, name)); // "" = the outermost { or [
                        counters.Add(reader.TokenType == JsonTokenType.StartArray ? 0 : -1);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        path.RemoveAt(path.Count - 1);
                        counters.RemoveAt(counters.Count - 1);
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Number:
                    case JsonTokenType.String:
                        string key = JoinPath(path, counters.Count == 0 ? "" : ChildKey(counters, name));
                        string value;
                        Kind kind;
                        if (reader.TokenType == JsonTokenType.String) { value = reader.GetString(); kind = HexColor.IsMatch(value) ? Kind.Color : Kind.Text; }
                        else if (reader.TokenType == JsonTokenType.Number) { value = Encoding.UTF8.GetString(reader.ValueSpan.ToArray()); kind = Kind.Number; }
                        else { value = reader.TokenType == JsonTokenType.True ? "true" : "false"; kind = Kind.Bool; }
                        // TokenStartIndex..BytesConsumed is the token in bytes (with the quotes for text); turn that
                        // into a position in characters, because the file is edited as a string.
                        int startByte = (int)reader.TokenStartIndex, endByte = (int)reader.BytesConsumed;
                        int start = Encoding.UTF8.GetCharCount(bytes, 0, startByte);
                        int length = Encoding.UTF8.GetCharCount(bytes, startByte, endByte - startByte);
                        file.Values.Add(new Value { Key = key, Text = value, Kind = kind, Start = start, Length = length, Quoted = reader.TokenType == JsonTokenType.String });
                        break;
                    // Null and comments: nothing to edit.
                }
            }
        }

        // The name of the next thing inside the current object or list: its property name, or "[n]" in a list
        // (and then the list's counter moves on to n + 1).
        private static string ChildKey(List<int> counters, string name)
        {
            int last = counters.Count - 1;
            if (counters[last] < 0)
                return name ?? "";
            string key = "[" + counters[last] + "]";
            counters[last]++;
            return key;
        }

        // Glues the path together the way people write it: player.colors[0].name
        private static string JoinPath(List<string> path, string last)
        {
            var sb = new StringBuilder();
            foreach (string part in path)
                AppendPart(sb, part);
            AppendPart(sb, last);
            return sb.ToString();
        }

        private static void AppendPart(StringBuilder sb, string part)
        {
            if (part.Length == 0)
                return;
            if (sb.Length > 0 && part[0] != '[')
                sb.Append('.');
            sb.Append(part);
        }

        // XML: every attribute (name="value") and every element that only holds text (<name>value</name>).
        // PreserveWhitespace keeps the spaces and line breaks, so saving changes nothing else in the file.
        private static void ReadXml(File file, string text)
        {
            file.Xml = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            file.Newline = text.Contains("\r\n") ? "\r\n" : "\n"; // used after the <?xml ...?> line when saving
            if (file.Xml.Root != null)
                WalkXml(file, file.Xml.Root, file.Xml.Root.Name.LocalName);
        }

        private static void WalkXml(File file, XElement el, string path)
        {
            if (file.Values.Count >= MaxValuesPerFile)
                return;
            foreach (XAttribute a in el.Attributes())
                if (!a.IsNamespaceDeclaration)
                    file.Values.Add(new Value { Key = path + "@" + a.Name.LocalName, Text = a.Value, Kind = KindOf(a.Value, false), Attribute = a });
            bool hasChildren = false;
            foreach (XElement child in el.Elements())
            {
                hasChildren = true;
                WalkXml(file, child, path + "." + child.Name.LocalName);
            }
            if (!hasChildren && el.Value.Trim().Length > 0)
                file.Values.Add(new Value { Key = path, Text = el.Value.Trim(), Kind = KindOf(el.Value.Trim(), false), Element = el });
        }

        // ================= changing and saving =================

        // Changes one value in memory and marks the file to be saved a moment later (see Tick).
        public static void Set(File file, int index, string text)
        {
            if (index < 0 || index >= file.Values.Count)
                return;
            Value v = file.Values[index];
            if (v.Text == text)
                return;
            if (file.Format == "json")
                SetJson(file, v, text);
            else if (v.Attribute != null)
                v.Attribute.Value = text;
            else if (v.Element != null)
                v.Element.Value = text;
            else
            {
                string written = v.Quoted && file.Format == "toml" ? text.Replace("\\", "\\\\").Replace("\"", "\\\"") : text;
                string line = file.Lines[v.Line];
                file.Lines[v.Line] = line.Substring(0, v.Start) + written + line.Substring(v.Start + v.Length);
                v.Length = written.Length;
            }
            v.Text = text;
            file.Dirty = true;
            file.ChangedAt = Time.unscaledTime;
        }

        // JSON: writes the new value in JSON form right over the old one in the text, then reads the text again so
        // every value's position is up to date (the new value may be longer or shorter than the old one).
        // The value keeps its type: true/false stays a bool, numbers stay numbers, text stays "quoted text".
        private static readonly JsonSerializerOptions QuoteOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static void SetJson(File file, Value v, string text)
        {
            string raw;
            double d;
            if (v.Kind == Kind.Bool)
                raw = text.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            else if (v.Kind == Kind.Number && ConfigPage.TryParseNumber(text, out d))
                raw = text; // the number's own text, so 42.0 stays 42.0
            else
                raw = JsonSerializer.Serialize(text, QuoteOptions); // adds the quotes and escapes " and \ inside the text
            file.JsonText = file.JsonText.Substring(0, v.Start) + raw + file.JsonText.Substring(v.Start + v.Length);
            file.Values.Clear();
            ReadJson(file, file.JsonText);
        }

        // Called every frame by ModConfigsMod: saves files that changed more than half a second ago.
        public static void Tick()
        {
            foreach (Group g in groups)
                foreach (File f in g.Files)
                    if (f.Dirty && Time.unscaledTime - f.ChangedAt > 0.5f)
                        Save(f);
        }

        // Saves everything that changed, right now (when the panel closes or before scanning again).
        public static void Flush()
        {
            foreach (Group g in groups)
                foreach (File f in g.Files)
                    if (f.Dirty)
                        Save(f);
        }

        private static void Save(File file)
        {
            file.Dirty = false;
            try
            {
                BackUp(file);
                string text;
                if (file.Format == "json")
                    text = file.JsonText; // already holds the changes, with everything else as it was
                else if (file.Format == "xml")
                {
                    // ToString leaves out the <?xml ...?> line, so it is put back in front. The line break after it
                    // is usually kept by PreserveWhitespace already; only add one when it is missing.
                    string xml = file.Xml.ToString(SaveOptions.DisableFormatting);
                    string decl = file.Xml.Declaration != null ? file.Xml.Declaration.ToString() : "";
                    text = decl.Length > 0 && !xml.StartsWith("\n") && !xml.StartsWith("\r") ? decl + file.Newline + xml : decl + xml;
                }
                else
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < file.Lines.Count; i++)
                        sb.Append(file.Lines[i]).Append(file.Endings[i]);
                    text = sb.ToString();
                }
                // GetPreamble gives the BOM bytes for this encoding (none for plain UTF-8), then the text follows.
                byte[] bom = file.Encoding.GetPreamble();
                byte[] body = file.Encoding.GetBytes(text);
                var all = new byte[bom.Length + body.Length];
                Buffer.BlockCopy(bom, 0, all, 0, bom.Length);
                Buffer.BlockCopy(body, 0, all, bom.Length, body.Length);
                System.IO.File.WriteAllBytes(file.Path, all);
                file.Error = null;
            }
            catch (Exception e)
            {
                file.Error = "Couldn't save: " + e.Message;
                MelonLogger.Warning("[ModConfigs] Couldn't save " + file.Path + ": " + e.Message);
            }
        }

        // ================= backups and reset =================

        private static string BackupPath(File file)
        {
            return System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "ModConfigs", "Backups", file.Shown);
        }

        // Copies the file before its very first change. If a backup is already there, it holds the older original,
        // so it is kept as it is.
        private static void BackUp(File file)
        {
            string backup = BackupPath(file);
            if (System.IO.File.Exists(backup))
                return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
            System.IO.File.Copy(file.Path, backup);
        }

        public static bool HasBackup(Group group)
        {
            foreach (File f in group.Files)
                if (System.IO.File.Exists(BackupPath(f)))
                    return true;
            return false;
        }

        // RESET TO DEFAULTS for a file page: puts each backed-up file back the way it was before ModConfigs changed
        // it, removes the backup (the next change makes a fresh one), and reads the files again.
        public static void Restore(Group group)
        {
            foreach (File f in group.Files)
            {
                string backup = BackupPath(f);
                f.Dirty = false;
                try
                {
                    if (System.IO.File.Exists(backup))
                    {
                        System.IO.File.Copy(backup, f.Path, true);
                        System.IO.File.Delete(backup);
                    }
                    Load(f);
                }
                catch (Exception e)
                {
                    f.Error = "Couldn't reset: " + e.Message;
                }
            }
        }

        // "Reload from disk": throws away unsaved changes and reads the files again (for when you edited a file
        // in Notepad while the game was open).
        public static void Reload(Group group)
        {
            foreach (File f in group.Files)
            {
                f.Dirty = false;
                try { Load(f); }
                catch (Exception e) { f.Error = "Couldn't read: " + e.Message; }
            }
        }

        // ================= the page =================

        // Builds the page for one mod's files. Each value gets the row that fits its kind.
        public static void BuildPage(Group group, ConfigPage page)
        {
            page.Label("These settings are read from the mod's own config file. Most mods read it when the game starts, so restart the game after you change something. RESET TO DEFAULTS puts the file back the way it was before MOD CONFIGS first changed it.", 18);
            foreach (File file in group.Files)
            {
                page.Header(System.IO.Path.GetFileName(file.Path));
                page.Label(file.Shown, 16).color = UiKit.DimText;
                if (file.Error != null)
                    page.Label(file.Error, 18).color = UiKit.Purple;                string section = null;
                for (int i = 0; i < file.Values.Count; i++)
                {
                    Value v = file.Values[i];
                    if (v.Section != section)
                    {
                        section = v.Section;
                        if (section != null)
                            page.Label("[" + section + "]", 20).color = UiKit.Purple;
                    }
                    var bind = new ValueBinding(file, i);
                    switch (v.Kind)
                    {
                        case Kind.Bool:
                            page.Toggle(v.Key, new Func<bool>(bind.GetBool), new Action<bool>(bind.SetBool));
                            break;
                        case Kind.Number:
                            bool whole = !v.Text.Contains(".");
                            page.Number(v.Key, new Func<double>(bind.GetNumber), new Action<double>(bind.SetNumber), whole ? 1 : 0.1, -1e12, 1e12, whole ? "0" : "0.######");
                            break;
                        case Kind.Color:
                            page.ColorPicker(v.Key, new Func<Color>(bind.GetColor), new Action<Color>(bind.SetColor));
                            break;
                        default:
                            page.TextBox(v.Key, new Func<string>(bind.GetText), new Action<string>(bind.SetText));
                            break;
                    }
                }
                if (file.Values.Count >= MaxValuesPerFile)
                    page.Label("(Only the first " + MaxValuesPerFile + " settings are shown. Open the file for the rest.)", 18);
                var open = new ShowFile(file.Path);
                page.Button("SHOW THE FILE IN ITS FOLDER", new Action(open.Click));
                page.Space(20);
            }
            var reload = new ReloadBinding(group);
            page.Button("RELOAD FROM DISK", new Action(reload.Click));
        }

        // Reads and writes one value for the page rows. It finds the value by its position each time, because the
        // file may have been read again since the page was built.
        private sealed class ValueBinding
        {
            private readonly File file;
            private readonly int index;
            public ValueBinding(File f, int i) { file = f; index = i; }
            private string Current { get { return index < file.Values.Count ? file.Values[index].Text : ""; } }

            public bool GetBool() { return Current.Equals("true", StringComparison.OrdinalIgnoreCase); }
            // Keeps the file's own spelling: True/False, TRUE/FALSE or true/false.
            public void SetBool(bool on)
            {
                string old = Current;
                string text = on ? "true" : "false";
                if (old.Length > 1 && char.IsUpper(old[1])) text = text.ToUpperInvariant();
                else if (old.Length > 0 && char.IsUpper(old[0])) text = char.ToUpperInvariant(text[0]) + text.Substring(1);
                FileConfigs.Set(file, index, text);
            }
            public double GetNumber() { double d; return ConfigPage.TryParseNumber(Current, out d) ? d : 0; }
            public void SetNumber(double d)
            {
                bool whole = !Current.Contains(".");
                FileConfigs.Set(file, index, whole ? Math.Round(d).ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.0#####", CultureInfo.InvariantCulture));
            }
            public Color GetColor() { Color c; return UiKit.TryParseHex(Current, out c) ? c : Color.white; }
            public void SetColor(Color c) { FileConfigs.Set(file, index, UiKit.ToHex(c)); }
            public string GetText() { return Current; }
            public void SetText(string t) { FileConfigs.Set(file, index, t); }
        }

        // Opens Windows Explorer with the file selected ("/select," is Explorer's own option for that).
        private sealed class ShowFile
        {
            private readonly string path;
            public ShowFile(string p) { path = p; }
            public void Click()
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); }
                catch (Exception e) { MelonLogger.Warning("[ModConfigs] Can't open Explorer: " + e.Message); }
            }
        }

        private sealed class ReloadBinding
        {
            private readonly Group group;
            public ReloadBinding(Group g) { group = g; }
            public void Click()
            {
                Reload(group);
                HubPanel.Later(false, false); // a button click: rebuild on the next frame (see HubPanel.Later)
            }
        }
    }
}
