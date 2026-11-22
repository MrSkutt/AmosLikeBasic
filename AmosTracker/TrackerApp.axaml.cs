using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AmosLikeBasic;

public partial class TrackerApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The sound card and the theme chosen in AMOS Like Basic
            AudioOutput.Start();
            var config = new ConfigService("AmosProject");
            config.Load();
            ThemeResources.Apply(AmosThemes.ByName(config.Config.DefaultTheme));

            TrackerWindow.RecentSettings = "AmosTracker";
            var tracker = new TrackerWindow();
            if (Program.FileToOpen is { } file) tracker.Opened += (_, _) => tracker.LoadFile(file);
            desktop.MainWindow = tracker;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
