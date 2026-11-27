using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace AmosLikeBasic.Tests;

public class GraphicsTests
{
    private static unsafe uint PixelAt(AmosGraphics g, int x, int y)
    {
        using var fb = g.GetActiveScreen().Lock();
        return ((uint*)((byte*)fb.Address + y * fb.RowBytes))[x];
    }

    // A BAR entirely outside the screen used to be clamped onto the edge and drew a line there
    [AvaloniaTheory]
    [InlineData(660, 10, 700, 20, 639, 15)]   // to the right
    [InlineData(-50, 10, -10, 20, 0, 15)]     // to the left
    [InlineData(10, -30, 20, -5, 15, 0)]      // above
    [InlineData(10, 500, 20, 520, 15, 479)]   // below
    public void A_bar_outside_the_screen_draws_nothing(int x1, int y1, int x2, int y2, int edgeX, int edgeY)
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        uint before = PixelAt(g, edgeX, edgeY);

        g.Bar(x1, y1, x2, y2, Colors.Red);
        g.Ink = Colors.Red;
        g.Bar(x1, y1, x2, y2);

        Assert.Equal(before, PixelAt(g, edgeX, edgeY));
    }

    [AvaloniaFact]
    public void A_bar_partly_outside_the_screen_is_clipped()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        uint before = PixelAt(g, 639, 15);

        g.Bar(600, 10, 700, 20, Colors.Red);

        Assert.NotEqual(before, PixelAt(g, 639, 15));
    }
}

// RASTER COLOUR: the layer's rasters only show where it has the key colour
public class RasterColourTests
{
    private const string Picture = """
        SCREEN 640,480
        INKG "Red" : BAR 0,0,319,479
        INKG "Blue" : BAR 320,0,639,479
        RASTER 1, 0, 0, 480, "Lime, Lime"
        """;

    private static SkiaSharp.SKColor[] Run(string key)
    {
        var r = BasicRunner.Run(Picture + "\n" + key + "\nLoop:\n  WAIT VBL\nGOTO Loop", 0.6, capture: true);
        Assert.True(r.Errors.Count == 0, r.Describe());
        return new[] { r.Frame!.GetPixel(100, 240), r.Frame!.GetPixel(500, 240) };
    }

    private static void Near(SkiaSharp.SKColor c, byte r, byte g, byte b)
    {
        Assert.InRange(c.Red, Math.Max(0, r - 12), Math.Min(255, r + 12));
        Assert.InRange(c.Green, Math.Max(0, g - 12), Math.Min(255, g + 12));
        Assert.InRange(c.Blue, Math.Max(0, b - 12), Math.Min(255, b + 12));
    }

    [AvaloniaFact]
    public void The_raster_replaces_only_the_key_colour()
    {
        var px = Run("RASTER COLOUR 0, \"Red\"");
        Near(px[0], 0, 255, 0);     // the red half shows the raster
        Near(px[1], 0, 0, 255);     // the blue half is untouched
    }

    [AvaloniaFact]
    public void The_tolerance_decides_how_close_a_colour_must_be()
    {
        // #E00000 is 12 % from the red in the picture
        Near(Run("RASTER COLOUR 0, \"#E00000\", 0")[0], 255, 0, 0);
        Near(Run("RASTER COLOUR 0, \"#E00000\", 20")[0], 0, 255, 0);
        Near(Run("RASTER COLOUR 0, \"#E00000\", 20")[0], 0, 255, 0);
    }

    [AvaloniaFact]
    public void Gfxmode_0_turns_the_key_off_again()
    {
        var px = Run("RASTER COLOUR 0, \"Red\"\nRASTER GFXMODE 0, 0");
        Near(px[0], 255, 0, 0);
    }
}

// PRINTG wraps at the right edge like a C64, unless PRINTG WRAP OFF
public class PrintgWrapTests
{
    [AvaloniaFact]
    public void Text_carries_on_on_the_next_row()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        g.ConfigureText(16, 16, "C64 Pro Mono");   // 40 columns
        g.ConsolePrint(new string('*', 45), newLine: false);
        Assert.Equal(1, g.CursorY);
        Assert.Equal(5, g.CursorX);
    }

    [AvaloniaFact]
    public void Wrap_off_runs_past_the_edge()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        g.ConfigureText(16, 16, "C64 Pro Mono");
        g.TextWrap = false;
        g.ConsolePrint(new string('*', 45), newLine: false);
        Assert.Equal(0, g.CursorY);
        Assert.Equal(45, g.CursorX);
    }
}

// With the CRT filter on, the PRINT text is drawn into the game picture (so it gets the effect)
// and the text box above it is hidden
public class CrtConsoleTests
{
    private static void Pump()
    {
        for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    // Light pixels in the top left corner, where the first row of text is (the CRT vignette darkens
    // the corner, so "light" is a little over a third of white)
    private static unsafe int TextPixels(Avalonia.Media.Imaging.Bitmap frame)
    {
        int w = frame.PixelSize.Width;
        var px = new byte[w * 40 * 4];
        fixed (byte* p = px) frame.CopyPixels(new Avalonia.PixelRect(0, 0, w, 40), (nint)p, px.Length, w * 4);
        int n = 0;
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 200; x++)
                if (px[(y * w + x) * 4] > 90 && px[(y * w + x) * 4 + 1] > 90) n++;
        return n;
    }

    [AvaloniaFact]
    public void Print_text_is_drawn_into_the_picture_with_crt()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        var w = new ScreenWindow { Width = 640, Height = 480 };
        w.ScreenControl.Graphics = g;
        w.UpdateScreenSize(640, 480);
        w.Console.Text = "HELLO WORLD";
        w.Show();
        Pump();

        g.CrtEnabled = true;
        g.CrtCurvature = 0;
        g.CrtScanlines = 0;
        w.ScreenControl.InvalidateVisual();
        Pump();
        Assert.Equal(0, w.Console.Opacity);
        Assert.True(TextPixels(Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)!) > 20);

        g.CrtEnabled = false;
        w.ScreenControl.InvalidateVisual();
        Pump();
        Assert.Equal(1, w.Console.Opacity);
        w.Close();
    }
}

// The CRT settings in percent, as SCREEN CRT and the settings window give them
public class CrtSettingsTests
{
    [Fact]
    public void Standard_settings_are_the_old_look()
    {
        var g = new AmosGraphics();
        Assert.Equal(new float[] { 55, 50, 50, 50, 122, 100 }, g.CrtPercentages().Select(v => MathF.Round(v)));
    }

    [Fact]
    public void Percentages_are_kept_within_bounds()
    {
        var g = new AmosGraphics();
        g.SetCrtPercentages(150, -5, 30);
        Assert.Equal(1f, g.CrtScanlines);
        Assert.Equal(0f, g.CrtCurvature);
        Assert.Equal(0.3f, g.CrtMask, 3);
        Assert.Equal(0.5f, g.CrtVignette);   // not given: unchanged
    }
}

// Sprites placed at their handle, and file names worked out as expressions
public class SpriteHandleTests
{
    [AvaloniaFact]
    public void A_sprite_is_put_with_its_handle_at_the_position_and_a_file_name_can_be_an_expression()
    {
        var r = BasicRunner.Run("""
            SCREEN 640,480
            N = 1
            SPRITE LOAD 1, "Graphics/Bobs/Solarsystem/0" & N & ".png"
            SPRITE HANDLE 1, 32, 32
            SPRITE POS 1, 320, 240
            SPRITE ON 1
            Loop:
              WAIT VBL
            GOTO Loop
            """, 0.6, capture: true);
        Assert.True(r.Errors.Count == 0, r.Describe());
        var empty = r.Frame!.GetPixel(10, 10);
        // The planet's middle is at 320, 240: it covers 300, 240 on its left, not only what is right of 320
        Assert.NotEqual(empty, r.Frame.GetPixel(300, 240));
        Assert.NotEqual(empty, r.Frame.GetPixel(320, 240));
        Assert.Equal(empty, r.Frame.GetPixel(370, 290));
    }

    // A rotated, zoomed font in a layer with RASTER COLOUR: no edge of the key colour left between the
    // raster and the rest of the letter (smoothing would blend red into the blue and leave a red line)
    [AvaloniaFact]
    public void A_rotated_font_in_a_layer_with_RASTER_COLOUR_has_no_edge_of_the_key_colour()
    {
        var r = BasicRunner.Run("""
            SCREEN 640,480
            SCREEN SELECT 1
            FONT LOAD 0, "Graphics/font.png", 32, 25
            FONT MAP 0, "ABCDEFGHIJKLMNOPQRSTUVWXYZ!?:;0123456789@()£$%.’[]”,-"
            FONT ZOOM 0, 140
            FONT ROTATE 0, 30
            FONT PRINT #1, 0, 100, 200, "HELLO"
            RASTER 1, 0, 0, 480, "Lime, Lime"
            RASTER COLOUR 1, "Red"
            Loop:
              WAIT VBL
            GOTO Loop
            """, 0.6, capture: true);
        Assert.True(r.Errors.Count == 0, r.Describe());
        int red = 0, lime = 0;
        for (int y = 100; y < 400; y++)
            for (int x = 60; x < 460; x++)
            {
                var c = r.Frame!.GetPixel(x, y);
                if (c.Red > 120 && c.Green < 90 && c.Blue < 90) red++;
                if (c.Green > 200 && c.Red < 60 && c.Blue < 60) lime++;
            }
        Assert.True(lime > 500, $"the raster shows on the letters ({lime} pixels)");
        Assert.True(red == 0, $"{red} pixels of red edge");
    }
}

// BUMP: a picture lit by a lamp, bright near it and dark far from it; and grey PNGs load
public class BumpTests
{
    [AvaloniaFact]
    public void A_grey_picture_loads_and_BUMP_lights_it_round_the_lamp()
    {
        var r = BasicRunner.Run("""
            SCREEN 640,480
            SPRITE LOAD 1, "Graphics/Textures/320_001.png"
            SPRITE POS 1, 0, 0
            SPRITE ON 1
            SPRITE EFFECT ADD 1, BUMP, 280, 280, 8, 150
            Loop:
              WAIT VBL
            GOTO Loop
            """, 0.8, capture: true);
        Assert.True(r.Errors.Count == 0, r.Describe());
        double Light(int cx, int cy)
        {
            double sum = 0;
            for (int y = cy - 20; y < cy + 20; y++)
                for (int x = cx - 20; x < cx + 20; x++)
                {
                    var c = r.Frame!.GetPixel(x, y);
                    sum += c.Red + c.Green + c.Blue;
                }
            return sum / 1600 / 3;
        }
        // (the picture itself is a little darker at 280, 280 than at 80, 80: only the lamp makes it lighter)
        double near = Light(280, 280), far = Light(80, 80);
        Assert.True(near > far + 40, $"near the lamp {near:0}, far from it {far:0}");
    }
}

// SCREEN FRAME: the game picture in a monitor, at its own shape, the monitor on top
public class MonitorFrameTests
{
    [AvaloniaFact]
    public void The_glass_is_found_in_the_middle_of_the_monitor_picture()
    {
        var g = new AmosGraphics();
        Assert.Null(g.SetFrame(Path.Combine(Repo.Root, "Graphics/Frames/C64Monitor.png")));
        Assert.Equal(new SkiaSharp.SKRectI(49, 52, 591, 397), g.Frame!.Glass);
        Assert.NotNull(g.SetFrame(Path.Combine(Repo.Root, "Graphics/font.png")));   // no glass in its middle
        Assert.Null(g.SetFrame(null));
        Assert.Null(g.Frame);
    }

    [AvaloniaFact]
    public void The_picture_fills_the_glass_and_the_monitor_is_fitted_round_it()
    {
        var r = BasicRunner.Run("""
            SCREEN 640,480
            SCREEN FRAME "Graphics/Frames/C64Monitor.png"
            INKG "Red"
            BAR 0, 0, 639, 479
            Loop:
              WAIT VBL
            GOTO Loop
            """, 0.6, capture: true);
        Assert.True(r.Errors.Count == 0, r.Describe());
        var f = r.Frame!;
        // The middle of the monitor is made narrower and its plain sides wider so that its glass is 4:3, and the red
        // picture fills the glass right out to its edges (from x 90 to 550), with no black beside it
        var bezel = f.GetPixel(70, 30);
        Assert.True(bezel.Red > 150 && bezel.Green > 130 && bezel.Blue > 110, bezel.ToString());
        var middle = f.GetPixel(320, 240);
        Assert.True(middle.Red > 200 && middle.Green < 60, middle.ToString());
        foreach (int x in new[] { 95, 545 })
        {
            var edge = f.GetPixel(x, 240);
            Assert.True(edge.Red > 200 && edge.Green < 60, $"x {x}: {edge}");
        }
    }
}
