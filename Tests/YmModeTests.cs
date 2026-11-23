using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// A maxYMiser song in the tracker as maxYMiser has it (YM mode): opened, played by the sound chip as its
// replay plays it, edited, and saved in its own file again
public class YmModeTests
{
    private static (byte[] file, TrackerSong song) Open()
    {
        var file = MaxYMiserTests.Song();
        return (file, YmImport.OpenMaxYMiser(file, MaxYMiserSong.Find(file)!, "Test"));
    }

    private static float[] Play(TrackerSong song, int samples, params int[] mute)
    {
        S.ManualRendering = true;
        S.Reset();
        foreach (int c in mute) S.SetTrackMute(c, true);
        S.TrackPlay(TrackerSong.FromJson(song.ToJson()), loop: false);
        var b = new float[samples];
        S.Render(b);
        S.TrackStop();
        for (int c = 1; c <= 4; c++) S.SetTrackMute(c, false);
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
    public void A_maxYMiser_song_opens_as_maxYMiser_has_it()
    {
        var (_, song) = Open();
        Assert.NotNull(song.Ym);
        Assert.Equal(3, song.Channels);
        Assert.Equal(32, song.Instruments.Count);
        Assert.Equal(("Lead", "Bass"), (song.Instruments[0].Name, song.Instruments[1].Name));
        Assert.Equal(1, song.Instruments[0].Ym!.VolumeSeq);
        Assert.Equal(new[] { 0xF, 0xC }, song.Ym!.Sequences[1].Steps);
        // Each track plays its own pattern at the place: the lead pattern 0, the bass pattern 1
        Assert.Equal((0, 1), (song.PatternAt(0, 0), song.PatternAt(0, 1)));
        var lead = song.Composed(0)[0, 0];
        Assert.Equal((69, 1, 'S', 3), (lead.Note, lead.Instrument, lead.Effect, lead.Param));
        Assert.Equal(TrackerCell.NoteOff, song.Composed(0)[4, 0].Note);
        Assert.Equal(45, song.Composed(0)[0, 1].Note);
    }

    [Fact]
    public void It_is_saved_as_it_was_and_with_what_was_changed()
    {
        var (file, song) = Open();
        Assert.Equal(file, YmImport.SaveMaxYMiser(TrackerSong.FromJson(song.ToJson())));

        // A note in the bass's track, a second effect, a sequence changed
        var bass = song.Patterns[song.PatternAt(0, 1)];
        bass[8, 1] = new TrackerCell(48, 2, '\0', 0, TrackerCell.NoVolume, 'X', 0x37);
        song.Ym!.Sequences[1] = new TrackerYmSequence { Steps = { 0xF, 0xE, 0xD }, Repeat = 2 };
        var back = MaxYMiserSong.Find(YmImport.SaveMaxYMiser(TrackerSong.FromJson(song.ToJson())))!;
        var line = back.Patterns[back.Order[0, 1]][8];
        Assert.Equal((48, 2, 'X', 0x37), (line.Note, line.Instrument, line.Effect2, line.Param2));
        Assert.Equal(69, back.Patterns[back.Order[0, 0]][0].Note);   // the lead as it was
        Assert.Equal(new[] { 0xF, 0xE, 0xD }, back.Sequences[1].Steps);
    }

    [Fact]
    public void A_pattern_one_track_changes_gets_a_number_of_its_own()
    {
        var (_, song) = Open();
        // Both tracks play pattern 0 at a second place; the bass's copy is changed there
        song.Order.Add(0);
        song.ChannelOrder.Add(new[] { 0, 0, song.Ym!.EmptyPattern, song.Ym.EmptyPattern });
        song.Patterns[0][2, 1] = new TrackerCell(40, 2, '\0', 0);
        var back = MaxYMiserSong.Find(YmImport.SaveMaxYMiser(song))!;
        Assert.Equal(2, back.Length);
        Assert.Equal(0, back.Order[1, 0]);                             // the lead's pattern keeps its number
        Assert.NotEqual(0, back.Order[1, 1]);                          // the changed one is new
        Assert.Equal(40, back.Patterns[back.Order[1, 1]][2].Note);
        Assert.Equal(0, back.Patterns[0][2].Note);
    }

    [Fact]
    public void The_chip_plays_the_tracks_as_the_replay_does()
    {
        var (_, song) = Open();
        // The bass alone: A-2 (110 Hz), the tone with a SID on it
        var bass = Play(song, 22050, 1);
        Assert.InRange(Crossings(bass, 4410, 4410 + 11025), 24, 31);
        // The lead alone: A-4 and its octave arpeggio, then silent after its note off (line 4, speed 3)
        var lead = Play(song, 22050, 2);
        Assert.True(lead.Skip(441).Take(2205).Max(Math.Abs) > 0.05, "the lead sounds");
        Assert.True(lead.Skip(13230).Take(4410).Max(Math.Abs) < 0.02, "and stops at its note off");
    }

    [Fact]
    public void Converted_it_is_the_trackers_own()
    {
        var (_, song) = Open();
        var r = YmImport.ImportMaxYMiser(YmImport.ToMaxYMiser(song), "Test")!;
        Assert.Null(r.Song.Ym);
        Assert.Contains(r.Song.Instruments, i => i.Name == "Bass" && i.Ym == null);
    }

    [Fact]
    public void A_cell_with_two_effects_is_kept_in_the_trackers_file()
    {
        var cell = TrackerCell.Parse("C-4 01 05 S03 X37");
        Assert.Equal(('S', 3, 'X', 0x37, 5), (cell.Effect, cell.Param, cell.Effect2, cell.Param2, cell.Volume));
        Assert.Equal("C-4 01 05 S03 X37", cell.ToString());
        Assert.Equal("C-4 01 .. ...", TrackerCell.Parse("C-4 01 .. ...").ToString());
        Assert.Equal(cell, TrackerCell.Parse(cell.ToString()));
    }
}
