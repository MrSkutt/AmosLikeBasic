using System;
using Avalonia;

namespace AmosLikeBasic;

internal static class Program
{
    // Set when the app should run a finished game instead of the IDE (see GamePlayer)
    public static string? GamePath { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        GamePath = GamePlayer.FindGame(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .LogToTrace();
}