using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Threading;
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
    // NEW: Array for passing in e.g. Y positions for 50 bars
    public float[] ShaderParams { get; set; } = new float[50]; 
    public float[] ShaderHeights { get; set; } = new float[50]; 
    public SKColor[] ShaderColors { get; set; } = new SKColor[50]; 
    public SKColor[] ShaderColorsTo { get; set; } = new SKColor[50];
    
    public Vector4[] ShaderValues { get; set; } = new Vector4[2]; // t.ex. 8 slots
    // ShaderValues as they were at the latest WAIT VBL (the scroll position is in [0]). The screen
    // is drawn from this copy, so that it never shows one layer scrolled for the next frame and
    // another not yet. null = the program does not use WAIT VBL, draw the live values.
    public Vector4[]? PresentedShaderValues { get; set; }

    // PresentedShaderValues as they were at the latest screen refresh (see LatchShownFrame)
    public Vector4[]? ShownShaderValues { get; set; }

    // Visible (SCREEN ON/OFF) at the latest WAIT VBL, and at the latest screen refresh
    public bool? PresentedVisible { get; set; }
    public bool? ShownVisible { get; set; }

    // A copy of the pixels taken at the latest screen refresh (see LatchShownFrame). Avalonia draws
    // a frame on its render thread a little after the refresh, and by then the program may already
    // be drawing the next frame into this layer (a game that swaps two layers, like Frog Hop).
    public SKImage? ShownImage { get; set; }
    public Queue<SKImage> RetiredImages { get; } = new();

    // An old copy may still be in a frame the render thread has not drawn yet, so it is kept for a few frames
    public void RetireShownImage()
    {
        if (ShownImage != null) RetiredImages.Enqueue(ShownImage);
        ShownImage = null;
        while (RetiredImages.Count > 4) RetiredImages.Dequeue().Dispose();
    }
    
    // Shader-support
    public string? SkSlCode { get; set; }
    public SKRuntimeEffect? CachedEffect { get; set; }
    public string? CompiledSkSlCode { get; set; } // The code that CachedEffect was compiled from
}


// Draws the whole game screen: every layer is drawn at the game's own resolution (e.g. 640x480) into an
// image on the GPU, which is then scaled up to the window just once. That way shaders and
// raster bars cost the same whatever the window size (before, they ran for every pixel in the window,
// which made full screen on large screens very slow).
// The PRINT console as it looks on screen (text in a grid of Cols x Rows, INK and PAPER), for drawing it
// into the game picture when the CRT filter is on, so that the text gets the picture tube look too
public sealed record ConsoleOverlay(string Text, Color Ink, Color Paper, int Cols, int Rows);

public sealed class ScreenDrawOperation : ICustomDrawOperation
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly AmosGraphics _graphics;
    private readonly List<LayerDrawOperation> _layers;

    public ScreenDrawOperation(Rect bounds, AmosGraphics graphics, List<LayerDrawOperation> layers)
    {
        Bounds = bounds;
        _graphics = graphics;
        _layers = layers;
    }

    public Rect Bounds { get; }

    public long Frame { get; init; }   // the WAIT VBL frame this draws (for FrameLog)
    public ConsoleOverlay? Console { get; init; }   // the PRINT text, when it is drawn into the picture

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
        var gr = lease.GrContext;

        int w = (int)Bounds.Width, h = (int)Bounds.Height;
        if (w <= 0 || h <= 0) return;

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // 1. All layers at the game's resolution
            using var screen = CreateSurface(gr, w, h);
            var paper = Console?.Paper ?? Colors.Transparent;
            screen.Canvas.Clear(new SKColor(paper.R, paper.G, paper.B, paper.A));
            foreach (var layer in _layers)
                layer.DrawTo(screen.Canvas, gr);
            if (Console != null) DrawConsole(screen.Canvas, Console, w, h);
            using var image = screen.Snapshot();

            // 2. Scale up to the window. First by whole pixels to the largest integer factor
            //    (sharp, equally sized pixels), then smoothly the rest of the way ("sharp bilinear").
            var m = canvas.TotalMatrix;
            float scale = Math.Min(Math.Abs(m.ScaleX), Math.Abs(m.ScaleY));
            int factor = Math.Max(1, (int)Math.Floor(scale + 0.001f));
            var dest = new SKRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Right, (float)Bounds.Bottom);

            if (_graphics.CrtEnabled && DrawCrt(canvas, image, w, h, dest, scale))
                return;

            if (factor == 1)
            {
                canvas.DrawImage(image, new SKRect(0, 0, w, h), dest, Linear);
                return;
            }

            using var big = CreateSurface(gr, w * factor, h * factor);
            big.Canvas.Clear(SKColors.Transparent);
            big.Canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKRect(0, 0, w * factor, h * factor), Nearest);
            using var bigImage = big.Snapshot();
            canvas.DrawImage(bigImage, new SKRect(0, 0, w * factor, h * factor), dest, Linear);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Screen Render Crash: " + ex.Message);
        }
        finally
        {
            _graphics.CountRenderedFrame(System.Diagnostics.Stopwatch.GetTimestamp() - start);
            FrameLog.Write("draw", Frame, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    // The console text on top of the layers, placed as the text box places it (the same font size,
    // the font's own character width and line spacing), so the text does not move when CRT is turned on
    private static void DrawConsole(SKCanvas canvas, ConsoleOverlay console, int w, int h)
    {
        if (string.IsNullOrEmpty(console.Text) || console.Ink.A == 0) return;
        float cw = (float)w / console.Cols, ch = (float)h / console.Rows;
        var typeface = AmosGraphics.BundledFont(ConsoleFont) ?? SKTypeface.FromFamilyName(ConsoleFont) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Math.Min(cw * 1.8f, ch * 0.9f)) { Edging = SKFontEdging.Alias };
        using var paint = new SKPaint { Color = new SKColor(console.Ink.R, console.Ink.G, console.Ink.B, console.Ink.A) };
        float advance = font.MeasureText("M");
        float lineHeight = font.Spacing;
        float baseline = -font.Metrics.Ascent;
        var lines = console.Text.Replace("\r\n", "\n").Split('\n');
        for (int r = 0; r < lines.Length && r * lineHeight < h; r++)
        {
            var line = lines[r];
            for (int c = 0; c < line.Length && c * advance < w; c++)
                if (line[c] != ' ')
                    canvas.DrawText(line[c].ToString(), c * advance, r * lineHeight + baseline, font, paint);
        }
    }

    public const string ConsoleFont = "Topaz a600a1200a400";

    // ── CRT-filter ───────────────────────────────────────────────────
    // A shader that draws the game picture to the window like a picture tube screen. It runs in
    // the game's coordinates (the canvas is already scaled up), so the scanlines follow the game's rows
    // whatever the window size. It is a single pass with a little maths per screen pixel.
    private const string CrtShaderCode = @"
uniform shader image;
uniform float2 res;         // the game's resolution
uniform float scanlines;    // 0-1
uniform float curvature;    // 0-1
uniform float mask;         // 0-1, the phosphor stripes
uniform float vignette;     // 0-1, darker edges
uniform float brightness;   // 1 = as the picture is
uniform float softness;     // 0 = sharp pixels, 1 = soft as on a TV
uniform float pixelScale;   // screen pixels per game pixel

half4 main(float2 p) {
    // Curved screen: pull the coordinates out towards the edges
    float2 c = p / res * 2.0 - 1.0;
    float k = curvature * 0.12;
    c *= 1.0 + k * float2(c.y * c.y, c.x * c.x);
    float2 uv = c * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return half4(0.0, 0.0, 0.0, 1.0);

    float2 sp = uv * res;
    // Soft: the picture is sampled smoothly between its pixels; sharp: at the middle of each pixel
    half4 col = mix(image.eval(floor(sp) + 0.5), image.eval(sp), half(softness));

    // Scanlines: brightest in the middle of each row of the game picture, darker between the rows
    float fy = fract(sp.y) - 0.5;
    float line = 1.0 - scanlines * 0.9 * (fy * fy * 4.0);

    // A faint colour mask in red, green and blue, like the phosphor stripes of a picture tube
    float m = mod(floor(p.x * pixelScale), 3.0);
    float d = 1.0 - mask * 0.36;
    float3 stripes = m < 1.0 ? float3(1.0, d, d) : (m < 2.0 ? float3(d, 1.0, d) : float3(d, d, 1.0));

    // Vignette: darker towards the edges and corners
    float vig = pow(clamp(16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y), 0.0, 1.0), vignette * 0.36);

    // A little brighter overall, so that the scanlines and mask do not make the picture too dark.
    // The colours are premultiplied by alpha, so they must not become larger than alpha.
    float3 rgb = clamp(float3(col.rgb) * line * stripes * vig * brightness, 0.0, float(col.a));
    return half4(half3(rgb), col.a);
}";

    private static SKRuntimeEffect? _crtEffect;
    private static bool _crtCompileFailed;

    private bool DrawCrt(SKCanvas canvas, SKImage image, int w, int h, SKRect dest, float scale)
    {
        if (_crtEffect == null && !_crtCompileFailed)
        {
            _crtEffect = SKRuntimeEffect.CreateShader(CrtShaderCode, out var errors);
            if (_crtEffect == null || !string.IsNullOrEmpty(errors))
            {
                System.Diagnostics.Debug.WriteLine($"CRT shader error: {errors}");
                _crtEffect = null;
                _crtCompileFailed = true; // do not try every frame; ordinary scaling is used instead
            }
        }
        if (_crtEffect == null) return false;

        // Linear sampling gives softer pixels horizontally; the scanlines give the sharpness vertically
        using var imageShader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, Linear);
        var children = new SKRuntimeEffectChildren(_crtEffect) { { "image", imageShader } };
        var uniforms = new SKRuntimeEffectUniforms(_crtEffect)
        {
            { "res", new[] { (float)w, (float)h } },
            { "scanlines", Math.Clamp(_graphics.CrtScanlines, 0f, 1f) },
            { "curvature", Math.Clamp(_graphics.CrtCurvature, 0f, 1f) },
            { "mask", Math.Clamp(_graphics.CrtMask, 0f, 1f) },
            { "vignette", Math.Clamp(_graphics.CrtVignette, 0f, 1f) },
            { "brightness", Math.Clamp(_graphics.CrtBrightness, 0.3f, 2f) },
            { "softness", Math.Clamp(_graphics.CrtSoftness, 0f, 1f) },
            { "pixelScale", Math.Max(1f, scale) }
        };
        using var shader = _crtEffect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(dest, paint);
        return true;
    }

    // RGBA can be rendered to on every GPU backend (BGRA not always on OpenGL)
    internal static SKSurface CreateSurface(GRContext? gr, int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        return (gr != null ? SKSurface.Create(gr, true, info) : null) ?? SKSurface.Create(info);
    }
}

// Draws a layer: composes the bitmap + texts + sprites on a Skia surface (on the GPU when there
// is one) and then runs the layer's shader with the surface as inputTexture, into the game's screen image.
// Everything happens on the render thread, without reading anything back to the CPU.
public sealed class LayerDrawOperation
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);
    private static readonly SKSamplingOptions Sharp = new(SKFilterMode.Nearest, SKMipmapMode.None);

    // Sharp pixels when an image is drawn at a whole size (100 %, 200 % ...) and not rotated, as on the
    // Amiga; smooth otherwise. Smoothing a 200 % bitmap font blends the colours at every edge, which
    // made a thin line of half-colours that RASTER COLOUR could not recognise. So in a layer with
    // RASTER COLOUR everything is sharp, also rotated or zoomed 140 %: as with a palette colour, a
    // pixel is the key colour or it is not, never a blend of it.
    private SKSamplingOptions SamplingFor(SKCanvas c)
    {
        if (KeyedLayer) return Sharp;
        var m = c.TotalMatrix;
        static bool Whole(float v) => v >= 0.999f && Math.Abs(v - MathF.Round(v)) < 0.001f;
        bool rotated = Math.Abs(m.SkewX) > 0.0001f || Math.Abs(m.SkewY) > 0.0001f;
        return !rotated && Whole(Math.Abs(m.ScaleX)) && Whole(Math.Abs(m.ScaleY)) ? Sharp : Sampling;
    }

    private readonly AmosGraphics _graphics;
    private readonly GpuLayer _layer;

    // RASTER COLOUR is on (mode 2 in the shader's first value)
    private bool KeyedLayer
    {
        get
        {
            var values = _layer.ShownShaderValues ?? _layer.PresentedShaderValues ?? _layer.ShaderValues;
            return values.Length > 0 && Math.Abs(values[0].Z - 2f) < 0.01f;
        }
    }
    private readonly List<AmosGraphics.QueuedFontText> _texts;
    private readonly List<AmosGraphics.SpriteRenderState> _sprites;
    private GRContext? _gr; // Set during Render: the GPU context the effects run in

    private readonly SKImage? _pixels;   // the layer's pixels as they were at the screen refresh, if copied

    public LayerDrawOperation(Rect bounds, AmosGraphics graphics, GpuLayer layer,
        List<AmosGraphics.QueuedFontText> texts, List<AmosGraphics.SpriteRenderState> sprites, SKImage? pixels = null)
    {
        Bounds = bounds;
        _graphics = graphics;
        _layer = layer;
        _texts = texts;
        _sprites = sprites;
        _pixels = pixels;
    }

    public Rect Bounds { get; }

    public void DrawTo(SKCanvas canvas, GRContext? gr)
    {
        _gr = gr;

        var effect = GetOrCompileEffect();
        if (effect == null) return;

        try
        {
            using var surface = ScreenDrawOperation.CreateSurface(_gr,
                _layer.Bitmap.PixelSize.Width, _layer.Bitmap.PixelSize.Height);
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

    // Recompile when the code has changed (e.g. RASTER SPACE). Failed attempts also
    // remember the code so that a broken shader is not recompiled every frame.
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

        // Scale all premultiplied channels (alpha too) by the opacity: fade towards black/transparent
        byte a = (byte)Math.Round(Math.Clamp(_layer.Opacity, 0, 1) * 255);
        using var fade = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, a), SKBlendMode.DstIn);
        using (input)
            return input.WithColorFilter(fade);
    }

    private void DrawLayerContent(SKCanvas c)
    {
        c.Clear(SKColors.Transparent);

        // Draw layer.Bitmap (background, tiles, PLOT/LINE): the copy from the screen refresh if there is one
        if (_pixels != null)
            c.DrawImage(_pixels, 0, 0);
        else
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
            // WITH EFFECTS: the whole text as one image, rotated/zoomed around its centre
            if (qt.GetProcessedImage(_graphics, _gr, textEffects) is not var (img, owned)) return;

            var pad = qt.GetTotalPadding(textEffects);
            DrawTransformed(c, img, qt.X - pad.left, qt.Y - pad.top, img.Width, img.Height,
                qt.Angle, qt.ZoomX, qt.ZoomY, null);
            if (owned) img.Dispose();
            return;
        }

        // WITHOUT EFFECTS: character by character, the whole text rotated/zoomed around its centre
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
                SKRect.Create(i * f.CharWidth, 0, f.CharWidth, f.CharHeight), SamplingFor(c));
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
            // WITH EFFECTS: the image has padding around it, drawn at natural size
            var (img, owned) = sprite.Sprite.GetEffectImage(_gr, sprite.BaseBitmap, sprite.Effects);
            var pad = AmosGraphics.Sprite.GetTotalPadding(sprite.Effects);

            DrawTransformed(c, img, sprite.X - sprite.HandleX - pad.left, sprite.Y - sprite.HandleY - pad.top, img.Width, img.Height,
                sprite.Angle, sprite.ZoomX, sprite.ZoomY, paint,
                HasHandle(sprite) ? pad.left + sprite.HandleX : null, HasHandle(sprite) ? pad.top + sprite.HandleY : null);
            if (owned) img.Dispose();
        }
        else
        {
            // WITHOUT EFFECTS - sprite.X is the LEFT EDGE (less the handle), the image is scaled to the sprite's size
            using var img = ToSkImage(sprite.BaseBitmap);
            DrawTransformed(c, img, sprite.X - sprite.HandleX, sprite.Y - sprite.HandleY, sprite.Width, sprite.Height,
                sprite.Angle, sprite.ZoomX, sprite.ZoomY, paint,
                HasHandle(sprite) ? sprite.HandleX : img.Width / 2f, HasHandle(sprite) ? sprite.HandleY : img.Height / 2f);
        }
    }

    // SPRITE HANDLE: the point of the sprite that SPRITE POS puts at x, y (AMOS's hot spot), and the one it is
    // zoomed and turned round; without one, the top left corner is at x, y and it turns round its middle
    private static bool HasHandle(AmosGraphics.SpriteRenderState s) => s.HandleX != 0 || s.HandleY != 0;

    // Draw the image in (x, y, w, h), rotated and zoomed around the pivot (default: the middle of w/h)
    private void DrawTransformed(SKCanvas c, SKImage img, double x, double y, double w, double h,
        double angle, double zoomX, double zoomY, SKPaint? paint, float? pivotX = null, float? pivotY = null)
    {
        float cX = pivotX ?? (float)(w / 2.0);
        float cY = pivotY ?? (float)(h / 2.0);

        c.Save();
        c.Translate((float)x + cX, (float)y + cY);
        c.RotateDegrees((float)angle);
        c.Scale((float)zoomX, (float)zoomY);
        c.Translate(-cX, -cY);
        // The size the image is stretched to counts too (a sprite drawn at its own size and zoomed 200 %)
        c.Scale((float)(w / img.Width), (float)(h / img.Height));
        c.DrawImage(img, 0, 0, SamplingFor(c), paint);
        c.Restore();
    }

    // Our WriteableBitmaps are always BGRA (see AmosGraphics.CopyPixelsBgra)
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

        // The screen size, separate from the image size
        if (effect.Uniforms.Contains("iScreenResolution"))
            uniforms.Add("iScreenResolution", new[] { (float)Bounds.Width, (float)Bounds.Height });

        if (effect.Uniforms.Contains("iTime"))
            uniforms.Add("iTime", _layer.Timer);

        if (effect.Uniforms.Contains("uParams"))
        {
            var values = _layer.ShownShaderValues ?? _layer.PresentedShaderValues ?? _layer.ShaderValues;
            float[] uParamData = new float[values.Length * 4]; // each Vector4 = 4 floats
            for (int i = 0; i < values.Length; i++)
            {
                uParamData[i * 4 + 0] = values[i].X;
                uParamData[i * 4 + 1] = values[i].Y;
                uParamData[i * 4 + 2] = values[i].Z;
                uParamData[i * 4 + 3] = values[i].W;
            }

            uniforms.Add("uParams", uParamData);
        }

        // Make sure uPositions and uHeights are exactly 50 elements
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

            // Slots without a colour become transparent black (zeros)
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

    // The PRINT console in the window above this view; with the CRT filter on it is drawn into the
    // picture instead (and the window hides its text box), so the text gets the effect too
    public Func<ConsoleOverlay?>? ConsoleSource { get; set; }
    public event Action<bool>? ConsoleDrawnInPicture;

    // The views on screen; the latest one reports the screen refreshes to DisplayClock (WAIT VBL)
    private static readonly List<AmosGpuView> Shown = new();
    private TopLevel? _topLevel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        Shown.Add(this);
        _topLevel?.RequestAnimationFrame(OnRefresh);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Shown.Remove(this);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRefresh(TimeSpan _)
    {
        if (_topLevel == null) return;               // removed from the window
        if (Shown.Count > 0 && Shown[^1] == this && !DisplayClock.IsDuplicateRefresh())
        {
            // Lock in the frame this refresh shows before WAIT VBL lets the program go on
            if (Graphics != null && Graphics.LatchShownFrame()) InvalidateVisual();
            DisplayClock.Tick();
        }
        _topLevel.RequestAnimationFrame(OnRefresh);
    }
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

        // One draw call for the whole screen; the drawing itself happens on the render thread in ScreenDrawOperation
        var layers = new List<LayerDrawOperation>();
        for (int layerIndex = 0; layerIndex < layersCopy.Count; layerIndex++)
        {
            var layer = layersCopy[layerIndex];
            if (layer.Bitmap == null || !(layer.ShownVisible ?? layer.PresentedVisible ?? layer.Visible)) continue;

            layer.SkSlCode ??= RasterShaderCode;

            var texts = fontTextsCopy.Where(qt => qt.Layer == -1 || qt.Layer == layerIndex).ToList();
            var sprites = spritesCopy.Where(s => s.Layer == -1 || s.Layer == layerIndex).ToList();
            layers.Add(new LayerDrawOperation(screenRect, Graphics, layer, texts, sprites, layer.ShownImage));
        }
        long frame = Graphics.ShownFrame;
        if (FrameLog.On)
        {
            // extra = the layers drawn, as bits (layer 0 = 1, layer 1 = 2, layer 2 = 4 ...)
            int mask = 0;
            for (int i = 0; i < layersCopy.Count && i < 30; i++)
                if (layersCopy[i].ShownVisible ?? layersCopy[i].PresentedVisible ?? layersCopy[i].Visible) mask |= 1 << i;
            FrameLog.Write("paint", frame, mask);
        }
        bool consoleInPicture = Graphics.CrtEnabled && ConsoleSource != null;
        ConsoleDrawnInPicture?.Invoke(consoleInPicture);
        ctx.Custom(new ScreenDrawOperation(screenRect, Graphics, layers)
        {
            Frame = frame,
            Console = consoleInPicture ? ConsoleSource!() : null
        });
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

    // CRT filter: the game picture is shown as on an old picture tube screen when it is scaled up to
    // the window (SCREEN CRT, F8). The strengths are 0-1.
    public bool CrtEnabled { get; set; }
    public float CrtScanlines { get; set; } = 0.55f;
    public float CrtCurvature { get; set; } = 0.5f;
    public float CrtMask { get; set; } = 0.5f;
    public float CrtVignette { get; set; } = 0.5f;
    public float CrtBrightness { get; set; } = 1.22f;   // a little brighter, so the lines and mask do not darken it
    public float CrtSoftness { get; set; } = 1f;

    // The CRT settings as percentages, in the order SCREEN CRT takes them
    public float[] CrtPercentages() => new[]
    {
        CrtScanlines * 100, CrtCurvature * 100, CrtMask * 100, CrtVignette * 100, CrtBrightness * 100, CrtSoftness * 100
    };

    public void SetCrtPercentages(params float[] p)
    {
        if (p.Length > 0) CrtScanlines = Math.Clamp(p[0] / 100f, 0f, 1f);
        if (p.Length > 1) CrtCurvature = Math.Clamp(p[1] / 100f, 0f, 1f);
        if (p.Length > 2) CrtMask = Math.Clamp(p[2] / 100f, 0f, 1f);
        if (p.Length > 3) CrtVignette = Math.Clamp(p[3] / 100f, 0f, 1f);
        if (p.Length > 4) CrtBrightness = Math.Clamp(p[4] / 100f, 0.3f, 2f);
        if (p.Length > 5) CrtSoftness = Math.Clamp(p[5] / 100f, 0f, 1f);
    }
    public readonly object LockObject = new(); // The correct name for the lock
    private int _currentScreen = 0;
    private readonly System.Diagnostics.Stopwatch _refreshTimer = new();
    public double LastCpuUsage { get; private set; }

    public int CursorX { get; set; } = 0;
    public int CursorY { get; set; } = 0;
    public int TextRows { get; private set; } = 30; // Adjust to the font size
    public int TextCols { get; private set; } = 80;
    public int CharWidth { get; private set; } = 8; // T.ex. 8x16 font
    public int CharHeight { get; private set; } = 16;
    public string Style { get; private set; } = "Normal";
    private string font = "Courier New";
    private bool _fontConfigured;   // FONT SET has run: then TEXT uses that font as well
    public Color PaperColor { get; set; } = Colors.Transparent; // Background colour for text
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

    // Set the font size (call at the start)
    public void ConfigureText(int w, int h, string text)
    {
        CharWidth = w;
        CharHeight = h;
        TextCols = Width / w;
        TextRows = Height / h;
        font = text;
        _fontConfigured = true;
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

    // PRINTG WRAP: text that reaches the right edge carries on on the next row, as on a C64
    public bool TextWrap { get; set; } = true;

    public void ConsolePrint(string text, bool newLine = true)
    {
        // Split into lines to handle \n correctly
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!TextWrap || TextCols <= 0)
            {
                if (line.Length > 0) DrawStringInternal(line);
            }
            else
            {
                // As much as fits on the row, then on to the next row (which scrolls at the bottom)
                while (line.Length > 0)
                {
                    int room = TextCols - CursorX;
                    if (room <= 0) { ConsoleNewLine(); continue; }
                    var part = line.Length > room ? line[..room] : line;
                    DrawStringInternal(part);
                    line = line[part.Length..];
                    if (CursorX >= TextCols) ConsoleNewLine();
                }
            }

            // If it is the last part and newLine is false, no line break (semicolon in BASIC)
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
        // Work out the start position
        int px = CursorX * CharWidth;
        int py = CursorY * CharHeight;

        // 1. Draw the PAPER (background box) for the whole string
        if (PaperColor != Colors.Transparent)
            Bar(px, py, px + s.Length * CharWidth - 1, py + CharHeight - 1, PaperColor);

        // 2. The text, character by character in the grid
        DrawTextNow(GetActiveScreen(), px, py, s, font, CharHeight, Ink, CharWidth, CharHeight);

        // Move the cursor forward
        CursorX += s.Length;
    }

    // A helper for drawing the PAPER box (the same as Bar but takes the colour directly)
    public void Bar(int x1, int y1, int x2, int y2, Color c)
    {
        lock (LockObject)
        {
            EnsureScreen();
            Normalize(ref x1, ref y1, ref x2, ref y2);
            // Entirely outside the screen: nothing to draw (clamping it would draw a line along the edge)
            if (x2 < 0 || y2 < 0 || x1 >= Width || y1 >= Height) return;
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

                // 1. Move the memory up
                // Destination: start (row 0)
                // Source: row 'pixels'
                // Number of bytes: (totalHeight - pixels) * rowBytes
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

                // 2. Clear the bottom strip with the PAPER colour (or black/transparent)
                Color clearCol = PaperColor; // Or Colors.Transparent if you like

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

        // The effects belong to the font (not to one print) so that they survive FONT CLEAR
        public List<SpriteEffect> Effects { get; } = new();
        public int EffectsVersion;

        // Text + static effects (BLUR/COLOR) per text string, so they are not recalculated every frame.
        // Only used from the render thread (LayerDrawOperation). The images live on the GPU.
        public Dictionary<string, SKImage> StaticCache { get; } = new();
        public int StaticCacheVersion = -1;
        public GRContext? StaticCacheContext;

        public void ClearStaticCache()
        {
            foreach (var img in StaticCache.Values) img.Dispose();
            StaticCache.Clear();
        }
    }

    // FONT EFFECT ADD: if the font already has the effect type it is updated instead (Time is kept),
    // so that ADD inside a loop does not stack up new effects every frame.
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
                    // WAVE is recalculated every frame, no cache to invalidate
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

    // NEW: Shared image bank (like sprites but with support for frames)
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
            using var b = OpenBitmap(fullPath);
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
            using var b = OpenBitmap(fullPath);
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
    private const int FIRST_BAR_SLOT = 1; // Slot 0 is reserved for the background

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

    // Find the first free run of slots (bars may have been removed in the middle)
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

        // Free the old bar first so that its slots can be reused
        if (bars.Remove(basicBarId, out var existing))
            ClearBarShaderSlots(layerIdx, existing);

        int firstSlot = FindFreeSlots(bars, neededSlots);
        if (firstSlot < 0)
        {
            OnError?.Invoke($"[RASTER] Not enough shader slots! Need {neededSlots}. " +
                            $"(Total used: {CountUsedSlots(bars)}/{WRAP_OFFSET})");
            if (existing != null)
            {
                // Restore the old bar
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

    // The wrap copy is WRAP_OFFSET slots after the original, one screen height away
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

    // RASTER COLOUR: the layer's rasters only show where its pixels have this colour (mode 2 in the
    // shader). tolerance is how far off a colour may be, 0 – 1. RASTER GFXMODE layer, 0 turns it off.
    public void SetRasterKeyColour(int layerIdx, Color colour, float tolerance)
    {
        lock (LockObject)
        {
            var frame = DrawingFrame;
            if (layerIdx < 0 || layerIdx >= frame.Count) return;
            var layer = frame[layerIdx];
            var v0 = layer.ShaderValues[0];
            layer.ShaderValues[0] = new Vector4(v0.X, v0.Y, 2f, v0.W);
            var v1 = layer.ShaderValues[1];
            layer.ShaderValues[1] = new Vector4(v1.X, v1.Y, (colour.R << 16) | (colour.G << 8) | colour.B, Math.Clamp(tolerance, 0f, 1f));
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
        public int ImageBankId { get; set; } = -1; // -1 = uses its own Frames
        public HashSet<string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SpriteEffect> Effects { get; } = new(); // ✅ Not AmosGraphics.SpriteEffect

        private int _effectsVersion = 0;
        // Cache for static effects (BLUR/COLOR). Owned by the render thread; it lives on the GPU
        // and therefore belongs to the GRContext it was created in.
        private SKImage? _staticEffectsImage;
        private WriteableBitmap? _staticEffectsSource;   // The frame the cache is built from
        private GRContext? _staticEffectsContext;
        private int _staticEffectsVersion = -1;
    
        // ✅ Work out the total padding from all effects
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

        // The current frame without effects (from the image bank or the sprite's own frames)
        public WriteableBitmap? GetBaseBitmap(Dictionary<int, ImageBankEntry> imageBank)
        {
            if (ImageBankId >= 0 && imageBank.TryGetValue(ImageBankId, out var entry) && entry.Frames.Count > 0)
                return entry.Frames[Math.Clamp(CurrentFrame, 0, entry.Frames.Count - 1)];

            return Frames.Count > 0 ? Frames[Math.Clamp(CurrentFrame, 0, Frames.Count - 1)] : null;
        }

        // Runs the effect chain on the GPU. Called ONLY from the render thread (LayerDrawOperation), which owns the cache.
        // Owned = true means the caller must Dispose the image; false that it lives in the cache.
        public (SKImage Image, bool Owned) GetEffectImage(GRContext? gr, WriteableBitmap baseBitmap,
            List<SpriteEffect> allEffects)
        {
            // Rotation/zoom is NOT done here - LayerDrawOperation takes care of it with a transform
            var staticEffects = allEffects.Where(e => !e.IsDynamic).ToList();
            var dynamicEffects = allEffects.Where(e => e.IsDynamic).ToList();

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

            // Dynamic effects (WAVE) run every frame
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
        public float Alpha { get; set; } = 1.0f; // 0.0–1.0 (1.0 = fully opaque)
        public float FadeTarget { get; set; } = -1f; // -1 = no active fade
        public float FadeStep { get; set; } = 0f; // delta per frame

    }

    public void SpriteImage(int spriteId, int imageBankId, int frameId = 0)
    {
        var s = GetSprite(spriteId);
        s.ImageBankId = imageBankId;
        s.CurrentFrame = frameId;

        // Get the bitmap from the image bank
        var bmp = s.GetBaseBitmap(GetImageBank());
        if (bmp != null)
        {
            // Update the width/height if they have not been set by hand
            s.Width = (int)bmp.Size.Width;
            s.Height = (int)bmp.Size.Height;
        }

    }

    private readonly Dictionary<int, Sprite> _sprites = new();

    // Everything Render needs to know about a sprite, copied at one and the same moment
    public sealed record SpriteRenderState(
        Sprite Sprite, WriteableBitmap BaseBitmap, List<SpriteEffect> Effects,
        int X, int Y, int Width, int Height,
        double Angle, double ZoomX, double ZoomY, float Alpha, int Layer, int HandleX = 0, int HandleY = 0);

    // A snapshot from the latest WAIT VBL, made on the runner thread so that Render never
    // reads a sprite that is being changed. null = the program does not use WAIT VBL.
    private List<SpriteRenderState>? _presentedSprites;

    // The caller must hold LockObject
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
                s.Angle, s.ZoomX, s.ZoomY, s.Alpha, s.Layer, s.HandleX, s.HandleY));
        }

        return states;
    }

    public List<SpriteRenderState> GetSpriteRenderStates()
    {
        lock (LockObject)
        {
            return _shownSprites ?? _presentedSprites ?? BuildSpriteStates();
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
    
        // Effects (BLUR/WAVE/COLOR) that apply to the font right now
        internal List<SpriteEffect> GetEffects(AmosGraphics graphics)
        {
            var font = graphics.GetFont(FontId);
            if (font == null) return new List<SpriteEffect>();
            lock (graphics.LockObject)
                return font.Effects.Where(e => e.RequiresGpuContext).ToList();
        }

        // ✅ Work out the total padding from all effects
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
    
        // Render the text with effects on the GPU. Only called from the render thread.
        // Owned = true means the caller must Dispose the image; false that it lives in the font's cache.
        public (SKImage Image, bool Owned)? GetProcessedImage(AmosGraphics graphics, GRContext? gr,
            List<SpriteEffect> effects)
        {
            var font = graphics.GetFont(FontId);
            if (font == null) return null;

            var staticEffects = effects.Where(e => !e.IsDynamic).ToList();
            var dynamicEffects = effects.Where(e => e.IsDynamic).ToList();

            // The cache is cleared when the effects change, when the GPU context changes,
            // or if the text changes often (e.g. a score counter)
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

            // Apply dynamic effects (WAVE) every frame
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
            // Work out the size of the whole text
            int baseWidth = Text.Length * font.CharWidth;
            int baseHeight = font.CharHeight;
        
            if (baseWidth <= 0 || baseHeight <= 0) return null;
        
            // Create a base bitmap with the text
            var baseBitmap = new WriteableBitmap(
                new PixelSize(baseWidth, baseHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        
            // Draw the text into the bitmap
            using (var fb = baseBitmap.Lock())
            {
                unsafe
                {
                    uint* ptr = (uint*)fb.Address;
                    int stride = fb.RowBytes / 4;
                
                    // Clear to transparent
                    for (int i = 0; i < baseWidth * baseHeight; i++)
                        ptr[i] = 0;
                
                    // Draw each character
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

    // A snapshot from the latest WAIT VBL. Without it the screen can be drawn in the middle of a
    // FONT CLEAR / FONT PRINT / FONT EFFECT ADD lap and show the text without effects.
    // null = the program does not use WAIT VBL, draw the live list.
    private List<QueuedFontText>? _presentedFontTexts;

    // Counts the frames presented by WAIT VBL (for FrameLog)
    public long PresentedFrame { get; private set; }

    // The frame the screen shows: at every screen refresh, before WAIT VBL is let go for the next
    // frame, the latest presented frame is locked in here. Without this the screen would be drawn
    // in a race with the BASIC thread: when the program is quick it sometimes presents the next
    // frame before the refresh is drawn and sometimes after, so the picture now and then stood
    // still for a refresh and then jumped two frames. null = no game window refreshes (tests).
    private List<QueuedFontText>? _shownFontTexts;
    private List<SpriteRenderState>? _shownSprites;
    public long ShownFrame { get; private set; }

    // Called by the game window at each screen refresh; true when there is a new frame to draw
    public bool LatchShownFrame()
    {
        lock (LockObject)
        {
            if (_presentedSprites == null && _presentedFontTexts == null) return false;   // no WAIT VBL yet
            bool changed = ShownFrame != PresentedFrame;
            _shownFontTexts = _presentedFontTexts;
            _shownSprites = _presentedSprites;
            foreach (var layer in _layers)
            {
                layer.ShownShaderValues = layer.PresentedShaderValues;
                layer.ShownVisible = layer.PresentedVisible;
                // The program is waiting at WAIT VBL now, so the pixels are a whole frame: copy them
                if (changed)
                {
                    layer.RetireShownImage();
                    if (layer.ShownVisible == true && layer.Bitmap != null)
                        layer.ShownImage = LayerDrawOperation.ToSkImage(layer.Bitmap);
                }
            }
            ShownFrame = PresentedFrame;
            return changed;
        }
    }

    private void ClearShownFrame()
    {
        _shownFontTexts = null;
        _shownSprites = null;
        foreach (var layer in _layers) { layer.ShownShaderValues = null; layer.ShownVisible = null; layer.RetireShownImage(); }
    }

    // Called at WAIT VBL: lock in what should be shown for this frame
    public void PresentFrame()
    {
        lock (LockObject)
        {
            PresentedFrame++;
            _presentedFontTexts = _fontTexts.ToList();
            _presentedSprites = BuildSpriteStates();
            foreach (var layer in _layers)
            {
                layer.PresentedShaderValues = (Vector4[])layer.ShaderValues.Clone();
                layer.PresentedVisible = layer.Visible;
            }
        }
    }

    public IEnumerable<QueuedFontText> GetQueuedTexts()
    {
        lock (LockObject)
        {
            return (_shownFontTexts ?? _presentedFontTexts ?? _fontTexts).ToList(); // ✅ Return a copy
        }
    }


    // ✅ NEW: Methods for clearing everything
    public void ClearAllResources()
    {
        lock (LockObject)
        {
            _sprites.Clear();
            _groups.Clear();
            _presentedSprites = null;
            ClearShownFrame();
            _imageBank.Clear();
            _tiles.Clear();
            _tilesInWidth = 0;
            _tileWidth = 32;
            _tileHeight = 32;
            _map = new int[0, 0];
            _fonts.Clear();
            _fontTexts.Clear();
            _presentedFontTexts = null;
            foreach (var layer in _layers) { layer.PresentedShaderValues = null; layer.PresentedVisible = null; }
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
            ClearShownFrame();
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
            // ✅ Clear the tiles first so that DrawMap can detect an empty list
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
            // ✅ Empty the map array safely
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

        // We make sure _currentScreen points to an existing layer
        int index = (_currentScreen >= 0 && _currentScreen < DrawingFrame.Count)
            ? _currentScreen
            : 0;

        // Info.txt: "All drawing operations always happen on the inactive frame"
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
            // We set the flag on both A and B so that it lasts even after SwapBuffers
            if (layerIdx >= 0 && layerIdx < _layers.Count) _layers[layerIdx].Visible = visible;
        }
    }

    public void SetShadervalues(int layerIdx, int slot, float nr, float value)
    {
        lock (LockObject)
        {

            // Single buffer: use DrawingFrame as usual
            var frame = DrawingFrame;
            if (layerIdx >= 0 && layerIdx < frame.Count)
            {
                var layer = frame[layerIdx];
                if (slot >= 0 && slot < 2)
                {
                    // z and w of slot 1 hold RASTER COLOUR's key; keep them
                    var old = layer.ShaderValues[slot];
                    layer.ShaderValues[slot] = slot == 1 ? new Vector4(nr, value, old.Z, old.W) : new Vector4(nr, value, 0f, 0f);
                }
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

                // ✅ SAFETY CHECK: Upgrade the arrays if they are too small
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

                // ✅ SAFETY CHECK: Upgrade the arrays if they are too small
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
    /// ✅ HELPER: Upgrades old 22-slot arrays to 50 slots
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
        // CPU time as a percentage of the VBL
        LastCpuUsagePercent = Math.Min(100, (_vblTimer.Elapsed.TotalMilliseconds / targetVblMs) * 100);

        Interlocked.Add(ref _statBasicTicks, _vblTimer.ElapsedTicks);
        Interlocked.Increment(ref _statVblFrames);
    }

    // ── Measurements for the window title ──────────────────────────────
    // Collected continuously and read (and reset) with TakeFrameStats.

    private long _statBasicTicks;     // Stopwatch time the BASIC code has run between WAIT VBLs
    private int _statVblFrames;       // Antal WAIT VBL
    private int _statRenderedFrames;  // Frames drawn (render thread)
    private long _statRenderTicks;    // Time spent drawing them
    private long _statLastTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    private TimeSpan _statLastCpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
    private readonly System.Diagnostics.Process _process = System.Diagnostics.Process.GetCurrentProcess();

    public readonly record struct FrameStats(
        double Fps,            // frames drawn per second
        double BasicPercent,   // share of the time the BASIC code has been working (-1 = no WAIT VBLs to measure between)
        double RenderMs,       // average time to draw a frame on the render thread
        double CpuPercent);    // the whole app's CPU time, 100 % = one core (like Activity Monitor)

    // Called by ScreenDrawOperation for every frame drawn
    public void CountRenderedFrame(long elapsedTicks)
    {
        Interlocked.Increment(ref _statRenderedFrames);
        Interlocked.Add(ref _statRenderTicks, elapsedTicks);
    }

    // Average since the previous call
    public FrameStats TakeFrameStats()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long elapsed = Math.Max(1, now - _statLastTimestamp);
        _statLastTimestamp = now;
        double seconds = (double)elapsed / System.Diagnostics.Stopwatch.Frequency;

        int rendered = Interlocked.Exchange(ref _statRenderedFrames, 0);
        long renderTicks = Interlocked.Exchange(ref _statRenderTicks, 0);
        int vblFrames = Interlocked.Exchange(ref _statVblFrames, 0);
        long basicTicks = Interlocked.Exchange(ref _statBasicTicks, 0);

        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        double cpuPercent = (cpu - _statLastCpu).TotalSeconds / seconds * 100;
        _statLastCpu = cpu;

        return new FrameStats(
            rendered / seconds,
            vblFrames > 0 ? (double)basicTicks / elapsed * 100 : -1,
            rendered > 0 ? (double)renderTicks / rendered * 1000 / System.Diagnostics.Stopwatch.Frequency : 0,
            cpuPercent);
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

            // Force the correct size on all arrays
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

        // Force the UI thread to update the size of the view
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // This forces the Viewbox to recalculate the scaling
            // We do it via a call to InvalidateMeasure in MainWindow
            OnScreenResolutionChanged?.Invoke(w, h);
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    public event Action<int, int>? OnScreenResolutionChanged;

    public void SetDrawingScreen(int id)
    {
        lock (LockObject)
        {
            // We must make sure the layer exists in BOTH lists
            while (ActiveFrame.Count <= id)
            {
                var layer = new GpuLayer
                {
                    Bitmap = CreateEmptyBitmap(Width > 0 ? Width : 640, Height > 0 ? Height : 480),
                    Offset = new Point(0, 0), Opacity = 1.0
                };
                // Initialise ShaderHeights to 0 so that the layer is transparent by default
                for (int i = 0; i < 22; i++) layer.ShaderHeights[i] = 0;
                ActiveFrame.Add(layer);
            }

            _currentScreen = id;
        }
    }

    private void EnsureScreen()
    {
        // We check whether the lists are empty instead of whether they are null
        if (_layers.Count == 0 || _layers.Count == 0)
        {
            lock (LockObject)
            {
                // If they are empty, initialise the default size (e.g. 640x480)
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

        // If no background is given: fully transparent
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
        // Only clear the current active screen/layer
        ClearBitmap(GetActiveScreen(), color);
    }

    public void ClearAll(Color color)
    {
        EnsureScreen();
        // Clear all layers
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
            // 2. Reset the shader parameters for the layer we just cleared
            // We do this for BOTH the active and inactive frame so that no old data is left
            foreach (var frame in new[] { ActiveFrame, ActiveFrame })
            {
                foreach (var layer in ActiveFrame)
                {
                    // Reset the heights so that no rasters/rainbows are drawn
                    for (int i = 0; i < 22; i++) layer.ShaderHeights[i] = 0;
                    // Reset the weather/scroll parameters
                    for (int i = 0; i < layer.ShaderValues.Length; i++) layer.ShaderValues[i] = Vector4.Zero;
                    layer.PresentedShaderValues = null;
                    layer.ShownShaderValues = null;
                }
            }

            // 3. If it is the main screen, clear the system lists too
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

                if ((val & 0xFFFFFF00) == 0) // Alpha, red and green = 0 give transparent (as before)
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
            // Entirely outside the screen: nothing to draw (clamping it would draw a line along the edge)
            if (x2 < 0 || y2 < 0 || x1 >= Width || y1 >= Height) return;
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

            // Convert Ink to uint (BGRA)
            uint cVal = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                // Local function for safe pixel setting
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
                long rx = r1, ry = r2; // Use long to avoid overflow when squaring
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

                // Iterate through the height (Y axis)
                for (int y = -r2; y <= r2; y++)
                {
                    // Work out the width at this Y coordinate from the ellipse equation
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

            // FIX: Create the colour in BGRA format
            uint fillColor = (uint)((Ink.A << 24) | (Ink.R << 16) | (Ink.G << 8) | Ink.B);

            unsafe
            {
                uint* ptr = (uint*)fb.Address;
                int stride = fb.RowBytes / 4;

                // Now both targetColor and fillColor are in the same format (BGRA)
                uint targetColor = ptr[y1 * stride + x1];

                if (targetColor == fillColor) return;

                // Scanline Flood Fill Algorithm (Stack-baserad)
                Stack<(int x, int y)> stack = new();
                stack.Push((x1, y1));

                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    int offset = cy * stride + cx;

                    // Move left as far as possible
                    int lx = cx;
                    while (lx >= 0 && ptr[cy * stride + lx] == targetColor)
                    {
                        lx--;
                    }

                    lx++; // Go back one step to the last valid pixel

                    // Move right and fill, and scan the rows above and below
                    bool spanAbove = false;
                    bool spanBelow = false;

                    int rx = lx;
                    while (rx < w && ptr[cy * stride + rx] == targetColor)
                    {
                        // Fill the pixel
                        ptr[cy * stride + rx] = fillColor;

                        // Check the row above
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

                        // Check the row below
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
        // Without FONT SET: Arial 20, as TEXT has always had
        if (_fontConfigured) DrawTextNow(GetActiveScreen(), x, y, t, font, CharHeight, Ink, 0, 0);
        else DrawTextNow(GetActiveScreen(), x, y, t, "Arial", 20, Ink, 0, 0);
    }

    // The fonts in the fonts folder that comes with the app, by family name. They are used before
    // the computer's own fonts, so that FONT SET "C64 PRO MONO" works without installing anything.
    private static Dictionary<string, SKTypeface>? _bundledFonts;
    private static readonly object BundledFontsLock = new();

    // Short names for the fonts whose real family names are long
    private static readonly Dictionary<string, string> FontAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Topaz"] = "Topaz a600a1200a400",
        ["Topaz A1200"] = "Topaz a600a1200a400",
        ["Topaz A500"] = "Topaz a500a1000a2000",
        ["TopazPlus"] = "TopazPlus a500a1000a2000",
    };

    internal static SKTypeface? BundledFont(string family)
    {
        lock (BundledFontsLock)
        {
            if (_bundledFonts == null)
            {
                _bundledFonts = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "fonts"), Path.Combine(Environment.CurrentDirectory, "fonts") })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, "*.ttf").Concat(Directory.GetFiles(dir, "*.otf")))
                    {
                        var tf = SKTypeface.FromFile(file);
                        if (tf != null) _bundledFonts.TryAdd(tf.FamilyName, tf);
                    }
                }
            }
            string name = family.Trim();
            if (FontAliases.TryGetValue(name, out var real)) name = real;
            return _bundledFonts.TryGetValue(name, out var found) ? found : null;
        }
    }

    // Draws text straight into the layer's bitmap with SkiaSharp, in the calling thread. Then the text lands
    // in the right layer and in the right order with everything else that is drawn, even when the program swaps layers
    // every frame. With cellWidth > 0 every character is drawn in a cell of its own and without soft edges,
    // as in PRINTG, so that pixel fonts such as the C64 font are sharp.
    private void DrawTextNow(WriteableBitmap target, int x, int y, string text, string family, float size,
        Color ink, int cellWidth, int cellHeight)
    {
        if (string.IsNullOrEmpty(text) || size <= 0) return;
        bool italic = Style?.Contains("Italic", StringComparison.OrdinalIgnoreCase) == true;
        bool bold = Style?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;
        var typeface = BundledFont(family)
                       ?? SKTypeface.FromFamilyName(family,
                           bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                           SKFontStyleWidth.Normal,
                           italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                       ?? SKTypeface.Default;
        bool grid = cellWidth > 0;
        using var skFont = new SKFont(typeface, size) { Edging = grid ? SKFontEdging.Alias : SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = new SKColor(ink.R, ink.G, ink.B, ink.A), IsAntialias = !grid };
        float baseline = y - skFont.Metrics.Ascent;

        lock (LockObject)
        {
            using var fb = target.Lock();
            var info = new SKImageInfo(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
            if (surface == null) return;
            var canvas = surface.Canvas;
            if (cellWidth <= 0)
            {
                canvas.DrawText(text, x, baseline, skFont, paint);
                return;
            }
            for (int i = 0; i < text.Length; i++)
            {
                float cx = x + i * cellWidth;
                canvas.Save();
                canvas.ClipRect(new SKRect(cx, y, cx + cellWidth, y + cellHeight));
                canvas.DrawText(text[i].ToString(), cx, baseline, skFont, paint);
                canvas.Restore();
            }
        }
    }

    public void FontLoad(int id, string file, int tw, int th)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(file);

            using var b = OpenBitmap(fullPath);
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
                        // 1. COPY PIXELS FROM THE FONT ATLAS
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
                Layer = _currentScreen // ← inherit the active layer automatically
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
                Text = c[0].ToString(), // A single character as a string
                Angle = f.Angle,
                ZoomX = f.ZoomX,
                ZoomY = f.ZoomY,
                Layer = _currentScreen
            });
        }
    }

    // -------------------------------------------------------------------------------
    //  HELPER FOR COLOUR CORRECTION (MAC vs PC)
    // -------------------------------------------------------------------------------
    // All our WriteableBitmaps store pixels as BGRA (premultiplied alpha), just as their
    // PixelFormat says. Images from disk and RenderTargetBitmap have the platform's order
    // (RGBA on macOS, BGRA on Windows), so they are converted when they are copied in.
    // An image file as a bitmap whose pixels can be copied. A grey PNG (or one with 16 bits a colour)
    // cannot be copied as it is ("CopyPixels is not supported for this bitmap type"), so it is made a
    // colour image first.
    public static Bitmap OpenBitmap(string path)
    {
        if (!NeedsColour(path)) return new Bitmap(path);
        using var grey = SkiaSharp.SKBitmap.Decode(path) ?? throw new InvalidDataException("not an image");
        using var colour = grey.Copy(SkiaSharp.SKColorType.Bgra8888) ?? throw new InvalidDataException("the image could not be converted");
        using var data = colour.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    private static bool NeedsColour(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[26];
            if (f.Read(head, 0, 26) < 26 || head[0] != 0x89 || head[1] != (byte)'P') return false;
            int depth = head[24], type = head[25];
            return type == 0 || type == 4 || depth == 16;   // grey, grey with alpha, 16 bits
        }
        catch (IOException) { return false; }
    }

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

            // Pick out the colour channels (BGRA format in the WriteableBitmap)
            uint b = (pixel >> 16) & 0xFF;
            uint g = (pixel >> 8) & 0xFF;
            uint r = pixel & 0xFF;

            // Make black (0,0,0) transparent
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

            using var b = OpenBitmap(fullPath);
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
    public int GetTilesInWidth() => _tilesInWidth; // NEW: Getter
    public (int width, int height) GetTileSize() => (_tileWidth, _tileHeight);

    // The tile at a point on the map in dots, as TILE(layer, x, y) reads it; -1 outside the map
    public int TileAtPoint(int x, int y) => GetMapTile(x / Math.Max(1, _tileWidth), y / Math.Max(1, _tileHeight));

    public void LoadTileBank(string f, int tw, int th)
    {
        try
        {
            string fullPath = ResourceLoader.GetPath(f);

            using var b = OpenBitmap(fullPath);
            _tileWidth = tw;
            _tileHeight = th;
            _tiles.Clear();

            // Update the class variable to avoid division by zero in the palette
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

            // Declare tilesInWidth here and keep it in the class variable _tilesInWidth
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
                        // Copy exactly that cell from the source image
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

        // Update the bitmap in the current GPU layer instead of in the old _screens list
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
                CachedEffect = null, // Forced to recompile for new dimensions
                ShaderParams = (float[])layerI.ShaderParams.Clone(),
                ShaderHeights = (float[])layerI.ShaderHeights.Clone(),
                ShaderColors = (SKColor[])layerI.ShaderColors.Clone(),
                ShaderColorsTo = (SKColor[])layerI.ShaderColorsTo.Clone(),
                ShaderValues = (Vector4[])layerI.ShaderValues.Clone()
            };
        }

        // Initialise the map with -1 (empty) instead of 0 (the first tile)
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
            _map[x, y] = -1;

        // Copy over the old data if there was any
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
        lock (LockObject) // ✅ ADD THIS
        {
            if (_map.GetLength(0) == 0 || _tiles.Count == 0) return;

            // Get the layer we are going to draw on
            var target = GetActiveScreen();
            int targetW = target.PixelSize.Width;
            int targetH = target.PixelSize.Height;

            for (int y = 0; y < _map.GetLength(1); y++)
            {
                for (int x = 0; x < _map.GetLength(0); x++)
                {
                    int tid = _map[x, y];
                    if (tid < 0 || tid >= _tiles.Count) continue;

                    // Work out the coordinates in the big layer
                    int dx = x * _tileWidth - ox;
                    int dy = y * _tileHeight - oy;

                    // IMPORTANT: Only draw if we are inside the layer's bounds
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
        if (t == null) return; // ✅ ADD A NULL CHECK

        var target = GetActiveScreen();
        if (target == null) return; // ✅ EXTRA SAFETY

        using var dst = target.Lock();
        using var src = t.Lock();

        unsafe
        {
            var dp = (byte*)dst.Address;
            var sp = (byte*)src.Address;
            // IMPORTANT: Use target.PixelSize instead of the global Width/Height
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

                    // Check whether the source pixel (the tile) actually has any alpha (is not fully transparent)
                    if (sr[si + 3] > 0)
                    {
                        dr[di + 0] = sr[si + 0];
                        dr[di + 1] = sr[si + 1];
                        dr[di + 2] = sr[si + 2];
                        dr[di + 3] = sr[si + 3]; // Use the source's alpha instead of a hard-coded 255
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
            using var b = OpenBitmap(fullPath);
            int w = (int)b.Size.Width, h = (int)b.Size.Height;
            CreateSprite(id, w, h);
            var s = GetSprite(id);
            using (var fb = s.Bitmap.Lock())
            {
                CopyPixelsBgra(b, new PixelRect(0, 0, w, h), fb);

                unsafe
                {
                    // Fix the colours first
                    FixupImageColors(fb.Address.ToPointer(), w * h);

                    // Then read the transparent key from the fixed data (the first pixel)
                    var p = (byte*)fb.Address;
                    // Skia/WriteableBitmap is BGRA (little-endian uint: B, G, R, A)
                    // So p[0]=B, p[1]=G, p[2]=R, p[3]=A
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

            using var sourceInfo = OpenBitmap(fullPath);
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

    // NEW method with smart file name parsing
    public void LoadSpriteSheetAuto(int id, string fileName, int? width = null, int? height = null,
        int? frameCount = null)
    {
        try
        {
            // Parse from the file name if parameters are missing
            string fileNameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);

            int w = width ?? ParseSpriteSheetParam(fileNameOnly, "W", 32);
            int h = height ?? ParseSpriteSheetParam(fileNameOnly, "H", 32);
            int count = frameCount ?? ParseSpriteSheetParam(fileNameOnly, "[BF]", 8);

            // Call the ordinary method
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
        using var b = OpenBitmap(ResourceLoader.GetPath(file));
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

    /// <summary>Sets the alpha immediately (0–255). Cancels any fade in progress.</summary>
    public void SpriteAlpha(int id, int alpha255)
    {
        var s = GetSprite(id);
        s.Alpha = Math.Clamp(alpha255 / 255f, 0f, 1f);
        s.FadeTarget = -1f;
        s.FadeStep = 0f;
    }

    /// <summary>Starts an automatic fade towards targetAlpha255 over 'frames' frames.</summary>
    public void StartSpriteFade(int id, int targetAlpha255, int frames)
    {
        var s = GetSprite(id);
        float target = Math.Clamp(targetAlpha255 / 255f, 0f, 1f);

        if (frames <= 0 || Math.Abs(target - s.Alpha) < 0.001f)
        {
            // Set immediately if 0 frames or already at the target
            s.Alpha = target;
            s.FadeTarget = -1f;
            s.FadeStep = 0f;
            if (target <= 0f) s.Visible = false;
            return;
        }

        s.FadeTarget = target;
        s.FadeStep = (target - s.Alpha) / frames;

        // If we fade in from invisible: turn the sprite on straight away
        if (target > 0f && !s.Visible)
            s.Visible = true;
    }

    /// <summary>Returns true if the sprite has a fade in progress.</summary>
    public bool IsSpriteAFading(int id) =>
        _sprites.TryGetValue(id, out var s) && s.FadeStep != 0f;

    /// <summary>
    /// Called by WAIT VBL once per frame.
    /// Steps all active sprite fades forward, with auto-hide on a fade out to 0.
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
                        sp.Visible = false; // auto-hide on a fade out to 0
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
        
            // The same for font effects (once per font, however many texts have been written)
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

    /// <summary>Sets the opacity of a layer immediately (0–255).</summary>
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

    /// <summary>Starts a fade on a whole layer.</summary>
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

    /// <summary>Returns true if the layer has a fade in progress.</summary>
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

    // ── HITBOX - rectangle against rectangle ────────────────────

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

// ── HITCIRCLE - circle against circle ───────────────────────

    public bool SpriteHitCircle(int id1, int id2)
    {
        if (!_sprites.TryGetValue(id1, out var s1) ||
            !_sprites.TryGetValue(id2, out var s2)) return false;
        if (!s1.Visible || !s2.Visible) return false;

        // Centre and radius of each sprite
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

// ── Group collisions ─────────────────────────────────────────

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
                    $"[SpriteHitGroup] error in SpriteHit({id},{otherId}): {ex.Message}\n{ex.StackTrace}");
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
                System.Diagnostics.Debug.WriteLine($"[HitBoxGroup] sprite {otherId} is not in _sprites!");
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
// Runs sprite/font effects as shaders. With a GRContext everything happens on the GPU and the result
// stays there as a texture; without one (headless) a CPU surface is used.
// BUMP: the image lit by a lamp, its brightness taken as height (bitmap bump mapping, as in the demos of
// the 1990s): where it gets brighter it slopes, and a slope turned towards the lamp is lit and shines.
// The lamp is at LightX, LightY in the image's own pixels (on a 640 x 480 picture at 0, 0, the screen's),
// lights to Radius pixels round it, and Depth is how steep the slopes are.
public class BumpEffect : SpriteEffect
{
    public float LightX { get; set; }
    public float LightY { get; set; }
    public float Depth { get; set; } = 6f;
    public float Radius { get; set; } = 320f;

    private static SKRuntimeEffect? _effect;
    private static readonly object _lock = new();

    public override bool RequiresGpuContext => true;
    public override bool IsDynamic => true;

    private const string ShaderCode = @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float2 uLight;
uniform float uDepth;
uniform float uRadius;

float lum(float2 p) {
    half4 c = inputTexture.eval(clamp(p, float2(0.5), iResolution - 0.5));
    return dot(float3(c.rgb), float3(0.299, 0.587, 0.114));
}

// the height a little smoothed, so that single pixels do not make grain
float height(float2 p) {
    return (lum(p) * 2.0 + lum(p + float2(1, 0)) + lum(p - float2(1, 0)) + lum(p + float2(0, 1)) + lum(p - float2(0, 1))) / 6.0;
}

half4 main(float2 p) {
    half4 c = inputTexture.eval(p);
    float dx = height(p + float2(1, 0)) - height(p - float2(1, 0));
    float dy = height(p + float2(0, 1)) - height(p - float2(0, 1));
    float3 n = normalize(float3(-dx * uDepth, -dy * uDepth, 1.0));
    float3 toLight = float3(uLight - p, uRadius * 0.3);
    float3 l = normalize(toLight);
    float fall = clamp(1.0 - length(toLight.xy) / uRadius, 0.0, 1.0);
    float diffuse = max(dot(n, l), 0.0);
    float shine = pow(max(dot(n, normalize(l + float3(0, 0, 1))), 0.0), 24.0);
    float a = float(c.a);
    float3 rgb = float3(c.rgb) * (0.12 + diffuse * fall * 1.1) + shine * fall * a * 0.7;
    return half4(half3(clamp(rgb, 0.0, a)), c.a);
}";

    public override SKPaint? GetShaderPaint(SKImage inputImage)
    {
        lock (_lock)
        {
            if (_effect == null)
            {
                _effect = SKRuntimeEffect.CreateShader(ShaderCode, out var errors);
                if (_effect == null || !string.IsNullOrEmpty(errors))
                {
                    System.Diagnostics.Debug.WriteLine($"Bump shader error: {errors}");
                    _effect = null;
                    return null;
                }
            }
        }
        var children = new SKRuntimeEffectChildren(_effect);
        children.Add("inputTexture", inputImage.ToShader());
        var uniforms = new SKRuntimeEffectUniforms(_effect);
        uniforms.Add("iResolution", new[] { (float)inputImage.Width, (float)inputImage.Height });
        uniforms.Add("uLight", new[] { LightX, LightY });
        uniforms.Add("uDepth", Depth);
        uniforms.Add("uRadius", Math.Max(1f, Radius));
        return new SKPaint { Shader = _effect.ToShader(uniforms, children) };
    }
}

public static class ShaderHelper
{
    // Runs one effect and returns a new image owned by the caller, or null if the effect failed.
    // The image becomes larger than the input according to the effect's padding.
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

    // Runs the effects in order. Takes ownership of the input and returns an image the caller owns.
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

// ADD THESE AT THE END OF AmosGraphics.cs (before the last })

public abstract class SpriteEffect
{
    public virtual SKPaint? GetShaderPaint(SKImage inputImage) => null;

    public virtual bool RequiresGpuContext => false;

    // Run every frame (WAVE moves, BUMP's light moves) instead of once and kept
    public virtual bool IsDynamic => false;
    
    // ✅ NEW: How much the effect enlarges the image (for correct positioning)
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
    public float PhaseX { get; set; } = 0f;  // ✅ NEW: Phase offset for X
    public float PhaseY { get; set; } = 0f;  // ✅ NEW: Phase offset for Y
    public float Time { get; set; } = 0f;
    
    private static SKRuntimeEffect? _globalCachedEffect;
    private static readonly object _shaderLock = new object();

    public override bool RequiresGpuContext => true;
    public override bool IsDynamic => true;
    
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
            uniforms.Add("uPhaseX", PhaseX);  // ✅ NEW
            uniforms.Add("uPhaseY", PhaseY);  // ✅ NEW
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
