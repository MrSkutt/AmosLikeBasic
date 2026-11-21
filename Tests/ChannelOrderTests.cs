using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// Channels with an order of their own: a place in the order list can take a channel's column from
// another pattern, so that a part that repeats is written once
public class ChannelOrderTests
{
    // Three channels; patterns 0 – 5 each with a note of its own in channels 0 and 1, row 0
    private static TrackerSong Song(int places)
    {
        var song = new TrackerSong { Channels = 3 };
        song.Patterns.Clear();
        song.Order.Clear();
        for (int i = 0; i < places; i++)
        {
            var p = new TrackerPattern(16);
            song.Patterns.Add(p);
            song.Order.Add(i);
        }
        return song;
    }

    [Fact]
    public void A_channel_can_play_another_pattern_at_a_place_and_back()
    {
        var song = Song(2);
        song.Patterns[1][0, 1] = TrackerCell.Parse("C-4 01 ...");
        song.SetPatternAt(0, 1, 1);
        Assert.Equal((0, 1, 0), (song.PatternAt(0, 0), song.PatternAt(0, 1), song.PatternAt(0, 2)));
        Assert.True(song.HasChannelOrder);
        Assert.Equal(60, song.Composed(0)[0, 1].Note);     // C-4 from pattern 1, at place 0
        Assert.Equal(TrackerCell.NoNote, song.Composed(0)[0, 0].Note);

        // Through the file
        var again = TrackerSong.FromJson(song.ToJson());
        Assert.Equal(1, again.PatternAt(0, 1));

        // Places put in and taken out keep each place's channels
        song.InsertPosition(0, 1);
        Assert.Equal(1, song.PatternAt(1, 1));
        song.RemovePosition(0);
        Assert.Equal(1, song.PatternAt(0, 1));

        song.SetPatternAt(0, 1, 0);                         // back to the place's own
        Assert.False(song.HasChannelOrder);
        Assert.DoesNotContain("ChannelOrder", song.ToJson());
    }

    [Fact]
    public void Sharing_writes_each_channels_parts_once_and_plays_the_same()
    {
        // Channel 0 goes A B A B A B, channel 1 X Y Z X Y Z: six different places, but two and three parts
        var song = Song(6);
        string[] first = { "C-4", "D-4" }, second = { "E-4", "F-4", "G-4" };
        for (int i = 0; i < 6; i++)
        {
            song.Patterns[i][0, 0] = TrackerCell.Parse($"{first[i % 2]} 01 ...");
            song.Patterns[i][4, 1] = TrackerCell.Parse($"{second[i % 3]} 01 ...");
        }
        var before = Enumerable.Range(0, 6).Select(song.Composed).ToList();

        int fewer = song.ShareChannels();
        Assert.Equal(3, fewer);
        Assert.Equal(3, song.Patterns.Count);
        for (int pos = 0; pos < 6; pos++)
            for (int r = 0; r < 16; r++)
                for (int c = 0; c < 3; c++)
                    Assert.Equal(before[pos][r, c], song.Composed(pos)[r, c]);
    }

    [Fact]
    public void Sharing_is_left_out_when_it_saves_little()
    {
        // Every place different in every channel: nothing to share
        var song = Song(4);
        for (int i = 0; i < 4; i++) song.Patterns[i][0, 0] = TrackerCell.Parse($"C-{i + 2} 01 ...");
        Assert.Equal(0, song.ShareChannels());
        Assert.False(song.HasChannelOrder);
    }

    [Fact]
    public void A_channel_from_another_pattern_is_heard()
    {
        // The place's own pattern is empty; channel 1 plays pattern 1, which has a note
        var song = Song(2);
        song.Instruments.Add(new TrackerInstrument { Name = "Sine", Wave = "Sine", Volume = 0.5, Attack = 0, Sustain = 100 });
        song.Patterns[1][0, 1] = TrackerCell.Parse("A-4 01 ...");
        song.Order.RemoveAt(1);
        song.SetPatternAt(0, 1, 1);
        S.ManualRendering = true;
        S.Reset();
        S.TrackPlay(song, loop: false);
        var b = new float[8820];
        S.Render(b);
        S.TrackStop();
        S.Render(new float[2205]);
        Assert.True(b.Skip(2000).Max(Math.Abs) > 0.05, "the note of pattern 1 plays at place 0");
    }

    [AvaloniaFact]
    public void Alt_and_the_arrows_change_the_channels_pattern_here_and_Alt_N_gives_it_its_own()
    {
        var w = new TrackerWindow { Width = 1280, Height = 820 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var view = w.FindControl<PatternView>("Pattern")!;
        view.Focus();
        Dispatcher.UIThread.RunJobs();
        var song = (TrackerSong)typeof(TrackerWindow).GetField("_song", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        while (song.Patterns.Count < 2) song.Patterns.Add(new TrackerPattern(song.Patterns[0].Rows));
        int channel = view.CursorChannel, place = song.Order[0];

        void Press(Key key, RawInputModifiers mods)
        {
            w.KeyPress(key, mods, PhysicalKey.None, null);
            w.KeyRelease(key, mods, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
        }
        Press(Key.Up, RawInputModifiers.Alt);
        Assert.Equal(place + 1, song.PatternAt(0, channel));
        Assert.Equal(place + 1, view.PatternIndexOf(channel));
        Press(Key.Down, RawInputModifiers.Alt);
        Assert.Equal(place, song.PatternAt(0, channel));

        int count = song.Patterns.Count;
        Press(Key.N, RawInputModifiers.Alt);
        Assert.Equal(count + 1, song.Patterns.Count);
        Assert.Equal(count, song.PatternAt(0, channel));
        w.Close();
    }
}
