using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace AmosLikeBasic;

public enum WorkspaceFolder { Projects, Graphics, Sound, Export }

// The folder that relative paths in programs count from ("Graphics/hero.png", files written with
// OPEN OUT, songs saved by the tracker ...), and the four folders the file dialogs start in.
//
// Run from the source code, it is the repository folder. Installed (a release download), it is
// Documents/AmosLikeBasic: on every start the example projects, graphics and sounds are copied
// there and an Export folder is made, so you can change them. A file already there is only
// replaced when a new version of the app brings a changed one and you have not changed yours: the
// files the app copied are listed with a fingerprint in .copied.json, and a file whose fingerprint
// no longer matches is yours and is left alone. The folders can be moved in Project → Folders...; the choice is kept in
// folders.json next to the IDE's config.json. A program's "Graphics/..." and "Sound/..." are then
// looked for in the chosen folders first.
public static class Workspace
{
    public static string Root { get; private set; } = Environment.CurrentDirectory;

    public static bool Installed { get; private set; }

    private static readonly Dictionary<WorkspaceFolder, string> Chosen = new();
    private static string? _settingsFile;

    public static string Projects => FolderOf(WorkspaceFolder.Projects);
    public static string Graphics => FolderOf(WorkspaceFolder.Graphics);
    public static string Sound => FolderOf(WorkspaceFolder.Sound);
    public static string Export => FolderOf(WorkspaceFolder.Export);

    public static string FolderOf(WorkspaceFolder folder) =>
        Chosen.TryGetValue(folder, out var path) ? path : DefaultOf(folder);

    public static string DefaultOf(WorkspaceFolder folder) => Path.Combine(Root, folder.ToString());

    public static void Setup() =>
        Setup(AppExporter.FindSourceRoot(), AppContext.BaseDirectory,
              Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
              Path.Combine(ConfigService.GetConfigFolder("AmosProject"), "folders.json"));

    // sourceRoot is the repository when run from the source code, otherwise null.
    // appDir is where the app's own files are, documents the user's Documents folder,
    // settingsFile where the chosen folders are kept (null: not kept).
    internal static void Setup(string? sourceRoot, string appDir, string documents, string? settingsFile = null)
    {
        _settingsFile = settingsFile;
        Chosen.Clear();
        if (sourceRoot != null)
        {
            Installed = false;
            Root = sourceRoot;
        }
        else
        {
            Installed = true;
            Root = Path.Combine(documents, "AmosLikeBasic");
            try
            {
                Directory.CreateDirectory(Root);
                var copied = LoadCopied();
                foreach (var folder in new[] { "Projects", "Graphics", "Sound" })
                    CopyExamples(Path.Combine(appDir, folder), folder, copied);
                SaveCopied(copied);
                Directory.CreateDirectory(DefaultOf(WorkspaceFolder.Export));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not set up {Root}: {ex.Message}");
            }
        }

        LoadChoices();
        if (Directory.Exists(Root)) Environment.CurrentDirectory = Root;
    }

    // Sets a folder (null or the default: back to the default) and keeps the choice
    public static void Choose(WorkspaceFolder folder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || SamePath(path, DefaultOf(folder))) Chosen.Remove(folder);
        else Chosen[folder] = Path.GetFullPath(path);
        SaveChoices();
    }

    // Where a program's file is: "Graphics/..." and "Sound/..." in the chosen folders when they
    // have been moved, otherwise null (then the usual places are searched)
    public static string? MovedPath(string relativePath)
    {
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var folder in new[] { WorkspaceFolder.Graphics, WorkspaceFolder.Sound, WorkspaceFolder.Projects })
        {
            var prefix = folder + "/";
            if (Chosen.ContainsKey(folder) && clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(FolderOf(folder), clean[prefix.Length..]);
        }
        return null;
    }

    // The folder for a file dialog to start in (made if it is missing)
    public static async Task<IStorageFolder?> StartFolderAsync(IStorageProvider storage, WorkspaceFolder folder, string? sub = null)
    {
        string dir = FolderOf(folder);
        if (sub != null && Directory.Exists(Path.Combine(dir, sub))) dir = Path.Combine(dir, sub);
        try { Directory.CreateDirectory(dir); } catch { return null; }
        return await storage.TryGetFolderFromPathAsync(dir);
    }

    private static void LoadChoices()
    {
        if (_settingsFile == null || !File.Exists(_settingsFile)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_settingsFile));
            if (saved == null) return;
            foreach (var (name, path) in saved)
                if (Enum.TryParse<WorkspaceFolder>(name, out var folder) && !string.IsNullOrWhiteSpace(path))
                    Chosen[folder] = path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read {_settingsFile}: {ex.Message}");
        }
    }

    private static void SaveChoices()
    {
        if (_settingsFile == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            var saved = new Dictionary<string, string>();
            foreach (var (folder, path) in Chosen) saved[folder.ToString()] = path;
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not write {_settingsFile}: {ex.Message}");
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    // Copies the app's files into the working folder (relative is the path under Root, with /):
    // missing files, and newer versions of files you have not changed. Your own changes are never overwritten.
    private static void CopyExamples(string from, string relative, Dictionary<string, string> copied)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(Path.Combine(Root, relative));
        foreach (var file in Directory.GetFiles(from))
        {
            string key = relative + "/" + Path.GetFileName(file);
            string dest = Path.Combine(Root, relative, Path.GetFileName(file));
            string source = Fingerprint(file);
            if (!File.Exists(dest))
            {
                File.Copy(file, dest);
                copied[key] = source;
            }
            else if (copied.TryGetValue(key, out var asCopied) && asCopied != source && Fingerprint(dest) == asCopied)
            {
                // Still as the app left it, and the new version has changed it: update it
                File.Copy(file, dest, overwrite: true);
                copied[key] = source;
            }
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyExamples(dir, relative + "/" + Path.GetFileName(dir), copied);
    }

    private static string CopiedFile => Path.Combine(Root, ".copied.json");

    private static Dictionary<string, string> LoadCopied()
    {
        try
        {
            if (File.Exists(CopiedFile))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CopiedFile)) ?? new();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not read {CopiedFile}: {ex.Message}"); }
        return new();
    }

    private static void SaveCopied(Dictionary<string, string> copied)
    {
        try { File.WriteAllText(CopiedFile, JsonSerializer.Serialize(copied)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not write {CopiedFile}: {ex.Message}"); }
    }

    private static string Fingerprint(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
