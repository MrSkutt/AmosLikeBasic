using System;
using Avalonia;

namespace AmosLikeBasic;

internal static class Program
{
    // Satt när appen ska köra ett färdigt spel i stället för IDE:n (se GamePlayer)
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