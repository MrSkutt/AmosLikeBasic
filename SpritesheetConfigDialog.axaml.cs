using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AmosLikeBasic;

// Splits a picture into frames: shows the whole sheet with the frame grid on top and the frames
// as an animation, so you see straight away whether the sizes are right
public partial class SpritesheetConfigDialog : Window
{
    private readonly Bitmap? _sheet;
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private int _animFrame;
    private bool _updating;

    public SpritesheetConfigDialog() : this(null, null) { }

    public SpritesheetConfigDialog(Bitmap? sheet, SpritesheetConfig? initialConfig, string? title = null)
    {
        InitializeComponent();
        _sheet = sheet;
        if (title != null) TitleText.Text = title;

        // The file name or the program gives the sizes; otherwise a guess from the picture
        var config = initialConfig ?? (sheet != null ? SheetGuess.Guess(sheet) : null);
        if (config != null)
        {
            _updating = true;
            FrameWidthUpDown.Value = config.FrameWidth;
            FrameHeightUpDown.Value = config.FrameHeight;
            FrameCountUpDown.Value = config.FrameCount;
            _updating = false;
        }

        Preview.Sheet = sheet;
        FrameWidthUpDown.ValueChanged += (_, _) => OnSizeChanged(fitCount: true);
        FrameHeightUpDown.ValueChanged += (_, _) => OnSizeChanged(fitCount: true);
        FrameCountUpDown.ValueChanged += (_, _) => OnSizeChanged(fitCount: false);
        _animTimer.Tick += (_, _) => { _animFrame++; ShowAnimFrame(); };
        Opened += (_, _) => { OnSizeChanged(fitCount: false); _animTimer.Start(); };
        Closed += (_, _) => _animTimer.Stop();
    }

    private SpritesheetConfig Current => new()
    {
        FrameWidth = Math.Max(1, (int)(FrameWidthUpDown.Value ?? 32)),
        FrameHeight = Math.Max(1, (int)(FrameHeightUpDown.Value ?? 32)),
        FrameCount = Math.Max(1, (int)(FrameCountUpDown.Value ?? 1))
    };

    private int FitCount(SpritesheetConfig c) =>
        _sheet == null ? c.FrameCount
            : Math.Max(1, (_sheet.PixelSize.Width / c.FrameWidth) * (_sheet.PixelSize.Height / c.FrameHeight));

    private void OnSizeChanged(bool fitCount)
    {
        if (_updating) return;
        var c = Current;
        // A new frame size fills the picture; the count can then be lowered for a part-filled last row
        if (fitCount && _sheet != null)
        {
            _updating = true;
            FrameCountUpDown.Value = c.FrameCount = FitCount(c);
            _updating = false;
        }

        Preview.FrameWidth = c.FrameWidth;
        Preview.FrameHeight = c.FrameHeight;
        Preview.FrameCount = c.FrameCount;
        Preview.InvalidateVisual();

        if (_sheet != null)
        {
            int cols = _sheet.PixelSize.Width / c.FrameWidth, rows = _sheet.PixelSize.Height / c.FrameHeight;
            bool even = _sheet.PixelSize.Width % c.FrameWidth == 0 && _sheet.PixelSize.Height % c.FrameHeight == 0;
            InfoText.Text = $"Picture {_sheet.PixelSize.Width}×{_sheet.PixelSize.Height}: {cols} × {rows} frames fit." +
                            (even ? "" : "\nThe frames do not fill the picture evenly.") +
                            (c.FrameCount > FitCount(c) ? $"\nOnly {FitCount(c)} frames fit." : "");
        }
        _animFrame = 0;
        ShowAnimFrame();
    }

    private void ShowAnimFrame()
    {
        if (_sheet == null) return;
        var c = Current;
        int cols = _sheet.PixelSize.Width / c.FrameWidth;
        int count = Math.Min(c.FrameCount, FitCount(c));
        if (cols < 1 || count < 1 || c.FrameHeight > _sheet.PixelSize.Height) { AnimImage.Source = null; return; }
        int i = _animFrame % count;
        AnimImage.Source = new CroppedBitmap(_sheet,
            new PixelRect(i % cols * c.FrameWidth, i / cols * c.FrameHeight, c.FrameWidth, c.FrameHeight));
    }

    private void GuessButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_sheet == null) return;
        var g = SheetGuess.Guess(_sheet);
        _updating = true;
        FrameWidthUpDown.Value = g.FrameWidth;
        FrameHeightUpDown.Value = g.FrameHeight;
        FrameCountUpDown.Value = g.FrameCount;
        _updating = false;
        OnSizeChanged(fitCount: false);
    }

    private void OkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(Current);

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);
}

// The sheet, enlarged, with lines between the frames; cells past the frame count are dimmed
public class SheetPreview : Control
{
    public Bitmap? Sheet { get; set; }
    public int FrameWidth { get; set; } = 32;
    public int FrameHeight { get; set; } = 32;
    public int FrameCount { get; set; } = 1;

    private double Zoom => Sheet == null ? 1 : Math.Clamp(Math.Floor(512.0 / Math.Max(Sheet.PixelSize.Width, Sheet.PixelSize.Height)), 1, 8);

    protected override Size MeasureOverride(Size availableSize) =>
        Sheet == null ? default : new Size(Sheet.PixelSize.Width * Zoom, Sheet.PixelSize.Height * Zoom);

    public override void Render(DrawingContext context)
    {
        if (Sheet == null) return;
        double z = Zoom;
        int w = Sheet.PixelSize.Width, h = Sheet.PixelSize.Height;
        var all = new Rect(0, 0, w * z, h * z);

        // Checkerboard so that transparent parts show
        var light = new SolidColorBrush(Color.FromRgb(70, 70, 90));
        var dark = new SolidColorBrush(Color.FromRgb(50, 50, 66));
        const double sq = 8;
        for (double y = 0; y < all.Height; y += sq)
            for (double x = 0; x < all.Width; x += sq)
                context.FillRectangle(((int)(x / sq) + (int)(y / sq)) % 2 == 0 ? light : dark,
                    new Rect(x, y, Math.Min(sq, all.Width - x), Math.Min(sq, all.Height - y)));

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(Sheet, new Rect(0, 0, w, h), all);

        if (FrameWidth < 1 || FrameHeight < 1) return;
        int cols = w / FrameWidth, rows = h / FrameHeight;
        var dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (r * cols + c >= FrameCount)
                    context.FillRectangle(dim, new Rect(c * FrameWidth * z, r * FrameHeight * z, FrameWidth * z, FrameHeight * z));
        // Whatever is left over at the right or bottom edge is not part of any frame
        context.FillRectangle(dim, new Rect(cols * FrameWidth * z, 0, all.Width - cols * FrameWidth * z, all.Height));
        context.FillRectangle(dim, new Rect(0, rows * FrameHeight * z, cols * FrameWidth * z, all.Height - rows * FrameHeight * z));

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 212, 0)), 1);
        for (int c = 0; c <= cols; c++)
            context.DrawLine(pen, new Point(c * FrameWidth * z + 0.5, 0), new Point(c * FrameWidth * z + 0.5, rows * FrameHeight * z));
        for (int r = 0; r <= rows; r++)
            context.DrawLine(pen, new Point(0, r * FrameHeight * z + 0.5), new Point(cols * FrameWidth * z, r * FrameHeight * z + 0.5));
    }
}

// Thin lines between the pixels of the sprite editor's enlarged picture
public class PixelGridOverlay : Control
{
    public int PixelsWide { get; set; }
    public int PixelsHigh { get; set; }
    public int Zoom { get; set; } = 1;

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 127, 178, 255)), 1);
        var strong = new Pen(new SolidColorBrush(Color.FromArgb(140, 127, 178, 255)), 1);
        double w = PixelsWide * Zoom, h = PixelsHigh * Zoom;
        // Every eighth line stronger, to help count pixels
        for (int x = 0; x <= PixelsWide; x++)
            context.DrawLine(x % 8 == 0 ? strong : pen, new Point(x * Zoom + 0.5, 0), new Point(x * Zoom + 0.5, h));
        for (int y = 0; y <= PixelsHigh; y++)
            context.DrawLine(y % 8 == 0 ? strong : pen, new Point(0, y * Zoom + 0.5), new Point(w, y * Zoom + 0.5));
    }
}
