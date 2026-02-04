using System;
using System.IO;

public static class ResourceLoader
{
    /// <summary>
    /// Returnerar fullständig sökväg till en resursfil.
    /// Fungerar både från .app på macOS och i utvecklingsmiljö.
    /// </summary>
    public static string GetPath(string relativePath)
    {
        string baseDir = AppContext.BaseDirectory;

        // macOS .app Resources-mapp
        string resourcePath = Path.Combine(baseDir, "..", "Resources", relativePath);
        resourcePath = Path.GetFullPath(resourcePath);

        if (File.Exists(resourcePath))
            return resourcePath;

        // Fallback: utvecklingsmiljö
        if (File.Exists(relativePath))
            return Path.GetFullPath(relativePath);

        throw new FileNotFoundException($"Resource not found: {relativePath}");
    }
}