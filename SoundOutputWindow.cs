using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AmosLikeBasic;

// View → Sound Output: the sound card and which of its channels the sound goes to. For cards with
// many channels, such as a mixer that listens on channels 17 – 18 instead of 1 – 2.
public class SoundOutputWindow : Window
{
    public SoundOutputWindow()
    {
        Title = "Sound Output";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");

        var current = AudioOutput.Current;
        const string systemDefault = "The system's default output";
        var devices = new[] { systemDefault }.Concat(AudioOutput.Devices()).ToList();
        var deviceBox = new ComboBox { ItemsSource = devices, HorizontalAlignment = HorizontalAlignment.Stretch };
        deviceBox.SelectedItem = string.IsNullOrEmpty(current.Device) || !devices.Contains(current.Device) ? systemDefault : current.Device;

        var pairs = Enumerable.Range(1, AudioOutput.MaxPair).Select(p => $"Channels {p * 2 - 1} – {p * 2}").ToList();
        var pairBox = new ComboBox { ItemsSource = pairs, SelectedIndex = current.Pair - 1, HorizontalAlignment = HorizontalAlignment.Stretch };

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            string? device = deviceBox.SelectedItem as string;
            AudioOutput.Save(new AudioOutput.Settings
            {
                Device = device == systemDefault ? null : device,
                Pair = pairBox.SelectedIndex + 1
            });
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);

        var title = new TextBlock { Text = "SOUND OUTPUT", FontWeight = FontWeight.Bold };
        title[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosTitleFg");
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                title,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    Text = "Most sound cards play on channels 1 – 2. A card or mixer with many channels may listen on others " +
                           "(the system's own stereo setting is not used here), so choose them below. " +
                           "The choice is used from the next start."
                },
                new TextBlock { Text = "Sound card", Margin = new Thickness(0, 6, 0, 0) },
                deviceBox,
                new TextBlock { Text = "Channels" },
                pairBox,
                new TextBlock { Text = $"Now: {AudioOutput.Description}", FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10,
                    Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel }
                }
            }
        };
    }
}
