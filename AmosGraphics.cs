using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Vector = Avalonia.Vector;

namespace AmosLikeBasic;

public sealed class GpuLayer
{
    public WriteableBitmap Bitmap { get; init; } = null!;
    public WriteableBitmap? CompositeBitmap { get; set; }
    
    public Point Offset { get; set; }
    public double Opacity { get; set; } = 1.0;
    public float FadeTarget { get; set; } = -1f;
    public float FadeStep   { get; set; } = 0f;
    public bool Visible { get; set; } = true; 
    public float Timer { get; set; } // For animations
    // NYTT: Array för att skicka in t.ex. Y-positioner för 50 bars
    public float[] ShaderParams { get; set; } = new float[50]; 
    public float[] ShaderHeights { get; set; } = new float[50]; 
    public SKColor[] ShaderColors { get; set; } = new SKColor[50]; 
    public SKColor[] ShaderColorsTo { get; set; } = new SKColor[50];
    
    public Vector4[] ShaderValues { get; set; } = new Vector4[2]; // t.ex. 8 slots
    
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
                var bitmapToUse = _layer.CompositeBitmap ?? _layer.Bitmap;
                using var fb = bitmapToUse.Lock();
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

                // Lägg till denna för att shadern ska veta skärmens storlek separat från bildens storlek
                if (_layer.CachedEffect.Uniforms.Contains("iScreenResolution"))
                    uniforms.Add("iScreenResolution", new float[] { (float)_destRect.Width, (float)_destRect.Height });

                if (_layer.CachedEffect.Uniforms.Contains("iTime"))
                    uniforms.Add("iTime", _layer.Timer);

                if (_layer.CachedEffect.Uniforms.Contains("uParams")) 
                {
                    int slotCount = _layer.ShaderValues.Length;
                    float[] uParamData = new float[slotCount * 4]; // varje Vector4 = 4 floats

                    for (int i = 0; i < slotCount; i++)
                    {
                        Vector4 v = _layer.ShaderValues[i];
                        int baseIndex = i * 4;
                        uParamData[baseIndex + 0] = v.X;
                        uParamData[baseIndex + 1] = v.Y;
                        uParamData[baseIndex + 2] = v.Z;
                        uParamData[baseIndex + 3] = v.W;
                    }

                    uniforms.Add("uParams", uParamData);
                }
                    
                // Säkerställ att uPositions och uHeights är exakt 50 element
                if (_layer.CachedEffect.Uniforms.Contains("uPositions")) 
                {
                    float[] p50 = new float[50];
                    if (_layer.ShaderParams != null) 
                        Array.Copy(_layer.ShaderParams, p50, Math.Min(_layer.ShaderParams.Length, 50));
                    uniforms.Add("uPositions", p50);
                }

                if (_layer.CachedEffect.Uniforms.Contains("uHeights")) 
                {
                    float[] h50 = new float[50];
                    if (_layer.ShaderHeights != null) 
                        Array.Copy(_layer.ShaderHeights, h50, Math.Min(_layer.ShaderHeights.Length, 50));
                    uniforms.Add("uHeights", h50);
                }

                if (_layer.CachedEffect.Uniforms.Contains("uColors")) 
                {
                    float[] cFrom = new float[50 * 4];
                    float[] cTo = new float[50 * 4];
        
                    int colorCount = (_layer.ShaderColors != null) ? Math.Min(50, _layer.ShaderColors.Length) : 0;
        
                    for (int i = 0; i < 50; i++) 
                    {
                        if (i < colorCount) {
                            cFrom[i * 4 + 0] = _layer.ShaderColors[i].Red / 255f;
                            cFrom[i * 4 + 1] = _layer.ShaderColors[i].Green / 255f;
                            cFrom[i * 4 + 2] = _layer.ShaderColors[i].Blue / 255f;
                            cFrom[i * 4 + 3] = _layer.ShaderColors[i].Alpha / 255f;

                            cTo[i * 4 + 0] = _layer.ShaderColorsTo[i].Red / 255f;
                            cTo[i * 4 + 1] = _layer.ShaderColorsTo[i].Green / 255f;
                            cTo[i * 4 + 2] = _layer.ShaderColorsTo[i].Blue / 255f;
                            cTo[i * 4 + 3] = _layer.ShaderColorsTo[i].Alpha / 255f;
                        } else {
                            // Standardvärden (Svart transparent)
                            cFrom[i * 4 + 3] = 0.0f;
                            cTo[i * 4 + 3] = 0.0f;
                        }
                    }
                    uniforms.Add("uColors", cFrom);
                    uniforms.Add("uColorsTo", cTo);
                }

                using var shader = _layer.CachedEffect.ToShader(true, uniforms, children);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(new SKRect((float)_destRect.X, (float)_destRect.Y, (float)_destRect.Right, (float)_destRect.Bottom), paint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Shader Render Crash: " + ex.Message);
            }
        }
    }
}


public sealed class AmosGpuView : Control
{
    private ScreenWindow? _screenWindow;

    public AmosGraphics Graphics { get; set; } = null!;
    private RenderTargetBitmap? _framebuffer;
    string RasterShaderCode = Shader.RasterShaderCode;
    string RasterShaderCode2 = Shader.RasterShaderCode2;
    string RasterShaderCode3 = Shader.RasterShaderCode3;
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

        EnsureFramebuffer(Graphics.Width, Graphics.Height);

        using (var fbCtx = _framebuffer!.CreateDrawingContext())
        {
            List<GpuLayer> layersCopy;
            List<AmosGraphics.QueuedFontText> fontTextsCopy;
            List<int> spriteIdsCopy;

            lock (Graphics.LockObject)
            {
                var amosRect = new Rect(0, 0, Graphics.Width, Graphics.Height);
                fbCtx.DrawRectangle(Brushes.Transparent, null, amosRect);

                layersCopy = new List<GpuLayer>(Graphics.ActiveFrame);
                fontTextsCopy = Graphics.GetQueuedTexts().ToList();
                spriteIdsCopy = Graphics.GetSpriteIds();
            }

            // RITA GPU-LAGER MED SHADER
            foreach (var layer in layersCopy)
            {
                if (layer.Bitmap == null) continue;
                if (!layer.Visible) continue;
                var bmpSize = layer.Bitmap.Size;
                var offset = layer.Offset;

                int w = (int)bmpSize.Width;
                int h = (int)bmpSize.Height;

                // Skapa CompositeBitmap om den saknas
                if (layer.CompositeBitmap == null ||
                    layer.CompositeBitmap.PixelSize != layer.Bitmap.PixelSize)
                {
                    layer.CompositeBitmap = new WriteableBitmap(
                        layer.Bitmap.PixelSize,
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Premul);
                }
                if (layer.SkSlCode is null)
                    layer.SkSlCode = RasterShaderCode;
                // Skapa en RenderTargetBitmap och rita allt dit via Avalonia
                var compositeRtb = new RenderTargetBitmap(
                    layer.Bitmap.PixelSize,
                    new Vector(96, 96));

                using (var compCtx = compositeRtb.CreateDrawingContext())
                {
                    var bmpSize2 = layer.Bitmap.Size;

                    // Rita layer.Bitmap (bakgrund, tiles, PLOT/LINE)
                    compCtx.DrawImage(layer.Bitmap, new Rect(bmpSize2), new Rect(bmpSize2));

                    int layerIndex = layersCopy.IndexOf(layer);
                    
                    // Rita font-texter
                    foreach (var qt in fontTextsCopy)
                    {
                        var f = Graphics.GetFont(qt.FontId);
                        if (f == null) continue;
                        if (qt.Layer != -1 && qt.Layer != layerIndex) continue;

                        double totalW = qt.Text.Length * f.CharWidth;
                        double cX = totalW / 2.0;
                        double cY = f.CharHeight / 2.0;
                        double angleRad = qt.Angle * Math.PI / 180.0;

                        for (int i = 0; i < qt.Text.Length; i++)
                        {
                            char c = qt.Text[i];
                            if (c == ' ') continue;
                            var charBmp = Graphics.GetFontChar(f, c);
                            if (charBmp == null) continue;

                            var transform =
                                Matrix.CreateTranslation(i * f.CharWidth, 0)
                                * Matrix.CreateTranslation(-cX, -cY)
                                * Matrix.CreateScale(qt.ZoomX, qt.ZoomY)
                                * Matrix.CreateRotation(angleRad)
                                * Matrix.CreateTranslation(cX + qt.X, cY + qt.Y);

                            using (compCtx.PushPostTransform(transform))
                                compCtx.DrawImage(charBmp, new Rect(charBmp.Size),
                                    new Rect(0, 0, f.CharWidth, f.CharHeight));
                        }
                    }

                    // Rita sprites
                    foreach (var id in spriteIdsCopy)
                    {
                        var sprite = Graphics.GetSprite(id);
                        if (!sprite.Visible) continue;
                        if (sprite.Layer != -1 && sprite.Layer != layerIndex) continue;

                        var bmp = sprite.GetBitmap(Graphics.GetImageBank());

                        double cX = bmp.Size.Width / 2.0;
                        double cY = bmp.Size.Height / 2.0;
                        double angleRad = sprite.Angle * Math.PI / 180.0;

                        var transform =
                            Matrix.CreateTranslation(-cX, -cY)
                            * Matrix.CreateScale(sprite.ZoomX, sprite.ZoomY)
                            * Matrix.CreateRotation(angleRad)
                            * Matrix.CreateTranslation(cX + sprite.X, cY + sprite.Y);

                        using (compCtx.PushPostTransform(transform))
                        using (compCtx.PushOpacity(sprite.Alpha))
                            compCtx.DrawImage(bmp, new Rect(bmp.Size),
                                new Rect(0, 0, sprite.Width, sprite.Height));
                    }
                }

                // Kopiera RenderTargetBitmap → CompositeBitmap
                var pixels = new byte[
                    layer.CompositeBitmap.PixelSize.Width *
                    layer.CompositeBitmap.PixelSize.Height * 4];

                unsafe
                {
                    fixed (byte* p = pixels)
                    {
                        compositeRtb.CopyPixels(
                            new PixelRect(layer.CompositeBitmap.PixelSize),
                            (nint)p,
                            pixels.Length,
                            layer.CompositeBitmap.PixelSize.Width * 4);
        
                        // ✅ APPLICERA OPACITY PÅ VARJE PIXEL (endast om opacity < 1.0)
                        float opacity = (float)layer.Opacity;
                        if (opacity < 0.999f) // Optimera - skippa om nästan helt synlig
                        {
                            int pixelCount = layer.CompositeBitmap.PixelSize.Width * 
                                             layer.CompositeBitmap.PixelSize.Height;
            
                            for (int i = 0; i < pixelCount; i++)
                            {
                                int idx = i * 4;
                                // BGRA format: [B][G][R][A]
                
                                // FADE BÅDE FÄRG OCH ALPHA för mjuk övergång från svart
                                byte b = p[idx + 0];
                                byte g = p[idx + 1];
                                byte r = p[idx + 2];
                                byte a = p[idx + 3];
                
                                // Dämpa RGB proportionellt (fade mot svart)
                                p[idx + 0] = (byte)(b * opacity);
                                p[idx + 1] = (byte)(g * opacity);
                                p[idx + 2] = (byte)(r * opacity);
                                p[idx + 3] = (byte)(a * opacity);
                            }
                        }
                        // Om opacity >= 0.999, gör INGENTING - använd pixlarna som de är
                    }
                }

                using (var fb = layer.CompositeBitmap.Lock())
                    System.Runtime.InteropServices.Marshal.Copy(
                        pixels, 0, fb.Address, pixels.Length);
                
                var screenRect = new Rect(0, 0, Graphics.Width, Graphics.Height);
                var drawOp = new ShaderDrawOperation(screenRect, layer, screenRect);
                ctx.Custom(drawOp); // Opacity redan applicerad på pixlarna
            }
        }

        // Rita framebuffer till skärmen
        ctx.DrawImage(
            _framebuffer!,
            new Rect(_framebuffer.Size),
            new Rect(0, 0, Bounds.Width, Bounds.Height));
    }
}


public sealed class AmosGraphics
{
    public Action<string>? OnError { get; set; }
    
    private readonly List<GpuLayer> _layers = new();
    public List<GpuLayer> ActiveFrame => _layers;
    private List<GpuLayer> DrawingFrame => _layers;
    
    public Dictionary<int, ImageBankEntry> GetImageBank() => _imageBank;

    private readonly System.Diagnostics.Stopwatch _vblTimer = new();
    public double LastCpuUsagePercent { get; private set; } = 0;
    public readonly object LockObject = new(); // Korrekt namn för låset
    private int _currentScreen = 0;
    private readonly System.Diagnostics.Stopwatch _refreshTimer = new();
    public double LastCpuUsage { get; private set; }

    public int CursorX { get; set; } = 0;
    public int CursorY { get; set; } = 0;
    public int TextRows { get; private set; } = 30; // Anpassa efter fontstorlek
    public int TextCols { get; private set; } = 80;
    public int CharWidth { get; private set; } = 8; // T.ex. 8x16 font
    public int CharHeight { get; private set; } = 16;
    public string Style { get; private set; } = "Normal";
    private string font = "Courier New";
    public Color PaperColor { get; set; } = Colors.Transparent; // Bakgrundsfärg för text
    public float Alpha = 255f;

    public float FadeTarget = -1f;
    public float FadeStep = 0f;

    string RasterShaderCode = Shader.RasterShaderCode;
    string RasterShaderCode2 = Shader.RasterShaderCode2;
    string RasterShaderCode3 = Shader.RasterShaderCode3;

    public void ClearFrames()
    {
        ActiveFrame.Clear();
    }

    public ProjectFile ExportProject(string programText)
    {
        return new ProjectFile(
            Version: 2,
            ProgramText: programText ?? "");
    }
    // Sätt font-storlek (anropa i början)
    public void ConfigureText(int w, int h, string text)
    {
        CharWidth = w;
        CharHeight = h;
        TextCols = Width / w;
        TextRows = Height / h;
        font = text;
    }

    public void FontTextStyle(string style)
    {
        Style = style;
    }

    public void Locate(int x, int y)
    {
        CursorX = Math.Clamp(x, 0, TextCols - 1);
        CursorY = Math.Clamp(y, 0, TextRows - 1);
    }

    public void ConsolePrint(string text, bool newLine = true)
    {
        // Dela upp i rader för att hantera \n korrekt
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length > 0)
            {
                // Rita texten grafiskt på nuvarande position
                DrawStringInternal(line);
            }

            // Om det är sista delen och newLine är false, gör ingen radbrytning (semikolon i BASIC)
            if (i < lines.Length - 1 || newLine)
            {
                ConsoleNewLine();
            }
        }
    }

    private void ConsoleNewLine()
    {
        CursorX = 0;
        CursorY++;
        if (CursorY >= TextRows)
        {
            CursorY = TextRows - 1;
            ScrollUp(CharHeight);
        }
    }

    private void DrawStringInternal(string s)
    {
        // Beräkna startposition
        int px = CursorX * CharWidth;
        int py = CursorY * CharHeight;

        Color currentInk = Ink;
        Color currentPaper = PaperColor;
        int currentH = CharHeight;
        int currentW = CharWidth;

        // 1. Rita PAPER (Bakgrundsbox) för hela strängen direkt (snabbare)
        if (currentPaper != Colors.Transparent)
        {
            Bar(px, py, px + s.Length * currentW - 1, py + currentH - 1, currentPaper);
        }

        // 2. Rita TEXT (Ink) tecken för tecken för att garantera rutnätet
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureScreen();

            bool isItalic = Style?.Contains("Italic", StringComparison.OrdinalIgnoreCase) == true;
            bool isBold = Style?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;

            var fontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal;
            var fontWeight = isBold ? FontWeight.Bold : FontWeight.Normal;

            var typeface = new Typeface(font, fontStyle, fontWeight);


            // Skapa en bitmap som rymmer hela texten
            var ps = new PixelSize(s.Length * currentW, currentH);
            if (ps.Width == 0 || ps.Height == 0) return;

            using var rtb = new RenderTargetBitmap(ps);
            using (var ctx = rtb.CreateDrawingContext())
            {
                // Se till att RTB är tömd
                ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ps.Width, ps.Height));

                // Rita varje tecken i sin exakta "box"
                for (int i = 0; i < s.Length; i++)
                {
                    string charStr = s[i].ToString();
                    var ft = new FormattedText(
                        charStr,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        currentH,
                        new SolidColorBrush(currentInk)
                    );

                    // Tvinga positionen: i * CharWidth
                    // Vi använder PushClip för att hindra breda tecken från att blöda in i nästa ruta
                    using (ctx.PushClip(new Rect(i * currentW, 0, currentW, currentH)))
                    {
                        ctx.DrawText(ft, new Point(i * currentW, 0));
                    }
                }
            }

            // Kopiera pixlar till ActiveScreen (med RGBA->BGRA fixen)
            var b = new byte[ps.Width * ps.Height * 4];
            unsafe
            {
                fixed (byte* p = b) rtb.CopyPixels(new PixelRect(ps), (nint)p, b.Length, ps.Width * 4);
            }

            lock (LockObject)
            {
                using (var dst = GetActiveScreen().Lock())
                    unsafe
                    {
                        var dp = (byte*)dst.Address;
                        for (int r = 0; r < Math.Min(ps.Height, currentH); r++)
                        {
                            int ty = py + r;
                            if (ty < 0 || ty >= Height) continue;
                            var dr = dp + ty * dst.RowBytes;

                            for (int c = 0; c < ps.Width; c++)
                            {
                                int tx = px + c;
                                if (tx < 0 || tx >= Width) continue;

                                int si = (r * ps.Width + c) * 4;
                                int di = tx * 4;

                                byte alpha = b[si + 3];
                                if (alpha > 0)
                                {
                                    dr[di + 0] = b[si + 0]; // B -> B (ingen konvertering!)
                                    dr[di + 1] = b[si + 1]; // G -> G
                                    dr[di + 2] = b[si + 2]; // R -> R
                                    dr[di + 3] = 255;       // A
                                }
                            }
                        }
                    }
            }
        }, Avalonia.Threading.DispatcherPriority.Render).Wait(); // Vänta på att ritningen är klar

        // Flytta cursorn framåt
        CursorX += s.Length;
    }

    // En hjälpare för att rita PAPER-boxen (samma som din Bar men tar in färg direkt)
    public void Bar(int x1, int y1, int x2, int y2, Color c)
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
                // Pre-calculate colors
                byte a = c.A;
                byte r = (byte)(c.R * a / 255);
                byte g = (byte)(c.G * a / 255);
                byte b = (byte)(c.B * a / 255);

                for (var y = y1; y <= y2; y++)
                {
                    var row = p + y * fb.RowBytes;
                    for (var x = x1; x <= x2; x++)
                    {
                        var i = x * 4;
                        row[i + 0] = b;
                        row[i + 1] = g;
                        row[i + 2] = r;
                        row[i + 3] = a;
                    }
                }
            }
        }
    }

    public void ScrollUp(int pixels)
    {
        lock (LockObject)
        {
            var bmp = GetActiveScreen();
            using var fb = bmp.Lock();
            unsafe
            {
                byte* ptr = (byte*)fb.Address;
                int rowBytes = fb.RowBytes;
                int totalHeight = bmp.PixelSize.Height;

                // 1. Flytta minnet uppåt
                // Destination: start (rad 0)
                // Källa: rad 'pixels'
                // Antal bytes: (totalHeight - pixels) * rowBytes
                int bytesToMove = (totalHeight - pixels) * rowBytes;

                if (bytesToMove > 0)
                {
                    Buffer.MemoryCopy(
                        ptr + pixels * rowBytes, // Source
                        ptr, // Dest
                        bytesToMove, // DestSize
                        bytesToMove // SourceSize
                    );
                }

                // 2. Rensa botten-remsan med PAPER-färgen (eller svart/transparent)
                Color clearCol = PaperColor; // Eller Colors.Transparent om du vill

                byte a = clearCol.A;
                byte r = (byte)(clearCol.R * a / 255);
                byte g = (byte)(clearCol.G * a / 255);
                byte b = (byte)(clearCol.B * a / 255);

                for (int y = totalHeight - pixels; y < totalHeight; y++)
                {
                    byte* row = ptr + y * rowBytes;
                    for (int x = 0; x < bmp.PixelSize.Width; x++)
                    {
                        int i = x * 4;
                        row[i + 0] = b;
                        row[i + 1] = g;
                        row[i + 2] = r;
                        row[i + 3] = a;
                    }
                }
            }
        }
    }
    
    public List<WriteableBitmap> GetTileBitmaps() => _tiles;

    internal sealed class Font
    {
        public List<WriteableBitmap> CharBitmaps { get; } = new();
        public int CharWidth { get; set; }
        public int CharHeight { get; set; }
        public double Angle { get; set; } = 0;
        public double ZoomX { get; set; } = 1.0;
        public double ZoomY { get; set; } = 1.0;
        public double BaseZoomX { get; set; } = 1.0;
        public double BaseZoomY { get; set; } = 1.0;
        public bool BaseZoomInitialized;
        public string CharMap { get; set; } = "";
        public int Layer { get; set; } = 0;
    }

    // NY: Delad bildbank (som sprites men med stöd för frames)
    public sealed class ImageBankEntry
    {
        public List<WriteableBitmap> Frames { get; } = new();
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
    }

    private readonly Dictionary<int, ImageBankEntry> _imageBank = new();

    public void LoadImageBank(int id, string fileName)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(fileName);
            using var b = new Bitmap(fullPath);
            int w = (int)b.Size.Width, h = (int)b.Size.Height;

            var entry = new ImageBankEntry { FrameWidth = w, FrameHeight = h };
            var bmp = CreateEmptyBitmap(w, h);
            using (var fb = bmp.Lock())
            {
                b.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);
                unsafe
                {
                    FixupImageColors(fb.Address.ToPointer(), w * h);
                }
            }

            entry.Frames.Add(bmp);

            lock (LockObject) _imageBank[id] = entry;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[IMAGE LOAD] {ex.Message}");
        }
    }

    public void LoadImageBankSheet(int id, string fileName, int frameW, int frameH, int count)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(fileName);
            using var b = new Bitmap(fullPath);
            int cols = (int)b.Size.Width / frameW;
            int sheetH = (int)b.Size.Height;

            var entry = new ImageBankEntry { FrameWidth = frameW, FrameHeight = frameH };

            for (int i = 0; i < count; i++)
            {
                int col = i % cols, row = i / cols;
                if ((row + 1) * frameH > sheetH) break;

                var frame = CreateEmptyBitmap(frameW, frameH);
                using (var fb = frame.Lock())
                {
                    b.CopyPixels(new PixelRect(col * frameW, row * frameH, frameW, frameH),
                        fb.Address, fb.RowBytes * frameH, fb.RowBytes);
                    unsafe
                    {
                        FixupImageColors(fb.Address.ToPointer(), frameW * frameH);
                    }
                }

                entry.Frames.Add(frame);
            }

            lock (LockObject) _imageBank[id] = entry;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[IMAGE LOAD SHEET] {ex.Message}");
        }
    }

    private class RasterBarAllocation
    {
        public int BasicBarId { get; set; }
        public int FirstShaderSlot { get; set; }
        public int ShaderSlotCount { get; set; }
        public float StartY { get; set; }
        public float TotalHeight { get; set; }
        public List<Color> Colors { get; set; } = new();
        public bool Wrap { get; set; } = false;
    }

    private readonly Dictionary<int, Dictionary<int, RasterBarAllocation>> _rasterBars = new();
    private readonly Dictionary<int, int> _totalShaderSlotsUsed = new();

    private const int MAX_RASTER_SLOTS = 50;
    private const int WRAP_OFFSET = 25;

    private Dictionary<int, RasterBarAllocation> GetLayerBars(int layerIdx)
    {
        if (!_rasterBars.TryGetValue(layerIdx, out var bars))
        {
            bars = new Dictionary<int, RasterBarAllocation>();
            _rasterBars[layerIdx] = bars;
        }

        return bars;
    }

    private int GetLayerSlotsUsed(int layerIdx)
    {
        return _totalShaderSlotsUsed.TryGetValue(layerIdx, out var n) ? n : 1;
    }

    private void SetLayerSlotsUsed(int layerIdx, int value)
    {
        _totalShaderSlotsUsed[layerIdx] = value;
    }

    public void SetRasterBar(int layerIdx, int basicBarId, float x, float y, float height, string colorString)
    {
        if (basicBarId < 1 || basicBarId > 100)
        {
            OnError?.Invoke($"[RASTER] Bar ID must be between 1-100, got {basicBarId}");
            return;
        }

        var bars = GetLayerBars(layerIdx);
        int slotsUsed = GetLayerSlotsUsed(layerIdx);

        var colorNames = colorString.Split(',')
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        if (colorNames.Count == 0)
        {
            OnError?.Invoke("[RASTER] No colors specified!");
            return;
        }

        if (colorNames.Count > 10)
        {
            OnError?.Invoke("[RASTER] Maximum 10 colors per bar!");
            return;
        }

        var colors = new List<Color>();
        foreach (var name in colorNames)
        {
            try
            {
                colors.Add(Color.Parse(name));
            }
            catch
            {
                OnError?.Invoke($"[RASTER] Unknown color: '{name}'");
                return;
            }
        }

        int neededSlots = Math.Max(1, colors.Count - 1);

        RasterBarAllocation? existing = null;
        if (bars.TryGetValue(basicBarId, out existing))
        {
            slotsUsed -= existing.ShaderSlotCount;
            ClearBarShaderSlots(layerIdx, existing);
        }

        if (slotsUsed + neededSlots > WRAP_OFFSET)
        {
            int available = WRAP_OFFSET - slotsUsed;
            OnError?.Invoke($"[RASTER] Not enough shader slots! Need {neededSlots}, only {available} available. " +
                            $"(Total used: {slotsUsed}/25)");
            if (existing != null)
                SetLayerSlotsUsed(layerIdx, slotsUsed + existing.ShaderSlotCount);
            return;
        }

        int firstSlot = slotsUsed;
        SetLayerSlotsUsed(layerIdx, slotsUsed + neededSlots);

        var allocation = new RasterBarAllocation
        {
            BasicBarId = basicBarId,
            FirstShaderSlot = firstSlot,
            ShaderSlotCount = neededSlots,
            StartY = y,
            TotalHeight = height,
            Colors = colors
        };

        bars[basicBarId] = allocation;
        ApplyRasterBarToShader(layerIdx, allocation);
    }

    public void SetRasterBarWrapped(int layerIdx, int basicBarId, float x, float y,
        float height, string colorString, bool wrap = true)
    {
        if (basicBarId < 1 || basicBarId > 100)
        {
            OnError?.Invoke($"[RASTER] Bar ID must be between 1-100, got {basicBarId}");
            return;
        }

        var bars = GetLayerBars(layerIdx);
        int slotsUsed = GetLayerSlotsUsed(layerIdx);

        var colorNames = colorString.Split(',')
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        if (colorNames.Count == 0)
        {
            OnError?.Invoke("[RASTER] No colors specified!");
            return;
        }

        if (colorNames.Count > 10)
        {
            OnError?.Invoke("[RASTER] Maximum 10 colors per bar!");
            return;
        }

        var colors = new List<Color>();
        foreach (var name in colorNames)
        {
            try
            {
                colors.Add(Color.Parse(name));
            }
            catch
            {
                OnError?.Invoke($"[RASTER] Unknown color: '{name}'");
                return;
            }
        }

        int neededSlots = Math.Max(1, colors.Count - 1);

        RasterBarAllocation? existing = null;
        if (bars.TryGetValue(basicBarId, out existing))
        {
            slotsUsed -= existing.ShaderSlotCount;
            ClearBarShaderSlots(layerIdx, existing);
        }

        if (slotsUsed + neededSlots > WRAP_OFFSET)
        {
            int available = WRAP_OFFSET - slotsUsed;
            OnError?.Invoke($"[RASTER] Not enough shader slots! Need {neededSlots}, only {available} available. " +
                            $"(Total used: {slotsUsed}/25)");
            if (existing != null)
                SetLayerSlotsUsed(layerIdx, slotsUsed + existing.ShaderSlotCount);
            return;
        }

        int firstSlot = slotsUsed;
        SetLayerSlotsUsed(layerIdx, slotsUsed + neededSlots);

        var allocation = new RasterBarAllocation
        {
            BasicBarId = basicBarId,
            FirstShaderSlot = firstSlot,
            ShaderSlotCount = neededSlots,
            StartY = y,
            TotalHeight = height,
            Colors = colors,
            Wrap = wrap
        };

        bars[basicBarId] = allocation;
        ApplyRasterBarToShader(layerIdx, allocation);

        if (wrap)
        {
            if (y < 0)
                SetRasterBar(layerIdx, basicBarId + 1000, x, y + Height, height, colorString);
            else if (y + height > Height)
                SetRasterBar(layerIdx, basicBarId + 1000, x, y - Height, height, colorString);
        }
    }

    public void SetRasterWrap(int layerIdx, int basicBarId, bool enabled)
    {
        var bars = GetLayerBars(layerIdx);
        if (!bars.TryGetValue(basicBarId, out var bar))
        {
            OnError?.Invoke($"[RASTER WRAP] Bar {basicBarId} does not exist!");
            return;
        }

        bar.Wrap = enabled;
        ApplyRasterBarToShader(layerIdx, bar);
    }

    private void ApplyRasterBarToShader(int layerIdx, RasterBarAllocation bar)
    {
        if (bar.Colors.Count == 1)
        {
            SetShaderParams(layerIdx, bar.FirstShaderSlot, bar.StartY, bar.TotalHeight);
            SetShaderColors(layerIdx, bar.FirstShaderSlot, bar.Colors[0], bar.Colors[0]);
        }
        else
        {
            int segments = bar.Colors.Count - 1;
            float segmentHeight = bar.TotalHeight / segments;
            for (int i = 0; i < segments; i++)
            {
                int slot = bar.FirstShaderSlot + i;
                float segY = bar.StartY + (i * segmentHeight);
                SetShaderParams(layerIdx, slot, segY, segmentHeight);
                SetShaderColors(layerIdx, slot, bar.Colors[i], bar.Colors[i + 1]);
            }
        }
    }

    public void MoveRasterBar(int layerIdx, int basicBarId, float newY)
    {
        var bars = GetLayerBars(layerIdx);
        if (!bars.TryGetValue(basicBarId, out var bar))
        {
            OnError?.Invoke($"[RASTER] Bar {basicBarId} does not exist!");
            return;
        }

        bar.StartY = newY;
        ApplyRasterBarToShader(layerIdx, bar);

        if (bar.Wrap)
        {
            float wrappedY = newY > 0 ? newY - Height : newY + Height;
            if (bar.Colors.Count == 1)
            {
                int wrapSlot = bar.FirstShaderSlot + WRAP_OFFSET;
                SetShaderParams(layerIdx, wrapSlot, wrappedY, bar.TotalHeight);
                SetShaderColors(layerIdx, wrapSlot, bar.Colors[0], bar.Colors[0]);
            }
            else
            {
                int segments = bar.Colors.Count - 1;
                float segmentHeight = bar.TotalHeight / segments;
                for (int i = 0; i < segments; i++)
                {
                    int wrapSlot = bar.FirstShaderSlot + i + WRAP_OFFSET;
                    float segY = wrappedY + (i * segmentHeight);
                    SetShaderParams(layerIdx, wrapSlot, segY, segmentHeight);
                    SetShaderColors(layerIdx, wrapSlot, bar.Colors[i], bar.Colors[i + 1]);
                }
            }
        }
        else
        {
            lock (LockObject)
            {
                int wrapStart = bar.FirstShaderSlot + WRAP_OFFSET;
                int wrapEnd = wrapStart + bar.ShaderSlotCount;
                for (int slot = wrapStart; slot < wrapEnd; slot++)
                {

                        if (layerIdx >= 0 && layerIdx < DrawingFrame.Count)
                            DrawingFrame[layerIdx].ShaderHeights[slot] = 0;
          
                }
            }
        }
    }

    public void SetRasterGfxMode(int layerIdx, bool onGraphics)
    {
        lock (LockObject)
        {
            float modeValue = onGraphics ? 1f : 0f;

                var frame = DrawingFrame;
                if (layerIdx >= 0 && layerIdx < frame.Count)
                {
                    var v = frame[layerIdx].ShaderValues[0];
                    frame[layerIdx].ShaderValues[0] = new Vector4(v.X, v.Y, modeValue, v.W);
                }
        }
    }

    public void SetRasterSpaceMode(int layerIdx, int onGraphics)
    {
        lock (LockObject)
        {
           // float modeValue = onGraphics ? 1f : 0f;

           string RasterCode = "";
           
           if (onGraphics == 0) RasterCode = RasterShaderCode;
           if (onGraphics == 1) RasterCode = RasterShaderCode2;          
           if (onGraphics == 2) RasterCode = RasterShaderCode3;
            var frame = DrawingFrame;
            if (layerIdx >= 0 && layerIdx < frame.Count)
            {
                frame[layerIdx].SkSlCode = RasterCode;
            }
        }
    }    
    
    
    public void DeleteRasterBar(int layerIdx, int basicBarId)
    {
        var bars = GetLayerBars(layerIdx);
        if (!bars.TryGetValue(basicBarId, out var bar)) return;

        ClearBarShaderSlots(layerIdx, bar);
        bars.Remove(basicBarId);
        SetLayerSlotsUsed(layerIdx, GetLayerSlotsUsed(layerIdx) - bar.ShaderSlotCount);
    }

    public void ClearAllRasterBars(int layerIdx = -1)
    {
        if (layerIdx == -1)
        {
            foreach (var kvp in _rasterBars)
            foreach (var bar in kvp.Value.Values)
                ClearBarShaderSlots(kvp.Key, bar);
            _rasterBars.Clear();
            _totalShaderSlotsUsed.Clear();
        }
        else
        {
            if (_rasterBars.TryGetValue(layerIdx, out var bars))
            {
                foreach (var bar in bars.Values)
                    ClearBarShaderSlots(layerIdx, bar);
                bars.Clear();
            }

            _totalShaderSlotsUsed.Remove(layerIdx);
        }
    }

    private void ClearBarShaderSlots(int layerIdx, RasterBarAllocation bar)
    {
        lock (LockObject)
        {
            for (int i = 0; i < bar.ShaderSlotCount; i++)
            {
                int slot = bar.FirstShaderSlot + i;

                    if (layerIdx >= 0 && layerIdx < DrawingFrame.Count)
                        DrawingFrame[layerIdx].ShaderHeights[slot] = 0;
                
            }
        }
    }

    public string GetRasterBarDebugInfo()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kvp in _rasterBars)
        {
            int idx = kvp.Key;
            var bars = kvp.Value;
            int slotsUsed = GetLayerSlotsUsed(idx);
            sb.AppendLine($"Layer {idx}: Slots Used: {slotsUsed}/25, Bars: {bars.Count}");
            foreach (var bar in bars.Values.OrderBy(b => b.BasicBarId))
            {
                sb.AppendLine($"  Bar {bar.BasicBarId}: Slots {bar.FirstShaderSlot}-" +
                              $"{bar.FirstShaderSlot + bar.ShaderSlotCount - 1} " +
                              $"({bar.Colors.Count} colors)");
            }
        }

        return sb.ToString();
    }

    internal Font? GetFont(int id) => _fonts.GetValueOrDefault(id);
    public int Layer { get; set; } = 0; 

    internal WriteableBitmap? GetFontChar(Font f, char c)
    {
        string map = string.IsNullOrEmpty(f.CharMap) ? "" : f.CharMap;
        int charIdx = !string.IsNullOrEmpty(map) ? map.IndexOf(char.ToUpper(c)) : c - 32;
        return (charIdx >= 0 && charIdx < f.CharBitmaps.Count) ? f.CharBitmaps[charIdx] : null;
    }


    public sealed class Sprite
    {
        public int ImageBankId { get; set; } = -1; // -1 = använder egna Frames
        public HashSet<string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Sprite(int width, int height, WriteableBitmap firstFrame)
        {
            Width = width;
            Height = height;
            Frames.Add(firstFrame);
            Ink = Colors.White;
            TransparentKey = Colors.Magenta;
            Visible = false;
        }

        public WriteableBitmap GetBitmap(Dictionary<int, ImageBankEntry> imageBank)
        {
            if (ImageBankId >= 0 && imageBank.TryGetValue(ImageBankId, out var entry))
            {
                int fi = Math.Clamp(CurrentFrame, 0, entry.Frames.Count - 1);
                return entry.Frames[fi];
            }

            return Frames[Math.Clamp(CurrentFrame, 0, Frames.Count - 1)];
        }

        public int Width { get; set; }
        public int Height { get; set; }
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
        public int Layer { get; set; } = 0; 

        // --- Alpha & Fade ---
        public float Alpha { get; set; } = 1.0f; // 0.0–1.0 (1.0 = helt ogenomskinlig)
        public float FadeTarget { get; set; } = -1f; // -1 = ingen aktiv fade
        public float FadeStep { get; set; } = 0f; // delta per frame
    }

    public void SpriteImage(int spriteId, int imageBankId, int frameId = 0)
    {
        var s = GetSprite(spriteId);
        s.ImageBankId = imageBankId;
        s.CurrentFrame = frameId;

        // Hämta bitmap från imagebanken
        var bmp = s.GetBitmap(GetImageBank());
        if (bmp != null)
        {
            // Uppdatera bredd/höjd om de inte redan satts manuellt
            s.Width = (int)bmp.Size.Width;
            s.Height = (int)bmp.Size.Height;
        }
        
    }

    private readonly Dictionary<int, Sprite> _sprites = new();
    private readonly Dictionary<string, HashSet<int>> _groups = new(StringComparer.OrdinalIgnoreCase);
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
        public int Layer { get; set; } = 0;
    }

    public readonly List<QueuedFontText> _fontTexts = new();

    public IEnumerable<QueuedFontText> GetQueuedTexts()
    {
        lock (LockObject)
        {
            return _fontTexts.ToList(); // ✅ Returnera kopia
        }
    }

    
    // ✅ NYTT: Metoder för att rensa allt
    public void ClearAllResources()
    {
        lock (LockObject)
        {
            _sprites.Clear();
            _groups.Clear();
            _imageBank.Clear();
            _tiles.Clear();
            _tilesInWidth = 0;
            _tileWidth = 32;
            _tileHeight = 32;
            _map = new int[0, 0];
            _fonts.Clear();
            _fontTexts.Clear();
            ClearAllRasterBars(0);
        }
    }

    public void ClearAllSprites()
    {
        lock (LockObject)
        {
            _sprites.Clear();
            _groups.Clear();
        }
    }

    public void ClearAllImageBank()
    {
        lock (LockObject)
        {
            _imageBank.Clear();
        }
    }

    public void ClearAllTiles()
    {
        lock (LockObject)
        {
            // ✅ Rensa tiles först så DrawMap kan upptäcka tom lista
            _tiles.Clear();
            _tilesInWidth = 0;
            _tileWidth = 32;
            _tileHeight = 32;
        }
    }

    public void ClearAllMaps()
    {
        lock (LockObject)
        {
            // ✅ Töm map-arrayen säkert
            _map = new int[0, 0];
        }
    }

    public void ClearAllFonts()
    {
        lock (LockObject)
        {
            _fonts.Clear();
            _fontTexts.Clear();
        }
    }

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
        int index = (_currentScreen >= 0 && _currentScreen < DrawingFrame.Count)
            ? _currentScreen
            : 0;

        // Info.txt: "Alla ritaroperationer sker alltid på den inaktiva framen"
        return DrawingFrame[index].Bitmap;
    }

    public int GetActiveScreenNumber()
    {
        return _currentScreen;
    }

    public void SetScreenVisible(int layerIdx, bool visible)
    {
        lock (LockObject)
        {
            // Vi sätter flaggan på både A och B så att den består även efter SwapBuffers
            if (layerIdx >= 0 && layerIdx < _layers.Count) _layers[layerIdx].Visible = visible;
        }
    }

    public void SetShadervalues(int layerIdx, int slot, float nr, float value)
    {
        lock (LockObject)
        {

                // Single buffer: Använd DrawingFrame som vanligt
                var frame = DrawingFrame;
                if (layerIdx >= 0 && layerIdx < frame.Count)
                {
                    var layer = frame[layerIdx];
                    if (slot >= 0 && slot < 2)
                        layer.ShaderValues[slot] = new Vector4(nr, value, 0f, 0f);
                }
            
        }
    }



    public void SetShaderParams(int layerIdx, int slot, float y, float height)
    {
        lock (LockObject)
        {
                var frame = DrawingFrame;
                if (layerIdx >= 0 && layerIdx < frame.Count)
                {
                    var layer = frame[layerIdx];

                    // ✅ SÄKERHETSKOLL: Uppgradera arrayer om de är för små
                    EnsureShaderArraySize(layer);

                    if (slot >= 0 && slot < layer.ShaderParams.Length)
                    {
                        layer.ShaderParams[slot] = y;
                        layer.ShaderHeights[slot] = height;
                    }
                }
            
        }
    }

    public void SetShaderColors(int layerIdx, int slot, Color c1, Color c2)
    {
        lock (LockObject)
        {

                var frame = DrawingFrame;
                if (layerIdx >= 0 && layerIdx < frame.Count)
                {
                    var layer = frame[layerIdx];

                    // ✅ SÄKERHETSKOLL: Uppgradera arrayer om de är för små
                    EnsureShaderArraySize(layer);

                    if (slot >= 0 && slot < layer.ShaderColors.Length)
                    {
                        layer.ShaderColors[slot] = new SKColor(c1.R, c1.G, c1.B, 255);
                        layer.ShaderColorsTo[slot] = new SKColor(c2.R, c2.G, c2.B, 255);

                        if (slot == 0 && layer.ShaderHeights[0] <= 0)
                            layer.ShaderHeights[0] = (float)Height;
                    }
                }
            
        }
    }

    /// <summary>
    /// ✅ HJÄLPMETOD: Uppgraderar gamla 22-slots arrayer till 50 slots
    /// </summary>
    private void EnsureShaderArraySize(GpuLayer layer)
    {
        const int REQUIRED_SIZE = 50;

        if (layer.ShaderParams == null || layer.ShaderParams.Length < REQUIRED_SIZE)
        {
            var oldParams = layer.ShaderParams ?? new float[0];
            layer.ShaderParams = new float[REQUIRED_SIZE];
            Array.Copy(oldParams, layer.ShaderParams, Math.Min(oldParams.Length, REQUIRED_SIZE));
        }

        if (layer.ShaderHeights == null || layer.ShaderHeights.Length < REQUIRED_SIZE)
        {
            var oldHeights = layer.ShaderHeights ?? new float[0];
            layer.ShaderHeights = new float[REQUIRED_SIZE];
            Array.Copy(oldHeights, layer.ShaderHeights, Math.Min(oldHeights.Length, REQUIRED_SIZE));
        }

        if (layer.ShaderColors == null || layer.ShaderColors.Length < REQUIRED_SIZE)
        {
            var oldColors = layer.ShaderColors ?? new SKColor[0];
            layer.ShaderColors = new SKColor[REQUIRED_SIZE];
            Array.Copy(oldColors, layer.ShaderColors, Math.Min(oldColors.Length, REQUIRED_SIZE));
        }

        if (layer.ShaderColorsTo == null || layer.ShaderColorsTo.Length < REQUIRED_SIZE)
        {
            var oldColorsTo = layer.ShaderColorsTo ?? new SKColor[0];
            layer.ShaderColorsTo = new SKColor[REQUIRED_SIZE];
            Array.Copy(oldColorsTo, layer.ShaderColorsTo, Math.Min(oldColorsTo.Length, REQUIRED_SIZE));
        }
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
    

    // ---------------- Screen & Core ----------------

    public void Screen(int w, int h)
    {
        lock (LockObject)
        {
            ClearAll(Colors.Transparent);
            Width = w;
            Height = h;
            _layers.Clear();

            var lA = new GpuLayer
                { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0)};
            var lB = new GpuLayer
                { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0)};

            for (int i = 0; i < 22; i++)
            {
                lA.ShaderHeights[i] = 0;
                lB.ShaderHeights[i] = 0;
            }

            // Tvinga fram korrekt storlek på alla arrayer
            lA.ShaderParams = new float[50];
            lA.ShaderHeights = new float[50];
            lA.ShaderColors = new SKColor[50];
            lA.ShaderColorsTo = new SKColor[50];
            lB.ShaderParams = new float[50];
            lB.ShaderHeights = new float[50];
            lB.ShaderColors = new SKColor[50];
            lB.ShaderColorsTo = new SKColor[50];

            _layers.Add(lA);
            _currentScreen = 0;
        }

        // Tvinga UI-tråden att uppdatera storleken på vyn
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Detta tvingar Viewbox att räkna om skalningen
            // Vi gör det via ett anrop till InvalidateMeasure i MainWindow
            OnScreenResolutionChanged?.Invoke(w, h);
        }, Avalonia.Threading.DispatcherPriority.Render);
    }
    
    public event Action<int, int>? OnScreenResolutionChanged;
    
    public void SetDrawingScreen(int id)
    {
        lock (LockObject)
        {
            // Vi måste se till att lagret finns i BÅDA listorna
            while (ActiveFrame.Count <= id)
            {
                var layer = new GpuLayer
                {
                    Bitmap = CreateEmptyBitmap(Width > 0 ? Width : 640, Height > 0 ? Height : 480),
                    Offset = new Point(0, 0), Opacity = 1.0
                };
                // Initiera ShaderHeights till 0 så att lagret är transparent som standard
                for (int i = 0; i < 22; i++) layer.ShaderHeights[i] = 0;
                ActiveFrame.Add(layer);
            }

            _currentScreen = id;
        }
    }

    private void EnsureScreen()
    {
        // Vi kollar om listorna är tomma istället för om de är null
        if (_layers.Count == 0 || _layers.Count == 0)
        {
            lock (LockObject)
            {
                // Om de är tomma, initiera standardstorlek (t.ex. 640x480)
                _layers.Clear();

                _layers.Add(new GpuLayer
                {
                    Bitmap = CreateEmptyBitmap(640, 480),
                    Offset = new Point(0, 0)
                });
            }
        }
    }

    private WriteableBitmap CreateEmptyBitmap(int w, int h, Color? background = null, GpuLayer? targetLayer = null)
    {
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
    }

    public void ClearAll(Color color)
    {
        EnsureScreen();
        // Rensa alla lager
        foreach (var lay in ActiveFrame)
        {
            ClearBitmap(lay.Bitmap, color);
        }

        foreach (var lay in ActiveFrame)
        {
            ClearBitmap(lay.Bitmap, color);
        }

        lock (LockObject)
        {
            // 2. Nollställ shader-parametrarna för lagret vi just rensade
            // Vi gör detta för BÅDE Active och Inactive frame så att ingen gammal data hänger kvar
            foreach (var frame in new[] { ActiveFrame, ActiveFrame })
            {
                foreach (var layer in ActiveFrame)
                {
                    // Nollställ höjderna så att inga rasters/rainbows ritas
                    for (int i = 0; i < 22; i++) layer.ShaderHeights[i] = 0;
                    // Nollställ väder/scroll-parametrar
                    for (int i = 0; i < layer.ShaderValues.Length; i++) layer.ShaderValues[i] = Vector4.Zero;
                }
            }

            // 3. Om det är huvudskärmen, rensa även systemlistorna
            // if (_currentScreen == 0) 
            //{
            _fontTexts.Clear();
            foreach (var s in _sprites.Values) s.Visible = false;
            //}
        }
    }

    internal void ClearBitmap(WriteableBitmap bmp, Color c)
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
    
    public void Scroll(int sid, float x, float y)
    {
        lock (LockObject)
        {
            
                if (sid >= 0 && sid < DrawingFrame.Count)
                {
                    var v = DrawingFrame[sid].ShaderValues[0];
                    DrawingFrame[sid].ShaderValues[0] = new Vector4(x, y, v.Z, v.W);
                }
            
        }
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
            unsafe
            {
                uint* p = (uint*)fb.Address;
                // BGRA format: [B][G][R][A] i minnet
                uint val = (uint)((c.A << 24) | (c.B << 16) | (c.G << 8) | c.R);
            
                if ((val & 0xFF00FFFF) == 0)  // Kontrollera om BGR = 0
                    p[y * (fb.RowBytes / 4) + x] = 0;
                else 
                    p[y * (fb.RowBytes / 4) + x] = val;
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
                byte a = Ink.A;
                byte rb = (byte)(Ink.R * a / 255);
                byte gb = (byte)(Ink.G * a / 255);
                byte bb = (byte)(Ink.B * a / 255);

                for (var y = y1; y <= y2; y++)
                {
                    var row = p + y * fb.RowBytes;
                    for (var x = x1; x <= x2; x++)
                    {
                        var i = x * 4;
                        row[i + 0] = rb;
                        row[i + 1] = gb;
                        row[i + 2] = bb;
                        row[i + 3] = a; 
                    }
                }
            }
        }
    }

    public void Circle(int x1, int y1, int r)
    {
        lock (LockObject)
        {
            EnsureScreen();
            var bmp = GetActiveScreen();
            using var fb = bmp.Lock();

            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;

            // Konvertera Ink till uint (BGRA)
            uint cVal = (uint)((Ink.A << 24) | (Ink.B << 16) | (Ink.G << 8) | Ink.R);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                // Lokal funktion för säker pixel-sättning
                void SetPixel(int px, int py)
                {
                    if (px >= 0 && px < w && py >= 0 && py < h)
                        ptr[py * stride + px] = cVal;
                }

                int x = 0, y = r;
                int d = 3 - 2 * r;

                while (y >= x)
                {
                    SetPixel(x1 + x, y1 + y);
                    SetPixel(x1 - x, y1 + y);
                    SetPixel(x1 + x, y1 - y);
                    SetPixel(x1 - x, y1 - y);
                    SetPixel(x1 + y, y1 + x);
                    SetPixel(x1 - y, y1 + x);
                    SetPixel(x1 + y, y1 - x);
                    SetPixel(x1 - y, y1 - x);

                    x++;
                    if (d > 0)
                    {
                        y--;
                        d = d + 4 * (x - y) + 10;
                    }
                    else
                    {
                        d = d + 4 * x + 6;
                    }
                }
            }
        }
    }

    public void Ellipse(int x1, int y1, int r1, int r2)
    {
        lock (LockObject)
        {
            EnsureScreen();
            var bmp = GetActiveScreen();
            using var fb = bmp.Lock();

            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            uint cVal = (uint)((Ink.A << 24) | (Ink.B << 16) | (Ink.G << 8) | Ink.R);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                void SetPixel(int px, int py)
                {
                    if (px >= 0 && px < w && py >= 0 && py < h)
                        ptr[py * stride + px] = cVal;
                }

                int x = 0, y = r2;
                long rx = r1, ry = r2; // Använd long för att undvika overflow vid kvadrat
                long rxSq = rx * rx;
                long rySq = ry * ry;
                long twoRxSq = 2 * rxSq;
                long twoRySq = 2 * rySq;
                long p;
                long px = 0;
                long py = twoRxSq * y;

                // Region 1
                p = (long)Math.Round(rySq - (rxSq * ry) + (0.25 * rxSq));
                while (px < py)
                {
                    SetPixel(x1 + x, y1 + y);
                    SetPixel(x1 - x, y1 + y);
                    SetPixel(x1 + x, y1 - y);
                    SetPixel(x1 - x, y1 - y);
                    x++;
                    px += twoRySq;
                    if (p < 0)
                    {
                        p += rySq + px;
                    }
                    else
                    {
                        y--;
                        py -= twoRxSq;
                        p += rySq + px - py;
                    }
                }

                // Region 2
                p = (long)Math.Round(rySq * (x + 0.5) * (x + 0.5) + rxSq * (y - 1) * (y - 1) - rxSq * rySq);
                while (y >= 0)
                {
                    SetPixel(x1 + x, y1 + y);
                    SetPixel(x1 - x, y1 + y);
                    SetPixel(x1 + x, y1 - y);
                    SetPixel(x1 - x, y1 - y);
                    y--;
                    py -= twoRxSq;
                    if (p > 0)
                    {
                        p += rxSq - py;
                    }
                    else
                    {
                        x++;
                        px += twoRySq;
                        p += rxSq - py + px;
                    }
                }
            }
        }
    }

    public void CircleF(int x1, int y1, int r1, int r2)
    {
        lock (LockObject)
        {
            EnsureScreen();
            var bmp = GetActiveScreen();
            using var fb = bmp.Lock();

            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            uint cVal = (uint)((Ink.A << 24) | (Ink.B << 16) | (Ink.G << 8) | Ink.R);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                // Iterera genom höjden (Y-axeln)
                for (int y = -r2; y <= r2; y++)
                {
                    // Beräkna bredden vid denna Y-koordinat baserat på ellips-ekvationen
                    // x = r1 * sqrt(1 - y^2/r2^2)
                    int halfWidth = (int)(r1 * Math.Sqrt(1.0 - (double)(y * y) / (r2 * r2)));

                    int drawY = y1 + y;
                    if (drawY < 0 || drawY >= h) continue;

                    int startX = x1 - halfWidth;
                    int endX = x1 + halfWidth;

                    // Clamp X
                    if (startX < 0) startX = 0;
                    if (endX >= w) endX = w - 1;

                    if (startX <= endX)
                    {
                        uint* row = ptr + drawY * stride;
                        for (int xx = startX; xx <= endX; xx++)
                        {
                            row[xx] = cVal;
                        }
                    }
                }
            }
        }
    }

    public void Fill(int x1, int y1)
    {
        lock (LockObject)
        {
            EnsureScreen();
            var bmp = GetActiveScreen();
            using var fb = bmp.Lock();
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;

            if (x1 < 0 || x1 >= w || y1 < 0 || y1 >= h) return;

            // FIX: Skapa färg i BGRA-format
            uint fillColor = (uint)((Ink.A << 24) | (Ink.B << 16) | (Ink.G << 8) | Ink.R);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                // Nu är både targetColor och fillColor i samma format (BGRA)
                uint targetColor = ptr[y1 * stride + x1];

                if (targetColor == fillColor) return;

                // Scanline Flood Fill Algorithm (Stack-baserad)
                Stack<(int x, int y)> stack = new();
                stack.Push((x1, y1));

                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    int offset = cy * stride + cx;

                    // Flytta vänster så långt det går
                    int lx = cx;
                    while (lx >= 0 && ptr[cy * stride + lx] == targetColor)
                    {
                        lx--;
                    }

                    lx++; // Gå tillbaka ett steg till sista giltiga pixel

                    // Flytta höger och fyll, samt scanna raderna ovanför och under
                    bool spanAbove = false;
                    bool spanBelow = false;

                    int rx = lx;
                    while (rx < w && ptr[cy * stride + rx] == targetColor)
                    {
                        // Fyll pixel
                        ptr[cy * stride + rx] = fillColor;

                        // Kolla raden ovanför
                        if (cy > 0)
                        {
                            uint colorAbove = ptr[(cy - 1) * stride + rx];
                            if (!spanAbove && colorAbove == targetColor)
                            {
                                stack.Push((rx, cy - 1));
                                spanAbove = true;
                            }
                            else if (spanAbove && colorAbove != targetColor)
                            {
                                spanAbove = false;
                            }
                        }

                        // Kolla raden under
                        if (cy < h - 1)
                        {
                            uint colorBelow = ptr[(cy + 1) * stride + rx];
                            if (!spanBelow && colorBelow == targetColor)
                            {
                                stack.Push((rx, cy + 1));
                                spanBelow = true;
                            }
                            else if (spanBelow && colorBelow != targetColor)
                            {
                                spanBelow = false;
                            }
                        }

                        rx++;
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
        });
    }

    public void FontLoad(int id, string file, int tw, int th)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(file);

            using var b = new Bitmap(fullPath);
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
        catch (Exception ex)
        {
            OnError?.Invoke($"[FONT LOAD] Error loading '{file}': {ex.Message}");
        }
    }

    public void FontRotate(int id, double angle)
    {
        if (_fonts.TryGetValue(id, out var f)) f.Angle = angle;
    }

    public void FontZoom(int id, double zx, double zy)
    {
        if (!_fonts.TryGetValue(id, out var f))
            return;

        if (!f.BaseZoomInitialized)
        {
            f.BaseZoomX = zx;
            f.BaseZoomY = zy;
            f.BaseZoomInitialized = true;
        }

        f.ZoomX = zx;
        f.ZoomY = zy;
    }

    public void FontMap(int id, string map)
    {
        if (_fonts.TryGetValue(id, out var f)) f.CharMap = map;
    }

    public void FontPrint(int id, int x, int y, string text)
    {
        if (!_fonts.TryGetValue(id, out var f)) return;

        double totalUnscaledW = text.Length * f.CharWidth;
        double totalUnscaledH = f.CharHeight;
        double centerX = totalUnscaledW / 2.0;
        double centerY = totalUnscaledH / 2.0;
        double compensateX = centerX * (f.BaseZoomX - 1.0);
        double compensateY = centerY * (f.BaseZoomY - 1.0);

        x = x + (int)compensateX;
        y = y + (int)compensateY;

        lock (LockObject)
        {
            _fontTexts.Add(new QueuedFontText
            {
                FontId = id,
                X = x,
                Y = y,
                Text = text,
                Angle = f.Angle,
                ZoomX = f.ZoomX,
                ZoomY = f.ZoomY,
                Layer = _currentScreen // ← ärv aktivt lager automatiskt
            });
        }
    }


    public void FontClear()
    {
        lock (LockObject)
        {
            _fontTexts.Clear();
        }
    }

    public void FontChar(int id, int x, int y, string c)
    {
        if (!_fonts.TryGetValue(id, out var f) || string.IsNullOrEmpty(c)) return;

        lock (LockObject)
        {
            _fontTexts.Add(new QueuedFontText
            {
                FontId = id,
                X = x,
                Y = y,
                Text = c[0].ToString(), // Enskilt tecken som sträng
                Angle = f.Angle,
                ZoomX = f.ZoomX,
                ZoomY = f.ZoomY,
                Layer = _currentScreen
            });
        }
    }

    // -------------------------------------------------------------------------------
    //  HJÄLPMETOD FÖR FÄRGKORRIGERING (MAC vs PC)
    // -------------------------------------------------------------------------------
    private unsafe void FixupImageColors(void* address, int pixelCount)
    {
        uint* p = (uint*)address;
    
        for (int i = 0; i < pixelCount; i++)
        {
            uint pixel = p[i];
        
            // Plocka ut färgkanaler (BGRA format i WriteableBitmap)
            uint b = (pixel >> 16) & 0xFF;
            uint g = (pixel >> 8) & 0xFF;
            uint r = pixel & 0xFF;
        
            // Gör svart (0,0,0) transparent
            if (r == 0 && g == 0 && b == 0)
            {
                p[i] = 0;
            }
        }
    }

    public void LoadBackground(string f)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(f);

            using var b = new Bitmap(fullPath);
            lock (LockObject)
            {
                EnsureScreen();
                var layer = DrawingFrame[_currentScreen];
                using (var fb = layer.Bitmap.Lock())
                {
                    b.CopyPixels(new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb.Address,
                        fb.RowBytes * layer.Bitmap.PixelSize.Height, fb.RowBytes);
                }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[LOADBACKGROUND]: {ex.Message}");
        }
    }
    
    public void SpriteLayer(int id, int layerIdx)
    {
        var s = GetSprite(id);
        s.Layer = layerIdx;
    }

    // ---------------- Tiles ----------------
    public int GetTilesInWidth() => _tilesInWidth; // NYTT: Getter

    public void LoadTileBank(string f, int tw, int th)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(f);

            using var b = new Bitmap(fullPath);
            _tileWidth = tw;
            _tileHeight = th;
            _tiles.Clear();

            // Uppdatera klassvariabeln för att undvika division med noll i paletten
            _tilesInWidth = (int)b.Size.Width / tw;

            int cs = _tilesInWidth;
            int rs = (int)b.Size.Height / th;

            for (int y = 0; y < rs; y++)
            {
                for (int x = 0; x < cs; x++)
                {
                    var t = CreateEmptyBitmap(tw, th);
                    using (var fb = t.Lock())
                    {
                        b.CopyPixels(new PixelRect(x * tw, y * th, tw, th), fb.Address, fb.RowBytes * th, fb.RowBytes);
                        unsafe
                        {
                            FixupImageColors(fb.Address.ToPointer(), tw * th);
                        }
                    }

                    _tiles.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[TILE LOAD] Error loading '{f}': {ex.Message}");
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
                            FixupImageColors(fb.Address.ToPointer(), tw * th);
                        }
                    }

                    _tiles.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[TILE STREAM] Error: {ex.Message}");
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
        if (_currentScreen < ActiveFrame.Count)
        {
            var layerI = ActiveFrame[_currentScreen];
            ActiveFrame[_currentScreen] = new GpuLayer
            {
                Bitmap = CreateEmptyBitmap(pixelW, pixelH),
                Offset = layerI.Offset,
                Opacity = layerI.Opacity,
                Visible = layerI.Visible,
                Timer = layerI.Timer,
                SkSlCode = layerI.SkSlCode,
                CachedEffect = null, // Tvingas kompileras om för nya dimensioner
                ShaderParams = (float[])layerI.ShaderParams.Clone(),
                ShaderHeights = (float[])layerI.ShaderHeights.Clone(),
                ShaderColors = (SKColor[])layerI.ShaderColors.Clone(),
                ShaderColorsTo = (SKColor[])layerI.ShaderColorsTo.Clone(),
                ShaderValues = (Vector4[])layerI.ShaderValues.Clone()
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
        lock (LockObject) // ✅ LÄGG TILL DETTA
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
    }

    private void DrawTileToBackbuffer(WriteableBitmap t, int dx, int dy)
    {
        if (t == null) return; // ✅ LÄGG TILL NULL-CHECK

        var target = GetActiveScreen();
        if (target == null) return; // ✅ EXTRA SÄKERHET

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

                    // Kolla om käll-pixeln (tilen) faktiskt har någon alfa (inte är helt transparent)
                    if (sr[si + 3] > 0)
                    {
                        dr[di + 0] = sr[si + 0];
                        dr[di + 1] = sr[si + 1];
                        dr[di + 2] = sr[si + 2];
                        dr[di + 3] = sr[si + 3]; // Använd källans alfa istället för hårdkodat 255
                    }
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
        var sprite = new Sprite(w, h, f);
        _sprites[id] = sprite;
        SpriteClear(id, Colors.Magenta);
    }

    public bool HasSprite(int id) => _sprites.ContainsKey(id);

    public (int w, int h) GetSpriteSize(int id)
    {
        var s = GetSprite(id);
        return (s.Width, s.Height);
    }

    public WriteableBitmap GetSpriteBitmap(int id) => GetSprite(id).Bitmap;

    public List<int> GetSpriteIds()
    {
        lock (LockObject)
        {
            return _sprites.Keys.OrderBy(id => id).ToList();
        }
    }

    public void LoadSprite(int id, string fileName)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(fileName);
            using var b = new Bitmap(fullPath);
            int w = (int)b.Size.Width, h = (int)b.Size.Height;
            CreateSprite(id, w, h);
            var s = GetSprite(id);
            using (var fb = s.Bitmap.Lock())
            {
                b.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);

                unsafe
                {
                    // Fixa färger först
                    FixupImageColors(fb.Address.ToPointer(), w * h);

                    // Läs sedan av transparent key från den fixade datan (första pixeln)
                    var p = (byte*)fb.Address;
                    // Skia/WriteableBitmap är BGRA (Little Endian uint: B, G, R, A)
                    // Så p[0]=B, p[1]=G, p[2]=R, p[3]=A
                    s.TransparentKey = Color.FromArgb(p[3], p[2], p[1], p[0]);
                }
            }
            //_sprites[id].Layer = layer;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SPRITE LOAD] Error loading '{fileName}': {ex.Message}");
        }
    }

    public void LoadSpriteSheet(int id, string fileName, int frameW, int frameH, int count)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(fileName);

            using var sourceInfo = new Bitmap(fullPath);
            int sheetW = (int)sourceInfo.Size.Width;
            int sheetH = (int)sourceInfo.Size.Height;
            int cols = sheetW / frameW;

            CreateSprite(id, frameW, frameH);
            var s = GetSprite(id);

            s.Frames.Clear();

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;

                int srcX = col * frameW;
                int srcY = row * frameH;

                if (srcY + frameH > sheetH) break;

                var f = CreateEmptyBitmap(frameW, frameH);
                using (var fb = f.Lock())
                {
                    sourceInfo.CopyPixels(new PixelRect(srcX, srcY, frameW, frameH), fb.Address, fb.RowBytes * frameH,
                        fb.RowBytes);

                    unsafe
                    {
                        FixupImageColors(fb.Address.ToPointer(), frameW * frameH);

                        if (i == 0)
                        {
                            var p = (byte*)fb.Address;
                            s.TransparentKey = Color.FromArgb(p[3], p[2], p[1], p[0]);
                        }
                    }
                }

                s.Frames.Add(f);
            }

            s.CurrentFrame = 0;
            //_sprites[id].Layer = layer;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SPRITE SHEET LOAD] Error loading '{fileName}': {ex.Message}");
        }
    }

    // NY metod med smart filnamns-parsing
    public void LoadSpriteSheetAuto(int id, string fileName, int? width = null, int? height = null,
        int? frameCount = null)
    {
        try
        {
            // Parse från filnamnet om parametrar saknas
            string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);

            int w = width ?? ParseSpriteSheetParam(fileNameOnly, "W", 32);
            int h = height ?? ParseSpriteSheetParam(fileNameOnly, "H", 32);
            int count = frameCount ?? ParseSpriteSheetParam(fileNameOnly, "[BF]", 8);

            // Anropa vanliga metoden
            LoadSpriteSheet(id, fileName, w, h, count);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SPRITE SHEET AUTO] Error: {ex.Message}");
        }
    }

    private int ParseSpriteSheetParam(string fileName, string prefix, int defaultValue)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            $"_{prefix}(\\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? int.Parse(match.Groups[1].Value) : defaultValue;
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
                FixupImageColors(fb.Address.ToPointer(), s.Width * s.Height);
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

    /// <summary>Sätter alpha omedelbart (0–255). Avbryter eventuell pågående fade.</summary>
    public void SpriteAlpha(int id, int alpha255)
    {
        var s = GetSprite(id);
        s.Alpha = Math.Clamp(alpha255 / 255f, 0f, 1f);
        s.FadeTarget = -1f;
        s.FadeStep = 0f;
    }

    /// <summary>Startar en automatisk fade mot targetAlpha255 på 'frames' frames.</summary>
    public void StartSpriteFade(int id, int targetAlpha255, int frames)
    {
        var s = GetSprite(id);
        float target = Math.Clamp(targetAlpha255 / 255f, 0f, 1f);

        if (frames <= 0 || Math.Abs(target - s.Alpha) < 0.001f)
        {
            // Omedelbar sättning om 0 frames eller redan på målet
            s.Alpha = target;
            s.FadeTarget = -1f;
            s.FadeStep = 0f;
            if (target <= 0f) s.Visible = false;
            return;
        }

        s.FadeTarget = target;
        s.FadeStep = (target - s.Alpha) / frames;

        // Om vi fadar in från osynlig: aktivera spriten direkt
        if (target > 0f && !s.Visible)
            s.Visible = true;
    }

    /// <summary>Returnerar true om spriten har en pågående fade.</summary>
    public bool IsSpriteAFading(int id) =>
        _sprites.TryGetValue(id, out var s) && s.FadeStep != 0f;

    /// <summary>
    /// Anropas av WAIT VBL en gång per frame.
    /// Stegar alla aktiva sprite-fades framåt och auto-hide vid fade-out till 0.
    /// </summary>
    public void TickSpriteFades()
    {
        lock (LockObject)
        {
            foreach (var kv in _sprites)
            {
                var sp = kv.Value;
                if (sp.FadeStep == 0f) continue;

                sp.Alpha += sp.FadeStep;

                bool reached = sp.FadeStep > 0f
                    ? sp.Alpha >= sp.FadeTarget // fade in
                    : sp.Alpha <= sp.FadeTarget; // fade ut

                if (reached)
                {
                    sp.Alpha = sp.FadeTarget;
                    sp.FadeStep = 0f;
                    sp.FadeTarget = -1f;

                    if (sp.Alpha <= 0f)
                        sp.Visible = false; // auto-hide vid fade-out till 0
                }
            }
        }
    }
    
    
    public void TickLayerFades()
    {
        lock (LockObject)
        {
            foreach (var layer in _layers.Concat(_layers))
            {
                if (layer.FadeStep == 0f) continue;

                layer.Opacity += layer.FadeStep;

                bool reached = layer.FadeStep > 0f
                    ? layer.Opacity >= layer.FadeTarget
                    : layer.Opacity <= layer.FadeTarget;

                if (reached)
                {
                    layer.Opacity = layer.FadeTarget;
                    layer.FadeStep = 0f;
                    layer.FadeTarget = -1f;
                }
            }
        }
    }

    /// <summary>Sätter opacity på ett lager omedelbart (0–255).</summary>
    public void ScreenAlpha(int layerIdx, int alpha255)
    {
        float val = Math.Clamp(alpha255 / 255f, 0f, 1f);
        lock (LockObject)
        {
            if (layerIdx >= 0 && layerIdx < _layers.Count)
            {
                _layers[layerIdx].Opacity = val;
                _layers[layerIdx].FadeStep = 0f;
                _layers[layerIdx].FadeTarget = -1f;
            }
        }
    }

    /// <summary>Startar en fade på ett helt lager.</summary>
    public void StartScreenFade(int layerIdx, int targetAlpha255, int frames)
    {
        float target = Math.Clamp(targetAlpha255 / 255f, 0f, 1f);
        lock (LockObject)
        {
            foreach (var frame in new[] { _layers, _layers })
            {
                if (layerIdx < 0 || layerIdx >= frame.Count) continue;
                var layer = frame[layerIdx];

                if (frames <= 0 || Math.Abs(target - (float)layer.Opacity) < 0.001f)
                {
                    layer.Opacity = target;
                    layer.FadeStep = 0f;
                    layer.FadeTarget = -1f;
                    continue;
                }

                layer.FadeTarget = target;
                layer.FadeStep = (target - (float)layer.Opacity) / frames;
            }
        }
    }

    /// <summary>Returnerar true om lagret har en pågående fade.</summary>
    public bool IsLayerFading(int layerIdx) =>
        layerIdx >= 0 && layerIdx < _layers.Count && _layers[layerIdx].FadeStep != 0f;


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


    public bool SpriteHit(int id1, int id2, int step = 2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) || !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;

        var bmp1 = s1.GetBitmap(_imageBank);
        var bmp2 = s2.GetBitmap(_imageBank);
        if (bmp1 == null || bmp2 == null) return false;

        int x1 = s1.X - s1.HandleX, y1 = s1.Y - s1.HandleY;
        int x2 = s2.X - s2.HandleX, y2 = s2.Y - s2.HandleY;

        if (!(x1 < x2 + s2.Width && x1 + s1.Width > x2 && y1 < y2 + s2.Height && y1 + s1.Height > y2))
            return false;

        int overlapLeft = Math.Max(x1, x2);
        int overlapTop = Math.Max(y1, y2);
        int overlapRight = Math.Min(x1 + s1.Width, x2 + s2.Width);
        int overlapBottom = Math.Min(y1 + s1.Height, y2 + s2.Height);

        using var fb1 = bmp1.Lock();
        using var fb2 = bmp2.Lock();

        unsafe
        {
            byte* p1 = (byte*)fb1.Address;
            byte* p2 = (byte*)fb2.Address;
            var key1 = s1.TransparentKey;
            var key2 = s2.TransparentKey;

            for (int y = overlapTop; y < overlapBottom; y += step)
            {
                for (int x = overlapLeft; x < overlapRight; x += step)
                {
                    int localX1 = x - x1;
                    int localY1 = y - y1;
                    int localX2 = x - x2;
                    int localY2 = y - y2;

                    if (localX1 < 0 || localY1 < 0 || localX1 >= s1.Width || localY1 >= s1.Height) continue;
                    if (localX2 < 0 || localY2 < 0 || localX2 >= s2.Width || localY2 >= s2.Height) continue;

                    int idx1 = (localY1 * fb1.RowBytes) + (localX1 * 4);
                    int idx2 = (localY2 * fb2.RowBytes) + (localX2 * 4);

                    byte* px1 = p1 + idx1;
                    byte* px2 = p2 + idx2;

                    bool solid1 = !(px1[2] == key1.R && px1[1] == key1.G && px1[0] == key1.B);
                    bool solid2 = !(px2[2] == key2.R && px2[1] == key2.G && px2[0] == key2.B);

                    if (solid1 && solid2) return true;
                }
            }
        }

        return false;
    }

    public void SpriteAddGroup(int id, string group)
    {
        if (!_sprites.TryGetValue(id, out var s)) return;
        s.Groups.Add(group);
        if (!_groups.TryGetValue(group, out var set))
        {
            set = new HashSet<int>();
            _groups[group] = set;
        }

        set.Add(id);
    }

    public void SpriteRemoveGroup(int id, string group)
    {
        if (!_sprites.TryGetValue(id, out var s)) return;
        s.Groups.Remove(group);
        if (_groups.TryGetValue(group, out var set))
            set.Remove(id);
    }

    public void SpriteClearGroup(string group)
    {
        if (!_groups.TryGetValue(group, out var set)) return;
        foreach (int id in set)
        {
            if (_sprites.TryGetValue(id, out var s))
                s.Groups.Remove(group);
        }

        set.Clear();
    }

    // ── HITBOX — rektangel mot rektangel ────────────────────────

    public bool SpriteHitBox(int id1, int id2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) ||
            !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;

        int x1 = s1.X - s1.HandleX, y1 = s1.Y - s1.HandleY;
        int x2 = s2.X - s2.HandleX, y2 = s2.Y - s2.HandleY;

        return x1 < x2 + s2.Width && x1 + s1.Width > x2 &&
               y1 < y2 + s2.Height && y1 + s1.Height > y2;
    }

// ── HITCIRCLE — cirkel mot cirkel ───────────────────────────

    public bool SpriteHitCircle(int id1, int id2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) ||
            !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;

        // Mittpunkt och radie för varje sprite
        double cx1 = s1.X - s1.HandleX + s1.Width / 2.0;
        double cy1 = s1.Y - s1.HandleY + s1.Height / 2.0;
        double cx2 = s2.X - s2.HandleX + s2.Width / 2.0;
        double cy2 = s2.Y - s2.HandleY + s2.Height / 2.0;

        double r1 = Math.Min(s1.Width, s1.Height) / 2.0;
        double r2 = Math.Min(s2.Width, s2.Height) / 2.0;

        double dx = cx1 - cx2;
        double dy = cy1 - cy2;
        return (dx * dx + dy * dy) <= (r1 + r2) * (r1 + r2);
    }

// ── Grupp-kollisioner ────────────────────────────────────────

    public int SpriteHitGroup(int id, string group)
    {
        if (!_groups.TryGetValue(group, out var set)) return 0;
        foreach (int otherId in set)
        {
            if (otherId == id) continue;
            try
            {
                if (SpriteHit(id, otherId)) return otherId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SpriteHitGroup] fel vid SpriteHit({id},{otherId}): {ex.Message}\n{ex.StackTrace}");
            }
        }

        return 0;
    }

    public int SpriteHitBoxGroup(int id, string group)
    {
        if (!_groups.TryGetValue(group, out var set)) return 0;

        foreach (int otherId in set)
        {
            if (otherId == id) continue;

            if (!_sprites.TryGetValue(otherId, out var s2))
            {
                System.Diagnostics.Debug.WriteLine($"[HitBoxGroup] sprite {otherId} finns inte i _sprites!");
                continue;
            }

            if (SpriteHitBox(id, otherId))
                return otherId;
        }

        return 0;
    }

    public int SpriteHitCircleGroup(int id, string group)
    {
        if (!_groups.TryGetValue(group, out var set)) return 0;
        foreach (int otherId in set)
        {
            if (otherId == id) continue;
            if (SpriteHitCircle(id, otherId)) return otherId;
        }

        return 0;
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
}