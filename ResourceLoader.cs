    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    public static class ResourceLoader
    {
        public static string GetPath(string relativePath)
        {
            // 0. If the path is already absolute and the file exists, use it directly (fix for SAM PLAY with full paths)
            if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            {
                return relativePath;
            }

            // 1. Strip a leading slash so Path.Combine does not think it is an absolute path
            var cleanPath = relativePath.TrimStart('/', '\\');

            // 2. Find out exactly where the executable is (safer than AppContext.BaseDirectory in some bundles)
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            string baseDir = !string.IsNullOrEmpty(exePath) 
                ? Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory 
                : AppContext.BaseDirectory;

            // 3. List of possible places to look
            var searchPaths = new List<string>
            {
                // Alt A: Standard macOS bundle resources (../Resources)
                Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", cleanPath)),
            
                // Alt B: Directly next to the executable (standard .NET publish behaviour)
                Path.GetFullPath(Path.Combine(baseDir, cleanPath)),
            
                // Alt C: A Resources folder next to the executable (if the structure was copied as is)
                Path.GetFullPath(Path.Combine(baseDir, "Resources", cleanPath)),
            
                // Alt D: Fall back to the current working directory (development)
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, cleanPath))
            };

            // 4. Loop and see where the file is
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    // Console.WriteLine($"[ResourceLoader] Found: {path}"); // Uncomment for debugging
                    return path;
                }
            }

            // 5. If nothing was found, throw an error that says clearly where we looked
            var checkedPaths = string.Join("\n - ", searchPaths);
            throw new FileNotFoundException($"Resource not found: '{relativePath}'.\nChecked locations:\n - {checkedPaths}");
        }
    }