using Avalonia.Headless.XUnit;

namespace AmosLikeBasic.Tests;

// WAIT VBL follows the screen's refreshes (DisplayClock). These tests play refreshes at different
// rates and check which of them get a new frame, as if the program were always waiting at WAIT VBL.
public class DisplayClockTests : IDisposable
{
    public DisplayClockTests() => DisplayClock.Reset();
    public void Dispose() => DisplayClock.Reset();

    // Plays `count` refreshes `refreshMs` apart; returns the refresh numbers that got a new frame
    private static List<int> Play(double refreshMs, int count)
    {
        var frames = new List<int>();
        for (int i = 0; i < count; i++)
        {
            DisplayClock.Tick(1000 + i * refreshMs);
            if (DisplayClock.TryUseLatestTick()) frames.Add(i);
        }
        return frames;
    }

    private static List<int> Gaps(List<int> frames) => frames.Zip(frames.Skip(1), (a, b) => b - a).ToList();

    [Theory]
    [InlineData(1000.0 / 60)]
    [InlineData(1000.0 / 59.94)]   // many screens say 60 Hz but run at 59.94
    [InlineData(1000.0 / 60.05)]
    public void At_60_Hz_every_refresh_gets_a_frame(double refreshMs)
    {
        var frames = Play(refreshMs, 600);
        Assert.Equal(600, frames.Count);
    }

    [Fact]
    public void At_120_Hz_every_second_refresh_gets_a_frame()
    {
        var gaps = Gaps(Play(1000.0 / 120, 1200));
        Assert.All(gaps.Skip(30), g => Assert.Equal(2, g));   // after the refresh rate is measured
    }

    [Fact]
    public void At_144_Hz_the_frames_average_60_per_second()
    {
        var frames = Play(1000.0 / 144, 1440);   // 10 seconds
        Assert.InRange(frames.Count, 595, 610);
        Assert.All(Gaps(frames).Skip(30), g => Assert.InRange(g, 2, 3));
    }

    [Fact]
    public void A_second_call_for_the_same_refresh_is_not_a_new_refresh()
    {
        Play(1000.0 / 60, 60);                       // the refresh rate is known: 16.7 ms
        double last = 1000 + 59 * 1000.0 / 60;
        Assert.True(DisplayClock.IsDuplicateRefresh(last + 0.1));
        Assert.False(DisplayClock.IsDuplicateRefresh(last + 16.7));
    }

    [Fact]
    public void A_slow_start_does_not_make_refreshes_count_as_duplicates()
    {
        Play(1000.0 / 30, 40);                       // the first frames came at 30 per second
        double last = 1000 + 39 * 1000.0 / 30;
        Assert.False(DisplayClock.IsDuplicateRefresh(last + 16.7));   // a normal 60 Hz refresh still counts
        Assert.False(DisplayClock.IsDuplicateRefresh(last + 4.2));    // even at 240 Hz
    }

    [Theory]
    [InlineData(1000.0 / 60, 1)]
    [InlineData(1000.0 / 59.94, 1)]
    [InlineData(1000.0 / 120, 2)]
    [InlineData(1000.0 / 119.88, 2)]
    [InlineData(1000.0 / 240, 4)]
    [InlineData(1000.0 / 144, 0)]
    [InlineData(1000.0 / 75, 0)]
    [InlineData(1000.0 / 30, 1)]
    public void Refreshes_per_frame(double refreshMs, int expected) =>
        Assert.Equal(expected, DisplayClock.RefreshesPerFrame(refreshMs));

    [Fact]
    public async Task Without_a_window_WAIT_VBL_keeps_60_frames_per_second_by_the_clock()
    {
        DisplayClock.StartRun();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 30; i++) await DisplayClock.WaitFrameAsync(CancellationToken.None);
        // 30 frames = 0.5 s. It must wait (not run as fast as it can), but a busy build machine can
        // oversleep a lot, so the upper limit is loose.
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 400, 3000);
    }

    [Fact]
    public async Task WAIT_VBL_returns_at_a_refresh()
    {
        DisplayClock.StartRun();
        DisplayClock.Tick();
        var sinceTick = System.Diagnostics.Stopwatch.StartNew();
        await DisplayClock.WaitFrameAsync(CancellationToken.None);   // the first frame uses the latest refresh

        var wait = DisplayClock.WaitFrameAsync(CancellationToken.None);
        await Task.Delay(30);
        // No new refresh yet, so it waits. (After 100 ms without refreshes it rightly goes over to
        // the timer, so on a slow machine that stalled for that long the check says nothing.)
        if (sinceTick.ElapsedMilliseconds < 90)
            Assert.False(wait.IsCompleted);
        DisplayClock.Tick();
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
    }
}

// The screen shows the frame that was presented when it last refreshed, not one the program
// presents while the refresh is being drawn
public class ShownFrameTests
{
    [AvaloniaFact]
    public void A_refresh_locks_in_the_presented_scroll_position()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        var layer = g.ActiveFrame[0];

        g.Scroll(0, 10, 0);
        g.PresentFrame();
        Assert.True(g.LatchShownFrame());           // the refresh: frame 1 is shown

        g.Scroll(0, 20, 0);                         // the program goes on with frame 2 ...
        g.PresentFrame();                           // ... and presents it before the refresh is drawn
        Assert.Equal(10, layer.ShownShaderValues![0].X);

        Assert.True(g.LatchShownFrame());           // the next refresh shows frame 2
        Assert.Equal(20, layer.ShownShaderValues![0].X);
        Assert.False(g.LatchShownFrame());          // nothing new at the one after
    }

    // A game that swaps layers every frame (Frog Hop): which layers are on must belong to the same
    // frame as everything else on the screen, not to the one the program is drawing now
    [AvaloniaFact]
    public void A_refresh_locks_in_which_layers_are_on()
    {
        var g = new AmosGraphics();
        g.Screen(640, 480);
        g.SetDrawingScreen(2);
        var one = g.ActiveFrame[1];

        one.Visible = true;
        g.PresentFrame();
        g.LatchShownFrame();                         // frame 1 shows layer 1

        one.Visible = false;                         // the program swaps for frame 2 ...
        Assert.True(one.ShownVisible);               // ... but the screen still shows frame 1

        g.PresentFrame();
        g.LatchShownFrame();
        Assert.False(one.ShownVisible);
    }
}
