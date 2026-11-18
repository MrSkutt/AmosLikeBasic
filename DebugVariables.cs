using System;
using System.Collections.Generic;
using System.Globalization;

namespace AmosLikeBasic;

// The running program's variables for the IDE's variable watch. They may only be read and changed
// while the program is paused: the program then stands still, waiting for the next step, so the
// IDE is the only one touching them.
public sealed class DebugVariables
{
    internal Dictionary<string, object>? Live;
    internal HashSet<string>? Constants;

    // A copy of the variables as they are now (the program's own names, without the hidden ones)
    public Dictionary<string, object> Snapshot()
    {
        var copy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (Live == null) return copy;
        foreach (var (name, value) in Live)
            if (!name.StartsWith("__")) copy[name] = value;
        return copy;
    }

    public object? Get(string name) => Live != null && Live.TryGetValue(name, out var v) ? v : null;

    // Sets a variable from what was typed; returns an error message, or null when it went well
    public string? Set(string name, string text)
    {
        if (Live == null) return "The program is not running";
        if (Constants?.Contains(name) == true) return $"{name} is a constant (CONST) and cannot be changed";
        if (Live.TryGetValue(name, out var old) && old is IAmosArray) return $"{name} is an array: change its elements";
        var (value, error) = Parse(text, name.EndsWith('$') || old is string);
        if (error != null) return error;
        if (name.EndsWith('%') && value is double d) value = Math.Truncate(d);
        Live[name] = value!;
        return null;
    }

    // Sets one element of an array
    public static string? SetElement(IAmosArray array, int[] indices, string text)
    {
        var (value, error) = Parse(text, array is AmosStringArray);
        if (error != null) return error;
        try { array.Set(value!, indices); }
        catch (Exception ex) { return ex.Message; }
        return null;
    }

    // Text is taken as it is (quotes around it are dropped); numbers as in BASIC: 12, -3.5, $FF, %1010
    public static (object? value, string? error) Parse(string text, bool isString)
    {
        text = text.Trim();
        if (isString)
            return (text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text, null);

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return (n, null);
        try
        {
            if (text.StartsWith('$') || text.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
                return ((double)Convert.ToInt64(text.TrimStart('$', '&', 'H', 'h'), 16), null);
            if (text.StartsWith('%'))
                return ((double)Convert.ToInt64(text[1..], 2), null);
        }
        catch (FormatException) { }
        return (null, $"\"{text}\" is not a number");
    }

    // How a value is shown in the watch
    public static string Show(object? value) => value switch
    {
        string s => $"\"{s}\"",
        double d => d.ToString("G10", CultureInfo.InvariantCulture),
        IAmosArray a => $"DIM ({string.Join(", ", Array.ConvertAll(a.Dimensions, x => x - 1))})",
        null => "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
}
