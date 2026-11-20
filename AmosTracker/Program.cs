using System;
using Avalonia;

namespace AmosLikeBasic;

// AMOS Tracker: the tracker on its own. A song, MOD, XM or YM file given on the command line is opened.
internal static class Program
{
    public static string? FileToOpen { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        FileToOpen = args.Length > 0 ? args[0] : null;
        Workspace.Setup();
        AppBuilder.Configure<TrackerApp>()
            .UsePlatformDetect()
            .UseSkia()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
