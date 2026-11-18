using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace AmosLikeBasic;

// View → CRT Settings: sliders for the picture tube effect. Every change shows straight away in the
// game window, and the line at the bottom is the same setting as a SCREEN CRT command for a program.
public class CrtSettingsWindow : Window
{
    private readonly AmosGraphics _gfx;
    private readonly Action _redraw;
    private readonly CheckBox _on;
    private readonly Slider[] _sliders;
    private readonly TextBlock[] _values;
    private readonly TextBox _basicLine;
    private bool _updating;

    // Name, lowest, highest and what it does, in the order SCREEN CRT takes them (percent)
    private static readonly (string name, float min, float max, string tip)[] Settings =
    {
        ("Scanlines", 0, 100, "Dark lines between the rows of the picture"),
        ("Curvature", 0, 100, "How curved the glass is"),
        ("Phosphor mask", 0, 100, "The red, green and blue stripes of the tube"),
        ("Vignette", 0, 100, "Darker towards the edges and corners"),
        ("Brightness", 50, 200, "100 is the picture as it is; the standard 122 makes up for the lines and stripes"),
        ("Softness", 0, 100, "0 gives sharp pixels, 100 soft ones as on a TV"),
    };

    // Ready-made looks: scanlines, curvature, mask, vignette, brightness, softness
    private static readonly (string name, float[] values)[] Presets =
    {
        ("Standard", new float[] { 55, 50, 50, 50, 122, 100 }),
        ("Subtle", new float[] { 25, 15, 20, 25, 108, 60 }),
        ("Arcade monitor", new float[] { 75, 30, 70, 40, 135, 70 }),
        ("Old TV", new float[] { 60, 90, 60, 90, 128, 100 }),
        ("Sharp and flat", new float[] { 45, 0, 0, 0, 112, 0 }),
    };

    public CrtSettingsWindow(AmosGraphics gfx, Action redraw)
    {
        _gfx = gfx;
        _redraw = redraw;
        Title = "CRT Settings";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };

        _on = new CheckBox { Content = "CRT effect on (F8)", IsChecked = gfx.CrtEnabled, FontWeight = FontWeight.Bold };
        _on.IsCheckedChanged += (_, _) => { if (!_updating) Apply(); };
        panel.Children.Add(_on);

        var presets = new ComboBox
        {
            PlaceholderText = "Ready-made looks...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Presets.Select(p => p.name).ToList()
        };
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex < 0) return;
            Show(Presets[presets.SelectedIndex].values);
            _on.IsChecked = true;
            Apply();
        };
        panel.Children.Add(presets);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,44"), Margin = new Thickness(0, 4, 0, 0) };
        _sliders = new Slider[Settings.Length];
        _values = new TextBlock[Settings.Length];
        for (int i = 0; i < Settings.Length; i++)
        {
            var (name, min, max, tip) = Settings[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(label, tip);
            var slider = new Slider { Minimum = min, Maximum = max, Margin = new Thickness(8, 0) };
            ToolTip.SetTip(slider, tip);
            var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            slider.ValueChanged += (_, _) => { if (!_updating) Apply(); };
            Grid.SetRow(label, i); Grid.SetRow(slider, i); Grid.SetRow(value, i);
            Grid.SetColumn(slider, 1); Grid.SetColumn(value, 2);
            grid.Children.Add(label); grid.Children.Add(slider); grid.Children.Add(value);
            _sliders[i] = slider;
            _values[i] = value;
        }
        panel.Children.Add(grid);

        panel.Children.Add(new TextBlock { Text = "The same in a program:", FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        _basicLine = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Courier New, Monospace"), FontSize = 13 };
        var copy = new Button { Content = "Copy", Margin = new Thickness(6, 0, 0, 0) };
        copy.Click += async (_, _) =>
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(_basicLine.Text ?? "");
        };
        var lineRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        lineRow.Children.Add(_basicLine);
        Grid.SetColumn(copy, 1);
        lineRow.Children.Add(copy);
        panel.Children.Add(lineRow);

        var reset = new Button { Content = "Standard", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(reset, "Back to the standard look");
        reset.Click += (_, _) => { Show(Presets[0].values); Apply(); };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(reset);
        Grid.SetColumn(close, 1);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);

        Content = panel;
        Show(gfx.CrtPercentages());
        UpdateTexts();
    }

    // Shows values in the sliders without applying them
    private void Show(float[] percentages)
    {
        _updating = true;
        for (int i = 0; i < _sliders.Length && i < percentages.Length; i++)
            _sliders[i].Value = percentages[i];
        _updating = false;
        UpdateTexts();
    }

    // F8 in the game window changes it too; the window keeps up when it is activated again
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Activated += (_, _) => SyncOnOff();
    }

    private void SyncOnOff()
    {
        _updating = true;
        _on.IsChecked = _gfx.CrtEnabled;
        _updating = false;
        UpdateTexts();
    }

    private void Apply()
    {
        _gfx.CrtEnabled = _on.IsChecked == true;
        _gfx.SetCrtPercentages(_sliders.Select(s => (float)Math.Round(s.Value)).ToArray());
        UpdateTexts();
        _redraw();
    }

    private void UpdateTexts()
    {
        var values = _sliders.Select(s => (int)Math.Round(s.Value)).ToArray();
        for (int i = 0; i < values.Length; i++) _values[i].Text = values[i].ToString(CultureInfo.InvariantCulture);
        _basicLine.Text = $"SCREEN CRT {(_on.IsChecked == true ? 1 : 0)}, {string.Join(", ", values)}";
    }
}
