using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace AmosLikeBasic;

public partial class ColorPickerDialog : Window
{
    private Color? _result;

    // HSB state
    private double _hue = 0;         // 0–360
    private double _saturation = 1;  // 0–1
    private double _brightness = 1;  // 0–1

    private bool _draggingWheel = false;
    private bool _draggingBrightness = false;
    private bool _suppressEvents = false;

    private WriteableBitmap? _wheelBitmap;
    private WriteableBitmap? _brightnessBitmap;

    public ColorPickerDialog(Color initialColor)
    {
        InitializeComponent();

        RgbToHsb(initialColor.R, initialColor.G, initialColor.B,
                 out _hue, out _saturation, out _brightness);

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        DrawColorWheel();
        DrawBrightnessBar();
        SyncFromHsb();
    }

    // ─── Color Wheel ───────────────────────────────────────────────

    private void DrawColorWheel()
    {
        const int size = 200;
        _wheelBitmap = new WriteableBitmap(
            new PixelSize(size, size), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var buf = _wheelBitmap.Lock();
        unsafe
        {
            byte* ptr = (byte*)buf.Address;
            double cx = size / 2.0, cy = size / 2.0, r = size / 2.0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > r)
                    {
                        ptr[0] = ptr[1] = ptr[2] = ptr[3] = 0;
                    }
                    else
                    {
                        double hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360) % 360;
                        double sat = dist / r;
                        var c = HsbToColor(hue, sat, _brightness);
                        ptr[0] = c.B;
                        ptr[1] = c.G;
                        ptr[2] = c.R;
                        ptr[3] = 255;
                    }
                    ptr += 4;
                }
            }
        }

        ColorWheelCanvas.Background = new ImageBrush(_wheelBitmap) { Stretch = Stretch.Fill };
        UpdateWheelIndicator();
    }

    private void UpdateWheelIndicator()
    {
        double cx = 100, cy = 100, r = 92;
        double rad = _hue * Math.PI / 180.0;
        double ix = cx + r * _saturation * Math.Cos(rad);
        double iy = cy + r * _saturation * Math.Sin(rad);
        Canvas.SetLeft(WheelIndicator, ix - 8);
        Canvas.SetTop(WheelIndicator, iy - 8);
    }

    private void ColorWheel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingWheel = true;
        PickFromWheel(e.GetPosition(ColorWheelCanvas));
        e.Pointer.Capture(ColorWheelCanvas);
    }

    private void ColorWheel_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingWheel) return;
        PickFromWheel(e.GetPosition(ColorWheelCanvas));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _draggingWheel = false;
        _draggingBrightness = false;
        base.OnPointerReleased(e);
    }

    private void PickFromWheel(Point p)
    {
        double cx = 100, cy = 100;
        double dx = p.X - cx, dy = p.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        _hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360) % 360;
        _saturation = Math.Min(dist / 100.0, 1.0);

        UpdateWheelIndicator();
        SyncFromHsb();
    }

    // ─── Brightness Bar ────────────────────────────────────────────

    private void DrawBrightnessBar()
    {
        const int w = 24, h = 160;
        _brightnessBitmap = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var buf = _brightnessBitmap.Lock();
        unsafe
        {
            byte* ptr = (byte*)buf.Address;
            for (int y = 0; y < h; y++)
            {
                double bv = 1.0 - (double)y / h;
                var c = HsbToColor(_hue, _saturation, bv);
                for (int x = 0; x < w; x++)
                {
                    ptr[0] = c.B;
                    ptr[1] = c.G;
                    ptr[2] = c.R;
                    ptr[3] = 255;
                    ptr += 4;
                }
            }
        }

        BrightnessCanvas.Background = new ImageBrush(_brightnessBitmap) { Stretch = Stretch.Fill };
        UpdateBrightnessIndicator();
    }

    private void UpdateBrightnessIndicator()
    {
        double iy = (1.0 - _brightness) * 160.0;
        Canvas.SetLeft(BrightnessIndicator, 2);
        Canvas.SetTop(BrightnessIndicator, Math.Clamp(iy - 10, 0, 140));
    }

    private void Brightness_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingBrightness = true;
        PickBrightness(e.GetPosition(BrightnessCanvas));
        e.Pointer.Capture(BrightnessCanvas);
    }

    private void Brightness_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingBrightness) return;
        PickBrightness(e.GetPosition(BrightnessCanvas));
    }

    private void PickBrightness(Point p)
    {
        _brightness = Math.Clamp(1.0 - p.Y / 160.0, 0, 1);
        UpdateBrightnessIndicator();
        DrawColorWheel();      // redraw wheel at new brightness
        DrawBrightnessBar();
        SyncFromHsb();
    }

    // ─── Sync & Update ─────────────────────────────────────────────

    private void SyncFromHsb()
    {
        var c = HsbToColor(_hue, _saturation, _brightness);
        _suppressEvents = true;
        RedSlider.Value = c.R;
        GreenSlider.Value = c.G;
        BlueSlider.Value = c.B;
        _suppressEvents = false;
        UpdatePreview(c);
    }

    private void ColorSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        byte r = (byte)RedSlider.Value;
        byte g = (byte)GreenSlider.Value;
        byte b = (byte)BlueSlider.Value;
        RgbToHsb(r, g, b, out _hue, out _saturation, out _brightness);
        DrawColorWheel();
        DrawBrightnessBar();
        UpdateWheelIndicator();
        UpdateBrightnessIndicator();
        UpdatePreview(Color.FromRgb(r, g, b));
    }

    private void HexInput_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var txt = HexInput.Text?.TrimStart('#') ?? "";
        if (txt.Length == 6 && TryParseHex(txt, out byte r, out byte g, out byte b))
        {
            _suppressEvents = true;
            RedSlider.Value = r;
            GreenSlider.Value = g;
            BlueSlider.Value = b;
            _suppressEvents = false;
            RgbToHsb(r, g, b, out _hue, out _saturation, out _brightness);
            DrawColorWheel();
            DrawBrightnessBar();
            UpdateWheelIndicator();
            UpdateBrightnessIndicator();
            UpdatePreview(Color.FromRgb(r, g, b));
        }
    }

    private void UpdatePreview(Color c)
    {
        _suppressEvents = true;
        PreviewRect.Fill = new SolidColorBrush(c);
        HexInput.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        RedText.Text = c.R.ToString();
        GreenText.Text = c.G.ToString();
        BlueText.Text = c.B.ToString();
        _suppressEvents = false;
    }

    // ─── Buttons ───────────────────────────────────────────────────

    private void OkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result = Color.FromRgb(
            (byte)RedSlider.Value,
            (byte)GreenSlider.Value,
            (byte)BlueSlider.Value);
        Close(_result);
    }

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    // ─── Color Math ────────────────────────────────────────────────

    private static Color HsbToColor(double h, double s, double b)
    {
        h = ((h % 360) + 360) % 360;
        double c = b * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = b - c;
        double r, g, bv;

        if (h < 60)      { r = c; g = x; bv = 0; }
        else if (h < 120){ r = x; g = c; bv = 0; }
        else if (h < 180){ r = 0; g = c; bv = x; }
        else if (h < 240){ r = 0; g = x; bv = c; }
        else if (h < 300){ r = x; g = 0; bv = c; }
        else             { r = c; g = 0; bv = x; }

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((bv + m) * 255));
    }

    private static void RgbToHsb(byte ri, byte gi, byte bi,
        out double h, out double s, out double b)
    {
        double r = ri / 255.0, g = gi / 255.0, bv = bi / 255.0;
        double max = Math.Max(r, Math.Max(g, bv));
        double min = Math.Min(r, Math.Min(g, bv));
        double delta = max - min;

        b = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0) { h = 0; return; }
        if (max == r)      h = 60 * (((g - bv) / delta) % 6);
        else if (max == g) h = 60 * ((bv - r) / delta + 2);
        else               h = 60 * ((r - g) / delta + 4);
        if (h < 0) h += 360;
    }

    private static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        try
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
            return true;
        }
        catch { return false; }
    }
}