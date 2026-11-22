namespace AmosLikeBasic.Tests;

// Songs written as FastTracker 2 modules, and read back
public class XmExportTests
{
    private static string Folder() => Path.Combine(Path.GetTempPath(), "amos-xm-export-" + Guid.NewGuid().ToString("N"));

    // A song with a sampled instrument and a synth one, effects XM has and some it does not, and a
    // channel that plays another pattern at the second place
    private static TrackerSong Song(string folder)
    {
        Directory.CreateDirectory(folder);
        string wav = Path.Combine(folder, "tone.wav");
        var tone = Enumerable.Range(0, 2205).Select(i => (float)Math.Sin(2 * Math.PI * i * 100 / 22050)).ToArray();
        SampleBank.WriteWav(wav, new SampleData(tone, 22050));
        var song = new TrackerSong { Title = "Export", Speed = 4, Tempo = 140, Channels = 3 };
        song.Instruments.Add(new TrackerInstrument { Name = "Sampled", Wave = "Sample", Sample = wav, SampleNote = 60, LoopStart = 0, LoopEnd = 2205, Volume = 0.5 });
        song.Instruments.Add(new TrackerInstrument { Name = "Synth", Wave = "Square", Volume = 0.3, Attack = 0, Decay = 0, Sustain = 100, Release = 50,
            Table = new() { "SQUARE", "SQUARE +12" }, TableLoop = 0 });
        song.Patterns.Clear();
        song.Order.Clear();
        for (int i = 0; i < 2; i++)
        {
            song.Patterns.Add(new TrackerPattern(16));
            song.Order.Add(i);
        }
        var p = song.Patterns[0];
        p[0, 0] = TrackerCell.Parse("C-4 01 30 110");
        p[0, 1] = TrackerCell.Parse("A-4 02 .. J47");
        p[2, 1] = TrackerCell.Parse("E-5 .. .. G80");
        p[4, 2] = TrackerCell.Parse("--- .. .. V20");
        p[8, 1] = TrackerCell.Parse("=== .. .. ...");
        song.Patterns[1][0, 0] = TrackerCell.Parse("G-4 01 .. ...");
        song.SetPatternAt(1, 1, 0);   // channel 2 plays pattern 0's column at place 1
        return song;
    }

    [Fact]
    public void A_song_goes_to_XM_and_comes_back_with_its_rows_notes_and_effects()
    {
        string folder = Folder();
        var song = Song(folder);
        var r = XmExport.Write(song, (n, t) => ChiptuneSynth.RenderNotes(n, t));
        var back = XmImport.Import(r.Bytes, "back", folder, folder).Song;

        Assert.Equal((4, 140), (back.Speed, back.Tempo));
        Assert.Equal(2, back.Order.Count);
        var p = back.Patterns[back.Order[0]];
        Assert.Equal(60, p[0, 0].Note);
        Assert.Equal(0x30, p[0, 0].Volume);
        Assert.Equal(('1', 0x10), (p[0, 0].Effect, p[0, 0].Param));
        Assert.Equal(('0', 0x47), (p[0, 1].Effect, p[0, 1].Param));   // Jxy as the arpeggio
        // The filter set with the note is in an instrument of its own, played with it
        Assert.Equal('\0', p[2, 1].Effect);
        Assert.Contains("G80", back.Instruments[p[2, 1].Instrument - 1].Name);
        Assert.Equal(('V', 0x20), (p[4, 2].Effect, p[4, 2].Param));
        Assert.Equal(TrackerCell.NoteOff, p[8, 1].Note);
        // The channel's own pattern is written out: place 1 has pattern 0's A-4 in channel 2
        var q = back.Patterns[back.Order[1]];
        Assert.Equal((67, 69), (q[0, 0].Note, q[0, 1].Note));
        Assert.Contains(r.Notes, n => n.Contains("played into"));
    }

    [Fact]
    public void A_sample_plays_at_its_pitch_and_the_synths_notes_are_played_into_samples()
    {
        string folder = Folder();
        var song = Song(folder);
        var xm = XmExport.Write(song, (n, t) => ChiptuneSynth.RenderNotes(n, t));
        var back = XmImport.Import(xm.Bytes, "back", folder, folder).Song;
        Assert.Contains(back.Instruments, i => i.Name == "Sampled");

        // The sample's rate at its note is kept: 22050 Hz on C-4
        var sampled = back.Instruments.First(i => i.Name == "Sampled");
        var data = SampleBank.Get(sampled.Sample)!;
        Assert.Equal(22050, data.Rate * Math.Pow(2, (60 - sampled.SampleNote) / 12.0), 22050 * 0.01);

        // The synth's A-4 and E-5, each a sample of its own that sounds at its note
        var synth = back.Instruments.Where(i => i.Name.StartsWith("Synth")).ToList();
        Assert.True(synth.Count >= 2);
        foreach (var inst in synth)
        {
            var s = SampleBank.Get(inst.Sample)!;
            Assert.True(s.Data.Max(Math.Abs) > 0.1, "the note was played into the sample");
        }
    }
}
