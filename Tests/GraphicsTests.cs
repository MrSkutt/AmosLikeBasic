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
