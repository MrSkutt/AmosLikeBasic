using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AmosLikeBasic;

// Kör Tools/export_app.py från IDE:n och skickar utskrifterna vidare rad för rad.
// Exporten bygger appen med dotnet publish, så den kräver källkoden och .NET SDK.
public static class AppExporter
{
    public const string ScriptPath = "Tools/export_app.py";

    // Mappen med AmosLikeBasic.csproj och Tools/export_app.py, sökt uppåt från programmet
    // (t.ex. bin/Debug/net9.0/osx-arm64 -> projektmappen). null om källkoden inte finns.
    public static string? FindSourceRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AmosLikeBasic.csproj")) &&
                    File.Exists(Path.Combine(dir.FullName, ScriptPath)))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        return null;
    }

    // Kör exporten. Returnerar processens slutkod (0 = lyckades).
    public static async Task<int> RunAsync(string sourceRoot, string projectFile, string name, string platform,
        Action<string> log)
    {
        var args = new List<string>
        {
            "-u", // obuffrad utskrift, så att loggen uppdateras medan det pågår
            Path.Combine(sourceRoot, ScriptPath), projectFile, "--name", name, "--platform", platform
        };

        Exception? lastError = null;
        foreach (var python in OperatingSystem.IsWindows() ? new[] { "python", "py", "python3" } : new[] { "python3", "python" })
        {
            var psi = new ProcessStartInfo(python)
            {
                WorkingDirectory = sourceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["PATH"] = PathWithDotnet(psi.Environment["PATH"]);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";

            Process process;
            try
            {
                process = Process.Start(psi) ?? throw new Win32Exception($"Could not start {python}");
            }
            catch (Win32Exception ex)
            {
                // Den här Python-varianten finns inte, prova nästa
                lastError = ex;
                continue;
            }

            using (process)
            {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
        }

        throw new InvalidOperationException(
            "Python 3 was not found. Install it from python.org (on a Mac it comes with Xcode Command Line Tools).",
            lastError);
    }

    // Startas IDE:n från Finder är PATH kort och saknar ofta dotnet; lägg till vanliga platser
    private static string PathWithDotnet(string? path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(home, ".dotnet"),
            "/usr/local/share/dotnet",
            "/opt/homebrew/bin",
            "/usr/local/bin",
            @"C:\Program Files\dotnet"
        };
        var parts = (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && Directory.Exists(c) && !parts.Contains(c))
                parts.Add(c);
        return string.Join(Path.PathSeparator, parts);
    }

    // Visar en mapp i Finder / Utforskaren
    public static void OpenFolder(string folder)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { folder });
            else if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", folder);
            else Process.Start("xdg-open", new[] { folder });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open folder: {ex.Message}");
        }
    }
}
