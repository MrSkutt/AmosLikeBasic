using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using AmosLikeBasic;

namespace AmosLikeBasic.Tests;

// What happened when a BASIC program ran
public sealed class RunResult
{
    public List<string> Errors { get; } = new();
    public List<string> Prints { get; } = new();
    public Dictionary<string, object> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Completed { get; set; }
    public double Seconds { get; set; }

    public string Describe() => Errors.Count == 0 ? "no errors" : string.Join("\n", Errors);
}

// Runs a BASIC program in the real interpreter, headless, like the game window would.
// Must be called on the Avalonia UI thread ([AvaloniaFact]), since it pumps the dispatcher.
public static class BasicRunner
{
    public static string ProgramOf(string amosprojPath) =>
        JsonDocument.Parse(File.ReadAllText(amosprojPath)).RootElement.GetProperty("ProgramText").GetString()!;

    public static RunResult Run(string program, double maxSeconds, IReadOnlyCollection<string>? keysDown = null)
    {
        var result = new RunResult();
        var keys = new HashSet<string>(keysDown ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Files the program writes end up in a folder of their own; images and sounds are
        // found in the repository, as in the editor
        string oldDir = Environment.CurrentDirectory;
        string work = Path.Combine(Path.GetTempPath(), "amos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        LinkResources(work);
        Environment.CurrentDirectory = work;

        var graphics = new AmosGraphics { OnError = e => { lock (result) result.Errors.Add("Graphics: " + e); } };
        graphics.Screen(640, 480);
        var view = new AmosGpuView { Graphics = graphics, Width = 640, Height = 480 };
        var window = new Window { Content = view, Width = 640, Height = 480 };
        window.Show();

        var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => AmosRunner.ExecuteAsync(program,
            line =>
            {
                lock (result)
                {
                    if (line.StartsWith("@@PRINT ")) result.Prints.Add(line[8..]);
                    else if (!line.StartsWith("@@") && line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                        result.Errors.Add(line);
                }
                return Task.CompletedTask;
            },
            () => Task.CompletedTask, graphics, () => { },
            () => { lock (keys) return keys.FirstOrDefault() ?? ""; },
            k => { lock (keys) return keys.Contains(k); },
            null, cts.Token,
            v => { lock (result) result.Variables = new Dictionary<string, object>(v, StringComparer.OrdinalIgnoreCase); },
            _ => Task.CompletedTask,
            () => Task.FromResult("")));

        try
        {
            while (sw.Elapsed.TotalSeconds < maxSeconds && !task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(4);
            }
            result.Completed = task.IsCompleted;
            result.Seconds = sw.Elapsed.TotalSeconds;
            if (task.IsFaulted) lock (result) result.Errors.Add("Crash: " + task.Exception!.GetBaseException());
        }
        finally
        {
            cts.Cancel();
            try { task.Wait(3000); } catch { /* cancelled */ }
            ChiptuneSynth.StopMusic();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Environment.CurrentDirectory = oldDir;
            try { Directory.Delete(work, true); } catch { /* best effort */ }
        }
        return result;
    }

    // Links Graphics, Sound and Projects into the work folder, so that relative paths work
    private static void LinkResources(string work)
    {
        foreach (var name in new[] { "Graphics", "Sound", "Projects" })
        {
            string target = Repo.PathOf(name);
            string link = Path.Combine(work, name);
            try { Directory.CreateSymbolicLink(link, target); }
            catch { CopyDirectory(target, link); }   // e.g. Windows without symlink rights
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
