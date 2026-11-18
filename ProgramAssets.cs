using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AmosLikeBasic;

// The pictures a BASIC program loads, found by reading its text, so that the editors can offer them
// with the sizes the program uses:
//   SPRITE LOAD 1, "Graphics/Anim/Anim_Beast.png", 32, 56, 11
//   IMAGE LOAD 2, "Graphics/ForestHop/beetle.png", 32, 32, 3
//   TILE LOAD "Graphics/ForestHop/tiles.png", 32, 32
//   MAP LOAD "Graphics/test.amosmap"
// Sizes written as expressions (SPRITE LOAD 1, F$, W, H, N) are not worked out; they are left out.
public static class ProgramAssets
{
    public sealed record Asset(string Kind, string Number, string File, int? FrameWidth, int? FrameHeight, int? Frames, int Line)
    {
        public bool HasFrameSize => FrameWidth > 0 && FrameHeight > 0;

        // As it is shown in the editors' lists
        public string Label
        {
            get
            {
                string size = HasFrameSize ? $"  {FrameWidth}×{FrameHeight}" + (Frames > 1 ? $", {Frames} frames" : "") : "";
                string no = Number.Length > 0 ? " " + Number : "";
                return $"line {Line}: {Kind} LOAD{no}  {Path.GetFileName(File)}{size}";
            }
        }
    }

    // KIND LOAD [number,] "file" [, width, height [, count]]
    private static readonly Regex LoadRx = new(
        @"\b(SPRITE|IMAGE|TILE|MAP)\s+LOAD\s+(?:([A-Za-z0-9_]+)\s*,\s*)?""([^""]+)""((?:\s*,\s*[^,:;]+){0,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Asset> Find(string program)
    {
        var list = new List<Asset>();
        var lines = (program ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('\'') ||
                trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (Match m in LoadRx.Matches(line))
            {
                // Stop at a comment after the command
                int comment = line.IndexOf(';', m.Index);
                if (comment >= 0 && comment < m.Index + m.Length && !InQuotes(line, comment)) continue;

                var numbers = new List<int?>();
                foreach (var part in m.Groups[4].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    numbers.Add(int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null);
                int? w = numbers.Count > 0 ? numbers[0] : null;
                int? h = numbers.Count > 1 ? numbers[1] : null;
                int? count = numbers.Count > 2 ? numbers[2] : null;

                // Without sizes in the program the file name can tell them: Name_W32_H56_B11.png
                if (w == null && h == null)
                {
                    var fromName = FromFileName(m.Groups[3].Value);
                    if (fromName != null) (w, h, count) = fromName.Value;
                }

                list.Add(new Asset(m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value,
                                   m.Groups[3].Value, w, h, count, i + 1));
            }
        }
        return list;
    }

    // The file name convention Name_W32_H56_B11 (B or F for the number of frames)
    public static (int? w, int? h, int? frames)? FromFileName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var w = Regex.Match(name, @"_W(\d+)", RegexOptions.IgnoreCase);
        var h = Regex.Match(name, @"_H(\d+)", RegexOptions.IgnoreCase);
        var f = Regex.Match(name, @"_[BF](\d+)", RegexOptions.IgnoreCase);
        if (!w.Success && !h.Success && !f.Success) return null;
        return (w.Success ? int.Parse(w.Groups[1].Value) : null,
                h.Success ? int.Parse(h.Groups[1].Value) : null,
                f.Success ? int.Parse(f.Groups[1].Value) : null);
    }

    private static bool InQuotes(string line, int index)
    {
        bool q = false;
        for (int i = 0; i < index; i++) if (line[i] == '"') q = !q;
        return q;
    }
}
