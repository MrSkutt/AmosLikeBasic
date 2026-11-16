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
