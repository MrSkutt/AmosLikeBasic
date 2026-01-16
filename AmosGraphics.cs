using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace AmosLikeBasic;

public sealed class GpuLayer
{
    public WriteableBitmap Bitmap { get; init; } = null!;
    
    public Point Offset { get; set; }
    public double Opacity { get; set; } = 1.0;
    public float Timer { get; set; } // For animations
    // NYTT: Array för att skicka in t.ex. Y-positioner för 20 bars
    public float[] ShaderParams { get; set; } = new float[22]; 
    public float[] ShaderHeights { get; set; } = new float[22]; 
    public SKColor[] ShaderColors { get; set; } = new SKColor[22]; 
    public SKColor[] ShaderColorsTo { get; set; } = new SKColor[22];
    
    // Shader-support
    public string? SkSlCode { get; set; }
    public SKRuntimeEffect? CachedEffect { get; set; }
}

// Denna klass sköter själva Skia-ritningen
public class ShaderDrawOperation : ICustomDrawOperation
{
    private readonly GpuLayer _layer;
    private readonly Rect _destRect;

    public ShaderDrawOperation(Rect bounds, GpuLayer layer, Rect destRect)
    {
        Bounds = bounds;
        _layer = layer;
        _destRect = destRect;
    }

    public Rect Bounds { get; }

    public void Dispose()
    {
    }

    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (lease == null) return;

        using var skia = lease.Lease();
        var canvas = skia.SkCanvas;

        // 1. Kompilera shadern
        if (_layer.CachedEffect == null && !string.IsNullOrEmpty(_layer.SkSlCode))
        {
            _layer.CachedEffect = SKRuntimeEffect.Create(_layer.SkSlCode, out var errors);
            if (!string.IsNullOrEmpty(errors))
            {
                System.Diagnostics.Debug.WriteLine($"SkSL Error: {errors}");
                return;
            }
        }

        if (_layer.CachedEffect != null)
        {
            try
            {
                using var fb = _layer.Bitmap.Lock();
                var info = new SKImageInfo(_layer.Bitmap.PixelSize.Width, _layer.Bitmap.PixelSize.Height,
                    SKColorType.Bgra8888, SKAlphaType.Premul);
                using var skBitmap = new SKBitmap();
                skBitmap.InstallPixels(info, fb.Address, fb.RowBytes);
                using var image = SKImage.FromBitmap(skBitmap);

                var children = new SKRuntimeEffectChildren(_layer.CachedEffect);
                if (_layer.CachedEffect.Children.Contains("inputTexture"))
                    children.Add("inputTexture", image.ToShader());

                var uniforms = new SKRuntimeEffectUniforms(_layer.CachedEffect);
            
                if (_layer.CachedEffect.Uniforms.Contains("iResolution"))
                    uniforms.Add("iResolution", new float[] { (float)_layer.Bitmap.Size.Width, (float)_layer.Bitmap.Size.Height });

                if (_layer.CachedEffect.Uniforms.Contains("uPositions")) {
                    float[] p = new float[22];
                    Array.Copy(_layer.ShaderParams, p, 22);
                    uniforms.Add("uPositions", p);
                }
            
                if (_layer.CachedEffect.Uniforms.Contains("uHeights")) {
                    float[] h = new float[22];
                    Array.Copy(_layer.ShaderHeights, h, 22);
                    uniforms.Add("uHeights", h);
                }

                if (_layer.CachedEffect.Uniforms.Contains("uColors")) {
                    float[] cf = new float[22 * 4];
                    float[] ct = new float[22 * 4];
                    for (int i = 0; i < 22; i++) {
                        cf[i*4+0]=_layer.ShaderColors[i].Red/255f; cf[i*4+1]=_layer.ShaderColors[i].Green/255f;
                        cf[i*4+2]=_layer.ShaderColors[i].Blue/255f; cf[i*4+3]=_layer.ShaderColors[i].Alpha/255f;
                        ct[i*4+0]=_layer.ShaderColorsTo[i].Red/255f; ct[i*4+1]=_layer.ShaderColorsTo[i].Green/255f;
                        ct[i*4+2]=_layer.ShaderColorsTo[i].Blue/255f; ct[i*4+3]=_layer.ShaderColorsTo[i].Alpha/255f;
                    }
                    uniforms.Add("uColors", cf);
                    uniforms.Add("uColorsTo", ct);
                }

                using var shader = _layer.CachedEffect.ToShader(true, uniforms, children);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(
                    new SKRect((float)_destRect.X, (float)_destRect.Y, (float)_destRect.Right, (float)_destRect.Bottom),
                    paint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Render Error: " + ex.Message);
            }
        }
    }
}


public sealed class AmosGpuView : Control
{
    private ScreenWindow? _screenWindow; 

    public AmosGraphics Graphics { get; set; } = null!;
    private RenderTargetBitmap? _framebuffer;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Graphics != null && Graphics.Width > 0) return new Size(Graphics.Width, Graphics.Height);
        return new Size(640, 480);
    }


    private void EnsureFramebuffer(int w, int h)
    {
        if (_framebuffer != null &&
            _framebuffer.PixelSize.Width == w &&
            _framebuffer.PixelSize.Height == h)
            return;

        _framebuffer = new RenderTargetBitmap(
            new PixelSize(w, h),
            new Vector(96, 96)); // upplösning 96 DPI
    }
    

    public override void Render(DrawingContext ctx)
    {
        if (Graphics == null) return;
        
        // 1. Se till att framebuffer finns
        EnsureFramebuffer(Graphics.Width, Graphics.Height);


        using (var fbCtx = _framebuffer!.CreateDrawingContext())
        {
            lock (Graphics.LockObject)
            {
                var amosRect = new Rect(0, 0, Graphics.Width, Graphics.Height);
                fbCtx.DrawRectangle(Brushes.Transparent, null, amosRect);

                // RITA RAINBOWS
                foreach (var rb in Graphics.GetRainbows())
                {
                    if (rb.Colors.Count == 0) continue;
                    for (int y = 0; y < rb.Height; y++)
                    {
                        int screenY = rb.Offset + y;
                        if (screenY < 0 || screenY >= Graphics.Height) continue;
                        var color = rb.Colors[y % rb.Colors.Count];
                        fbCtx.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, screenY, Graphics.Width, 1));
                    }
                }

                // RITA GPU-LAGER
                foreach (var layer in Graphics.ActiveFrame)
                {
                    if (layer.Bitmap == null) continue;

                  
                    var bmpSize = layer.Bitmap.Size;
                    var offset = layer.Offset;

                    int w = (int)bmpSize.Width;
                    int h = (int)bmpSize.Height;

                    if (!string.IsNullOrEmpty(layer.SkSlCode))
                    {
                        // Använd vår custom Skia-operation
                        var drawOp = new ShaderDrawOperation(new Rect(0,0, Graphics.Width, Graphics.Height), layer, new Rect(layer.Offset, layer.Bitmap.Size));
                        ctx.Custom(drawOp);
                    }
                    else
                    {
                        // Skär ut den del av lagret som syns på skärmen
                        // Vi ritar 4 delar: original + de som "wrappar" horisontellt och/eller vertikalt
                        for (int dx = -w; dx <= w; dx += w)
                        {
                            for (int dy = -h; dy <= h; dy += h)
                            {
                                var drawRect = new Rect(offset.X + dx, offset.Y + dy, w, h);

                                // Snabb check: rita bara om den hamnar inom framebuffer
                                if (drawRect.Right < 0 || drawRect.Bottom < 0 ||
                                    drawRect.Left > Graphics.Width || drawRect.Top > Graphics.Height)
                                    continue;

                                fbCtx.DrawImage(layer.Bitmap, new Rect(bmpSize), drawRect);

                            }
                        }
                    }
                }

                // RITA TEXTER → loopa över en **kopierad lista**
                foreach (var qt in Graphics.GetQueuedTexts().ToList())
                {
                    var f = Graphics.GetFont(qt.FontId);
                    if (f == null)
                        continue;

                    int curX = qt.X;

                    double angleRad = qt.Angle * Math.PI / 180.0;

                    foreach (var c in qt.Text)
                    {
                        if (c == ' ')
                        {
                            curX += (int)(f.CharWidth * qt.ZoomX);
                            continue;
                        }

                        var charBmp = Graphics.GetFontChar(f, c);
                        if (charBmp == null)
                        {
                            curX += (int)(f.CharWidth * qt.ZoomX);
                            continue;
                        }

                        double w = f.CharWidth * qt.ZoomX;
                        double h = f.CharHeight * qt.ZoomY;

                        // Glyphens centrum i världens koordinater
                        double cx = curX + w / 2.0;
                        double cy = qt.Y + h / 2.0;

                        // Transform: flytta till centrum → rotera → flytta tillbaka
                        var transform =
                            Matrix.CreateTranslation(-cx, -cy) *
                            Matrix.CreateRotation(angleRad) *
                            Matrix.CreateTranslation(cx, cy);

                        using (fbCtx.PushPostTransform(transform))
                        {
                            fbCtx.DrawImage(
                                charBmp,
                                new Rect(charBmp.Size),
                                new Rect(curX, qt.Y, w, h));
                        }

                        curX += (int)w;
                    }
                }
                
                // RITA SPRITES
                foreach (var id in Graphics.GetSpriteIds())
                {
                    var sprite = Graphics.GetSprite(id);
                    if (!sprite.Visible) continue;

                    var bmp = Graphics.GetSpriteBitmap(id);

                    var destRect = new Rect(sprite.X - sprite.HandleX, sprite.Y - sprite.HandleY,
                        bmp.Size.Width * sprite.ZoomX, bmp.Size.Height * sprite.ZoomY);


                    // Target position BEFORE rotation
                    double x = sprite.X - sprite.HandleX * sprite.ZoomX;
                    double y = sprite.Y - sprite.HandleY * sprite.ZoomY;

                    // Center about which we rotate
                    double cx = sprite.X;
                    double cy = sprite.Y;

                    // Rotation in radians
                    double angleRad = sprite.Angle * Math.PI / 180.0;
                    double cos = Math.Cos(angleRad);
                    double sin = Math.Sin(angleRad);

                    // Build matrix: Translate to center → rotate → translate back
                    var matrix = new Matrix(
                        cos, sin,
                        -sin, cos,
                        cx - cos * cx + sin * cy - x + cx,
                        cy - sin * cx - cos * cy - y + cy
                    );

                    // Push transform + draw
                    using (fbCtx.PushPostTransform(matrix))
                    {
                        fbCtx.DrawImage(bmp, new Rect(bmp.Size), new Rect(x, y, sprite.Width, sprite.Height));
                    }
                }
            }
        }
        // 3. Rita sedan framebuffer till skärmen
        ctx.DrawImage(
            _framebuffer!,
            new Rect(_framebuffer.Size),
            new Rect(0, 0, Bounds.Width, Bounds.Height));
        
    }
}


public sealed class AmosGraphics
{
    private readonly List<GpuLayer> _frameA = new();
    private readonly List<GpuLayer> _frameB = new();
    private bool _isAActive = true;

    public List<GpuLayer> ActiveFrame => _isAActive ? _frameA : _frameB;
    public List<GpuLayer> InactiveFrame => _isAActive ? _frameB : _frameA;
    
    private readonly System.Diagnostics.Stopwatch _vblTimer = new();
    public double LastCpuUsagePercent { get; private set; } = 0;
    public readonly object LockObject = new(); // Korrekt namn för låset
    private int _currentScreen = 0;
    private readonly System.Diagnostics.Stopwatch _refreshTimer = new();
    public double LastCpuUsage { get; private set; }
    

// ... byt ut RasterShaderCode i AmosGraphics.cs ...
// ... inuti AmosGraphics klassen ...
    private const string RasterShaderCode = @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float uPositions[22];
uniform float uHeights[22];
uniform float4 uColors[22];
uniform float4 uColorsTo[22];

half4 main(float2 fragCoord) {
    float y = fragCoord.y;
    half4 mask = sample(inputTexture, fragCoord);
    float mode = uPositions[21]; 
    
    // 1. Beräkna Bakgrundsraster (Slot 0)
    float t = (y - uPositions[0]) / iResolution.y;
    float triangle = 1.0 - abs((t - floor(t)) * 2.0 - 1.0);
    half3 finalRGB = mix(uColors[0].rgb, uColorsTo[0].rgb, half(triangle));
    bool hasRaster = (uHeights[0] > 0.1);

    // 2. Bars (Slot 1-20)
    for (int i = 1; i < 21; i++) {
        float h = uHeights[i];
        if (h > 0.1) {
            float d = y - uPositions[i];
            if (d >= 0.0 && d <= h) {
                float bT = 1.0 - abs((d / h) * 2.0 - 1.0);
                finalRGB = mix(uColors[i].rgb, uColorsTo[i].rgb, half(bT));
                hasRaster = true;
            }
        }
    }

    // 3. Slutgiltig Mix
    if (mask.a < 0.01 && !hasRaster) return half4(0.0);

    if (mode > 0.5) {
        return half4(mask.rgb * finalRGB, mask.a);
    } else {
        if (mask.a > 0.1) return mask;
        return half4(finalRGB, 1.0);
    }
}";
    
    internal sealed class Rainbow
    {
        public int PaletteIndex; // I en modern motor använder vi detta som ett ID eller färg-filter
        public int Offset;
        public int Height;
        public List<Color> Colors { get; } = new();
    }
    private readonly Dictionary<int, Rainbow> _rainbows = new();
    internal IEnumerable<Rainbow> GetRainbows() => _rainbows.Values;
    
    public List<WriteableBitmap> GetTileBitmaps() => _tiles;
    
    internal sealed class Font
    {
        public List<WriteableBitmap> CharBitmaps { get; } = new();
        public int CharWidth { get; set; }
        public int CharHeight { get; set; }
        public double Angle { get; set; } = 0;
        public double ZoomX { get; set; } = 1.0;
        public double ZoomY { get; set; } = 1.0;
        public string CharMap { get; set; } = "";
    }
    
    internal Font? GetFont(int id) => _fonts.GetValueOrDefault(id);
    internal WriteableBitmap? GetFontChar(Font f, char c)
    {
        string map = string.IsNullOrEmpty(f.CharMap) ? "" : f.CharMap;
        int charIdx = !string.IsNullOrEmpty(map) ? map.IndexOf(char.ToUpper(c)) : c - 32;
        return (charIdx >= 0 && charIdx < f.CharBitmaps.Count) ? f.CharBitmaps[charIdx] : null;
    }
    
    
    public sealed class Sprite
    {
        public Sprite(int width, int height, WriteableBitmap firstFrame)
        {
            Width = width;
            Height = height;
            Frames.Add(firstFrame);
            Ink = Colors.White;
            TransparentKey = Colors.Magenta;
            Visible = false;
        }
        
        public int Width { get; }
        public int Height { get; }
        public List<WriteableBitmap> Frames { get; } = new();
        public int CurrentFrame { get; set; } = 0;
        public WriteableBitmap Bitmap => Frames[CurrentFrame];
        public int X { get; set; }
        public int Y { get; set; }
        public int HandleX { get; set; }
        public int HandleY { get; set; }
        public bool Visible { get; set; }
        public double Angle { get; set; } = 0;
        public double ZoomX { get; set; } = 1.0;
        public double ZoomY { get; set; } = 1.0;
        public Color Ink { get; set; }
        public Color TransparentKey { get; set; }
    }

    private readonly Dictionary<int, Sprite> _sprites = new();
    private readonly Dictionary<int, Font> _fonts = new();
    
    public sealed class QueuedFontText
    {
        public int FontId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Text { get; set; } = "";
        public double Angle { get; set; }
        public double ZoomX { get; set; }
        public double ZoomY { get; set; }
    }
    public readonly List<QueuedFontText> _fontTexts = new(); 
    public IEnumerable<QueuedFontText> GetQueuedTexts() => _fontTexts;
    
    private readonly List<WriteableBitmap> _tiles = new();
    private int _tilesInWidth = 0;
    private int[,] _map = new int[0, 0];
    private int _tileWidth = 32;
    private int _tileHeight = 32;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Color Ink { get; set; } = Colors.White;


    public WriteableBitmap GetActiveScreen()
    {
        EnsureScreen();
        
        // Vi säkerställer att _currentScreen pekar på ett existerande lager
        int index = (_currentScreen >= 0 && _currentScreen < InactiveFrame.Count) 
            ? _currentScreen 
            : 0;

        // Info.txt: "Alla ritaroperationer sker alltid på den inaktiva framen"
        return InactiveFrame[index].Bitmap;
    }
    
    public int GetActiveScreenNumber()
    {
        return _currentScreen;
    }
    
    public void SetShaderParams(int layerIdx, int slot, float y, float height)
    {
        lock (LockObject) {
            var frame = InactiveFrame;
            if (layerIdx >= 0 && layerIdx < frame.Count) {
                var layer = frame[layerIdx];
                if (slot >= 0 && slot < 22) { // Uppdaterat till 24
                    layer.ShaderParams[slot] = y;
                    layer.ShaderHeights[slot] = height;
                }
            }
        }
    }

    public void SetShaderColors(int layerIdx, int slot, Color c1, Color c2)
    {
        lock (LockObject) {
            var frame = InactiveFrame;
            if (layerIdx >= 0 && layerIdx < frame.Count) {
                var layer = frame[layerIdx];
                if (slot >= 0 && slot < 22) {
                    layer.ShaderColors[slot] = new SKColor(c1.R, c1.G, c1.B, 255); // Sätt Alpha till 255
                    layer.ShaderColorsTo[slot] = new SKColor(c2.R, c2.G, c2.B, 255);
                
                    // Om det är slot 0 (bakgrund) och höjden är 0, sätt den till skärmhöjd
                    if (slot == 0 && layer.ShaderHeights[0] <= 0) {
                        layer.ShaderHeights[0] = (float)Height;
                    }
                }
            }
        }
    }
    
    // ---------------- Project Export/Import ----------------

    public sealed record ProjectFile(
        int Version,
        string ProgramText,
        int ScreenWidth,
        int ScreenHeight,
        List<SpriteFile> Sprites,
        int MapWidth, // Lägg till dessa
        int MapHeight,
        List<int> MapData);

    public sealed record SpriteFile(
        int Id,
        int Width,
        int Height,
        int X,
        int Y,
        int HandleX,
        int HandleY,
        int CurrentFrame,
        bool Visible,
        string TransparentKey,
        List<string> FramesBase64);

    public ProjectFile ExportProject(string programText)
    {
        EnsureScreen();
        // 1. Exportera Sprites
        var sprites = new List<SpriteFile>();
        foreach (var (id, s) in _sprites.OrderBy(k => k.Key))
        {
            var framesData = new List<string>();
            foreach (var frameBmp in s.Frames)
            {
                using var fb = frameBmp.Lock();
                var size = fb.RowBytes * s.Height;
                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address, bytes, 0, size);
                framesData.Add(Convert.ToBase64String(bytes));
            }

            sprites.Add(new SpriteFile(id, s.Width, s.Height, s.X, s.Y, s.HandleX, s.HandleY, s.CurrentFrame, s.Visible,
                s.TransparentKey.ToString(), framesData));
        }

        // 2. Exportera Banan (NY LOGIK)
        var mapList = new List<int>();
        int mw = GetMapWidth();
        int mh = GetMapHeight();

        for (int y = 0; y < mh; y++)
        for (int x = 0; x < mw; x++)
            mapList.Add(GetMapTile(x, y));

        // 3. Skapa ProjectFile med ALLA 8 argument (inklusive de nya för Map)
        return new ProjectFile(
            Version: 1,
            ProgramText: programText ?? "",
            ScreenWidth: Width,
            ScreenHeight: Height,
            Sprites: sprites,
            MapWidth: mw,
            MapHeight: mh,
            MapData: mapList);
    }

    public void ImportProject(ProjectFile project)
    {
        if (project is null) return;
        int screenW = project.ScreenWidth <= 0 ? 640 : project.ScreenWidth;
        int screenH = project.ScreenHeight <= 0 ? 480 : project.ScreenHeight;

        Screen(project.ScreenWidth <= 0 ? 640 : project.ScreenWidth,
            project.ScreenHeight <= 0 ? 480 : project.ScreenHeight);
        _sprites.Clear();
        foreach (var sf in project.Sprites ?? new())
        {
            var firstFrame = CreateEmptyBitmap(sf.Width, sf.Height);
            var s = new Sprite(sf.Width, sf.Height, firstFrame);
            s.X = sf.X;
            s.Y = sf.Y;
            s.HandleX = sf.HandleX;
            s.HandleY = sf.HandleY;
            s.CurrentFrame = sf.CurrentFrame;
            s.Visible = sf.Visible;
            s.TransparentKey = Color.Parse(sf.TransparentKey);
            s.Frames.Clear();
            foreach (var b64 in sf.FramesBase64)
            {
                var f = CreateEmptyBitmap(sf.Width, sf.Height);
                var b = Convert.FromBase64String(b64);
                using (var fb = f.Lock()) System.Runtime.InteropServices.Marshal.Copy(b, 0, fb.Address, b.Length);
                s.Frames.Add(f);
            }

            _sprites[sf.Id] = s;
        }

        SetMapSize(project.MapWidth, project.MapHeight);
        int idx = 0;
        for (int y = 0; y < project.MapHeight; y++)
        for (int x = 0; x < project.MapWidth; x++)
            if (idx < project.MapData.Count)
                _map[x, y] = project.MapData[idx++];
    }
    
    
    public void BeginFrame()
    {
        _vblTimer.Restart();
    }

    public void EndFrame(double targetVblMs = 16.67) // ~60Hz = 16.67 ms per VBL
    {
        _vblTimer.Stop();
        // CPU-tid som procent av VBL
        LastCpuUsagePercent = Math.Min(100, (_vblTimer.Elapsed.TotalMilliseconds / targetVblMs) * 100);
    }


    // ---------------- Rainbows ----------------

    public void SetRainbow(int num, int paletteIdx, int offset, int height)
    {
        if (!_rainbows.TryGetValue(num, out var rb))
        {
            rb = new Rainbow();
            _rainbows[num] = rb;
        }
        rb.PaletteIndex = paletteIdx;
        rb.Offset = offset;
        rb.Height = height;
    }

    public void SetRainbowColors(int num, List<Color> colors)
    {
        if (_rainbows.TryGetValue(num, out var rb))
        {
            rb.Colors.Clear();
            rb.Colors.AddRange(colors);
        }
    }

    public int GetRainbowHeight(int num) => _rainbows.TryGetValue(num, out var rb) ? rb.Height : 0;

    public void SetRainbowGradient(int num, Color start, Color end, int steps)
    {
        if (!_rainbows.TryGetValue(num, out var rb)) return;

        rb.Colors.Clear();
        if (steps <= 1) { rb.Colors.Add(start); return; }

        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / (steps - 1);
            byte r = (byte)(start.R + (end.R - start.R) * t);
            byte g = (byte)(start.G + (end.G - start.G) * t);
            byte b = (byte)(start.B + (end.B - start.B) * t);
            rb.Colors.Add(Color.FromArgb(255, r, g, b));
        }
    }

    public void DelRainbow(int num) => _rainbows.Remove(num);
    
    // ---------------- Screen & Core ----------------

    public void Screen(int w, int h)
    {
        lock (LockObject)
        {
            Width = w; Height = h;
            _frameA.Clear(); _frameB.Clear();
            
            var lA = new GpuLayer { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0), SkSlCode = RasterShaderCode };
            var lB = new GpuLayer { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0), SkSlCode = RasterShaderCode };
            
            for(int i=0; i<22; i++) { lA.ShaderHeights[i] = 0; lB.ShaderHeights[i] = 0; }
            
            // Tvinga fram korrekt storlek på alla arrayer
            lA.ShaderParams = new float[24]; lA.ShaderHeights = new float[24];
            lA.ShaderColors = new SKColor[24]; lA.ShaderColorsTo = new SKColor[24];
            lB.ShaderParams = new float[24]; lB.ShaderHeights = new float[24];
            lB.ShaderColors = new SKColor[24]; lB.ShaderColorsTo = new SKColor[24];
            
            _frameA.Add(lA);
            _frameB.Add(lB);
            _currentScreen = 0;
        }
        
        // Tvinga UI-tråden att uppdatera storleken på vyn
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            // Detta tvingar Viewbox att räkna om skalningen
            // Vi gör det via ett anrop till InvalidateMeasure i MainWindow
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    
  
    
    public void SetDrawingScreen(int id)
    {
        lock (LockObject)
        {
            while (InactiveFrame.Count <= id)
            {
                var layer = new GpuLayer { Bitmap = CreateEmptyBitmap(Width > 0 ? Width : 640, Height > 0 ? Height : 480), Offset = new Point(0, 0) };
                layer.SkSlCode = RasterShaderCode; 
                // Initiera ShaderHeights till 0 så att lagret är transparent som standard
                for(int i=0; i<22; i++) layer.ShaderHeights[i] = 0;
                InactiveFrame.Add(layer);
            }
            while (ActiveFrame.Count <= id)
            {
                var layer = new GpuLayer { Bitmap = CreateEmptyBitmap(Width > 0 ? Width : 640, Height > 0 ? Height : 480), Offset = new Point(0, 0) };
                layer.SkSlCode = RasterShaderCode;
                for(int i=0; i<22; i++) layer.ShaderHeights[i] = 0;
                ActiveFrame.Add(layer);
            }
            _currentScreen = id;
        }
    }

    private void EnsureScreen()
    {
        // Vi kollar om listorna är tomma istället för om de är null
        if (_frameA.Count == 0 || _frameB.Count == 0)
        {
            lock (LockObject)
            {
                // Om de är tomma, initiera standardstorlek (t.ex. 640x480)
                _frameA.Clear();
                _frameB.Clear();

                _frameA.Add(new GpuLayer 
                { 
                    Bitmap = CreateEmptyBitmap(640, 480),
                    Offset = new Point(0, 0)
                });

                _frameB.Add(new GpuLayer 
                { 
                    Bitmap = CreateEmptyBitmap(640, 480),
                    Offset = new Point(0, 0)
                });
            }
        }
    }
    
    private WriteableBitmap CreateEmptyBitmap(int w, int h, Color? background = null, GpuLayer? targetLayer = null)
    {
        // Om vi skickar med ett targetLayer, sätt shadern där direkt
        if (targetLayer != null) {
            targetLayer.SkSlCode = RasterShaderCode;
        }
        
        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        // Om ingen bakgrund anges: helt transparent
        Color bg = background ?? Colors.Transparent;
        
        using (var fb = bmp.Lock())
        {
            unsafe
            {
                uint* p = (uint*)fb.Address;
                int count = w * h;

                // Premultiplied alpha
                uint a = bg.A;
                uint r = (uint)(bg.R * a / 255);
                uint g = (uint)(bg.G * a / 255);
                uint b = (uint)(bg.B * a / 255);
                uint val = (a << 24) | (r << 16) | (g << 8) | b;

                for (int i = 0; i < count; i++)
                    p[i] = val;
            }
        }
        return bmp;
    }


    
    public void Clear(Color color)
    {
        EnsureScreen();
        // Rensa bara den nuvarande aktiva skärmen/lagret
        ClearBitmap(GetActiveScreen(), color);
            
        // Om det är lager 0 vi rensar, kan vi även nollställa texter
        if (_currentScreen == 0) 
        {
            _fontTexts.Clear();
            _rainbows.Clear(); // <--- Lägg till denna rad!
        }

            
        Refresh();
    }

    private void ClearBitmap(WriteableBitmap bmp, Color c)
    {
        using var fb = bmp.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;

            byte a = c.A;
            byte r = (byte)(c.R * a / 255);
            byte g = (byte)(c.G * a / 255);
            byte b = (byte)(c.B * a / 255);

            for (var i = 0; i < fb.RowBytes * bmp.PixelSize.Height; i += 4)
            {
                p[i + 0] = b;
                p[i + 1] = g;
                p[i + 2] = r;
                p[i + 3] = a;
            }
        }
    }

    
    private void ClearBitmap2(WriteableBitmap bmp, Color c)
    {
        using var fb = bmp.Lock();
        unsafe
        {
            int rowPixels = bmp.PixelSize.Width;
            
            var p = (byte*)fb.Address;

            byte a = c.A;
            byte r = (byte)(c.R * a / 255);
            byte g = (byte)(c.G * a / 255);
            byte b = (byte)(c.B * a / 255);

            for (var i = 0; i < rowPixels; i += 4)
            {
                p[i + 0] = b;
                p[i + 1] = g;
                p[i + 2] = r;
                p[i + 3] = a;
            }
            
            // Kopiera raden till resten av bitmapen
            for (int y = 1; y < bmp.PixelSize.Height; y++)
            {
                Buffer.MemoryCopy(p, p + y * rowPixels, rowPixels * sizeof(uint), rowPixels * sizeof(uint));
            }
        }
    }
    
    public void SwapBuffers()
    {
        lock (LockObject)
        {
            _isAActive = !_isAActive;
            // Nu byter vi bara pekare, ingen kopiering här!
        }
    }
    
    public void DoubleBuffer()
    {
        lock (LockObject)
        {
            // Kopiera Inactive -> Active (eller vice versa) 
            // för att säkerställa att båda buffertarna ser likadana ut
            for (int i = 0; i < ActiveFrame.Count && i < InactiveFrame.Count; i++)
            {
                var sourceBmp = InactiveFrame[i].Bitmap;
                var destBmp = ActiveFrame[i].Bitmap;
                
                if (sourceBmp != null && destBmp != null)
                {
                    using var src = sourceBmp.Lock();
                    using var dst = destBmp.Lock();
                    unsafe
                    {
                        long size = (long)src.RowBytes * sourceBmp.PixelSize.Height;
                        Buffer.MemoryCopy((void*)src.Address, (void*)dst.Address, size, size);
                    }
                }
            }
        }
    }
    
    public void Refresh()
    {
    }

    public void Scroll(int sid, int x, int y)
    {
        if (sid >= 0 && sid < InactiveFrame.Count) 
            InactiveFrame[sid].Offset = new Point(-x, -y);
    }

    public Point GetScreenOffset(int sid)
    {
        if (sid >= 0 && sid < InactiveFrame.Count) 
            return InactiveFrame[sid].Offset;
        return new Point(0, 0);
    }

    // ---------------- Drawing ----------------
    public void Plot(int x, int y) => Plot(x, y, Ink);

    public void Plot(int x, int y, Color c)
    {
        lock (LockObject)
        {
            var bmp = GetActiveScreen();
            if ((uint)x >= (uint)bmp.PixelSize.Width || (uint)y >= (uint)bmp.PixelSize.Height) return;
            using var fb = bmp.Lock();
            unsafe {
                uint* p = (uint*)fb.Address;
                uint val = (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
                if ((val & 0x00FFFFFF) == 0) p[y * (fb.RowBytes / 4) + x] = 0;
                else p[y * (fb.RowBytes / 4) + x] = val | 0xFF000000;
            }
        }
    }

    public void Line(int x0, int y0, int x1, int y1) => Line(x0, y0, x1, y1, Ink);

    public void Line(int x0, int y0, int x1, int y1, Color c)
    {
        lock (LockObject)
        {
            EnsureScreen();
            int dx = Math.Abs(x1 - x0),
                sx = x0 < x1 ? 1 : -1,
                dy = -Math.Abs(y1 - y0),
                sy = y0 < y1 ? 1 : -1,
                err = dx + dy;
            while (true)
            {
                Plot(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }

    public void Box(int x1, int y1, int x2, int y2)
    {
        lock (LockObject)
        {
            Normalize(ref x1, ref y1, ref x2, ref y2);
            Line(x1, y1, x2, y1, Ink);
            Line(x2, y1, x2, y2, Ink);
            Line(x2, y2, x1, y2, Ink);
            Line(x1, y2, x1, y1, Ink);
        }
    }

    public void Bar(int x1, int y1, int x2, int y2)
    {
        lock (LockObject)
        {
            EnsureScreen();
            Normalize(ref x1, ref y1, ref x2, ref y2);
            x1 = Math.Clamp(x1, 0, Width - 1);
            x2 = Math.Clamp(x2, 0, Width - 1);
            y1 = Math.Clamp(y1, 0, Height - 1);
            y2 = Math.Clamp(y2, 0, Height - 1);
            using var fb = GetActiveScreen().Lock();
            unsafe
            {
                var p = (byte*)fb.Address;
                for (var y = y1; y <= y2; y++)
                {
                    var r = p + y * fb.RowBytes;
                    for (var x = x1; x <= x2; x++)
                    {
                        var i = x * 4;
                        r[i + 0] = Ink.B;
                        r[i + 1] = Ink.G;
                        r[i + 2] = Ink.R;
                        r[i + 3] = Ink.A;
                    }
                }
            }
        }
    }

    public void DrawText(int x, int y, string t)
    {
        if (string.IsNullOrEmpty(t)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            EnsureScreen();
            var ft = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Arial"),
                20, new SolidColorBrush(Ink));
            var ps = new PixelSize((int)Math.Max(1, ft.Width), (int)Math.Max(1, ft.Height));
            using var rtb = new RenderTargetBitmap(ps);
            using (var ctx = rtb.CreateDrawingContext())
            {
                ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ps.Width, ps.Height));
                ctx.DrawText(ft, new Point(0, 0));
            }

            var b = new byte[ps.Width * ps.Height * 4];
            unsafe
            {
                fixed (byte* p = b) rtb.CopyPixels(new PixelRect(ps), (nint)p, b.Length, ps.Width * 4);
            }

            using (var dst = GetActiveScreen().Lock())
                unsafe
                {
                    var dp = (byte*)dst.Address;
                    for (int r = 0; r < ps.Height; r++)
                    {
                        int ty = y + r;
                        if (ty < 0 || ty >= Height) continue;
                        var dr = dp + ty * dst.RowBytes;
                        for (int c = 0; c < ps.Width; c++)
                        {
                            int tx = x + c;
                            if (tx < 0 || tx >= Width) continue;
                            int si = (r * ps.Width + c) * 4, di = tx * 4;
                            if (b[si + 3] > 0)
                            {
                                dr[di + 0] = b[si + 0];
                                dr[di + 1] = b[si + 1];
                                dr[di + 2] = b[si + 2];
                                dr[di + 3] = b[si + 3];
                            }
                        }
                    }
                }

            Refresh();
        });
    }

    public void FontLoad(int id, string file, int tw, int th)
    {
    try
    {
        using var b = new Bitmap(file);
        var font = new Font { CharWidth = tw, CharHeight = th };
        int cols = (int)b.Size.Width / tw;
        int rows = (int)b.Size.Height / th;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                var t = CreateEmptyBitmap(tw, th);

                using (var fb = t.Lock())
                {
                    // 1. KOPIERA PIXLAR FRÅN FONT-ATLAS
                    b.CopyPixels(
                        new PixelRect(x * tw, y * th, tw, th),
                        fb.Address,
                        fb.RowBytes * th,
                        fb.RowBytes);

                    unsafe
                    {
                        uint* p = (uint*)fb.Address;
                        int count = tw * th;

                        for (int i = 0; i < count; i++)
                        {
                            uint pixel = p[i];

                            uint a = (pixel >> 24) & 0xFF;
                            uint r = (pixel >> 16) & 0xFF;
                            uint g = (pixel >> 8) & 0xFF;
                            uint bcol = pixel & 0xFF;

                            // Svart = transparent
                            if (r == 0 && g == 0 && bcol == 0)
                            {
                                p[i] = 0;
                            }
                            else
                            {
                                // Premultiplied alpha
                                r = (r * a) / 255;
                                g = (g * a) / 255;
                                bcol = (bcol * a) / 255;

                                // BGRA + Premul (Skia)
                                p[i] =
                                    (a << 24) |
                                    (r << 0) |
                                    (g << 8) |
                                    (bcol << 16);
                            }
                        }
                    }
                }

                font.CharBitmaps.Add(t);
            }
        }

        _fonts[id] = font;
    }
    catch
    {
    }
}
    
        public void FontRotate(int id, double angle) { if (_fonts.TryGetValue(id, out var f)) f.Angle = angle; }
        public void FontZoom(int id, double zx, double zy) { if (_fonts.TryGetValue(id, out var f)) { f.ZoomX = zx; f.ZoomY = zy; } }
        public void FontMap(int id, string map) { if (_fonts.TryGetValue(id, out var f)) f.CharMap = map; }

        public unsafe void FontPrint(int id, int x, int y, string text)
        {
            if (!_fonts.TryGetValue(id, out var f)) return;

            lock (LockObject)
            {
                if (_currentScreen == 0)
                {
                    _fontTexts.Add(new QueuedFontText
                    {
                        FontId = id,
                        X = x,
                        Y = y,
                        Text = text,
                        Angle = f.Angle,
                        ZoomX = f.ZoomX,
                        ZoomY = f.ZoomY
                    });
                }
                else
                {
                    var target = GetActiveScreen();
                    using var dst = target.Lock();
                    byte* dp = (byte*)dst.Address;
                    int rb = dst.RowBytes;
                    var qt = new QueuedFontText { Angle = f.Angle, ZoomX = f.ZoomX, ZoomY = f.ZoomY };
                    int curX = x;

                    foreach (var c in text)
                    {
                        if (c == ' ') { curX += (int)(f.CharWidth * f.ZoomX); continue; }
                        RenderFontCharInternal(dp, rb, f, curX, y, c, qt);
                        curX += (int)(f.CharWidth * f.ZoomX);
                    }
                }
            }
        }

    
        public void FontClear()
        {
            lock (LockObject)
            {
                _fontTexts.Clear();
            }
        }

        private unsafe void RenderFontTextInternal(byte* dp, int rb, QueuedFontText qt)
        {
            if (!_fonts.TryGetValue(qt.FontId, out var f)) return;
            int curX = qt.X;
            foreach (var c in qt.Text) 

            {            
                if (c == ' ')
                { 
                    curX += (int)(f.CharWidth * qt.ZoomX);
                    continue;
                }   
                RenderFontCharInternal(dp, rb, f, curX, qt.Y, c, qt); 
                curX += (int)(f.CharWidth * qt.ZoomX);
            }
        }

        private unsafe void RenderFontCharInternal(byte* dp, int rb, Font f, int x, int y, char c, QueuedFontText qt)
        {
            string map = string.IsNullOrEmpty(f.CharMap) ? "" : f.CharMap;
            int charIdx = !string.IsNullOrEmpty(map) ? map.IndexOf(char.ToUpper(c)) : c - 32;
            if (charIdx < 0 || charIdx >= f.CharBitmaps.Count) return;

            var charBmp = f.CharBitmaps[charIdx];
            using var src = charBmp.Lock();
            byte* sp = (byte*)src.Address;
            int srb = src.RowBytes;

            float zoomX = (float)qt.ZoomX;
            float zoomY = (float)qt.ZoomY;

            float cx = f.CharWidth / 2f;
            float cy = f.CharHeight / 2f;

            // För rotation
            double angleRad = qt.Angle * Math.PI / 180.0;
            double cosA = Math.Cos(angleRad);
            double sinA = Math.Sin(angleRad);

            int w = (int)(f.CharWidth * Math.Abs(zoomX));
            int h = (int)(f.CharHeight * Math.Abs(zoomY));

            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    // Koordinater i "tecknets lokala centrum"
                    float nx = (px / Math.Abs(zoomX)) - cx;
                    float ny = (py / Math.Abs(zoomY)) - cy;

                    // Spegling vid negativ zoom
                    if (zoomX < 0) nx = -nx;
                    if (zoomY < 0) ny = -ny;

                    // Rotation
                    double rx = nx * cosA - ny * sinA;
                    double ry = nx * sinA + ny * cosA;

                    // Åter till tecknets pixelkoordinater
                    int srcX = (int)(cx + rx);
                    int srcY = (int)(cy + ry);

                    if (srcX < 0 || srcX >= f.CharWidth || srcY < 0 || srcY >= f.CharHeight) continue;

                    byte* srcPx = sp + srcY * srb + srcX * 4;
                    if (srcPx[3] == 0) continue;

                    int di = (x + px) * 4;
                    byte* dr = dp + (y + py) * rb;
                    dr[di + 0] = srcPx[0];
                    dr[di + 1] = srcPx[1];
                    dr[di + 2] = srcPx[2];
                    dr[di + 3] = 255;
                }
            }
        }

    
        public void FontChar(int id, int x, int y, string c)
        {
            if (!_fonts.TryGetValue(id, out var f) || string.IsNullOrEmpty(c)) return;
            
            // Justering: Ditt ark verkar börja på 'A' (ASCII 65). 
            // Om vi vill ha siffror och tecken som i din bild behöver vi mappa rätt.
            int charIdx = -1;
            if (!string.IsNullOrEmpty(f.CharMap))
            {
                // Om vi har en karta, använd den
                charIdx = f.CharMap.IndexOf(char.ToUpper(c[0]));
            }
            else
            {
                // Annars kör vi standard ASCII-offset (börjar på space)
                charIdx = c[0] - 32;
            }
            
            if (charIdx < 0 || charIdx >= f.CharBitmaps.Count) return;

            var charBmp = f.CharBitmaps[charIdx];
            var target = GetActiveScreen();
            
            // Vi använder en förenklad RenderSprite-logik här för att stödja Zoom/Rotate
            using var dst = target.Lock();
            using var src = charBmp.Lock();
            unsafe
            {
                byte* dp = (byte*)dst.Address;
                byte* sp = (byte*)src.Address;
                int rb = dst.RowBytes;
                int srb = src.RowBytes;
                
                double angleRad = f.Angle * Math.PI / 180.0;
                double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
                double invZx = 1.0 / f.ZoomX, invZy = 1.0 / f.ZoomY;
                int hx = f.CharWidth / 2, hy = f.CharHeight / 2;

                double radius = Math.Sqrt(f.CharWidth * f.CharWidth + f.CharHeight * f.CharHeight) * Math.Max(f.ZoomX, f.ZoomY);
                int minX = Math.Max(0, (int)(x - radius)), maxX = Math.Min(target.PixelSize.Width - 1, (int)(x + radius));
                int minY = Math.Max(0, (int)(y - radius)), maxY = Math.Min(target.PixelSize.Height - 1, (int)(y + radius));

                for (int py = minY; py <= maxY; py++)
                {
                    byte* rowPtr = dp + py * rb;
                    double dy = py - y;
                    for (int px = minX; px <= maxX; px++)
                    {
                        double dx = px - x;
                        double lx = (dx * cosA + dy * sinA) * invZx + hx;
                        double ly = (dy * cosA - dx * sinA) * invZy + hy;
                        int ilx = (int)lx, ily = (int)ly;

                        if (ilx >= 0 && ilx < f.CharWidth && ily >= 0 && ily < f.CharHeight)
                        {
                            byte* srcPx = sp + ily * srb + ilx * 4;
                            if (srcPx[3] == 0 || (srcPx[0] == 0 && srcPx[1] == 0 && srcPx[2] == 0)) continue; 
                            int di = px * 4;
                            rowPtr[di + 0] = srcPx[0];
                            rowPtr[di + 1] = srcPx[1];
                            rowPtr[di + 2] = srcPx[2];
                            rowPtr[di + 3] = 255;
                        }
                    }
                }
            }
        }
    
        
        public void LoadBackground(string f)
        {
            try {
                using var b = new Bitmap(f);
                lock (LockObject) {
                    EnsureScreen();
                    var layer = InactiveFrame[_currentScreen];
                    using (var fb = layer.Bitmap.Lock()) {
                        b.CopyPixels(new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb.Address, fb.RowBytes * layer.Bitmap.PixelSize.Height, fb.RowBytes);
                        unsafe {
                            uint* p = (uint*)fb.Address;
                            int count = layer.Bitmap.PixelSize.Width * layer.Bitmap.PixelSize.Height;
                            for (int i = 0; i < count; i++) {
                                uint pixel = p[i];
                                // Byt plats på R och B (från RGBA till BGRA)
                                uint a = (pixel >> 24) & 0xFF;
                                uint r = (pixel >> 16) & 0xFF;
                                uint g = (pixel >> 8) & 0xFF;
                                uint bColor = pixel & 0xFF;
                            
                                if (r == 0 && g == 0 && bColor == 0) p[i] = 0;
                                else p[i] = (a << 24) | (r << 0) | (g << 8) | (bColor << 16); // Korrekt ordning för Skia
                            }
                        }
                    }
                }
            } catch { }
        }

    // ---------------- Tiles ----------------
    public int GetTilesInWidth() => _tilesInWidth; // NYTT: Getter

    public void LoadTileBank(string f, int tw, int th) {
        try {
            using var b = new Bitmap(f); 
            _tileWidth = tw; 
            _tileHeight = th; 
            _tiles.Clear();
            
            // Uppdatera klassvariabeln för att undvika division med noll i paletten
            _tilesInWidth = (int)b.Size.Width / tw;
            
            int cs = _tilesInWidth; 
            int rs = (int)b.Size.Height / th;

            for (int y = 0; y < rs; y++) {
                for (int x = 0; x < cs; x++) {
                    var t = CreateEmptyBitmap(tw, th);
                    using (var fb = t.Lock()) {
                        b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                        unsafe {
                            var p = (byte*)fb.Address;
                            for (int i = 0; i < tw * th; i++) {
                                byte temp = p[i * 4 + 0];
                                p[i * 4 + 0] = p[i * 4 + 2];
                                p[i * 4 + 2] = temp;
                            }
                        }
                    }
                    _tiles.Add(t);
                }
            }
        }
        catch {
            // Logga gärna felet här om du vill, t.ex. Console.WriteLine(ex.Message);
        }
    }

    public void LoadTileBank(System.IO.Stream stream, int tw, int th)
    {
        try
        {
            using var b = new Bitmap(stream);
            _tileWidth = tw;
            _tileHeight = th;
            _tiles.Clear();

            // Deklarera tilesInWidth här och spara den i klassvariabeln _tilesInWidth
            int tilesInWidth = (int)b.Size.Width / tw;
            _tilesInWidth = tilesInWidth;

            int tilesInHeight = (int)b.Size.Height / th;

            for (int y = 0; y < tilesInHeight; y++)
            {
                for (int x = 0; x < tilesInWidth; x++)
                {
                    var t = CreateEmptyBitmap(tw, th);
                    using (var fb = t.Lock())
                    {
                        // Kopiera exakt den rutan från källbilden
                        b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                        unsafe
                        {
                            var p = (byte*)fb.Address;
                            for (int i = 0; i < tw * th; i++)
                            {
                                byte temp = p[i * 4 + 0];
                                p[i * 4 + 0] = p[i * 4 + 2];
                                p[i * 4 + 2] = temp;
                            }
                        }
                    }

                    _tiles.Add(t);
                }
            }
        }
        catch
        {
        }
    }

    public void SetMapSize(int newW, int newH)
    {
        newW = Math.Max(1, newW);
        newH = Math.Max(1, newH);
        
        var oldMap = _map;
        _map = new int[newW, newH];
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            _map[x, y] = -1;

        int pixelW = newW * _tileWidth;
        int pixelH = newH * _tileHeight;

        // Uppdatera bitmappen i det aktuella GPU-lagret istället för i den gamla _screens-listan
        if (_currentScreen < InactiveFrame.Count)
        {
            var layer = InactiveFrame[_currentScreen];
            InactiveFrame[_currentScreen] = new GpuLayer 
            { 
                Bitmap = CreateEmptyBitmap(pixelW, pixelH),
                Offset = layer.Offset,
                Opacity = layer.Opacity
            };
            var layer2 = ActiveFrame[_currentScreen];
            ActiveFrame[_currentScreen] = new GpuLayer 
            { 
                Bitmap = CreateEmptyBitmap(pixelW, pixelH),
                Offset = layer.Offset,
                Opacity = layer.Opacity
            };
        }

        // Initiera banan med -1 (tomt) istället för 0 (första tilen)
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            _map[x, y] = -1;

        // Kopiera över gamla datan om den fanns
        int copyW = Math.Min(newW, oldMap.GetLength(0));
        int copyH = Math.Min(newH, oldMap.GetLength(1));

        for (int y = 0; y < copyH; y++)
        for (int x = 0; x < copyW; x++)
            _map[x, y] = oldMap[x, y];
    }

    public void SetMapTile(int x, int y, int tileId)
    {
        if (x >= 0 && x < _map.GetLength(0) && y >= 0 && y < _map.GetLength(1))
        {
            _map[x, y] = tileId;
        }
    }

    public void ClearMap()
    {
        for (int y = 0; y < _map.GetLength(1); y++)
        for (int x = 0; x < _map.GetLength(0); x++)
            _map[x, y] = -1;
    }

    public void DrawMap(int ox, int oy)
    {
        if (_map.GetLength(0) == 0 || _tiles.Count == 0) return;

        // Hämta det lagret vi ska rita på
        var target = GetActiveScreen();
        int targetW = target.PixelSize.Width;
        int targetH = target.PixelSize.Height;

        for (int y = 0; y < _map.GetLength(1); y++)
        {
            for (int x = 0; x < _map.GetLength(0); x++)
            {
                int tid = _map[x, y];
                if (tid < 0 || tid >= _tiles.Count) continue;

                // Beräkna koordinaterna i det stora lagret
                int dx = x * _tileWidth - ox;
                int dy = y * _tileHeight - oy;

                // VIKTIGT: Rita bara om vi är inom lagrets gränser
                if (dx >= 0 && dx < targetW && dy >= 0 && dy < targetH)
                {
                    DrawTileToBackbuffer(_tiles[tid], dx, dy);
                }
            }
        }
    }

    private void DrawTileToBackbuffer(WriteableBitmap t, int dx, int dy)
    {
        var target = GetActiveScreen();
        using var dst = target.Lock();
        using var src = t.Lock();
        unsafe
        {
            var dp = (byte*)dst.Address;
            var sp = (byte*)src.Address;
            // VIKTIGT: Använd target.PixelSize istället för globala Width/Height
            int tw = target.PixelSize.Width;
            int th = target.PixelSize.Height;

            for (int y = 0; y < _tileHeight; y++)
            {
                int ty = dy + y;
                if (ty < 0 || ty >= th) continue;
                var dr = dp + ty * dst.RowBytes;
                var sr = sp + y * src.RowBytes;
                for (int x = 0; x < _tileWidth; x++)
                {
                    int tx = dx + x;
                    if (tx < 0 || tx >= tw) continue;
                    int si = x * 4, di = tx * 4;
                    dr[di + 0] = sr[si + 0];
                    dr[di + 1] = sr[si + 1];
                    dr[di + 2] = sr[si + 2];
                    dr[di + 3] = 255;
                }
            }
        }
    }

    public int GetMapWidth() => _map.GetLength(0);
    public int GetMapHeight() => _map.GetLength(1);

    public int GetMapTile(int x, int y)
    {
        if (x >= 0 && x < _map.GetLength(0) && y >= 0 && y < _map.GetLength(1))
            return _map[x, y];
        return -1;
    }



    // ---------------- Sprites ----------------

    public void CreateSprite(int id, int w, int h)
    {
        var f = CreateEmptyBitmap(w, h);
        _sprites[id] = new Sprite(w, h, f);
        SpriteClear(id, Colors.Magenta);
    }

    public bool HasSprite(int id) => _sprites.ContainsKey(id);

    public (int w, int h) GetSpriteSize(int id)
    {
        var s = GetSprite(id);
        return (s.Width, s.Height);
    }

    public WriteableBitmap GetSpriteBitmap(int id) => GetSprite(id).Bitmap;
    public List<int> GetSpriteIds() => _sprites.Keys.OrderBy(id => id).ToList();

    public void LoadSprite(int id, string fileName)
    {
        try
        {
            using var b = new Bitmap(fileName);
            int w = (int)b.Size.Width, h = (int)b.Size.Height;
            CreateSprite(id, w, h);
            var s = GetSprite(id);
            using (var fb = s.Bitmap.Lock())
            {
                b.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);
                unsafe
                {
                    var p = (byte*)fb.Address;
                    for (int i = 0; i < w * h; i++)
                    {
                        byte temp = p[i * 4 + 0];
                        p[i * 4 + 0] = p[i * 4 + 2];
                        p[i * 4 + 2] = temp;
                    }

                    s.TransparentKey = Color.FromArgb(p[3], p[2], p[1], p[0]);
                }
            }
        }
        catch
        {
        }
    }

    public void AddFrame(int id, string file)
    {
        var s = GetSprite(id);
        using var b = new Bitmap(file);
        var f = CreateEmptyBitmap(s.Width, s.Height);
        using (var fb = f.Lock())
        {
            b.CopyPixels(new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb.Address, fb.RowBytes * s.Height,
                fb.RowBytes);
            unsafe
            {
                var p = (byte*)fb.Address;
                for (int i = 0; i < s.Width * s.Height; i++)
                {
                    byte temp = p[i * 4 + 0];
                    p[i * 4 + 0] = p[i * 4 + 2];
                    p[i * 4 + 2] = temp;
                }
            }
        }

        s.Frames.Add(f);
    }

    public void SetSpriteFrame(int id, int idx)
    {
        var s = GetSprite(id);
        if (idx >= 0 && idx < s.Frames.Count) s.CurrentFrame = idx;
    }

    public void SpriteHandle(int id, int hx, int hy)
    {
        var s = GetSprite(id);
        s.HandleX = hx;
        s.HandleY = hy;
    }

    public void SpritePos(int id, int x, int y)
    {
        var s = GetSprite(id);
        s.X = x;
        s.Y = y;
    }

    public void SpriteRotate(int id, double angle) => GetSprite(id).Angle = angle;

    public void SpriteZoom(int id, double zx, double zy)
    {
        var s = GetSprite(id);
        s.ZoomX = zx;
        s.ZoomY = zy;
    }

    public void SpriteOn(int id) => GetSprite(id).Visible = true;
    public void SpriteOff(int id) => GetSprite(id).Visible = false;

    public void SpriteSetPixel(int id, int x, int y, Color c)
    {
        var s = GetSprite(id);
        if ((uint)x >= (uint)s.Width || (uint)y >= (uint)s.Height) return;
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var r = (byte*)fb.Address + y * fb.RowBytes;
            var i = x * 4;
            r[i + 0] = c.B;
            r[i + 1] = c.G;
            r[i + 2] = c.R;
            r[i + 3] = c.A;
        }
    }

    public void SpriteClear(int id, Color c)
    {
        var s = GetSprite(id);
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var i = 0; i < fb.RowBytes * s.Height; i += 4)
            {
                p[i + 0] = c.B;
                p[i + 1] = c.G;
                p[i + 2] = c.R;
                p[i + 3] = c.A;
            }
        }
    }

    public void SpriteInk(int id, Color c) => GetSprite(id).Ink = c;

    public void SpritePlot(int id, int x, int y)
    {
        var s = GetSprite(id);
        SpriteSetPixel(id, x, y, s.Ink);
    }

    public void SpriteBar(int id, int x1, int y1, int x2, int y2)
    {
        var s = GetSprite(id);
        Normalize(ref x1, ref y1, ref x2, ref y2);
        x1 = Math.Clamp(x1, 0, s.Width - 1);
        x2 = Math.Clamp(x2, 0, s.Width - 1);
        y1 = Math.Clamp(y1, 0, s.Height - 1);
        y2 = Math.Clamp(y2, 0, s.Height - 1);
        using var fb = s.Bitmap.Lock();
        unsafe
        {
            var p = (byte*)fb.Address;
            for (var y = y1; y <= y2; y++)
            {
                var r = p + y * fb.RowBytes;
                for (var x = x1; x <= x2; x++)
                {
                    var i = x * 4;
                    r[i + 0] = s.Ink.B;
                    r[i + 1] = s.Ink.G;
                    r[i + 2] = s.Ink.R;
                    r[i + 3] = s.Ink.A;
                }
            }
        }
    }

    public bool SpriteHit(int id1, int id2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) || !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;
        int x1 = s1.X - s1.HandleX, y1 = s1.Y - s1.HandleY, x2 = s2.X - s2.HandleX, y2 = s2.Y - s2.HandleY;
        return x1 < x2 + s2.Width && x1 + s1.Width > x2 && y1 < y2 + s2.Height && y1 + s1.Height > y2;
    }

    public Sprite GetSprite(int id)
    {
        if (!_sprites.TryGetValue(id, out var s))
        {
            CreateSprite(id, 32, 32);
            return _sprites[id];
        }

        return s;
    }

    private void Normalize(ref int x1, ref int y1, ref int x2, ref int y2)
    {
        if (x2 < x1) (x1, x2) = (x2, x1);
        if (y2 < y1) (y1, y2) = (y2, y1);
    }

    private unsafe void RenderSpriteInternal(byte* dp, int rb, Sprite s)
    {
        var bmp = s.Bitmap;
        int sw = bmp.PixelSize.Width, sh = bmp.PixelSize.Height;
        var k = s.TransparentKey;

        double angleRad = s.Angle * Math.PI / 180.0;
        double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);
        double invZoomX = 1.0 / s.ZoomX, invZoomY = 1.0 / s.ZoomY;

        // Enkel bounding box för att veta vilka pixlar på skärmen vi behöver kontrollera
        double radius = Math.Sqrt(sw * sw + sh * sh) * Math.Max(s.ZoomX, s.ZoomY);
        int minX = Math.Max(0, (int)(s.X - radius)), maxX = Math.Min(Width - 1, (int)(s.X + radius));
        int minY = Math.Max(0, (int)(s.Y - radius)), maxY = Math.Min(Height - 1, (int)(s.Y + radius));

        using var sLock = bmp.Lock();
        byte* sp = (byte*)sLock.Address;
        int srb = sLock.RowBytes;

        for (int y = minY; y <= maxY; y++)
        {
            byte* rowPtr = dp + y * rb;
            double dy = y - s.Y;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - s.X;
                // Rotera och skala tillbaka till käll-spritens koordinater
                double lx = (dx * cosA + dy * sinA) * invZoomX + s.HandleX;
                double ly = (dy * cosA - dx * sinA) * invZoomY + s.HandleY;
                int ilx = (int)lx, ily = (int)ly;

                if (ilx >= 0 && ilx < sw && ily >= 0 && ily < sh)
                {
                    byte* srcPx = sp + ily * srb + ilx * 4;
                    if (srcPx[2] == k.R && srcPx[1] == k.G && srcPx[0] == k.B) continue;
                    int di = x * 4;
                    rowPtr[di + 0] = srcPx[0];
                    rowPtr[di + 1] = srcPx[1];
                    rowPtr[di + 2] = srcPx[2];
                    rowPtr[di + 3] = 255;
                }
            }
        }
    }
}