using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AmosLikeBasic;

public partial class SpriteEditorWindow : Window
{
    private readonly AmosGraphics _gfx;
    private Color _currentColor = Colors.White;
    private int _zoom = 12;
    private bool _suppressUiEvents;

    public SpriteEditorWindow(AmosGraphics gfx)
    {
        _gfx = gfx;
        InitializeComponent();

        ZoomSlider.Value = _zoom;
        ZoomText.Text = $"{_zoom}x";

        EnsureSpriteAndBind();
    }

    private int SpriteId => (int)Math.Round(SpriteIdUpDown.Value ?? 0);

    private void EnsureSpriteAndBind()
    {
        if (!_gfx.HasSprite(SpriteId))
            _gfx.CreateSprite(SpriteId, (int)Math.Round(WidthUpDown.Value ?? 32), (int)Math.Round(HeightUpDown.Value ?? 32));

        SyncSizeFieldsFromSprite();
        BindSpriteBitmap();
    }

    private void SyncSizeFieldsFromSprite()
    {
        var (w, h) = _gfx.GetSpriteSize(SpriteId);

        _suppressUiEvents = true;
        try
        {
            WidthUpDown.Value = w;
            HeightUpDown.Value = h;
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void BindSpriteBitmap()
    {
        WriteableBitmap bmp = _gfx.GetSpriteBitmap(SpriteId);
        SpriteImage.Source = bmp;

        SpriteImage.Width = bmp.PixelSize.Width * _zoom;
        SpriteImage.Height = bmp.PixelSize.Height * _zoom;

        SpriteImage.InvalidateVisual();
    }

    private void SpriteIdUpDown_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;

        EnsureSpriteAndBind();
    }

    private void SizeUpDown_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;

        BindSpriteBitmap();
    }

    private void CreateButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var w = (int)Math.Round(WidthUpDown.Value ?? 32);
        var h = (int)Math.Round(HeightUpDown.Value ?? 32);

        _gfx.CreateSprite(SpriteId, w, h);
        _gfx.SpriteClear(SpriteId, Colors.Magenta);

        SyncSizeFieldsFromSprite();
        BindSpriteBitmap();
    }

    private void ClearButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_gfx.HasSprite(SpriteId))
        {
            _gfx.SpriteClear(SpriteId, Colors.Magenta);
            SpriteImage.InvalidateVisual();
        }
    }

    private void ZoomSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = Math.Max(1, (int)Math.Round(e.NewValue));
        ZoomText.Text = $"{_zoom}x";
        BindSpriteBitmap();
    }

    private void SpriteImage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SpriteImage.Focus();
        e.Pointer.Capture(SpriteImage);

        DrawAtPointer(e);
        e.Handled = true;
    }

    private void SpriteImage_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(SpriteImage).Properties.IsLeftButtonPressed)
            return;

        DrawAtPointer(e);
        e.Handled = true;
    }

    private void SpriteImage_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == SpriteImage)
            e.Pointer.Capture(null);

        e.Handled = true;
    }

    private void DrawAtPointer(PointerEventArgs e)
    {
        if (!_gfx.HasSprite(SpriteId))
            return;

        var pos = e.GetPosition(SpriteImage);

        var x = (int)(pos.X / _zoom);
        var y = (int)(pos.Y / _zoom);

        _gfx.SpriteSetPixel(SpriteId, x, y, _currentColor);

        Dispatcher.UIThread.Post(() => SpriteImage.InvalidateVisual());
    }

    private void PaletteWhite_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.White;
    private void PaletteBlack_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Black;
    private void PaletteRed_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Red;
    private void PaletteGreen_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Lime;
    private void PaletteBlue_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Blue;
    private void PaletteYellow_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Yellow;
    private void PaletteCyan_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Cyan;
    private void PaletteTransparent_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _currentColor = Colors.Magenta;
}