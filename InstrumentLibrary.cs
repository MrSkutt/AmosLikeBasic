using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AmosLikeBasic;

// The tracker's instrument library: Sound/Instruments/<Category>/<Name>.amosinst, one instrument per
// file as JSON (the same as in a song). The folders are the categories; a new folder is a new one.
// The ready-made instruments are written by Tools/build_instruments.py.
public static class InstrumentLibrary
{
    public const string Extension = ".amosinst";

    // The categories in the order a synth lists them; others (your own) come after, A – Z
    private static readonly string[] Order =
        { "Bass", "Lead", "Pad", "Keys", "Pluck", "Brass & Strings", "Chip", "Drums", "Percussion", "FX" };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // The library in the Sound folder (Documents/AmosLikeBasic/Sound when installed), or the app's own
    public static string Folder
    {
        get
        {
            string own = Path.Combine(Workspace.Sound, "Instruments");
            if (Directory.Exists(own)) return own;
            string app = Path.Combine(AppContext.BaseDirectory, "Sound", "Instruments");
            return Directory.Exists(app) ? app : own;
        }
    }

    public static List<string> Categories()
    {
        if (!Directory.Exists(Folder)) return new();
        return Directory.GetDirectories(Folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => Directory.EnumerateFiles(Path.Combine(Folder, name), "*" + Extension).Any())
            .OrderBy(name => Array.IndexOf(Order, name) is var i && i >= 0 ? i : Order.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The instruments of a category: (name, file), A – Z
    public static List<(string name, string file)> Instruments(string category)
    {
        string dir = Path.Combine(Folder, category);
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir, "*" + Extension)
            .Select(f => (Path.GetFileNameWithoutExtension(f), f))
            .OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static TrackerInstrument Load(string file)
    {
        var inst = JsonSerializer.Deserialize<TrackerInstrument>(File.ReadAllText(file), Json)
                   ?? throw new InvalidDataException($"{Path.GetFileName(file)} is not an instrument");
        inst.Table ??= new();
        inst.Lfo1 ??= new();
        inst.Lfo2 ??= new();
        if (string.IsNullOrWhiteSpace(inst.Name)) inst.Name = Path.GetFileNameWithoutExtension(file);
        return inst;
    }

    public static void Save(TrackerInstrument inst, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(inst, Json));
    }

    // Saves into a category of the library (in your own Sound folder); returns the file
    public static string SaveToLibrary(TrackerInstrument inst, string category)
    {
        string name = SafeName(string.IsNullOrWhiteSpace(inst.Name) ? "Instrument" : inst.Name);
        string file = Path.Combine(Workspace.Sound, "Instruments", SafeName(category), name + Extension);
        Save(inst, file);
        return file;
    }

    // A copy that does not share lists or LFOs with the original
    public static TrackerInstrument Copy(TrackerInstrument inst) =>
        JsonSerializer.Deserialize<TrackerInstrument>(JsonSerializer.Serialize(inst, Json), Json)!;

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().Length == 0 ? "Instrument" : name.Trim();
    }
}
