using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace AmosLikeBasic
{
    // The save dialog, with the file's name as it should be. A name suggested with its extension gets it
    // twice from some dialogs (the Mac's hides the extension in the name field and adds it again:
    // "SONG.DHS.dhs", "new_song.amostrk.amostrk"). So the name is suggested without it, the dialog is told
    // the extension, and the name it gives back has the extension once.
    public static class SaveDialog
    {
        // Where to save, or null when cancelled
        public static async Task<string?> AskPathAsync(IStorageProvider storage, FilePickerSaveOptions options)
        {
            var extensions = Extensions(options);
            if (extensions.Count > 0)
            {
                options.DefaultExtension ??= extensions[0].TrimStart('.');
                if (options.SuggestedFileName is { } name) options.SuggestedFileName = WithoutExtension(name, extensions);
            }
            var file = await storage.SaveFilePickerAsync(options);
            var path = file?.TryGetLocalPath();
            return path == null ? null : FixName(path, extensions);
        }

        // The same, as a storage file (made empty if it is not there yet, so that it can be had)
        public static async Task<IStorageFile?> AskFileAsync(IStorageProvider storage, FilePickerSaveOptions options)
        {
            var path = await AskPathAsync(storage, options);
            if (path == null) return null;
            if (!File.Exists(path)) await File.WriteAllBytesAsync(path, Array.Empty<byte>());
            return await storage.TryGetFileFromPathAsync(path);
        }

        // The extensions the dialog offers (".dhs"), the first the one it adds
        public static List<string> Extensions(FilePickerSaveOptions options) =>
            (options.FileTypeChoices ?? Array.Empty<FilePickerFileType>())
            .SelectMany(t => t.Patterns ?? Array.Empty<string>())
            .Where(p => p.StartsWith("*.") && p.Length > 2 && p.IndexOf('*', 1) < 0)
            .Select(p => p[1..])
            .ToList();

        // A name with one of the extensions at its end once, and with the first one if it has none
        public static string FixName(string path, IReadOnlyList<string> extensions)
        {
            if (extensions.Count == 0) return path;
            string folder = Path.GetDirectoryName(path) ?? "", name = Path.GetFileName(path);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var a in extensions)
                    foreach (var b in extensions)
                        if (name.Length > a.Length + b.Length && name.EndsWith(a + b, StringComparison.OrdinalIgnoreCase))
                        {
                            name = name[..^b.Length];
                            changed = true;
                        }
            }
            if (!extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) name += extensions[0];
            return Path.Combine(folder, name);
        }

        private static string WithoutExtension(string name, IReadOnlyList<string> extensions)
        {
            foreach (var e in extensions)
                if (name.Length > e.Length && name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return name[..^e.Length];
            return name;
        }
    }
}
