using System.Text;

namespace AmosLikeBasic.Tests;

// maxYMiser songs in SNDH files, read as they were written
public class MaxYMiserTests
{
    // A song as maxYMiser saves it after its replay routine: two instruments, five sequences, one place
    // in the order list. "Lead" plays A-4 with a volume of F then C and an octave arpeggio, then a note
    // off on line 4 (and S03 on line 0); "Bass" plays A-2 as a square with a SID ([F 0]) on it.
    internal static byte[] Song(int rate = 50)
    {
        var d = new List<byte>(new byte[64]);   // the replay routine would be here
        void Bytes(params int[] b) => d.AddRange(b.Select(x => (byte)x));
        void Text(string s) => d.AddRange(Encoding.ASCII.GetBytes(s));

        Text("MYM1INST");
        var bank = new byte[32 * 64];
        void Instrument(int i, string name, int speed, int arpFlags, int vol, int arp, int mix, int timer)
        {
            int at = i * 64;
            Encoding.Latin1.GetBytes(name).CopyTo(bank, at);
            bank[at + 17] = (byte)arpFlags;
            bank[at + 32] = (byte)speed;
            bank[at + 37] = 0x20;
            bank[at + 48] = (byte)vol;
            bank[at + 49] = (byte)arp;
            bank[at + 51] = (byte)mix;
            bank[at + 54] = (byte)timer;
        }
        Instrument(0, "Lead", 1, 4, 1, 2, 3, 0);
        Instrument(1, "Bass", 1, 0, 1, 0, 4, 5);
        d.AddRange(bank);
        d.AddRange(new byte[32]);   // the digidrums' lengths: none
        void Sequence(int repeat, params int[] words)
        {
            var s = new byte[128];
            for (int k = 0; k < words.Length; k++) { s[k * 2] = (byte)(words[k] >> 8); s[k * 2 + 1] = (byte)words[k]; }
            s[126] = (byte)words.Length;
            s[127] = (byte)repeat;
            d.AddRange(s);
        }
        Sequence(0, 0);                 // 0: not used
        Sequence(1, 0xF, 0xC);          // 1: volume, staying at C
        Sequence(0, 0, 12);             // 2: arpeggio, round and round
        Sequence(0, 0x100);             // 3: mixer, the tone
        Sequence(0, 0x105);             // 4: mixer, the tone and a SID
        Sequence(0, 0xF, 0);            // 5: the SID's steps

        Text("MYM1TRAK");
        var state = new byte[64];
        state[13] = 6;                  // song speed
        state[14] = (byte)rate;
        state[36] = 7;                  // timers on all channels
        d.AddRange(state);
        var order = new byte[1024 + 2];
        order[0] = 0; order[1] = 1; order[2] = 0xFF; order[3] = 0xFF;
        order[1024] = 1;                // length
        d.AddRange(order);
        // Pattern 0: A-4 Lead with S03, three empty lines, a note off, 59 empty lines
        Bytes(69, 1, 0, 'S', 3, 0, 0, 3);
        Bytes(1, 0, 0, 0, 0, 0, 0, 59);
        // Pattern 1: A-2 Bass, 63 empty lines
        Bytes(45, 2, 0, 0, 0, 0, 0, 63);
        Text("MYM1DIGI");
        return d.ToArray();
    }

    [Fact]
    public void The_song_is_found_with_its_instruments_sequences_order_and_patterns()
    {
        var m = MaxYMiserSong.Find(Song())!;
        Assert.Equal(1, m.Version);
        Assert.Equal(("Lead", "Bass"), (m.Instruments[0].Name, m.Instruments[1].Name));
        Assert.Equal(new[] { 0xF, 0xC }, m.Sequences[1].Steps);
        Assert.Equal(1, m.Sequences[1].Repeat);
        Assert.Equal((6, 50, 1), (m.Speed, m.Rate, m.Length));
        Assert.Equal(new[] { 0, 1, 0xFF, 0xFF }, Enumerable.Range(0, 4).Select(t => m.Order[0, t]));
        // The empty lines after each one are counted: the note off is on line 4
        Assert.Equal((69, 'S', 3), (m.Patterns[0][0].Note, m.Patterns[0][0].Effect1, m.Patterns[0][0].Param1));
        Assert.Equal(1, m.Patterns[0][4].Note);
        Assert.True(m.Patterns[0][5].Empty);
        Assert.Equal(45, m.Patterns[1][0].Note);
        Assert.Null(MaxYMiserSong.Find(new byte[2000]));
    }

    [Fact]
    public void A_song_is_written_back_as_it_was_with_only_its_changes()
    {
        var file = Song();
        var m = MaxYMiserSong.Find(file)!;
        Assert.Equal(file, m.Write());

        // A note more in pattern 1 and a sequence more: read back as they were made
        m.Patterns[1][10] = new MaxYMiserSong.Line(57, 2, 0, 'S', 4, '\0', 0);
        m.Sequences.Add(new MaxYMiserSong.Sequence { Steps = new[] { 0xF, 0xA, 5 }, Repeat = 2 });
        m.Instruments[1].Name = "Bass 2";
        var back = MaxYMiserSong.Find(m.Write())!;
        Assert.Equal((57, 2, 'S', 4), (back.Patterns[1][10].Note, back.Patterns[1][10].Instrument, back.Patterns[1][10].Effect1, back.Patterns[1][10].Param1));
        Assert.Equal(45, back.Patterns[1][0].Note);                  // what was there before is still there
        Assert.Equal(new[] { 0xF, 0xA, 5 }, back.Sequences[^1].Steps);
        Assert.Equal("Bass 2", back.Instruments[1].Name);
        Assert.Equal(69, back.Patterns[0][0].Note);
    }

    [Fact]
    public void The_notes_and_instruments_become_the_trackers_as_written()
    {
        var r = YmImport.ImportMaxYMiser(MaxYMiserSong.Find(Song())!, "test")!;
        var song = r.Song;
        var p = song.Composed(0);
        Assert.Equal(3, song.Channels);
        Assert.Equal((6, 125), (song.Speed, song.Tempo));

        var lead = p[0, 0];
        Assert.Equal(69, lead.Note);
        Assert.Equal(('F', 3), (lead.Effect, lead.Param));   // the song's speed, in the channel's free column
        var inst = song.Instruments[lead.Instrument - 1];
        Assert.Equal("Lead", inst.Name);
        // The arpeggio goes round, the volume falls from F to C and stays
        Assert.Equal(new[] { "SQUARE", "SQUARE +12" }, inst.Table.Take(2));
        Assert.True(inst.TableLoop >= 0);
        Assert.Equal(64, inst.Envelope[0].Level);
        Assert.True(inst.Envelope[^1].Level is > 10 and < 30);

        // The note off silences the channel
        Assert.Equal(0, p[4, 0].Volume);
        Assert.Equal(TrackerCell.NoNote, p[4, 0].Note);

        // The square with the SID on it: a pulse at the note
        var bass = p[0, 1];
        Assert.Equal(45, bass.Note);
        var b = song.Instruments[bass.Instrument - 1];
        Assert.Equal("Bass", b.Name);
        Assert.StartsWith("PULSE P25", b.Table[0]);
        Assert.Contains("maxYMiser", r.Report);
    }

    [Fact]
    public void A_replay_faster_than_the_trackers_ticks_keeps_the_rows_as_long()
    {
        var song = YmImport.ImportMaxYMiser(MaxYMiserSong.Find(Song(rate: 200))!, "fast")!.Song;
        // Six calls of 1/200 s a row: three ticks of the tracker at tempo 250 (100 a second)
        Assert.Equal((3, 250), (song.Speed, song.Tempo));
        Assert.Equal(6.0 / 200, song.Speed / (song.Tempo * 0.4), 3);
    }
}
