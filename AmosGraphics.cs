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
    public string? CompiledSkSlCode { get; set; } // Koden som CachedEffect kompilerades från
}


// Ritar ett lager: komponerar bitmap + texter + sprites på en Skia-yta (på GPU:n när en
// finns) och kör sedan lagrets shader med ytan som inputTexture. Allt sker på render-tråden,
// utan att något läses tillbaka till CPU:n.
public sealed class LayerDrawOperation : ICustomDrawOperation
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly AmosGraphics _graphics;
    private readonly GpuLayer _layer;
    private readonly List<AmosGraphics.QueuedFontText> _texts;
    private readonly List<AmosGraphics.SpriteRenderState> _sprites;
    private GRContext? _gr; // Satt under Render: GPU-kontexten effekterna körs i

    public LayerDrawOperation(Rect bounds, AmosGraphics graphics, GpuLayer layer,
        List<AmosGraphics.QueuedFontText> texts, List<AmosGraphics.SpriteRenderState> sprites)
    {
        Bounds = bounds;
        _graphics = graphics;
        _layer = layer;
        _texts = texts;
        _sprites = sprites;
    }

    public Rect Bounds { get; }

    public void Dispose()
    {
    }

    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null) return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        _gr = lease.GrContext;

        var effect = GetOrCompileEffect();
        if (effect == null) return;

        try
        {
            // RGBA går att rendera till på alla GPU-backends (BGRA inte alltid på OpenGL)
            var info = new SKImageInfo(_layer.Bitmap.PixelSize.Width, _layer.Bitmap.PixelSize.Height,
                SKColorType.Rgba8888, SKAlphaType.Premul);

            using var surface = (_gr != null ? SKSurface.Create(_gr, true, info) : null)
                                ?? SKSurface.Create(info);
            DrawLayerContent(surface.Canvas);
            using var composite = surface.Snapshot();

            using var input = CreateInputShader(composite);
            var children = new SKRuntimeEffectChildren(effect);
            if (effect.Children.Contains("inputTexture"))
                children.Add("inputTexture", input);

            using var shader = effect.ToShader(CreateUniforms(effect), children);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(new SKRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Right, (float)Bounds.Bottom), paint);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Shader Render Crash: " + ex.Message);
        }
    }

    // Kompilera om när koden ändrats (t.ex. RASTER SPACE). Även misslyckade försök
    // kommer ihåg koden så att en trasig shader inte kompileras om varje frame.
    private SKRuntimeEffect? GetOrCompileEffect()
    {
        var code = _layer.SkSlCode;
        if (!string.IsNullOrEmpty(code) && code != _layer.CompiledSkSlCode)
        {
            _layer.CompiledSkSlCode = code;
            _layer.CachedEffect = SKRuntimeEffect.CreateShader(code, out var errors);
            if (!string.IsNullOrEmpty(errors))
            {
                System.Diagnostics.Debug.WriteLine($"SkSL Error: {errors}");
                _layer.CachedEffect = null;
            }
        }

        return _layer.CachedEffect;
    }

    private SKShader CreateInputShader(SKImage composite)
    {
        var input = composite.ToShader();
        if (_layer.Opacity >= 0.999) return input;

        // Skala alla förmultiplicerade kanaler (även alpha) med opacity: fade mot svart/genomskinligt
        byte a = (byte)Math.Round(Math.Clamp(_layer.Opacity, 0, 1) * 255);
        using var fade = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, a), SKBlendMode.DstIn);
        using (input)
            return input.WithColorFilter(fade);
    }

    private void DrawLayerContent(SKCanvas c)
    {
        c.Clear(SKColors.Transparent);

        // Rita layer.Bitmap (bakgrund, tiles, PLOT/LINE)
        using (var bg = ToSkImage(_layer.Bitmap))
            c.DrawImage(bg, 0, 0);

        foreach (var qt in _texts)
            DrawText(c, qt);

        foreach (var sprite in _sprites)
            DrawSprite(c, sprite);
    }

    private void DrawText(SKCanvas c, AmosGraphics.QueuedFontText qt)
    {
        var f = _graphics.GetFont(qt.FontId);
        if (f == null) return;

        var textEffects = qt.GetEffects(_graphics);
        if (textEffects.Count > 0)
        {
            // MED EFFEKTER: hela texten som en bild, roteras/zoomas runt sin mittpunkt
            if (qt.GetProcessedImage(_graphics, _gr, textEffects) is not var (img, owned)) return;

            var pad = qt.GetTotalPadding(textEffects);
            DrawTransformed(c, img, qt.X - pad.left, qt.Y - pad.top, img.Width, img.Height,
                qt.Angle, qt.ZoomX, qt.ZoomY, null);
            if (owned) img.Dispose();
            return;
        }

        // UTAN EFFEKTER: tecken för tecken, hela texten roteras/zoomas runt sin mittpunkt
        double totalW = qt.Text.Length * f.CharWidth;
        float cX = (float)(totalW / 2.0);
        float cY = f.CharHeight / 2f;

        c.Save();
        c.Translate(cX + qt.X, cY + qt.Y);
        c.RotateDegrees((float)qt.Angle);
        c.Scale((float)qt.ZoomX, (float)qt.ZoomY);
        c.Translate(-cX, -cY);

        for (int i = 0; i < qt.Text.Length; i++)
        {
            char ch = qt.Text[i];
            if (ch == ' ') continue;
            var charBmp = _graphics.GetFontChar(f, ch);
            if (charBmp == null) continue;

            using var img = ToSkImage(charBmp);
            c.DrawImage(img, new SKRect(0, 0, img.Width, img.Height),
                SKRect.Create(i * f.CharWidth, 0, f.CharWidth, f.CharHeight), Sampling);
        }

        c.Restore();
    }

    private void DrawSprite(SKCanvas c, AmosGraphics.SpriteRenderState sprite)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(sprite.Alpha, 0f, 1f) * 255))
        };

        if (sprite.Effects.Count > 0)
        {
            // MED EFFEKTER: bilden har padding runt sig, ritas i naturlig storlek
            var (img, owned) = sprite.Sprite.GetEffectImage(_gr, sprite.BaseBitmap, sprite.Effects);
            var pad = AmosGraphics.Sprite.GetTotalPadding(sprite.Effects);

            DrawTransformed(c, img, sprite.X - pad.left, sprite.Y - pad.top, img.Width, img.Height,
                sprite.Angle, sprite.ZoomX, sprite.ZoomY, paint);
            if (owned) img.Dispose();
        }
        else
        {
            // UTAN EFFEKT - sprite.X är VÄNSTER KANT, bilden skalas till spritens storlek
            using var img = ToSkImage(sprite.BaseBitmap);
            DrawTransformed(c, img, sprite.X, sprite.Y, sprite.Width, sprite.Height,
                sprite.Angle, sprite.ZoomX, sprite.ZoomY, paint, img.Width / 2f, img.Height / 2f);
        }
    }

    // Rita bilden i (x, y, w, h), roterad och zoomad runt pivot (standard: mitten av w/h)
    private static void DrawTransformed(SKCanvas c, SKImage img, double x, double y, double w, double h,
        double angle, double zoomX, double zoomY, SKPaint? paint, float? pivotX = null, float? pivotY = null)
    {
        float cX = pivotX ?? (float)(w / 2.0);
        float cY = pivotY ?? (float)(h / 2.0);

        c.Save();
        c.Translate((float)x + cX, (float)y + cY);
        c.RotateDegrees((float)angle);
        c.Scale((float)zoomX, (float)zoomY);
        c.Translate(-cX, -cY);
        c.DrawImage(img, new SKRect(0, 0, img.Width, img.Height), new SKRect(0, 0, (float)w, (float)h), Sampling, paint);
        c.Restore();
    }

    // Våra WriteableBitmaps är alltid BGRA (se AmosGraphics.CopyPixelsBgra)
    internal static SKImage ToSkImage(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var info = new SKImageInfo(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKImage.FromPixelCopy(info, fb.Address, fb.RowBytes);
    }

    private SKRuntimeEffectUniforms CreateUniforms(SKRuntimeEffect effect)
    {
        var uniforms = new SKRuntimeEffectUniforms(effect);

        if (effect.Uniforms.Contains("iResolution"))
            uniforms.Add("iResolution", new[] { (float)_layer.Bitmap.Size.Width, (float)_layer.Bitmap.Size.Height });

        // Skärmens storlek separat från bildens storlek
        if (effect.Uniforms.Contains("iScreenResolution"))
            uniforms.Add("iScreenResolution", new[] { (float)Bounds.Width, (float)Bounds.Height });

        if (effect.Uniforms.Contains("iTime"))
            uniforms.Add("iTime", _layer.Timer);

        if (effect.Uniforms.Contains("uParams"))
        {
            var values = _layer.ShaderValues;
            float[] uParamData = new float[values.Length * 4]; // varje Vector4 = 4 floats
            for (int i = 0; i < values.Length; i++)
            {
                uParamData[i * 4 + 0] = values[i].X;
                uParamData[i * 4 + 1] = values[i].Y;
                uParamData[i * 4 + 2] = values[i].Z;
                uParamData[i * 4 + 3] = values[i].W;
            }

            uniforms.Add("uParams", uParamData);
        }

        // Säkerställ att uPositions och uHeights är exakt 50 element
        if (effect.Uniforms.Contains("uPositions"))
        {
            float[] p50 = new float[50];
            Array.Copy(_layer.ShaderParams, p50, Math.Min(_layer.ShaderParams.Length, 50));
            uniforms.Add("uPositions", p50);
        }

        if (effect.Uniforms.Contains("uHeights"))
        {
            float[] h50 = new float[50];
            Array.Copy(_layer.ShaderHeights, h50, Math.Min(_layer.ShaderHeights.Length, 50));
            uniforms.Add("uHeights", h50);
        }

        if (effect.Uniforms.Contains("uColors"))
        {
            float[] cFrom = new float[50 * 4];
            float[] cTo = new float[50 * 4];
            int colorCount = Math.Min(50, Math.Min(_layer.ShaderColors.Length, _layer.ShaderColorsTo.Length));

            // Slots utan färg blir svart transparent (nollor)
            for (int i = 0; i < colorCount; i++)
            {
                var from = _layer.ShaderColors[i];
                var to = _layer.ShaderColorsTo[i];
                cFrom[i * 4 + 0] = from.Red / 255f;
                cFrom[i * 4 + 1] = from.Green / 255f;
                cFrom[i * 4 + 2] = from.Blue / 255f;
                cFrom[i * 4 + 3] = from.Alpha / 255f;
                cTo[i * 4 + 0] = to.Red / 255f;
                cTo[i * 4 + 1] = to.Green / 255f;
                cTo[i * 4 + 2] = to.Blue / 255f;
                cTo[i * 4 + 3] = to.Alpha / 255f;
            }

            uniforms.Add("uColors", cFrom);
            uniforms.Add("uColorsTo", cTo);
        }

        return uniforms;
    }
}


public sealed class AmosGpuView : Control
{
    public AmosGraphics Graphics { get; set; } = null!;
    private static readonly string RasterShaderCode = Shader.RasterShaderCode;
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Graphics != null && Graphics.Width > 0) return new Size(Graphics.Width, Graphics.Height);
        return new Size(640, 480);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Graphics == null) return;

        List<GpuLayer> layersCopy;
        List<AmosGraphics.QueuedFontText> fontTextsCopy;
        List<AmosGraphics.SpriteRenderState> spritesCopy;

        lock (Graphics.LockObject)
        {
            layersCopy = new List<GpuLayer>(Graphics.ActiveFrame);
            fontTextsCopy = Graphics.GetQueuedTexts().ToList();
            spritesCopy = Graphics.GetSpriteRenderStates();
        }

        var screenRect = new Rect(0, 0, Graphics.Width, Graphics.Height);

        // Ett draw-anrop per lager; själva ritningen sker på render-tråden i LayerDrawOperation
        for (int layerIndex = 0; layerIndex < layersCopy.Count; layerIndex++)
        {
            var layer = layersCopy[layerIndex];
            if (layer.Bitmap == null || !layer.Visible) continue;

            layer.SkSlCode ??= RasterShaderCode;

            var texts = fontTextsCopy.Where(qt => qt.Layer == -1 || qt.Layer == layerIndex).ToList();
            var sprites = spritesCopy.Where(s => s.Layer == -1 || s.Layer == layerIndex).ToList();
            ctx.Custom(new LayerDrawOperation(screenRect, Graphics, layer, texts, sprites));
        }
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

            // Kopiera pixlar till ActiveScreen (konverterade till BGRA)
            var b = new byte[ps.Width * ps.Height * 4];
            unsafe
            {
                fixed (byte* p = b)
                {
                    rtb.CopyPixels(new PixelRect(ps), (nint)p, b.Length, ps.Width * 4);
                    ToBgra(rtb, p, ps.Width, ps.Height, ps.Width * 4);
                }
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
                                    dr[di + 3] = 255; // A
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

        // Effekterna hör till fonten (inte till en utskrift) så att de överlever FONT CLEAR
        public List<SpriteEffect> Effects { get; } = new();
        public int EffectsVersion;

        // Text + statiska effekter (BLUR/COLOR) per textsträng, så de inte räknas om varje frame.
        // Används bara från render-tråden (LayerDrawOperation). Bilderna ligger på GPU:n.
        public Dictionary<string, SKImage> StaticCache { get; } = new();
        public int StaticCacheVersion = -1;
        public GRContext? StaticCacheContext;

        public void ClearStaticCache()
        {
            foreach (var img in StaticCache.Values) img.Dispose();
            StaticCache.Clear();
        }
    }

    // FONT EFFECT ADD: finns effekttypen redan på fonten uppdateras den i stället (Time behålls),
    // så att ADD inne i en loop inte staplar nya effekter varje frame.
    public bool AddFontEffect(int fontId, SpriteEffect effect)
    {
        lock (LockObject)
        {
            if (!_fonts.TryGetValue(fontId, out var f)) return false;

            var existing = f.Effects.FirstOrDefault(e => e.GetType() == effect.GetType());
            switch (existing, effect)
            {
                case (null, _):
                    f.Effects.Add(effect);
                    f.EffectsVersion++;
                    break;
                case (WaveEffect w, WaveEffect n):
                    // WAVE räknas om varje frame, ingen cache att invalidera
                    w.AmplitudeX = n.AmplitudeX;
                    w.FrequencyX = n.FrequencyX;
                    w.AmplitudeY = n.AmplitudeY;
                    w.FrequencyY = n.FrequencyY;
                    break;
                case (SpriteBlurEffect b, SpriteBlurEffect n) when b.Radius != n.Radius:
                    b.Radius = n.Radius;
                    f.EffectsVersion++;
                    break;
                case (ColorGradeEffect c, ColorGradeEffect n)
                    when c.Brightness != n.Brightness || c.Contrast != n.Contrast || c.Saturation != n.Saturation:
                    c.Brightness = n.Brightness;
                    c.Contrast = n.Contrast;
                    c.Saturation = n.Saturation;
                    f.EffectsVersion++;
                    break;
            }

            return true;
        }
    }

    public SpriteEffect? GetFontEffect(int fontId, int index)
    {
        lock (LockObject)
        {
            return _fonts.TryGetValue(fontId, out var f) && index >= 0 && index < f.Effects.Count
                ? f.Effects[index]
                : null;
        }
    }

    public void InvalidateFontEffects(int fontId)
    {
        lock (LockObject)
        {
            if (_fonts.TryGetValue(fontId, out var f)) f.EffectsVersion++;
        }
    }

    public void ClearFontEffects(int fontId)
    {
        lock (LockObject)
        {
            if (!_fonts.TryGetValue(fontId, out var f)) return;
            f.Effects.Clear();
            f.EffectsVersion++;
        }
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
                CopyPixelsBgra(b, new PixelRect(0, 0, w, h), fb);
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
                    CopyPixelsBgra(b, new PixelRect(col * frameW, row * frameH, frameW, frameH), fb);
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

    private const int WRAP_OFFSET = 25;
    private const int FIRST_BAR_SLOT = 1; // Slot 0 är reserverad för bakgrunden

    private Dictionary<int, RasterBarAllocation> GetLayerBars(int layerIdx)
    {
        if (!_rasterBars.TryGetValue(layerIdx, out var bars))
        {
            bars = new Dictionary<int, RasterBarAllocation>();
            _rasterBars[layerIdx] = bars;
        }

        return bars;
    }

    private static int CountUsedSlots(Dictionary<int, RasterBarAllocation> bars)
        => FIRST_BAR_SLOT + bars.Values.Sum(b => b.ShaderSlotCount);

    // Hitta första lediga sammanhängande slot-intervall (bars kan ha tagits bort mitt i)
    private static int FindFreeSlots(Dictionary<int, RasterBarAllocation> bars, int needed)
    {
        var used = new bool[WRAP_OFFSET];
        foreach (var b in bars.Values)
            for (int i = 0; i < b.ShaderSlotCount; i++)
                used[b.FirstShaderSlot + i] = true;

        for (int start = FIRST_BAR_SLOT; start + needed <= WRAP_OFFSET; start++)
        {
            int n = 0;
            while (n < needed && !used[start + n]) n++;
            if (n == needed) return start;
        }

        return -1;
    }

    public void SetRasterBar(int layerIdx, int basicBarId, float x, float y, float height, string colorString)
        => SetRasterBarWrapped(layerIdx, basicBarId, x, y, height, colorString, wrap: false);

    public void SetRasterBarWrapped(int layerIdx, int basicBarId, float x, float y,
        float height, string colorString, bool wrap = true)
    {
        if (basicBarId < 1 || basicBarId > 100)
        {
            OnError?.Invoke($"[RASTER] Bar ID must be between 1-100, got {basicBarId}");
            return;
        }

        var bars = GetLayerBars(layerIdx);

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

        // Frigör den gamla baren först så att dess slots kan återanvändas
        if (bars.Remove(basicBarId, out var existing))
            ClearBarShaderSlots(layerIdx, existing);

        int firstSlot = FindFreeSlots(bars, neededSlots);
        if (firstSlot < 0)
        {
            OnError?.Invoke($"[RASTER] Not enough shader slots! Need {neededSlots}. " +
                            $"(Total used: {CountUsedSlots(bars)}/{WRAP_OFFSET})");
            if (existing != null)
            {
                // Återställ den gamla baren
                bars[basicBarId] = existing;
                ApplyRasterBarToShader(layerIdx, existing);
                ApplyRasterBarWrap(layerIdx, existing);
            }
            return;
        }

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
        ApplyRasterBarWrap(layerIdx, allocation);
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
        ApplyRasterBarWrap(layerIdx, bar);
    }

    private void ApplyRasterBarToShader(int layerIdx, RasterBarAllocation bar)
        => WriteBarSlots(layerIdx, bar, bar.FirstShaderSlot, bar.StartY);

    // Wrap-kopian ligger WRAP_OFFSET slots efter originalet, en skärmhöjd bort
    private void ApplyRasterBarWrap(int layerIdx, RasterBarAllocation bar)
    {
        int wrapSlot = bar.FirstShaderSlot + WRAP_OFFSET;
        if (!bar.Wrap)
        {
            ClearShaderSlots(layerIdx, wrapSlot, bar.ShaderSlotCount);
            return;
        }

        float wrappedY = bar.StartY > 0 ? bar.StartY - Height : bar.StartY + Height;
        WriteBarSlots(layerIdx, bar, wrapSlot, wrappedY);
    }

    private void WriteBarSlots(int layerIdx, RasterBarAllocation bar, int firstSlot, float startY)
    {
        if (bar.Colors.Count == 1)
        {
            SetShaderParams(layerIdx, firstSlot, startY, bar.TotalHeight);
            SetShaderColors(layerIdx, firstSlot, bar.Colors[0], bar.Colors[0]);
        }
        else
        {
            int segments = bar.Colors.Count - 1;
            float segmentHeight = bar.TotalHeight / segments;
            for (int i = 0; i < segments; i++)
            {
                int slot = firstSlot + i;
                float segY = startY + (i * segmentHeight);
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
        ApplyRasterBarWrap(layerIdx, bar);
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
        if (!bars.Remove(basicBarId, out var bar)) return;

        ClearBarShaderSlots(layerIdx, bar);
    }

    public void ClearAllRasterBars(int layerIdx = -1)
    {
        if (layerIdx == -1)
        {
            foreach (var kvp in _rasterBars)
            foreach (var bar in kvp.Value.Values)
                ClearBarShaderSlots(kvp.Key, bar);
            _rasterBars.Clear();
        }
        else
        {
            if (_rasterBars.TryGetValue(layerIdx, out var bars))
            {
                foreach (var bar in bars.Values)
                    ClearBarShaderSlots(layerIdx, bar);
                bars.Clear();
            }
        }
    }

    private void ClearBarShaderSlots(int layerIdx, RasterBarAllocation bar)
    {
        ClearShaderSlots(layerIdx, bar.FirstShaderSlot, bar.ShaderSlotCount);
        ClearShaderSlots(layerIdx, bar.FirstShaderSlot + WRAP_OFFSET, bar.ShaderSlotCount);
    }

    private void ClearShaderSlots(int layerIdx, int firstSlot, int count)
    {
        lock (LockObject)
        {
            if (layerIdx < 0 || layerIdx >= DrawingFrame.Count) return;

            var heights = DrawingFrame[layerIdx].ShaderHeights;
            for (int slot = firstSlot; slot < firstSlot + count && slot < heights.Length; slot++)
                heights[slot] = 0;
        }
    }

    public string GetRasterBarDebugInfo()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kvp in _rasterBars)
        {
            int idx = kvp.Key;
            var bars = kvp.Value;
            int slotsUsed = CountUsedSlots(bars);
            sb.AppendLine($"Layer {idx}: Slots Used: {slotsUsed}/{WRAP_OFFSET}, Bars: {bars.Count}");
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
        public List<SpriteEffect> Effects { get; } = new(); // ✅ Inte AmosGraphics.SpriteEffect

        private int _effectsVersion = 0;
        // Cache för statiska effekter (BLUR/COLOR). Ägs av render-tråden; ligger på GPU:n
        // och hör därför ihop med den GRContext den skapades i.
        private SKImage? _staticEffectsImage;
        private WriteableBitmap? _staticEffectsSource;   // Frame som cachen bygger på
        private GRContext? _staticEffectsContext;
        private int _staticEffectsVersion = -1;
    
        // ✅ Beräkna total padding från alla effekter
        public static (int left, int right, int top, int bottom) GetTotalPadding(List<SpriteEffect> effects)
        {
            int totalLeft = 0, totalRight = 0, totalTop = 0, totalBottom = 0;
        
            foreach (var effect in effects)
            {
                var p = effect.GetPadding();
                totalLeft += p.leftPadding;
                totalRight += p.rightPadding;
                totalTop += p.topPadding;
                totalBottom += p.bottomPadding;
            }
        
            return (totalLeft, totalRight, totalTop, totalBottom);
        }
        
        public void InvalidateEffectCache()
        {
            _effectsVersion++;
        }

        public Sprite(int width, int height, WriteableBitmap firstFrame)
        {
            Width = width;
            Height = height;
            Frames.Add(firstFrame);
            Ink = Colors.White;
            TransparentKey = Colors.Magenta;
            Visible = false;
        }

        // Aktuell frame utan effekter (från imagebanken eller spritens egna frames)
        public WriteableBitmap? GetBaseBitmap(Dictionary<int, ImageBankEntry> imageBank)
        {
            if (ImageBankId >= 0 && imageBank.TryGetValue(ImageBankId, out var entry) && entry.Frames.Count > 0)
                return entry.Frames[Math.Clamp(CurrentFrame, 0, entry.Frames.Count - 1)];

            return Frames.Count > 0 ? Frames[Math.Clamp(CurrentFrame, 0, Frames.Count - 1)] : null;
        }

        // Kör effektkedjan på GPU:n. Anropas BARA från render-tråden (LayerDrawOperation), som äger cachen.
        // Owned = true betyder att anroparen ska Dispose:a bilden; false att den ligger i cachen.
        public (SKImage Image, bool Owned) GetEffectImage(GRContext? gr, WriteableBitmap baseBitmap,
            List<SpriteEffect> allEffects)
        {
            // Rotation/zoom görs INTE här - det sköts av LayerDrawOperation med en transform
            var staticEffects = allEffects.Where(e => e is not WaveEffect).ToList();
            var dynamicEffects = allEffects.Where(e => e is WaveEffect).ToList();

            SKImage current;
            bool owned;

            if (staticEffects.Count > 0)
            {
                bool cacheValid = _staticEffectsImage != null &&
                                  _staticEffectsVersion == _effectsVersion &&
                                  _staticEffectsSource == baseBitmap &&
                                  _staticEffectsContext == gr;
                if (!cacheValid)
                {
                    _staticEffectsImage?.Dispose();
                    _staticEffectsImage = ShaderHelper.ApplyChain(gr, LayerDrawOperation.ToSkImage(baseBitmap), staticEffects);
                    _staticEffectsVersion = _effectsVersion;
                    _staticEffectsSource = baseBitmap;
                    _staticEffectsContext = gr;
                }

                current = _staticEffectsImage!;
                owned = false;
            }
            else
            {
                current = LayerDrawOperation.ToSkImage(baseBitmap);
                owned = true;
            }

            // Dynamiska effekter (WAVE) körs varje frame
            foreach (var effect in dynamicEffects)
            {
                var next = ShaderHelper.Apply(gr, current, effect);
                if (next == null) continue;
                if (owned) current.Dispose();
                current = next;
                owned = true;
            }

            return (current, owned);
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
        var bmp = s.GetBaseBitmap(GetImageBank());
        if (bmp != null)
        {
            // Uppdatera bredd/höjd om de inte redan satts manuellt
            s.Width = (int)bmp.Size.Width;
            s.Height = (int)bmp.Size.Height;
        }

    }

    private readonly Dictionary<int, Sprite> _sprites = new();

    // Allt Render behöver veta om en sprite, kopierat vid ett och samma tillfälle
    public sealed record SpriteRenderState(
        Sprite Sprite, WriteableBitmap BaseBitmap, List<SpriteEffect> Effects,
        int X, int Y, int Width, int Height,
        double Angle, double ZoomX, double ZoomY, float Alpha, int Layer);

    // Ögonblicksbild från senaste WAIT VBL, gjord på runner-tråden så att Render aldrig
    // läser en sprite som håller på att ändras. null = programmet använder inte WAIT VBL.
    private List<SpriteRenderState>? _presentedSprites;

    // Anroparen måste hålla LockObject
    private List<SpriteRenderState> BuildSpriteStates()
    {
        var states = new List<SpriteRenderState>();
        foreach (var (_, s) in _sprites.OrderBy(kv => kv.Key))
        {
            if (!s.Visible) continue;
            var baseBitmap = s.GetBaseBitmap(_imageBank);
            if (baseBitmap == null) continue;

            states.Add(new SpriteRenderState(
                s, baseBitmap, s.Effects.Where(e => e.RequiresGpuContext).ToList(),
                s.X, s.Y, s.Width, s.Height,
                s.Angle, s.ZoomX, s.ZoomY, s.Alpha, s.Layer));
        }

        return states;
    }

    public List<SpriteRenderState> GetSpriteRenderStates()
    {
        lock (LockObject)
        {
            return _presentedSprites ?? BuildSpriteStates();
        }
    }

    public void AddSpriteEffect(int id, SpriteEffect effect)
    {
        var s = GetSprite(id);
        lock (LockObject)
        {
            s.Effects.Add(effect);
            s.InvalidateEffectCache();
        }
    }

    public void ClearSpriteEffects(int id)
    {
        var s = GetSprite(id);
        lock (LockObject)
        {
            s.Effects.Clear();
            s.InvalidateEffectCache();
        }
    }
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
    
        // Effekter (BLUR/WAVE/COLOR) som gäller för fonten just nu
        internal List<SpriteEffect> GetEffects(AmosGraphics graphics)
        {
            var font = graphics.GetFont(FontId);
            if (font == null) return new List<SpriteEffect>();
            lock (graphics.LockObject)
                return font.Effects.Where(e => e.RequiresGpuContext).ToList();
        }

        // ✅ Beräkna total padding från alla effekter
        public (int left, int right, int top, int bottom) GetTotalPadding(List<SpriteEffect> effects)
        {
            int totalLeft = 0, totalRight = 0, totalTop = 0, totalBottom = 0;
        
            foreach (var effect in effects)
            {
                var p = effect.GetPadding();
                totalLeft += p.leftPadding;
                totalRight += p.rightPadding;
                totalTop += p.topPadding;
                totalBottom += p.bottomPadding;
            }
        
            return (totalLeft, totalRight, totalTop, totalBottom);
        }
    
        // Rendera texten med effekter på GPU:n. Anropas bara från render-tråden.
        // Owned = true betyder att anroparen ska Dispose:a bilden; false att den ligger i fontens cache.
        public (SKImage Image, bool Owned)? GetProcessedImage(AmosGraphics graphics, GRContext? gr,
            List<SpriteEffect> effects)
        {
            var font = graphics.GetFont(FontId);
            if (font == null) return null;

            var staticEffects = effects.Where(e => e is not WaveEffect).ToList();
            var dynamicEffects = effects.Where(e => e is WaveEffect).ToList();

            // Cachen töms när effekterna ändras, när GPU-kontexten byts,
            // eller om texten byts ofta (t.ex. en poängräknare)
            if (font.StaticCacheVersion != font.EffectsVersion || font.StaticCacheContext != gr ||
                font.StaticCache.Count > 64)
            {
                font.ClearStaticCache();
                font.StaticCacheVersion = font.EffectsVersion;
                font.StaticCacheContext = gr;
            }

            if (!font.StaticCache.TryGetValue(Text, out var current))
            {
                var textBitmap = RenderText(graphics, font);
                if (textBitmap == null) return null;

                current = ShaderHelper.ApplyChain(gr, LayerDrawOperation.ToSkImage(textBitmap), staticEffects);
                font.StaticCache[Text] = current;
            }

            // Applicera dynamiska effekter (WAVE) varje frame
            bool owned = false;
            foreach (var effect in dynamicEffects)
            {
                var next = ShaderHelper.Apply(gr, current, effect);
                if (next == null) continue;
                if (owned) current.Dispose();
                current = next;
                owned = true;
            }

            return (current, owned);
        }

        private WriteableBitmap? RenderText(AmosGraphics graphics, Font font)
        {
            // Beräkna storlek för hela texten
            int baseWidth = Text.Length * font.CharWidth;
            int baseHeight = font.CharHeight;
        
            if (baseWidth <= 0 || baseHeight <= 0) return null;
        
            // Skapa bas-bitmap med texten
            var baseBitmap = new WriteableBitmap(
                new PixelSize(baseWidth, baseHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        
            // Rita texten till bitmap
            using (var fb = baseBitmap.Lock())
            {
                unsafe
                {
                    uint* ptr = (uint*)fb.Address;
                    int stride = fb.RowBytes / 4;
                
                    // Rensa till transparent
                    for (int i = 0; i < baseWidth * baseHeight; i++)
                        ptr[i] = 0;
                
                    // Rita varje tecken
                    for (int i = 0; i < Text.Length; i++)
                    {
                        char c = Text[i];
                        if (c == ' ') continue;
                    
                        var charBmp = graphics.GetFontChar(font, c);
                        if (charBmp == null) continue;
                    
                        using var charFb = charBmp.Lock();
                        byte* charPtr = (byte*)charFb.Address;
                    
                        int startX = i * font.CharWidth;
                    
                        for (int y = 0; y < font.CharHeight && y < baseHeight; y++)
                        {
                            for (int x = 0; x < font.CharWidth; x++)
                            {
                                int dstX = startX + x;
                                if (dstX >= baseWidth) continue;
                            
                                int srcIdx = (y * charFb.RowBytes) + (x * 4);
                                byte b = charPtr[srcIdx + 0];
                                byte g = charPtr[srcIdx + 1];
                                byte r = charPtr[srcIdx + 2];
                                byte alpha = charPtr[srcIdx + 3];
                            
                                if (alpha > 0)
                                {
                                    int dstIdx = y * stride + dstX;
                                    // BGRA format
                                    ptr[dstIdx] = ((uint)alpha << 24) | 
                                                  ((uint)r << 16) |
                                                  ((uint)g << 8) |
                                                  ((uint)b);
                                }
                            }
                        }
                    }
                }
            }

            return baseBitmap;
        }
    }

    public readonly List<QueuedFontText> _fontTexts = new();

    // Ögonblicksbild från senaste WAIT VBL. Utan den kan skärmen ritas mitt i ett
    // FONT CLEAR / FONT PRINT / FONT EFFECT ADD-varv och visa texten utan effekter.
    // null = programmet använder inte WAIT VBL, rita live-listan.
    private List<QueuedFontText>? _presentedFontTexts;

    // Anropas vid WAIT VBL: lås fast det som ska visas för den här framen
    public void PresentFrame()
    {
        lock (LockObject)
        {
            _presentedFontTexts = _fontTexts.ToList();
            _presentedSprites = BuildSpriteStates();
        }
    }

    public IEnumerable<QueuedFontText> GetQueuedTexts()
    {
        lock (LockObject)
        {
            return (_presentedFontTexts ?? _fontTexts).ToList(); // ✅ Returnera kopia
        }
    }


    // ✅ NYTT: Metoder för att rensa allt
    public void ClearAllResources()
    {
        lock (LockObject)
        {
            _sprites.Clear();
            _groups.Clear();
            _presentedSprites = null;
            _imageBank.Clear();
            _tiles.Clear();
            _tilesInWidth = 0;
            _tileWidth = 32;
            _tileHeight = 32;
            _map = new int[0, 0];
            _fonts.Clear();
            _fontTexts.Clear();
            _presentedFontTexts = null;
            ClearAllRasterBars(0);
        }
    }

    public void ClearAllSprites()
    {
        lock (LockObject)
        {
            _sprites.Clear();
            _groups.Clear();
            _presentedSprites = null;
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
            _presentedFontTexts = null;
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
                { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0) };
            var lB = new GpuLayer
                { Bitmap = CreateEmptyBitmap(w, h), Offset = new Point(0, 0) };

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
                uint val = (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);

                if ((val & 0xFFFFFF00) == 0) // Alpha, röd och grön = 0 ger genomskinligt (som förut)
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
                        row[i + 0] = bb;
                        row[i + 1] = gb;
                        row[i + 2] = rb;
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
            uint cVal = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

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
            uint cVal = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

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
            uint cVal = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

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
            uint fillColor = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

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
            var ft = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Arial"),
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
                fixed (byte* p = b)
                {
                    rtb.CopyPixels(new PixelRect(ps), (nint)p, b.Length, ps.Width * 4);
                    ToBgra(rtb, p, ps.Width, ps.Height, ps.Width * 4);
                }
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
                        CopyPixelsBgra(b, new PixelRect(x * tw, y * th, tw, th), fb);

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
                                    p[i] = (a << 24) | (r << 16) | (g << 8) | bcol;
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
    // Alla våra WriteableBitmaps lagrar pixlar som BGRA (förmultiplicerad alpha), precis som deras
    // PixelFormat säger. Bilder från disk och RenderTargetBitmap har plattformens ordning
    // (RGBA på macOS, BGRA på Windows), så de konverteras när de kopieras in.
    public static void CopyPixelsBgra(Bitmap src, PixelRect rect, ILockedFramebuffer fb)
    {
        src.CopyPixels(rect, fb.Address, fb.RowBytes * rect.Height, fb.RowBytes);
        unsafe
        {
            ToBgra(src, (byte*)fb.Address, rect.Width, rect.Height, fb.RowBytes);
        }
    }

    public static unsafe void ToBgra(Bitmap src, byte* p, int width, int height, int rowBytes)
    {
        if (src.Format != PixelFormat.Rgba8888) return;

        for (int y = 0; y < height; y++)
        {
            byte* row = p + y * rowBytes;
            for (int x = 0; x < width * 4; x += 4)
                (row[x], row[x + 2]) = (row[x + 2], row[x]);
        }
    }

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
                    CopyPixelsBgra(b, new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb);
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
                        CopyPixelsBgra(b, new PixelRect(x * tw, y * th, tw, th), fb);
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
                        CopyPixelsBgra(b, new PixelRect(x * tw, y * th, tw, th), fb);
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
        lock (LockObject) _sprites[id] = sprite;
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
                CopyPixelsBgra(b, new PixelRect(0, 0, w, h), fb);

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

            lock (LockObject) s.Frames.Clear();

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
                    CopyPixelsBgra(sourceInfo, new PixelRect(srcX, srcY, frameW, frameH), fb);

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

                lock (LockObject) s.Frames.Add(f);
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
            CopyPixelsBgra(b, new PixelRect(0, 0, (int)b.Size.Width, (int)b.Size.Height), fb);
            unsafe
            {
                FixupImageColors(fb.Address.ToPointer(), s.Width * s.Height);
            }
        }

        lock (LockObject) s.Frames.Add(f);
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
        
    public void TickSpriteEffects(float deltaTime)
    {
        lock (LockObject)
        {
            foreach (var sprite in _sprites.Values)
            {
                if (!sprite.Visible) continue;
 
                foreach (var effect in sprite.Effects)
                {
                    if (effect is WaveEffect wave)
                        wave.Time += deltaTime;
                }
            }
        
            // Samma för font-effekter (en gång per font, oavsett hur många texter som skrivits)
            foreach (var font in _fonts.Values)
            {
                foreach (var effect in font.Effects)
                {
                    if (effect is WaveEffect wave)
                        wave.Time += deltaTime;
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

        var bmp1 = s1.GetBaseBitmap(_imageBank);
        var bmp2 = s2.GetBaseBitmap(_imageBank);
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
// Kör sprite-/font-effekter som shaders. Med en GRContext sker allt på GPU:n och resultatet
// stannar där som en textur; utan (headless) används en CPU-yta.
public static class ShaderHelper
{
    // Kör en effekt och returnera en ny bild som anroparen äger, eller null om effekten misslyckades.
    // Bilden blir större än input enligt effektens padding.
    public static SKImage? Apply(GRContext? gr, SKImage input, SpriteEffect effect)
    {
        try
        {
            var (leftPad, rightPad, topPad, bottomPad) = effect.GetPadding();
            var info = new SKImageInfo(input.Width + leftPad + rightPad, input.Height + topPad + bottomPad,
                SKColorType.Rgba8888, SKAlphaType.Premul);

            using var paint = effect.GetShaderPaint(input);
            if (paint == null) return null;

            using var surface = (gr != null ? SKSurface.Create(gr, true, info) : null) ?? SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
            return surface.Snapshot();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Effect {effect.GetType().Name} error: {ex.Message}");
            return null;
        }
    }

    // Kör effekterna i tur och ordning. Tar över ägandet av input och returnerar en bild anroparen äger.
    public static SKImage ApplyChain(GRContext? gr, SKImage input, IEnumerable<SpriteEffect> effects)
    {
        var current = input;
        foreach (var effect in effects)
        {
            var next = Apply(gr, current, effect);
            if (next == null) continue;
            current.Dispose();
            current = next;
        }

        return current;
    }
}

// LÄGG TILL DESSA I SLUTET AV AmosGraphics.cs (före sista })

public abstract class SpriteEffect
{
    public virtual SKPaint? GetShaderPaint(SKImage inputImage) => null;

    public virtual bool RequiresGpuContext => false;
    
    // ✅ NYTT: Hur mycket effekten expanderar bilden (för korrekt positionering)
    public virtual (int leftPadding, int rightPadding, int topPadding, int bottomPadding) GetPadding() 
        => (0, 0, 0, 0);
}


public class SpriteBlurEffect : SpriteEffect
{
    public float Radius { get; set; } = 5f;
    
    // ✅ Global cached shader
    private static SKRuntimeEffect? _globalCachedEffect;
    private static readonly object _shaderLock = new object();

    public override bool RequiresGpuContext => true;

    private static string ShaderCode => @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float uRadius;

const int MAX_RADIUS = 20;

half4 main(float2 fragCoord) {
    half4 sum = half4(0.0);
    float samples = 0.0;

    for (int x = -MAX_RADIUS; x <= MAX_RADIUS; x++) {
        for (int y = -MAX_RADIUS; y <= MAX_RADIUS; y++) {
            float fx = float(x);
            float fy = float(y);

            if (abs(fx) > uRadius || abs(fy) > uRadius)
                continue;

            sum += inputTexture.eval(fragCoord + float2(fx, fy));
            samples += 1.0;
        }
    }

    if (samples < 1.0)
        return inputTexture.eval(fragCoord);

    return sum / half(samples);
}
";

    private static void EnsureShaderCompiled()
    {
        lock (_shaderLock)
        {
            if (_globalCachedEffect != null) return;

            try
            {
                var result = SKRuntimeEffect.CreateShader(ShaderCode, out var errorText);
            
                if (result == null || !string.IsNullOrEmpty(errorText))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Blur shader error: {errorText}");
                    _globalCachedEffect = null;
                }
                else
                {
                    _globalCachedEffect = result;
                    System.Diagnostics.Debug.WriteLine("✅ Blur shader compiled (global cache)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Blur shader exception: {ex.Message}");
                _globalCachedEffect = null;
            }
        }
    }

    public override SKPaint? GetShaderPaint(SKImage inputImage)
    {
        EnsureShaderCompiled();
        if (_globalCachedEffect == null) return null;
        
        try
        {
            var children = new SKRuntimeEffectChildren(_globalCachedEffect);
            children.Add("inputTexture", inputImage.ToShader());

            var uniforms = new SKRuntimeEffectUniforms(_globalCachedEffect);
            uniforms.Add("iResolution", new[] { (float)inputImage.Width, (float)inputImage.Height });
            uniforms.Add("uRadius", Radius);

            var shader = _globalCachedEffect.ToShader(uniforms, children);
            return new SKPaint { Shader = shader };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GetShaderPaint error: {ex.Message}");
            return null;
        }
    }
}

public class WaveEffect : SpriteEffect
{
    public float AmplitudeX { get; set; } = 5f;
    public float FrequencyX { get; set; } = 0.1f;
    public float AmplitudeY { get; set; } = 0f;
    public float FrequencyY { get; set; } = 0f;
    public float PhaseX { get; set; } = 0f;  // ✅ NYTT: Fasförskjutning för X
    public float PhaseY { get; set; } = 0f;  // ✅ NYTT: Fasförskjutning för Y
    public float Time { get; set; } = 0f;
    
    private static SKRuntimeEffect? _globalCachedEffect;
    private static readonly object _shaderLock = new object();

    public override bool RequiresGpuContext => true;
    
    public override (int leftPadding, int rightPadding, int topPadding, int bottomPadding) GetPadding()
    {
        int paddingX = (int)Math.Ceiling(AmplitudeX / 2.0f);
        int paddingY = (int)Math.Ceiling(AmplitudeY / 2.0f);
        return (paddingX, paddingX, paddingY, paddingY);
    }

    private static string ShaderCode => @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float uAmplitudeX;
uniform float uFrequencyX;
uniform float uAmplitudeY;
uniform float uFrequencyY;
uniform float uPhaseX;
uniform float uPhaseY;
uniform float uTime;

half4 main(float2 fragCoord) {
    float halfAmpX = uAmplitudeX * 0.5;
    float halfAmpY = uAmplitudeY * 0.5;
    
    float originalX = fragCoord.x - halfAmpX;
    float originalY = fragCoord.y - halfAmpY;

    float offsetX = sin(originalY * uFrequencyX + uTime + uPhaseX) * halfAmpX;
    float offsetY = sin(originalX * uFrequencyY + uTime + uPhaseY) * halfAmpY;
    float sampleX = originalX + offsetX;
    float sampleY = originalY + offsetY;
    
    if (sampleX < 0.0 || sampleX >= iResolution.x || 
        sampleY < 0.0 || sampleY >= iResolution.y) {
        return half4(0, 0, 0, 0);
    }
    
    return inputTexture.eval(float2(sampleX, sampleY));
}";

    private static void EnsureShaderCompiled()
    {
        lock (_shaderLock)
        {
            if (_globalCachedEffect != null) return;

            try
            {
                var result = SKRuntimeEffect.CreateShader(ShaderCode, out var errorText);
            
                if (result == null || !string.IsNullOrEmpty(errorText))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Wave shader error: {errorText}");
                    _globalCachedEffect = null;
                }
                else
                {
                    _globalCachedEffect = result;
                    System.Diagnostics.Debug.WriteLine("✅ Wave shader compiled (global cache)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Wave shader exception: {ex.Message}");
                _globalCachedEffect = null;
            }
        }
    }

    public override SKPaint? GetShaderPaint(SKImage inputImage)
    {
        EnsureShaderCompiled();
        if (_globalCachedEffect == null) return null;

        try
        {
            var children = new SKRuntimeEffectChildren(_globalCachedEffect);
            children.Add("inputTexture", inputImage.ToShader());

            var uniforms = new SKRuntimeEffectUniforms(_globalCachedEffect);
            uniforms.Add("iResolution", new[] { (float)inputImage.Width, (float)inputImage.Height });
            uniforms.Add("uAmplitudeX", AmplitudeX);
            uniforms.Add("uFrequencyX", FrequencyX);
            uniforms.Add("uAmplitudeY", AmplitudeY);
            uniforms.Add("uFrequencyY", FrequencyY);
            uniforms.Add("uPhaseX", PhaseX);  // ✅ NYTT
            uniforms.Add("uPhaseY", PhaseY);  // ✅ NYTT
            uniforms.Add("uTime", Time);

            var shader = _globalCachedEffect.ToShader(uniforms, children);
            return new SKPaint { Shader = shader };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GetShaderPaint error: {ex.Message}");
            return null;
        }
    }
}

public class ColorGradeEffect : SpriteEffect
{
    public float Brightness { get; set; } = 1f;
    public float Contrast { get; set; } = 1f;
    public float Saturation { get; set; } = 1f;

    private static SKRuntimeEffect? _globalCachedEffect;
    private static readonly object _shaderLock = new object();

    public override bool RequiresGpuContext => true;

    private static string ShaderCode => @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float uBrightness;
uniform float uContrast;
uniform float uSaturation;

half4 main(float2 fragCoord) {
    half4 color = inputTexture.eval(fragCoord);
    
    // Unpack color
    float r = color.r;
    float g = color.g;
    float b = color.b;
    float a = color.a;
    
    // 1. Brightness
    r *= uBrightness;
    g *= uBrightness;
    b *= uBrightness;
    
    // 2. Contrast (pivot around 0.5)
    r = (r - 0.5) * uContrast + 0.5;
    g = (g - 0.5) * uContrast + 0.5;
    b = (b - 0.5) * uContrast + 0.5;
    
    // 3. Saturation
    float gray = 0.299 * r + 0.587 * g + 0.114 * b;
    r = gray + (r - gray) * uSaturation;
    g = gray + (g - gray) * uSaturation;
    b = gray + (b - gray) * uSaturation;
    
    // Clamp
    r = clamp(r, 0.0, 1.0);
    g = clamp(g, 0.0, 1.0);
    b = clamp(b, 0.0, 1.0);
    
    return half4(r, g, b, a);
}";

    private static void EnsureShaderCompiled()
    {
        lock (_shaderLock)
        {
            if (_globalCachedEffect != null) return;

            try
            {
                var result = SKRuntimeEffect.CreateShader(ShaderCode, out var errorText);

                if (result == null || !string.IsNullOrEmpty(errorText))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ ColorGrade shader error: {errorText}");
                    _globalCachedEffect = null;
                }
                else
                {
                    _globalCachedEffect = result;
                    System.Diagnostics.Debug.WriteLine("✅ ColorGrade shader compiled (global cache)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ColorGrade shader exception: {ex.Message}");
                _globalCachedEffect = null;
            }
        }
    }

    public override SKPaint? GetShaderPaint(SKImage inputImage)
    {
        EnsureShaderCompiled();
        if (_globalCachedEffect == null) return null;

        try
        {
            var children = new SKRuntimeEffectChildren(_globalCachedEffect);
            children.Add("inputTexture", inputImage.ToShader());

            var uniforms = new SKRuntimeEffectUniforms(_globalCachedEffect);
            uniforms.Add("iResolution", new[] { (float)inputImage.Width, (float)inputImage.Height });
            uniforms.Add("uBrightness", Brightness);
            uniforms.Add("uContrast", Contrast);
            uniforms.Add("uSaturation", Saturation);

            var shader = _globalCachedEffect.ToShader(uniforms, children);
            return new SKPaint { Shader = shader };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ GetShaderPaint error: {ex.Message}");
            return null;
        }
    }

}
