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
    private readonly CheckBox _frameOn;
    private readonly ComboBox _frames;
    private readonly string[] _frameFiles;
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

        // A monitor round the picture: the pictures in Graphics/Frames, each with a see-through glass
        _frameFiles = FrameFiles();
        _frameOn = new CheckBox { Content = "Monitor frame", IsChecked = gfx.Frame != null, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) };
        ToolTip.SetTip(_frameOn, "The picture shown in a monitor, which is fitted to the picture's shape; the monitor is not touched by the CRT effect");
        _frames = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _frameFiles.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(_frameFiles, f => f == gfx.Frame?.File)),
            IsEnabled = _frameFiles.Length > 0
        };
        _frameOn.IsEnabled = _frameFiles.Length > 0;
        _frameOn.IsCheckedChanged += (_, _) => { if (!_updating) ApplyFrame(); };
        _frames.SelectionChanged += (_, _) => { if (!_updating && _frameOn.IsChecked == true) ApplyFrame(); };
        var frameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        frameRow.Children.Add(_frameOn);
        Grid.SetColumn(_frames, 1);
        frameRow.Children.Add(_frames);
        panel.Children.Add(frameRow);

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
        CrtConfig.Save(_gfx);
        UpdateTexts();
        _redraw();
    }

    private void UpdateTexts()
    {
        var values = _sliders.Select(s => (int)Math.Round(s.Value)).ToArray();
        for (int i = 0; i < values.Length; i++) _values[i].Text = values[i].ToString(CultureInfo.InvariantCulture);
        _basicLine.Text = $"SCREEN CRT {(_on.IsChecked == true ? 1 : 0)}, {string.Join(", ", values)}"
                          + (_gfx.Frame != null ? $" : SCREEN FRAME \"{_gfx.Frame.File}\"" : "");
    }

    // The monitor pictures, as a program names them ("Graphics/Frames/C64Monitor.png")
    private static string[] FrameFiles()
    {
        try
        {
            var dir = System.IO.Path.Combine(Workspace.Graphics, "Frames");
            if (!System.IO.Directory.Exists(dir)) return Array.Empty<string>();
            return System.IO.Directory.GetFiles(dir, "*.png").OrderBy(f => f)
                .Select(f => "Graphics/Frames/" + System.IO.Path.GetFileName(f)).ToArray();
        }
        catch (System.IO.IOException) { return Array.Empty<string>(); }
    }

    private void ApplyFrame()
    {
        string? file = _frameOn.IsChecked == true && _frames.SelectedIndex >= 0 ? _frameFiles[_frames.SelectedIndex] : null;
        var error = _gfx.SetFrame(file);
        if (error != null) _basicLine.Text = error;
        else UpdateTexts();
        CrtConfig.Save(_gfx);
        _redraw();
    }
}

// The CRT settings and the monitor frame, kept between runs of the editor (crt.json beside config.json)
public static class CrtConfig
{
    private sealed class Settings
    {
        public bool On { get; set; }
        public float[] Values { get; set; } = Array.Empty<float>();
        public string? Frame { get; set; }
    }

    private static string SettingsFile =>
        System.IO.Path.Combine(ConfigService.GetConfigFolder("AmosProject"), "crt.json");

    public static void Load(AmosGraphics gfx)
    {
        try
        {
            if (!System.IO.File.Exists(SettingsFile)) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(SettingsFile));
            if (s == null) return;
            gfx.CrtEnabled = s.On;
            if (s.Values.Length > 0) gfx.SetCrtPercentages(s.Values);
            if (s.Frame != null && gfx.SetFrame(s.Frame) != null) gfx.SetFrame(null);   // the picture is gone
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Could not read the CRT settings: " + ex.Message); }
    }

    public static void Save(AmosGraphics gfx)
    {
        try
        {
            var s = new Settings { On = gfx.CrtEnabled, Values = gfx.CrtPercentages(), Frame = gfx.Frame?.File };
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile)!);
            System.IO.File.WriteAllText(SettingsFile, System.Text.Json.JsonSerializer.Serialize(s,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Could not save the CRT settings: " + ex.Message); }
    }
}
