using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AmosLikeBasic;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.GamePath is { } gamePath)
            {
                // Player mode: only the game window, the program starts straight away
                var (program, title) = GamePlayer.LoadGame(gamePath);
                var window = new ScreenWindow { Title = title, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                var player = new GamePlayer(window);
                window.Opened += async (_, _) => await player.RunAsync(program);
                desktop.MainWindow = window;
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}