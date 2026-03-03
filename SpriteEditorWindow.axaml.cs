using System;
using System.IO;
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

    
    public SpriteEditorWindow(AmosGraphics gfx)
    {
        _gfx = gfx;
        InitializeComponent();

        ZoomSlider.Value = _zoom;
        ZoomText.Text = $"{_zoom}x";
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
        
        // Säkerställ att current frame är giltig
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
            
            // Sätt RenderOptions direkt på Image-objektet
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

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            PickColorAtPointer(e);
        }
        else
        {
            DrawAtPointer(e);
        }
        
        e.Handled = true;
    }

    private void SpriteImage_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(SpriteImage);
        
        if (point.Properties.IsLeftButtonPressed)
        {
            DrawAtPointer(e);
            e.Handled = true;
        }
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

    private void PickColorAtPointer(PointerEventArgs e)
    {
        if (!_gfx.HasSprite(SpriteId))
            return;

        var pos = e.GetPosition(SpriteImage);
        var x = (int)(pos.X / _zoom);
        var y = (int)(pos.Y / _zoom);

        var (w, h) = _gfx.GetSpriteSize(SpriteId);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return;

        var bmp = _gfx.GetSpriteBitmap(SpriteId);
        using var fb = bmp.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            var offset = y * fb.RowBytes + x * 4;
            byte b = p[offset + 0];
            byte g = p[offset + 1];
            byte r = p[offset + 2];
            byte a = p[offset + 3];

            _currentColor = Color.FromArgb(a, r, g, b);
            UpdateCurrentColorIndicator();
        }
    }
    
    private void PaletteWhite_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.White;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteBlack_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Black;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteRed_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Red;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteGreen_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Lime;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteBlue_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Blue;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteYellow_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Yellow;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteCyan_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Cyan;
        UpdateCurrentColorIndicator();
    }
    
    private void PaletteTransparent_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) 
    { 
        _currentColor = Colors.Magenta;
        UpdateCurrentColorIndicator();
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
        
        // Skapa ny tom frame
        var newFrame = CreateEmptyBitmap(w, h);
        sprite.Frames.Add(newFrame);
        
        _currentFrame = sprite.Frames.Count - 1;
        BindSpriteBitmap();
        UpdateFrameNavigation();
    }

    private void DeleteFrameButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sprite = _gfx.GetSprite(SpriteId);
        
        // Kan inte ta bort sista framen
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
            // Fyll med magenta (transparent key)
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
    //  LADDA & SPARA FUNKTIONALITET
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
                    bitmap.CopyPixels(
                        new PixelRect(0, 0, w, h),
                        fb.Address,
                        fb.RowBytes * h,
                        fb.RowBytes);
                }

                _currentFileName = file.Path.LocalPath;
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

            if (files.Count > 0)
            {
                var file = files[0];
                string fileName = Path.GetFileNameWithoutExtension(file.Name);
                
                // Försök parsa från filnamnet
                var parsedConfig = ParseSpritesheetFileName(fileName);
                
                // Visa dialog för att fråga om frame-storlek (med förvalda värden)
                var dialog = new SpritesheetConfigDialog(parsedConfig);
                var config = await dialog.ShowDialog<SpritesheetConfig?>(this);
            
                if (config == null)
                    return;

                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var bitmap = new Bitmap(stream);

                    int sheetW = (int)bitmap.Size.Width;
                    int sheetH = (int)bitmap.Size.Height;
                
                    int cols = sheetW / config.FrameWidth;
                    int rows = sheetH / config.FrameHeight;
                    int totalFrames = Math.Min(cols * rows, config.FrameCount);

                    // Skapa spriten med första frame-storleken
                    _gfx.CreateSprite(SpriteId, config.FrameWidth, config.FrameHeight);
                    var sprite = _gfx.GetSprite(SpriteId);
                    sprite.Frames.Clear();

                    // Ladda in alla frames
                    for (int i = 0; i < totalFrames; i++)
                    {
                        int col = i % cols;
                        int row = i / cols;
                    
                        var frameBmp = CreateEmptyBitmap(config.FrameWidth, config.FrameHeight);
                    
                        using (var fb = frameBmp.Lock())
                        {
                            bitmap.CopyPixels(
                                new PixelRect(col * config.FrameWidth, row * config.FrameHeight, config.FrameWidth, config.FrameHeight),
                                fb.Address,
                                fb.RowBytes * config.FrameHeight,
                                fb.RowBytes);
                        }
                    
                        sprite.Frames.Add(frameBmp);
                    }

                    _currentFileName = file.Path.LocalPath;
                    _currentFrame = 0;
                    UpdateFileNameDisplay();

                    SyncSizeFieldsFromSprite();
                    BindSpriteBitmap();
                    UpdateFrameNavigation();
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog($"Failed to load spritesheet: {ex.Message}");
                }
            }
        }

        private SpritesheetConfig ParseSpritesheetFileName(string fileName)
        {
            // Standardvärden
            int width = 32;
            int height = 32;
            int frameCount = 8;
            
            // Parse W### (Width)
            var wMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"_W(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (wMatch.Success)
                width = int.Parse(wMatch.Groups[1].Value);
            
            // Parse H### (Height)
            var hMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"_H(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (hMatch.Success)
                height = int.Parse(hMatch.Groups[1].Value);
            
            // Parse B### eller F### (Frame count)
            var fMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"_[BF](\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fMatch.Success)
                frameCount = int.Parse(fMatch.Groups[1].Value);

            return new SpritesheetConfig
            {
                FrameWidth = width,
                FrameHeight = height,
                FrameCount = frameCount
            };
        }
        
    private void UpdateCurrentColorIndicator()
    {
        CurrentColorRect.Fill = new SolidColorBrush(_currentColor);
    }

    private async void PickColorButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(_currentColor);
        var result = await dialog.ShowDialog<Color?>(this);
        
        if (result.HasValue)
        {
            _currentColor = result.Value;
            UpdateCurrentColorIndicator();
        }
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
            
            // Om det bara finns en frame, spara den direkt
            if (sprite.Frames.Count == 1)
            {
                await SaveSpriteToFile(_currentFileName);
            }
            else
            {
                // För multi-frame sprites, spara som spritesheet
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
        
        // Räkna ut layout (t.ex. 4 kolumner)
        int cols = (int)Math.Ceiling(Math.Sqrt(frameCount));
        int rows = (int)Math.Ceiling((double)frameCount / cols);
        
        int frameW = sprite.Width;
        int frameH = sprite.Height;
        
        int sheetW = cols * frameW;
        int sheetH = rows * frameH;
        
        // Skapa stor bitmap för hela sheet:et
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
                
                // Fyll med transparent färg (magenta)
                for (int i = 0; i < destLock.RowBytes * sheetH; i += 4)
                {
                    destPtr[i + 0] = 255; // B
                    destPtr[i + 1] = 0;   // G
                    destPtr[i + 2] = 255; // R
                    destPtr[i + 3] = 255; // A
                }
                
                // Kopiera varje frame
                for (int i = 0; i < frameCount; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    
                    var frame = sprite.Frames[i];
                    using var srcLock = frame.Lock();
                    byte* srcPtr = (byte*)srcLock.Address;
                    
                    int destX = col * frameW;
                    int destY = row * frameH;
                    
                    // Kopiera pixel för pixel
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