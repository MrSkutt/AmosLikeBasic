    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    public static class ResourceLoader
    {
        public static string GetPath(string relativePath)
        {
            // 0. Om sökvägen redan är absolut och filen finns, använd den direkt (Fix för SAM PLAY med fullständiga sökvägar)
            if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            {
                return relativePath;
            }

            // 1. Tvätta bort inledande slash så Path.Combine inte tror det är en absolut sökväg
            var cleanPath = relativePath.TrimStart('/', '\\');

            // 2. Ta fram exakt var exekverbaren ligger (säkrare än AppContext.BaseDirectory i vissa bundles)
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            string baseDir = !string.IsNullOrEmpty(exePath) 
                ? Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory 
                : AppContext.BaseDirectory;

            // 3. Lista över möjliga platser att leta på
            var searchPaths = new List<string>
            {
                // Alt A: Standard macOS bundle resources (../Resources)
                Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", cleanPath)),
            
                // Alt B: Direkt bredvid exekverbaren (standard .NET publish beteende)
                Path.GetFullPath(Path.Combine(baseDir, cleanPath)),
            
                // Alt C: En Resources-mapp bredvid exekverbaren (om strukturen kopierats rakt av)
                Path.GetFullPath(Path.Combine(baseDir, "Resources", cleanPath)),
            
                // Alt D: Fallback till nuvarande arbetskatalog (Development)
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, cleanPath))
            };

            // 4. Loopa och se var filen finns
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    // Console.WriteLine($"[ResourceLoader] Found: {path}"); // Avkommentera för debug
                    return path;
                }
            }

            // 5. Om inget hittades, kasta fel med tydlig info om var vi letade
            var checkedPaths = string.Join("\n - ", searchPaths);
            throw new FileNotFoundException($"Resource not found: '{relativePath}'.\nChecked locations:\n - {checkedPaths}");
        }
    }