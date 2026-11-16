using System;
using System.IO;

namespace AmosLikeBasic;

// The folder that relative paths in programs count from ("Graphics/hero.png", files written with
// OPEN OUT, songs saved by the tracker ...).
//
// Run from the source code, it is the repository folder. Installed (a release download), it is
// Documents/AmosLikeBasic, where the example projects are copied on the first start. The images
// and sounds that come with the app are still found inside the app by ResourceLoader, so the
// examples work without copying them; your own files go in Documents/AmosLikeBasic.
public static class Workspace
{
    public static string Root { get; private set; } = Environment.CurrentDirectory;

    public static string Projects => Path.Combine(Root, "Projects");

    public static bool Installed { get; private set; }

    public static void Setup() =>
        Setup(AppExporter.FindSourceRoot(), AppContext.BaseDirectory,
              Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    // sourceRoot is the repository when run from the source code, otherwise null.
    // appDir is where the app's own files are, documents the user's Documents folder.
    internal static void Setup(string? sourceRoot, string appDir, string documents)
    {
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
                CopyMissing(Path.Combine(appDir, "Projects"), Projects);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not set up {Root}: {ex.Message}");
            }
        }

        if (Directory.Exists(Root)) Environment.CurrentDirectory = Root;
    }

    // Copies the files that are not already there, so that your own changes are never overwritten
    private static void CopyMissing(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            string dest = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(dest)) File.Copy(file, dest);
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
