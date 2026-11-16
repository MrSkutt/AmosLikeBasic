using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AmosLikeBasic;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// The oscilloscopes, the instrument page and the focus in the tracker
public class TrackerViewTests : IDisposable
{
    public TrackerViewTests()
    {
        S.ManualRendering = true;
        S.Reset();
        S.Render(new float[2205]);
    }

    public void Dispose()
    {
        S.Reset();
        S.Render(new float[2205]);
        S.ManualRendering = false;
    }

    [Fact]
    public void The_scope_shows_what_a_channel_plays()
    {
        S.PlayMusic(1, "A4:1");
        S.Render(new float[4410]);
        var one = new float[S.ScopeSize];
        var two = new float[S.ScopeSize];
        S.ReadScope(1, one);
        S.ReadScope(2, two);
        Assert.True(one.Max(Math.Abs) > 0.05, "channel 1 plays a note");
        Assert.True(two.Max(Math.Abs) < 1e-6, "channel 2 is silent");
    }

    private static TrackerWindow Open()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.FindControl<PatternView>("Pattern")!.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Close(TrackerWindow w)
    {
        typeof(TrackerWindow).GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(w, false);
        w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Picking_an_instrument_keeps_the_keys_in_the_pattern()
    {
        var w = Open();
        var pattern = w.FindControl<PatternView>("Pattern")!;
        var list = w.FindControl<ListBox>("InstrumentList")!;
        list.Focus();
        list.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, pattern.Instrument);
        Assert.True(pattern.IsFocused, "the pattern has the focus again");
        Press(w, Key.Z);   // and a note can be typed straight away, with the new instrument
        Assert.Equal("C-4 03 ...", pattern.Song!.Patterns[pattern.PatternIndex][0, 0].ToString());
        Close(w);
    }

    [AvaloniaFact]
    public void F4_and_F3_switch_between_the_instrument_page_and_the_pattern()
    {
        var w = Open();
        var patternPage = w.FindControl<Control>("PatternPage")!;
        var instrumentPage = w.FindControl<Control>("InstrumentPage")!;

        Press(w, Key.F4);
        Assert.True(instrumentPage.IsVisible);
        Assert.False(patternPage.IsVisible);
        Assert.True(w.FindControl<Control>("InstrumentKeys")!.IsFocused);

        Press(w, Key.F3);
        Assert.False(instrumentPage.IsVisible);
        Assert.True(patternPage.IsVisible);
        Assert.True(w.FindControl<PatternView>("Pattern")!.IsFocused);
        Close(w);
    }

    [AvaloniaFact]
    public void Note_keys_on_the_instrument_page_play_without_writing_notes()
    {
        var w = Open();
        var pattern = w.FindControl<PatternView>("Pattern")!;
        Press(w, Key.F4);
        w.KeyPress(Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        S.Render(new float[4410]);
        var scope = new float[S.ScopeSize];
        S.ReadScope(pattern.CursorChannel + 1, scope);
        w.KeyRelease(Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(scope.Max(Math.Abs) > 0.01, "the instrument sounds");
        Assert.Equal("--- .. ...", pattern.Song!.Patterns[pattern.PatternIndex][0, 0].ToString());
        Close(w);
    }

    // Every wave and filter type draws without trouble
    [AvaloniaTheory]
    [InlineData("Pulse", "Low")]
    [InlineData("Square", "High")]
    [InlineData("Triangle", "Band")]
    [InlineData("Sawtooth", "Off")]
    [InlineData("Sine", "Low")]
    [InlineData("Noise", "High")]
    public void The_instrument_graphs_draw_every_setting(string wave, string filter)
    {
        var inst = new TrackerInstrument
        {
            Wave = wave, Filter = filter, PulseWidth = 20, PwmDepth = 10, PwmRate = 2, Cutoff = 800, Resonance = 70,
            FilterEnvDepth = 2000, Attack = 0, Decay = 0, Sustain = 50, Release = 0
        };
        var panel = new StackPanel();
        foreach (InstrumentGraph g in new InstrumentGraph[] { new WaveGraph(), new EnvelopeGraph(), new FilterGraph() })
        {
            g.Instrument = () => inst;
            g.Width = 320;
            panel.Children.Add(g);
        }
        var w = new Window { Content = panel, Width = 340, Height = 400 };
        w.Show();
        Assert.NotNull(w.CaptureRenderedFrame());
        w.Close();
    }
}
