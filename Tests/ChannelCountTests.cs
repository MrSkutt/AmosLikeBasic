using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AmosLikeBasic;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// Songs with 4 – 8 channels: the file, the synth, sound effects next to the song, and the editor
public class ChannelCountTests : IDisposable
{
    public ChannelCountTests()
    {
        S.ManualRendering = true;
        S.Reset();
        S.Render(new float[2205]);
    }

    public void Dispose()
    {
        S.TrackStop();
        S.Reset();
        S.Render(new float[2205]);
        S.ManualRendering = false;
    }

    private static float[] Render(double seconds)
    {
        var b = new float[(int)(seconds * S.SampleRate)];
        S.Render(b);
        return b;
    }

    private static float ScopePeak(int channel)
    {
        var buf = new float[S.ScopeSize];
        S.ReadScope(channel, buf);
        return buf.Max(Math.Abs);
    }

    // A song whose only note is in the last channel
    private static TrackerSong SongWithNoteIn(int channels)
    {
        var song = new TrackerSong { Channels = channels, Speed = 6, Tempo = 125 };
        song.Instruments.Add(new TrackerInstrument { Wave = "Sine", Volume = 0.5, Attack = 0, Decay = 0, Sustain = 100 });
        song.Patterns[0][0, channels - 1] = TrackerCell.Parse("A-4 01 ...");
        return song;
    }

    [Fact]
    public void Old_files_have_four_channels()
    {
        var song = TrackerSong.FromJson("{\"Patterns\":[[\"C-4 01 ...|--- .. ...|--- .. ...|E-4 01 ...\"]]}");
        Assert.Equal(4, song.Channels);
    }

    [Fact]
    public void A_song_with_six_channels_is_saved_and_loaded_with_six()
    {
        var song = SongWithNoteIn(6);
        string json = song.ToJson();
        var again = TrackerSong.FromJson(json);
        Assert.Equal(6, again.Channels);
        Assert.Equal("A-4 01 ...", again.Patterns[0][0, 5].ToString());
        // Each row has exactly six cells in the file
        Assert.Contains("\"--- .. ...|--- .. ...|--- .. ...|--- .. ...|--- .. ...|A-4 01 ...\"", json);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(12, 8)]
    public void The_number_of_channels_stays_within_4_and_8(int asked, int got) =>
        Assert.Equal(got, new TrackerSong { Channels = asked }.Channels);

    [Fact]
    public void An_eight_channel_song_plays_on_synth_channel_8()
    {
        S.TrackPlay(SongWithNoteIn(8), loop: false);
        Render(0.2);
        Assert.True(ScopePeak(8) > 0.1, "channel 8 sounds");
        Assert.True(ScopePeak(1) < 1e-6, "channel 1 is silent");
    }

    [Fact]
    public void Channels_past_the_song_are_not_played()
    {
        // The note is in channel 8, but the song only has 4 channels
        var song = SongWithNoteIn(8);
        song.Channels = 4;
        S.TrackPlay(song, loop: false);
        Render(0.2);
        Assert.True(ScopePeak(8) < 1e-6);
    }

    [Fact]
    public void Sound_effects_go_above_the_songs_channels()
    {
        var bank = TrackerSong.Load(Repo.PathOf("Sound/Tracker/sfx.amostrk"));
        var jump = bank.Instruments.First(i => i.Name == "Jump");

        S.TrackPlay(SongWithNoteIn(6));
        Render(0.1);
        Assert.Equal(new[] { 7, 8 }, new[] { S.PlayTrackSfx(jump, 1, 60), S.PlayTrackSfx(jump, 1, 60) });
    }

    [Fact]
    public void An_eight_channel_song_lends_channel_8_to_a_sound_effect_and_gets_it_back()
    {
        var bank = TrackerSong.Load(Repo.PathOf("Sound/Tracker/sfx.amostrk"));
        var song = SongWithNoteIn(8);
        // A second note on channel 8 a little later, without an instrument number: it must use the song's instrument
        song.Patterns[0][40, 7] = TrackerCell.Parse("A-5 .. ...");
        S.TrackPlay(song, loop: false);
        Render(0.1);

        Assert.Equal(8, S.PlayTrackSfx(bank.Instruments.First(i => i.Name == "Coin"), 4, 83, ms: 100));
        Render(1.0);
        // Row 40 at speed 6 and 50 ticks a second comes after 4.8 s
        Render(4.0);
        var b = new float[S.ScopeSize];
        S.ReadScope(8, b);
        Assert.True(b.Max(Math.Abs) > 0.1, "the song plays on channel 8 again");
    }

    [AvaloniaFact]
    public void The_editor_shows_and_writes_eight_channels()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var pattern = w.FindControl<PatternView>("Pattern")!;
        w.FindControl<NumericUpDown>("ChannelsBox")!.Value = 8;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(8, pattern.ChannelCount);

        pattern.Focus();
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);   // edit on
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 7; i++)
        {
            w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
            w.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
        }
        w.KeyPress(Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7, pattern.CursorChannel);
        Assert.Equal("C-4 01 ...", pattern.Song!.Patterns[0][0, 7].ToString());
        // Everything still fits: the pattern is not wider than the space it has
        Assert.True(pattern.PreferredWidth <= pattern.Bounds.Width + 1, $"{pattern.PreferredWidth} > {pattern.Bounds.Width}");

        typeof(TrackerWindow).GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(w, false);
        w.Close();
    }

    [AvaloniaFact]
    public void Enter_in_a_box_on_the_instrument_page_goes_back_to_playing_notes()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        var box = w.FindControl<StackPanel>("InstrumentPanel")!.GetLogicalDescendants().OfType<NumericUpDown>().First();
        var text = box.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
        (text as Control ?? box).Focus();
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(w.FindControl<Control>("InstrumentKeys")!.IsFocused);
        typeof(TrackerWindow).GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(w, false);
        w.Close();
    }
}
