namespace AmosLikeBasic.Tests;

// SIMPLIFY: fewer instruments, with their arpeggios and vibratos as effects
public class SimplifyTests
{
    private static TrackerInstrument Tabled(string name, params string[] table) => new()
    {
        Name = name, Wave = "Square", Volume = 0.2, Table = table.ToList(), TableLoop = 0,
        Envelope = new() { new(0, 64), new(8, 32) }
    };

    [Fact]
    public void Arpeggios_and_vibratos_become_effects_and_the_instruments_one()
    {
        var song = new TrackerSong { Speed = 6 };
        song.Instruments.Clear();
        song.Instruments.Add(Tabled("Arp 1", "SQUARE", "SQUARE +4", "SQUARE +7"));         // major
        song.Instruments.Add(Tabled("Arp 2", "SQUARE", "SQUARE +3", "SQUARE +7"));         // minor
        song.Instruments.Add(Tabled("Arp 3", "SQUARE +2", "SQUARE +5", "SQUARE +9"));      // an arpeggio on the note 2 up
        song.Instruments.Add(Tabled("Vib 1", "SQUARE +0.5", "SQUARE", "SQUARE -0.5", "SQUARE"));
        var p = song.Patterns[0];
        p[0, 0] = TrackerCell.Parse("C-4 01 ...");
        p[2, 0] = TrackerCell.Parse("C-4 02 30 ...");
        p[4, 0] = TrackerCell.Parse("C-4 03 ...");
        p[6, 0] = TrackerCell.Parse("C-4 04 ...");

        var r = InstrumentSimplifier.Simplify(song);
        Assert.Equal((4, 1, 3, 1), (r.Before, r.After, r.Arpeggios, r.Vibratos));
        var inst = Assert.Single(song.Instruments);
        Assert.Equal("Square", inst.Wave);
        Assert.Empty(inst.Table);
        Assert.Equal(new[] { (0, 64), (8, 32) }, inst.Envelope.Select(e => (e.Tick, e.Level)));

        Assert.Equal("C-4 01 .. 047", p[0, 0].ToString());
        Assert.Equal("--- .. .. 047", p[1, 0].ToString());   // on every row the note sounds
        Assert.Equal("C-4 01 30 037", p[2, 0].ToString());   // the volume column stays
        Assert.Equal("D-4 01 .. 037", p[4, 0].ToString());   // the note moved up 2: D-4 major is 0, 3, 7 from D
        Assert.Equal('4', p[6, 0].Effect);
        Assert.InRange(p[6, 0].Param & 15, 3, 5);              // half a semitone each way: depth 4
    }

    [Fact]
    public void Instruments_without_tables_and_envelopes_are_left_alone()
    {
        var song = new TrackerSong();
        int before = song.Instruments.Count;
        var json = song.ToJson();
        var r = InstrumentSimplifier.Simplify(song);
        Assert.Equal(before, r.After);
        Assert.Equal(json, song.ToJson());
    }

    [Fact]
    public void Tick_arpeggios_of_two_and_four_notes_become_J()
    {
        var song = new TrackerSong { Speed = 6 };
        song.Instruments.Clear();
        song.Instruments.Add(Tabled("Octave", "SQUARE", "SQUARE +12"));                              // two notes taking turns
        song.Instruments.Add(Tabled("Chord", "SQUARE", "SQUARE +4", "SQUARE +7", "SQUARE +12"));      // four, the octave last
        var p = song.Patterns[0];
        p[0, 0] = TrackerCell.Parse("C-4 01 ...");
        p[2, 0] = TrackerCell.Parse("C-4 02 ...");

        var r = InstrumentSimplifier.Simplify(song);
        Assert.Equal((2, 1, 2), (r.Before, r.After, r.Arpeggios));
        Assert.Equal("C-4 01 .. JC0", p[0, 0].ToString());
        Assert.Equal("C-4 01 .. J47", p[2, 0].ToString());
    }
}
