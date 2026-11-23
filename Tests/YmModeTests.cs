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
    public void A_new_song_starts_from_a_file_maxYMiser_saved_without_its_notes()
    {
        var song = YmImport.NewMaxYMiser(MaxYMiserTests.Song(), "New song");
        Assert.Equal(("Lead", "Bass"), (song.Instruments[0].Name, song.Instruments[1].Name));   // kept
        Assert.Single(song.Order);
        var notes = song.Composed(0);
        var heard = Enumerable.Range(0, notes.Rows).SelectMany(r => Enumerable.Range(0, 3).Select(c => notes[r, c])).Where(x => !x.Equals(TrackerCell.Empty)).ToList();
        Assert.True(heard.Count == 0, string.Join(", ", heard.Take(5)));
        var m = MaxYMiserSong.Find(YmImport.SaveMaxYMiser(song))!;
        Assert.Equal((1, 0), (m.Length, m.StartPlace));
        Assert.Equal(new[] { 0, 0, 0, 0xFF }, Enumerable.Range(0, 4).Select(t => m.Order[0, t]));
    }

    [Fact]
    public void An_empty_song_gets_three_instruments_to_start_with_that_sound()
    {
        var empty = MaxYMiserSong.Find(MaxYMiserTests.Song())!;
        foreach (var i in empty.Instruments) { i.Name = ""; i.VolumeSeq = 0; }
        var song = YmImport.NewMaxYMiser(empty.Write(), "New song");
        Assert.Equal(new[] { "Square", "SID bass", "Noise drum" }, song.Instruments.Take(3).Select(i => i.Name));
        Assert.Equal(32, song.Instruments.Count);
        // A-4 with the square on the first line: heard
        song.Patterns[song.PatternAt(0, 0)][0, 0] = new TrackerCell(69, 1, '\0', 0);
        var b = Play(song, 11025);
        Assert.True(b.Skip(441).Max(Math.Abs) > 0.05);
        Assert.InRange(Crossings(b, 2205, 2205 + 4410), 40, 48);   // 440 Hz for 0.1 s
    }

    [Fact]
    public void A_changed_title_is_written_into_the_files_header()
    {
        // A header as maxYMiser writes it: each tag with room after it
        var d = new byte[0x60];
        System.Text.Encoding.ASCII.GetBytes("SNDH").CopyTo(d, 12);
        System.Text.Encoding.ASCII.GetBytes("COMM").CopyTo(d, 0x10);
        System.Text.Encoding.ASCII.GetBytes("TITL").CopyTo(d, 0x30);
        System.Text.Encoding.ASCII.GetBytes("RIPP").CopyTo(d, 0x50);
        YmImport.SetSndhTag(d, "TITL", "My tune");
        Assert.Equal("My tune", System.Text.Encoding.ASCII.GetString(d, 0x34, 7));
        Assert.Equal(0, d[0x34 + 7]);
        YmImport.SetSndhTag(d, "TITL", new string('x', 40));        // cut to its room, a zero kept
        Assert.Equal(0, d[0x4F]);
        Assert.Equal((byte)'R', d[0x50]);
    }

    [Fact]
    public void A_MOD_or_XM_song_remembers_what_it_is_made_for()
    {
        var song = new TrackerSong { Mode = "MOD", Channels = 4 };
        Assert.Equal("MOD", TrackerSong.FromJson(song.ToJson()).Mode);
        Assert.DoesNotContain("\"Mode\"", new TrackerSong().ToJson());
        Assert.Null(TrackerSong.FromJson(new TrackerSong { Mode = "IT" }.ToJson()).Mode);
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
