using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AmosLikeBasic;

public partial class SpriteEditorWindow : Window
{
    private readonly AmosGraphics _gfx;
    private Color _currentColor = Colors.White;
    private int _zoom = 12;
    private bool _suppressUiEvents;
    private string? _currentFileName;
    private int _currentFrame = 0;
    private readonly Func<string>? _programText;
    private int _sheetColumns;   // columns of the loaded sheet, kept when saving; 0 for a new sprite

    public SpriteEditorWindow() : this(new AmosGraphics()) { }

    public SpriteEditorWindow(AmosGraphics gfx, Func<string>? programText = null)
    {
        _gfx = gfx;
        _programText = programText;
        InitializeComponent();

        ZoomSlider.Value = _zoom;
        ZoomText.Text = $"{_zoom}x";
        SetupDrawing();
        UpdateCurrentColorIndicator();

        EnsureSpriteAndBind();
    }

    private int SpriteId => (int)Math.Round(SpriteIdUpDown.Value ?? 0);

    private void EnsureSpriteAndBind()
    {
        if (!_gfx.HasSprite(SpriteId))
            _gfx.CreateSprite(SpriteId, (int)Math.Round(WidthUpDown.Value ?? 32), (int)Math.Round(HeightUpDown.Value ?? 32));

        _currentFrame = 0;
        SyncSizeFieldsFromSprite();
        BindSpriteBitmap();
        UpdateFrameNavigation();
        RefreshSpriteColours();
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
        var sprite = _gfx.GetSprite(SpriteId);
        
        // Make sure the current frame is valid
        if (_currentFrame >= sprite.Frames.Count)
            _currentFrame = sprite.Frames.Count - 1;
        if (_currentFrame < 0)
            _currentFrame = 0;

        sprite.CurrentFrame = _currentFrame;
        WriteableBitmap bmp = sprite.Bitmap;
        
        SpriteImage.Source = bmp;
        SpriteImage.Width = bmp.PixelSize.Width * _zoom;
        SpriteImage.Height = bmp.PixelSize.Height * _zoom;
        SpriteImage.InvalidateVisual();
        UpdateGridOverlay();
    }

    private void UpdateFrameNavigation()
    {
        var sprite = _gfx.GetSprite(SpriteId);
        bool hasMultipleFrames = sprite.Frames.Count > 1;

        FrameNavigationPanel.IsVisible = hasMultipleFrames;

        if (hasMultipleFrames)
        {
            FrameInfoText.Text = $"Frame {_currentFrame + 1} / {sprite.Frames.Count}";
            UpdateFrameThumbnails();
        }
    }

    private void UpdateFrameThumbnails()
    {
        FrameThumbnailsPanel.Children.Clear();
        var sprite = _gfx.GetSprite(SpriteId);

        for (int i = 0; i < sprite.Frames.Count; i++)
        {
            int frameIndex = i; // Capture for lambda
            var frame = sprite.Frames[i];

            var image = new Image
            {
                Source = frame,
                Stretch = Avalonia.Media.Stretch.Uniform
            };
            
            // Set RenderOptions directly on the Image object
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);

            var border = new Border
            {
                Width = 48,
                Height = 48,
                BorderBrush = frameIndex == _currentFrame ? Brushes.Yellow : Brushes.Gray,
                BorderThickness = new Thickness(2),
                Background = Brushes.Black,
                Child = image,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            border.PointerPressed += (s, e) =>
            {
                _currentFrame = frameIndex;
                BindSpriteBitmap();
                UpdateFrameNavigation();
            };

            FrameThumbnailsPanel.Children.Add(border);
        }
    }

    private void SpriteIdUpDown_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressUiEvents)
            return;

        _currentFileName = null;
        UpdateFileNameDisplay();

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

        _currentFrame = 0;
        SyncSizeFieldsFromSprite();
        BindSpriteBitmap();
        UpdateFrameNavigation();
    }

    // ══════════════════════════════════════════════════════════════
    //  DRAWING
    // ══════════════════════════════════════════════════════════════

    private const uint TransparentKey = 0xFFFF00FF;   // magenta, the engine's transparent colour (BGRA)
    private DrawTool _tool = DrawTool.Pencil;
    private readonly Stack<(WriteableBitmap frame, uint[] pixels)> _undo = new(), _redo = new();
    private uint[]? _strokeBefore, _work;
    private (int x, int y) _strokeStart, _strokeLast;
    private bool _stroking;
    private readonly List<Color> _recent = new();
    private readonly DispatcherTimer _animTimer = new();
    private int _animFrame;

    // The C64's 16 colours, then greys, skin and a few strong ones
    private static readonly string[] PaletteHex =
    {
        "#000000", "#FFFFFF", "#880000", "#AAFFEE", "#CC44CC", "#00CC55", "#0000AA", "#EEEE77",
        "#DD8855", "#664400", "#FF7777", "#333333", "#777777", "#AAFF66", "#0088FF", "#BBBBBB",
        "#FF0000", "#FF8800", "#FFDD00", "#00FF00", "#00FFFF", "#0044FF", "#8800FF", "#FF00AA",
        "#FFE0BD", "#E0AC69", "#8D5524", "#4A2C10", "#1A1A40", "#2E7D32", "#90CAF9", "#F8BBD0",
    };

    private void SetupDrawing()
    {
        // Transparent first, then the palette
        PalettePanel.Children.Add(Swatch(Color.FromUInt32(TransparentKey), "Transparent"));
        foreach (var hex in PaletteHex)
            PalettePanel.Children.Add(Swatch(Color.Parse(hex)));

        AnimSpeedSlider.ValueChanged += (_, _) => SetAnimSpeed();
        _animTimer.Tick += (_, _) => AnimTick();
        SetAnimSpeed();
        _animTimer.Start();
        Closed += (_, _) => _animTimer.Stop();
        KeyDown += OnEditorKeyDown;
    }

    private Border Swatch(Color c, string? tip = null)
    {
        bool transparent = c.ToUInt32() == TransparentKey || c.A == 0;
        var swatch = new Border
        {
            Width = 22, Height = 22, Margin = new Thickness(0, 0, 3, 3),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(c),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = transparent ? new TextBlock { Text = "T", Foreground = Brushes.White, FontWeight = FontWeight.Bold,
                                                 HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                                 VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } : null
        };
        ToolTip.SetTip(swatch, tip ?? $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        swatch.PointerPressed += (_, _) => SetColour(c);
        return swatch;
    }

    private void SetColour(Color c)
    {
        _currentColor = c;
        UpdateCurrentColorIndicator();
    }

    private void UpdateCurrentColorIndicator()
    {
        CurrentColorRect.Fill = new SolidColorBrush(_currentColor);
        ColorHexText.Text = _currentColor.ToUInt32() == TransparentKey ? "Transparent" : $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
    }

    // The colours the sprite uses most, over all its frames
    private void RefreshSpriteColours()
    {
        SpriteColoursPanel.Children.Clear();
        if (!_gfx.HasSprite(SpriteId)) return;
        var frames = _gfx.GetSprite(SpriteId).Frames.ConvertAll(ReadPixels);
        foreach (var c in GridTools.CommonColours(frames, 24, c => c == TransparentKey || c >> 24 < 16))
            SpriteColoursPanel.Children.Add(Swatch(ToColor(c)));
    }

    private void RememberColour(Color c)
    {
        _recent.Remove(c);
        _recent.Insert(0, c);
        if (_recent.Count > 9) _recent.RemoveAt(9);
        RecentPanel.Children.Clear();
        foreach (var r in _recent) RecentPanel.Children.Add(Swatch(r));
    }

    // Pixels as BGRA packed into uints, the bitmaps' own (premultiplied) format
    private static unsafe uint[] ReadPixels(WriteableBitmap bmp)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var px = new uint[w * h];
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
        {
            uint* row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
            for (int x = 0; x < w; x++) px[y * w + x] = row[x];
        }
        return px;
    }

    private static unsafe void WritePixels(WriteableBitmap bmp, uint[] px)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        using var fb = bmp.Lock();
        for (int y = 0; y < h; y++)
        {
            uint* row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
            for (int x = 0; x < w; x++) row[x] = px[y * w + x];
        }
    }

    private static uint ToPixel(Color c)
    {
        // Premultiplied, as the bitmaps store it
        uint a = c.A;
        uint r = c.R * a / 255, g = c.G * a / 255, b = c.B * a / 255;
        return a << 24 | r << 16 | g << 8 | b;
    }

    private static Color ToColor(uint p)
    {
        byte a = (byte)(p >> 24);
        if (a == 0) return Color.FromUInt32(TransparentKey);
        byte Un(uint v) => (byte)Math.Min(255, v * 255 / a);
        return Color.FromArgb(a, Un(p >> 16 & 0xFF), Un(p >> 8 & 0xFF), Un(p & 0xFF));
    }

    private WriteableBitmap? CurrentBitmap => _gfx.HasSprite(SpriteId) ? _gfx.GetSprite(SpriteId).Bitmap : null;

    private (int x, int y) PixelAt(PointerEventArgs e)
    {
        var pos = e.GetPosition(SpriteImage);
        return ((int)Math.Floor(pos.X / _zoom), (int)Math.Floor(pos.Y / _zoom));
    }

    private void Tool_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender == LineTool) SelectTool(DrawTool.Line);
        else if (sender == RectangleTool) SelectTool(DrawTool.Rectangle);
        else if (sender == EllipseTool) SelectTool(DrawTool.Ellipse);
        else if (sender == FillTool) SelectTool(DrawTool.Fill);
        else if (sender == PickerTool) SelectTool(DrawTool.Picker);
        else SelectTool(DrawTool.Pencil);
    }

    private void SelectTool(DrawTool tool)
    {
        _tool = tool;
        PencilTool.IsChecked = tool == DrawTool.Pencil;
        LineTool.IsChecked = tool == DrawTool.Line;
        RectangleTool.IsChecked = tool == DrawTool.Rectangle;
        EllipseTool.IsChecked = tool == DrawTool.Ellipse;
        FillTool.IsChecked = tool == DrawTool.Fill;
        PickerTool.IsChecked = tool == DrawTool.Picker;
    }

    private void SpriteImage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SpriteImage.Focus();
        var bmp = CurrentBitmap;
        if (bmp == null) return;
        var p = PixelAt(e);
        var props = e.GetCurrentPoint(this).Properties;

        // Right-click (or the picker) takes the colour under the pointer
        if (props.IsRightButtonPressed || _tool == DrawTool.Picker)
        {
            PickColour(bmp, p);
            e.Handled = true;
            return;
        }

        e.Pointer.Capture(SpriteImage);
        _strokeBefore = ReadPixels(bmp);
        _work = (uint[])_strokeBefore.Clone();
        _strokeStart = _strokeLast = p;
        _stroking = true;
        if (_tool == DrawTool.Fill)
        {
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            uint colour = ToPixel(_currentColor);
            GridTools.Fill(_work, w, h, p.x, p.y, colour);
            if (MirrorCheck.IsChecked == true) GridTools.Fill(_work, w, h, w - 1 - p.x, p.y, colour);
            WritePixels(bmp, _work);
            EndStroke();
        }
        else
            DrawStroke(bmp, p);
        e.Handled = true;
    }

    private void SpriteImage_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var bmp = CurrentBitmap;
        if (bmp == null) return;
        var p = PixelAt(e);
        ShowStatus(bmp, p);
        if (_stroking && e.GetCurrentPoint(SpriteImage).Properties.IsLeftButtonPressed)
        {
            DrawStroke(bmp, p);
            e.Handled = true;
        }
    }

    private void SpriteImage_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == SpriteImage)
            e.Pointer.Capture(null);
        if (_stroking) EndStroke();
        e.Handled = true;
    }

    private void SpriteImage_OnPointerExited(object? sender, PointerEventArgs e) => StatusText.Text = " ";

    private void DrawStroke(WriteableBitmap bmp, (int x, int y) p)
    {
        if (_work == null || _strokeBefore == null) return;
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        uint colour = ToPixel(_currentColor);
        bool mirror = MirrorCheck.IsChecked == true, filled = FilledCheck.IsChecked == true;
        var work = _work;
        void Plot(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < w && y < h) work[y * w + x] = colour;
            int mx = w - 1 - x;
            if (mirror && mx >= 0 && y >= 0 && mx < w && y < h) work[y * w + mx] = colour;
        }

        var (sx, sy) = _strokeStart;
        switch (_tool)
        {
            case DrawTool.Pencil:
                // A line from the last point, so that a quick stroke has no gaps
                GridTools.Line(_strokeLast.x, _strokeLast.y, p.x, p.y, Plot);
                break;
            default:
                // Shapes are drawn afresh on the picture from before the stroke, so they follow the pointer
                Array.Copy(_strokeBefore, work, work.Length);
                if (_tool == DrawTool.Line) GridTools.Line(sx, sy, p.x, p.y, Plot);
                else if (_tool == DrawTool.Rectangle) GridTools.Rectangle(sx, sy, p.x, p.y, filled, Plot);
                else if (_tool == DrawTool.Ellipse) GridTools.Ellipse(sx, sy, p.x, p.y, filled, Plot);
                break;
        }
        _strokeLast = p;
        WritePixels(bmp, work);
        SpriteImage.InvalidateVisual();
    }

    private void EndStroke()
    {
        _stroking = false;
        var bmp = CurrentBitmap;
        if (bmp != null && _strokeBefore != null && _work != null && !_strokeBefore.AsSpan().SequenceEqual(_work))
        {
            PushUndo(bmp, _strokeBefore);
            RememberColour(_currentColor);
            RefreshSpriteColours();
        }
        _strokeBefore = _work = null;
        SpriteImage.InvalidateVisual();
        UpdateFrameThumbnails();
    }

    private void PickColour(WriteableBitmap bmp, (int x, int y) p)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        if (p.x < 0 || p.y < 0 || p.x >= w || p.y >= h) return;
        SetColour(ToColor(ReadPixels(bmp)[p.y * w + p.x]));
        // Back to drawing, as in most paint programs
        if (_tool == DrawTool.Picker) SelectTool(DrawTool.Pencil);
    }

    private void ShowStatus(WriteableBitmap bmp, (int x, int y) p)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        if (p.x < 0 || p.y < 0 || p.x >= w || p.y >= h) { StatusText.Text = " "; return; }
        var c = ToColor(ReadPixels(bmp)[p.y * w + p.x]);
        string colour = c.ToUInt32() == TransparentKey ? "transparent" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        string size = _stroking && _tool is DrawTool.Rectangle or DrawTool.Ellipse
            ? $"   size {Math.Abs(p.x - _strokeStart.x) + 1} × {Math.Abs(p.y - _strokeStart.y) + 1}" : "";
        StatusText.Text = $"x {p.x}, y {p.y}   {colour}{size}";
    }

    // Undo keeps the whole frame from before each change
    private void PushUndo(WriteableBitmap frame, uint[] before)
    {
        _undo.Push((frame, before));
        _redo.Clear();
        if (_undo.Count > 100)
        {
            var keep = _undo.Reverse().Skip(_undo.Count - 100).ToList();
            _undo.Clear();
            foreach (var u in keep) _undo.Push(u);
        }
    }

    private void UndoButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoRedo(_undo, _redo);
    private void RedoButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoRedo(_redo, _undo);

    private void UndoRedo(Stack<(WriteableBitmap frame, uint[] pixels)> from, Stack<(WriteableBitmap frame, uint[] pixels)> to)
    {
        if (from.Count == 0) return;
        var (frame, pixels) = from.Pop();
        if (frame.PixelSize.Width * frame.PixelSize.Height != pixels.Length) return;
        to.Push((frame, ReadPixels(frame)));
        WritePixels(frame, pixels);
        // Show the frame the change was made in
        if (_gfx.HasSprite(SpriteId))
        {
            int i = _gfx.GetSprite(SpriteId).Frames.IndexOf(frame);
            if (i >= 0) _currentFrame = i;
        }
        BindSpriteBitmap();
        UpdateFrameNavigation();
        RefreshSpriteColours();
    }

    // Changes the whole frame in one step that can be undone
    private void ChangeFrame(Action<uint[], int, int> change)
    {
        var bmp = CurrentBitmap;
        if (bmp == null) return;
        var px = ReadPixels(bmp);
        PushUndo(bmp, (uint[])px.Clone());
        change(px, bmp.PixelSize.Width, bmp.PixelSize.Height);
        WritePixels(bmp, px);
        SpriteImage.InvalidateVisual();
        UpdateFrameThumbnails();
        RefreshSpriteColours();
    }

    private void ClearButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ChangeFrame((px, _, _) => Array.Fill(px, TransparentKey));
    private void FlipHButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame(GridTools.FlipHorizontal);
    private void FlipVButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame(GridTools.FlipVertical);
    private void ShiftLeft_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame((px, w, h) => GridTools.Shift(px, w, h, -1, 0));
    private void ShiftRight_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame((px, w, h) => GridTools.Shift(px, w, h, 1, 0));
    private void ShiftUp_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame((px, w, h) => GridTools.Shift(px, w, h, 0, -1));
    private void ShiftDown_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeFrame((px, w, h) => GridTools.Shift(px, w, h, 0, 1));

    private void GridCheck_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UpdateGridOverlay();

    private void UpdateGridOverlay()
    {
        var bmp = CurrentBitmap;
        if (bmp == null) return;
        GridOverlay.PixelsWide = bmp.PixelSize.Width;
        GridOverlay.PixelsHigh = bmp.PixelSize.Height;
        GridOverlay.Zoom = _zoom;
        GridOverlay.Width = bmp.PixelSize.Width * _zoom;
        GridOverlay.Height = bmp.PixelSize.Height * _zoom;
        GridOverlay.IsVisible = GridCheck.IsChecked == true && _zoom >= 4;
        GridOverlay.InvalidateVisual();
    }

    private void ZoomSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = Math.Max(1, (int)Math.Round(e.NewValue));
        ZoomText.Text = $"{_zoom}x";
        BindSpriteBitmap();
    }

    private void SetAnimSpeed()
    {
        int fps = (int)Math.Round(AnimSpeedSlider.Value);
        AnimSpeedText.Text = $"{fps} fps";
        _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
    }

    // The frames played in a loop, or the frame being drawn when Play is off
    private void AnimTick()
    {
        if (!_gfx.HasSprite(SpriteId)) { AnimImage.Source = null; return; }
        var frames = _gfx.GetSprite(SpriteId).Frames;
        if (frames.Count == 0) return;
        _animFrame = PlayCheck.IsChecked == true ? (_animFrame + 1) % frames.Count : Math.Min(_currentFrame, frames.Count - 1);
        AnimImage.Source = frames[_animFrame];
        AnimImage.InvalidateVisual();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Typing in a number box is not a shortcut
        if (e.Source is TextBox) return;
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Z when cmd && shift:
            case Key.Y when cmd: UndoRedo(_redo, _undo); break;
            case Key.Z when cmd: UndoRedo(_undo, _redo); break;
            case Key.P when !cmd: SelectTool(DrawTool.Pencil); break;
            case Key.L when !cmd: SelectTool(DrawTool.Line); break;
            case Key.R when !cmd: SelectTool(DrawTool.Rectangle); break;
            case Key.E when !cmd: SelectTool(DrawTool.Ellipse); break;
            case Key.F when !cmd: SelectTool(DrawTool.Fill); break;
            case Key.I when !cmd: SelectTool(DrawTool.Picker); break;
            case Key.G when !cmd: GridCheck.IsChecked = GridCheck.IsChecked != true; break;
            case Key.M when !cmd: MirrorCheck.IsChecked = MirrorCheck.IsChecked != true; break;
            case Key.OemComma: PrevFrameButton_OnClick(null, new Avalonia.Interactivity.RoutedEventArgs()); break;
            case Key.OemPeriod: NextFrameButton_OnClick(null, new Avalonia.Interactivity.RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    // ══════════════════════════════════════════════════════════════
    //  FRAME NAVIGATION
    // ══════════════════════════════════════════════════════════════

    private void PrevFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        if (_currentFrame > 0)
        {
            _currentFrame--;
            BindSpriteBitmap();
            UpdateFrameNavigation();
        }
    }

    private void NextFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        if (_currentFrame < sprite.Frames.Count - 1)
        {
            _currentFrame++;
            BindSpriteBitmap();
            UpdateFrameNavigation();
        }
    }

    private void AddFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        var (w, h) = _gfx.GetSpriteSize(SpriteId);
        
        // Create a new empty frame
        var newFrame = CreateEmptyBitmap(w, h);
        sprite.Frames.Add(newFrame);
        
        _currentFrame = sprite.Frames.Count - 1;
        BindSpriteBitmap();
        UpdateFrameNavigation();
    }

    private void CopyFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        var (w, h) = _gfx.GetSpriteSize(SpriteId);
        var copy = CreateEmptyBitmap(w, h);
        WritePixels(copy, ReadPixels(sprite.Frames[_currentFrame]));
        sprite.Frames.Insert(_currentFrame + 1, copy);

        _currentFrame++;
        BindSpriteBitmap();
        UpdateFrameNavigation();
    }

    private void DeleteFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        
        // Cannot remove the last frame
        if (sprite.Frames.Count <= 1)
            return;

        sprite.Frames.RemoveAt(_currentFrame);
        
        if (_currentFrame >= sprite.Frames.Count)
            _currentFrame = sprite.Frames.Count - 1;
        
        BindSpriteBitmap();
        UpdateFrameNavigation();
    }

    private WriteableBitmap CreateEmptyBitmap(int w, int h)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using var fb = bmp.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            // Fill with magenta (transparent key)
            for (var i = 0; i < fb.RowBytes * h; i += 4)
            {
                p[i + 0] = 255; // B
                p[i + 1] = 0;   // G
                p[i + 2] = 255; // R
                p[i + 3] = 255; // A
            }
        }

        return bmp;
    }

    // ══════════════════════════════════════════════════════════════
    //  LOAD & SAVE
    // ══════════════════════════════════════════════════════════════

    private async void LoadButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Sprite",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var bitmap = new Bitmap(stream);

                int w = (int)bitmap.Size.Width;
                int h = (int)bitmap.Size.Height;

                _gfx.CreateSprite(SpriteId, w, h);
                var spriteBmp = _gfx.GetSpriteBitmap(SpriteId);

                using (var fb = spriteBmp.Lock())
                {
                    AmosGraphics.CopyPixelsBgra(bitmap, new PixelRect(0, 0, w, h), fb);
                }

                _currentFileName = file.Path.LocalPath;
                _sheetColumns = 0;
                _currentFrame = 0;
                UpdateFileNameDisplay();

                SyncSizeFieldsFromSprite();
                BindSpriteBitmap();
                UpdateFrameNavigation();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"Failed to load sprite: {ex.Message}");
            }
        }
    }

    private async void LoadSpritesheetButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Spritesheet",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count == 0) return;

        try
        {
            var file = files[0];
            Bitmap bitmap;
            await using (var stream = await file.OpenReadAsync())
                bitmap = new Bitmap(stream);

            // A name like Hero_W32_H48_B8.png gives the sizes; otherwise the dialog guesses from the picture
            SpritesheetConfig? suggested = null;
            if (ProgramAssets.FromFileName(file.Name) is { } n)
                suggested = new SpritesheetConfig { FrameWidth = n.w ?? 32, FrameHeight = n.h ?? 32, FrameCount = n.frames ?? 1 };

            var dialog = new SpritesheetConfigDialog(bitmap, suggested, $"Split {file.Name} into frames");
            var config = await dialog.ShowDialog<SpritesheetConfig?>(this);
            if (config != null)
                ShowSheet(bitmap, config, file.Path.LocalPath);
            bitmap.Dispose();
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"Failed to load spritesheet: {ex.Message}");
        }
    }

    // Splits the sheet into the sprite's frames, in reading order as SPRITE LOAD does
    private void ShowSheet(Bitmap bitmap, SpritesheetConfig config, string path)
    {
        int cols = Math.Max(1, bitmap.PixelSize.Width / config.FrameWidth);
        int rows = Math.Max(1, bitmap.PixelSize.Height / config.FrameHeight);
        int totalFrames = Math.Max(1, Math.Min(cols * rows, config.FrameCount));

        _gfx.CreateSprite(SpriteId, config.FrameWidth, config.FrameHeight);
        var sprite = _gfx.GetSprite(SpriteId);
        sprite.Frames.Clear();
        for (int i = 0; i < totalFrames; i++)
        {
            var frameBmp = CreateEmptyBitmap(config.FrameWidth, config.FrameHeight);
            var rect = new PixelRect(i % cols * config.FrameWidth, i / cols * config.FrameHeight, config.FrameWidth, config.FrameHeight);
            if (rect.Right <= bitmap.PixelSize.Width && rect.Bottom <= bitmap.PixelSize.Height)
                using (var fb = frameBmp.Lock())
                    AmosGraphics.CopyPixelsBgra(bitmap, rect, fb);
            sprite.Frames.Add(frameBmp);
        }

        // Saving keeps the sheet's layout, so the program still finds the frames where it expects them
        _sheetColumns = Math.Min(cols, totalFrames);
        _currentFileName = path;
        _currentFrame = 0;
        UpdateFileNameDisplay();
        SyncSizeFieldsFromSprite();
        BindSpriteBitmap();
        UpdateFrameNavigation();
        RefreshSpriteColours();
    }

    // The pictures the BASIC program loads, read afresh each time the list opens
    private void ProgramPicturesBox_OnDropDownOpened(object? sender, EventArgs e) => FillProgramPictures();

    private void FillProgramPictures()
    {
        var assets = ProgramAssets.Find(_programText?.Invoke() ?? "").FindAll(a => a.Kind != "MAP");
        _suppressUiEvents = true;
        ProgramPicturesBox.ItemsSource = assets.Count > 0
            ? assets.ConvertAll(a => new ComboBoxItem { Content = a.Label, Tag = a })
            : new System.Collections.Generic.List<ComboBoxItem> { new() { Content = "(the program loads no pictures)", IsEnabled = false } };
        ProgramPicturesBox.SelectedIndex = -1;
        _suppressUiEvents = false;
    }

    private async void ProgramPicturesBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || ProgramPicturesBox.SelectedItem is not ComboBoxItem { Tag: ProgramAssets.Asset asset }) return;

        string path;
        try { path = ResourceLoader.GetPath(asset.File); }
        catch (FileNotFoundException) { path = asset.File; }
        if (!File.Exists(path))
        {
            await ShowErrorDialog($"Cannot find {asset.File}");
            return;
        }
        try
        {
            using var bitmap = new Bitmap(path);
            // The sizes the program uses; without them the whole picture is one frame, as SPRITE LOAD does
            var config = asset.HasFrameSize
                ? new SpritesheetConfig
                {
                    FrameWidth = asset.FrameWidth!.Value, FrameHeight = asset.FrameHeight!.Value,
                    FrameCount = asset.Frames ?? int.MaxValue
                }
                : new SpritesheetConfig { FrameWidth = bitmap.PixelSize.Width, FrameHeight = bitmap.PixelSize.Height, FrameCount = 1 };
            if (asset.Number.Length > 0 && int.TryParse(asset.Number, out var number) && number >= SpriteIdUpDown.Minimum)
            {
                _suppressUiEvents = true;
                SpriteIdUpDown.Value = number;
                _suppressUiEvents = false;
            }
            ShowSheet(bitmap, config, path);
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"Failed to load {asset.File}: {ex.Message}");
        }
    }

    private async void PickColorButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_currentColor);
        var result = await dialog.ShowDialog<Color?>(this);
        
        if (result.HasValue)
            SetColour(result.Value);
    }
    
    private async void SaveButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFileName))
        {
            SaveAsButton_OnClick(sender, e);
            return;
        }

        try
        {
            var sprite = _gfx.GetSprite(SpriteId);
            
            // If there is only one frame, save it directly
            if (sprite.Frames.Count == 1)
            {
                await SaveSpriteToFile(_currentFileName);
            }
            else
            {
                // For multi-frame sprites, save as a sprite sheet
                await SaveSpritesheetToFile(_currentFileName);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"Failed to save sprite: {ex.Message}");
        }
    }

    private async void SaveAsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        string suggestedName = sprite.Frames.Count > 1 
            ? $"spritesheet_{SpriteId}.png" 
            : $"sprite_{SpriteId}.png";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Sprite As",
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } }
            }
        });

        if (file != null)
        {
            try
            {
                if (sprite.Frames.Count == 1)
                {
                    await SaveSpriteToFile(file.Path.LocalPath);
                }
                else
                {
                    await SaveSpritesheetToFile(file.Path.LocalPath);
                }
                
                _currentFileName = file.Path.LocalPath;
                UpdateFileNameDisplay();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"Failed to save sprite: {ex.Message}");
            }
        }
    }

    private async Task SaveSpriteToFile(string filePath)
    {
        var bmp = _gfx.GetSpriteBitmap(SpriteId);
        await using var stream = File.Create(filePath);
        bmp.Save(stream);
    }

    private async Task SaveSpritesheetToFile(string filePath)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        int frameCount = sprite.Frames.Count;
        
        // The loaded sheet's layout, otherwise a square-ish grid (e.g. 4 columns)
        int cols = _sheetColumns > 0 ? Math.Min(_sheetColumns, frameCount) : (int)Math.Ceiling(Math.Sqrt(frameCount));
        int rows = (int)Math.Ceiling((double)frameCount / cols);
        
        int frameW = sprite.Width;
        int frameH = sprite.Height;
        
        int sheetW = cols * frameW;
        int sheetH = rows * frameH;
        
        // Create a big bitmap for the whole sheet
        var sheetBmp = new WriteableBitmap(
            new PixelSize(sheetW, sheetH),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        
        using (var destLock = sheetBmp.Lock())
        {
            unsafe
            {
                byte* destPtr = (byte*)destLock.Address;
                
                // Empty cells stay transparent
                new Span<byte>(destPtr, destLock.RowBytes * sheetH).Clear();
                
                // Copy each frame
                for (int i = 0; i < frameCount; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    
                    var frame = sprite.Frames[i];
                    using var srcLock = frame.Lock();
                    byte* srcPtr = (byte*)srcLock.Address;
                    
                    int destX = col * frameW;
                    int destY = row * frameH;
                    
                    // Copy pixel by pixel
                    for (int y = 0; y < frameH; y++)
                    {
                        for (int x = 0; x < frameW; x++)
                        {
                            int srcOffset = (y * srcLock.RowBytes) + (x * 4);
                            int destOffset = ((destY + y) * destLock.RowBytes) + ((destX + x) * 4);
                            
                            destPtr[destOffset + 0] = srcPtr[srcOffset + 0]; // B
                            destPtr[destOffset + 1] = srcPtr[srcOffset + 1]; // G
                            destPtr[destOffset + 2] = srcPtr[srcOffset + 2]; // R
                            destPtr[destOffset + 3] = srcPtr[srcOffset + 3]; // A
                        }
                    }
                }
            }
        }
        
        await using var stream = File.Create(filePath);
        sheetBmp.Save(stream);
    }

    private void UpdateFileNameDisplay()
    {
        if (string.IsNullOrEmpty(_currentFileName))
        {
            FileNameText.Text = "(no file loaded)";
        }
        else
        {
            FileNameText.Text = Path.GetFileName(_currentFileName);
        }
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new Window
        {
            Title = "Error",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 15,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new Button 
                    { 
                        Content = "OK", 
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Width = 80
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel && panel.Children[1] is Button btn)
        {
            btn.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }
}

public class SpritesheetConfig
{
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public int FrameCount { get; set; }
}