using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AmosLikeBasic;

// What the code editor knows about the language, read from the manual's reference chapters (built
// into the app): every command with its syntax and description, and every function from the
// functions table. Also the words the syntax colouring and the completion use.
public static class BasicHelp
{
    public sealed record Entry(string Name, string Syntax, string Text, bool IsFunction);

    private static readonly Lazy<Dictionary<string, Entry>> Entries = new(Load);

    // Words of the language that are not commands of their own
    private static readonly string[] ControlWords =
    {
        "IF", "THEN", "ELSE", "ELSEIF", "ENDIF", "END", "FOR", "TO", "STEP", "NEXT", "WHILE", "WEND", "DO", "LOOP",
        "REPEAT", "UNTIL", "SELECT", "CASE", "DEFAULT", "IS", "GOTO", "GOSUB", "RETURN", "PROC", "FUNCTION", "EXIT",
        "ON", "ERROR", "RESUME", "LOCAL", "CONST", "DIM", "DATA", "READ", "RESTORE", "AND", "OR", "XOR", "NOT", "MOD",
        "DIV", "LET", "REM", "STOP", "INC", "DEC", "SWAP"
    };

    // Every word that starts a statement: the commands of the manual and the control words
    public static IReadOnlyCollection<string> Keywords => KeywordSet.Value;
    private static readonly Lazy<HashSet<string>> KeywordSet = new(() =>
    {
        var set = new HashSet<string>(ControlWords, StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entries.Value.Values.Where(e => !e.IsFunction))
            foreach (Match m in Regex.Matches(e.Name, "[A-Z][A-Z0-9]*"))
                set.Add(m.Value);
        foreach (var cmd in AmosRunner.SubCommandWords.Keys) set.Add(cmd);
        return set;
    });

    public static IReadOnlyCollection<string> Functions => AmosRunner.FunctionNames;

    // The words that can follow a command (SPRITE → POS, ON, MOVE ...); empty if it has none
    public static IReadOnlyCollection<string> SubCommandsOf(string command) =>
        AmosRunner.SubCommandWords.TryGetValue(command, out var words) ? words : Array.Empty<string>();

    // The help for a word, trying the two-word name first ("MAP OBJECTS" before "MAP")
    public static Entry? Find(string word, string? nextWord = null)
    {
        var all = Entries.Value;
        if (nextWord != null && all.TryGetValue(word + " " + nextWord, out var pair)) return pair;
        return all.TryGetValue(word, out var e) ? e : null;
    }

    private static Dictionary<string, Entry> Load()
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ReadCommands(ReadManual("Manual.42-commands.md"), map);
            ReadFunctions(ReadManual("Manual.43-functions.md"), map);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Could not read the manual for help: " + ex.Message);
        }
        return map;
    }

    private static string ReadManual(string name)
    {
        using var stream = typeof(BasicHelp).Assembly.GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // "### NAME" sections: the first ```text block is the syntax, the rest the description
    private static void ReadCommands(string md, Dictionary<string, Entry> map)
    {
        var parts = Regex.Split(md, @"^### ", RegexOptions.Multiline);
        foreach (var part in parts.Skip(1))
        {
            int nl = part.IndexOf('\n');
            if (nl < 0) continue;
            string heading = part[..nl].Trim();
            string body = part[(nl + 1)..];
            int end = body.IndexOf("\n## ", StringComparison.Ordinal);   // the next letter's heading
            if (end >= 0) body = body[..end];

            string syntax = "";
            var code = Regex.Match(body, @"```text\s*\n(.*?)\n```", RegexOptions.Singleline);
            if (code.Success)
            {
                syntax = code.Groups[1].Value.Trim();
                body = body.Remove(code.Index, code.Length);
            }
            var entry = new Entry(heading, syntax, Plain(body), false);

            // "MAP OBJECTS" is found by its name; "DO ... LOOP" and "ON ... GOTO" by each word
            var words = Regex.Matches(heading, "[A-Z][A-Z0-9]*").Select(m => m.Value).ToList();
            if (words.Count == 2 && !heading.Contains("...")) map.TryAdd(heading, entry);
            foreach (var w in words) map.TryAdd(w, entry);
        }
    }

    // "| `NAME(args)` | what it gives | example | result |" rows
    private static void ReadFunctions(string md, Dictionary<string, Entry> map)
    {
        foreach (Match m in Regex.Matches(md, @"^\|\s*`([A-Z][A-Z0-9$]*)(\([^`]*\))?`\s*\|\s*([^|]*)\|\s*([^|]*)\|\s*([^|]*)\|", RegexOptions.Multiline))
        {
            string name = m.Groups[1].Value;
            string syntax = name + m.Groups[2].Value;
            string text = Plain(m.Groups[3].Value);
            string example = Plain(m.Groups[4].Value), result = Plain(m.Groups[5].Value);
            if (example.Length > 0) text += $"\n\nExample: {example}" + (result.Length > 0 ? $"  →  {result}" : "");
            map[name] = new Entry(name, syntax, text, true);
        }
    }

    // Markdown as plain text: no backticks or stars, tables as "a – b", code kept with an indent
    public static string Plain(string md)
    {
        var sb = new StringBuilder();
        bool inCode = false;
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            string line = raw;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (inCode) { sb.Append("    ").AppendLine(line); continue; }
            if (Regex.IsMatch(line, @"^\s*\|[\s\-|:]+\|\s*$")) continue;   // a table's dividing row
            if (line.TrimStart().StartsWith('|'))
                line = string.Join("  –  ", line.Trim().Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0));
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = line.Replace("`", "").Replace("**", "").Replace("*", "");
            sb.AppendLine(line);
        }
        return Regex.Replace(sb.ToString().Trim(), @"\n{3,}", "\n\n");
    }

    // The first sentence or line of a description, for a tooltip
    public static string Short(Entry e)
    {
        string text = e.Text;
        int cut = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (cut > 0) text = text[..cut];
        if (text.Length > 260) text = text[..257].TrimEnd() + "...";
        return text;
    }
}
