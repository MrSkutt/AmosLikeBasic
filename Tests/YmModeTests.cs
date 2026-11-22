using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// YM mode: channels 1 – 3 played by the Atari ST's sound chip, each instrument by its YM part
public class YmModeTests
{
    private static TrackerSong Song(TrackerYmInstrument ym, string cell = "A-4 01 .. ...")
    {
        var song = new TrackerSong { Channels = 3, Speed = 6, Ym = new TrackerYmSong() };
        song.Instruments.Add(new TrackerInstrument { Name = "YM", Wave = "Sine", Volume = 0.3, Attack = 0, Sustain = 100, Ym = ym });
        song.Patterns[0][0, 0] = TrackerCell.Parse(cell);
        return song;
    }

    private static TrackerYmInstrument Square(params int[] volumes) => new()
    {
        VolumeSeq = new TrackerYmSequence { Steps = volumes.ToList(), Repeat = volumes.Length - 1 },
        MixerSeq = new TrackerYmSequence { Steps = { 0x100 } }
    };

    private static float[] Play(TrackerSong song, int samples = 22050)
    {
        S.ManualRendering = true;
        S.Reset();
        S.TrackPlay(TrackerSong.FromJson(song.ToJson()), loop: false);
        var b = new float[samples];
        S.Render(b);
        S.TrackStop();
        S.Render(new float[2205]);
        S.Reset();
        return b;
    }

    private static int Crossings(float[] b, int from, int to)
    {
        int n = 0;
        for (int i = from + 1; i < to; i++) if (b[i - 1] < 0 && b[i] >= 0) n++;
        return n;
    }

    [Fact]
    public void The_chip_plays_a_YM_instrument_at_the_note()
    {
        var b = Play(Song(Square(15)));
        // A-4: 440 crossings a second, a square from the chip (not the instrument's sine)
        Assert.InRange(Crossings(b, 4410, 4410 + 11025), 107, 113);   // 440 a second, a quarter of one
        double rms = Math.Sqrt(b.Skip(4410).Take(11025).Select(v => (double)v * v).Average());
        Assert.True(rms > 0.05, $"it sounds ({rms})");
    }

    [Fact]
    public void The_volume_column_and_the_volume_sequence_are_the_chips_steps()
    {
        double Rms(TrackerSong s) { var b = Play(s); return Math.Sqrt(b.Skip(4410).Take(11025).Select(v => (double)v * v).Average()); }
        double full = Rms(Song(Square(15)));
        double column = Rms(Song(Square(15), "A-4 01 20 ..."));   // 32 of 64: two steps of 3 dB down
        double quiet = Rms(Song(Square(13)));                     // the volume sequence two steps down
        Assert.InRange(column / full, 0.4, 0.6);
        Assert.InRange(quiet / full, 0.4, 0.6);
    }

    [Fact]
    public void A_SID_on_the_tone_is_the_chips_own()
    {
        var sid = Square(15);
        sid.MixerSeq = new TrackerYmSequence { Steps = { 0x105 } };
        sid.TimerSeq = new TrackerYmSequence { Steps = { 0xF, 0 } };
        var b = Play(Song(sid));
        Assert.True(b.Skip(4410).Max(Math.Abs) > 0.05, "the tone and the SID together sound");
    }

    [Fact]
    public void Sequences_are_written_in_hex_with_a_bar_before_where_they_go_round()
    {
        var q = TrackerWindow.ParseYmSequence("F E | D C")!;
        Assert.Equal(new[] { 15, 14, 13, 12 }, q.Steps);
        Assert.Equal(2, q.Repeat);
        Assert.Equal("F E | D C", TrackerWindow.YmSequenceText(q, false));
        var stays = TrackerWindow.ParseYmSequence("F A 0")!;
        Assert.Equal(2, stays.Repeat);                                  // no bar: it stays on its last step
        Assert.Equal("F A 0", TrackerWindow.YmSequenceText(stays, false));
        Assert.Equal("0105 1000", TrackerWindow.YmSequenceText(new TrackerYmSequence { Steps = { 0x105, 0x1000 }, Repeat = 1 }, true));
        Assert.Equal(new[] { -12, 3 }, TrackerWindow.ParseYmSequence("-C 3")!.Steps);
        Assert.Null(TrackerWindow.ParseYmSequence("  "));
        Assert.Throws<FormatException>(() => TrackerWindow.ParseYmSequence("G"));
    }

    [Fact]
    public void A_maxYMiser_song_is_read_in_YM_mode_and_keeps_it_when_saved()
    {
        var song = YmImport.ImportMaxYMiser(MaxYMiserSong.Find(MaxYMiserTests.Song())!, "ym")!.Song;
        Assert.NotNull(song.Ym);
        var bass = song.Instruments.First(i => i.Name == "Bass");
        Assert.Equal(new[] { 0x105 }, bass.Ym!.MixerSeq!.Steps);
        Assert.Equal(new[] { 0xF, 0 }, bass.Ym.TimerSeq!.Steps);
        var back = TrackerSong.FromJson(song.ToJson());
        Assert.NotNull(back.Ym);
        Assert.Equal(new[] { 0x105 }, back.Instruments.First(i => i.Name == "Bass").Ym!.MixerSeq!.Steps);
        // A song without it is saved as before
        Assert.DoesNotContain("\"Ym\"", new TrackerSong().ToJson());
    }

    [AvaloniaFact]
    public void The_menu_turns_YM_mode_on_and_off_and_the_instrument_page_follows()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var song = (TrackerSong)typeof(TrackerWindow).GetField("_song", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        Assert.Null(song.Ym);
        w.FindControl<MenuItem>("MenuYmMode")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        song = (TrackerSong)typeof(TrackerWindow).GetField("_song", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        Assert.NotNull(song.Ym);
        Assert.Contains("(YM)", w.Title);
        Assert.True(w.FindControl<MenuItem>("MenuYmMode")!.IsChecked);
        typeof(TrackerWindow).GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(w, false);
        w.Close();
    }
}
