using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AmosLikeBasic.Tests;

// The tracker's menu: what the buttons do and more, also when the tracker runs on its own
public class TrackerMenuTests
{
    private static MenuItem Item(Window w, params string[] path)
    {
        var items = w.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>();
        MenuItem? found = null;
        foreach (var header in path)
        {
            found = items.First(i => (i.Header as string)?.Replace("_", "") == header);
            items = (found.ItemsSource ?? found.Items).OfType<MenuItem>();
        }
        return found!;
    }

    [AvaloniaFact]
    public void The_menu_has_the_files_the_song_the_themes_and_about()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var path in new[] { new[] { "File", "Open..." }, new[] { "File", "Save as XM (FastTracker 2)..." },
                     new[] { "File", "Save as WAV..." }, new[] { "Song", "Play song" }, new[] { "Help", "About AMOS Tracker..." } })
            Assert.NotNull(Item(w, path));
        Assert.Equal(10, Item(w, "View", "Theme").ItemsSource!.Cast<MenuItem>().Count());

        // A theme chosen here goes to the program that opened the tracker
        string? chosen = null;
        TrackerWindow.ThemeChanger = key => chosen = key;
        try
        {
            Item(w, "View", "Theme", "Dark Pro").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("DarkPro", chosen);
        }
        finally { TrackerWindow.ThemeChanger = null; }
        w.Close();
    }
}
