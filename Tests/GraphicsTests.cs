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
