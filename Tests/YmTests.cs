using System.Text;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// YM files (the Atari ST's sound chip, recorded): reading, the chip and the tracker conversion
public class YmTests : IDisposable
{
    private const int SR = 44100;

    public YmTests()
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

    // A frame: tone periods A, B, C, noise, mixer, volumes A, B, C, envelope period and shape (0xFF: not written)
    private static byte[] Frame(int a = 0, int b = 0, int c = 0, int noise = 0, int mixer = 0x3F, int va = 0, int vb = 0, int vc = 0,
                                int env = 0, int shape = 0xFF) =>
        new[] { (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8), (byte)c, (byte)(c >> 8), (byte)noise, (byte)mixer,
                (byte)va, (byte)vb, (byte)vc, (byte)env, (byte)(env >> 8), (byte)shape, (byte)0, (byte)0 };

    private static double Frequency(float[] b)
    {
        int crossings = 0;
        for (int i = 1; i < b.Length; i++) if (b[i - 1] < 0 && b[i] >= 0) crossings++;
        return crossings / (b.Length / (double)SR);
    }

    private static double Rms(float[] b) => Math.Sqrt(b.Average(x => (double)x * x));

    // A YM5 file with its header and texts, registers interleaved
    private static byte[] Ym5(byte[][] frames, int loop = 0)
    {
        var o = new List<byte>();
        void Text(string s) { o.AddRange(Encoding.ASCII.GetBytes(s)); o.Add(0); }
        void U32(int v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }
        void U16(int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
        o.AddRange("YM5!LeOnArD!"u8.ToArray());
        U32(frames.Length); U32(1); U16(0); U32(2000000); U16(50); U32(loop); U16(0);
        Text("Test tune"); Text("Me"); Text("Made in a test");
        for (int r = 0; r < 16; r++) foreach (var f in frames) o.Add(f[r]);
        o.AddRange("End!"u8.ToArray());
        return o.ToArray();
    }

    [Fact]
    public void YM5_and_YM3_files_are_read()
    {
        var frames = new[] { Frame(a: 284, mixer: 0x3E, va: 15), Frame(a: 142, mixer: 0x3E, va: 10) };
        var ym = YmFile.Read(Ym5(frames, loop: 1));
        Assert.Equal((2, 2000000, 50, 1, "Test tune", "Me"), (ym.Frames.Length, ym.Clock, ym.FrameRate, ym.LoopFrame, ym.Title, ym.Author));
        Assert.Equal(frames[1], ym.Frames[1]);

        // YM3: 14 registers, interleaved, no header
        var ym3 = new List<byte>("YM3!"u8.ToArray());
        for (int r = 0; r < 14; r++) foreach (var f in frames) ym3.Add(f[r]);
        var old = YmFile.Read(ym3.ToArray());
        Assert.Equal(2, old.Frames.Length);
        Assert.Equal(frames[0].Take(14), old.Frames[0].Take(14));
        Assert.Throws<InvalidDataException>(() => YmFile.Read("NOPE...."u8.ToArray()));
    }

    [Fact]
    public void The_chip_plays_tones_and_the_buzzer_at_their_pitch()
    {
        var b = new float[SR];
        var chip = new Ym2149(2000000, SR);
        chip.WriteFrame(Frame(a: 284, mixer: 0x3E, va: 15));    // 2 MHz / (16 × 284) = 440 Hz
        for (int i = 0; i < b.Length; i++) b[i] = (float)chip.Next();
        Assert.InRange(Frequency(b), 437, 443);

        chip = new Ym2149(2000000, SR);
        chip.WriteFrame(Frame(va: 16, env: 18, shape: 8));       // sawtooth: 2 MHz / (256 × 18) = 434 Hz
        for (int i = 0; i < b.Length; i++) b[i] = (float)chip.Next();
        Assert.InRange(Frequency(b), 431, 437);
        chip.WriteFrame(Frame(va: 16, env: 18, shape: 10));      // triangle: half of that
        for (int i = 0; i < b.Length; i++) b[i] = (float)chip.Next();
        Assert.InRange(Frequency(b), 214, 220);

        chip = new Ym2149(2000000, SR);
        chip.WriteFrame(Frame(a: 284, mixer: 0x3F, va: 15));     // the mixer lets nothing through
        for (int i = 0; i < b.Length; i++) b[i] = (float)chip.Next();
        Assert.True(Rms(b.AsSpan(4410).ToArray()) < 1e-3);
    }

    [Fact]
    public void A_YM_file_becomes_a_tracker_song_that_sounds_the_same()
    {
        // Two seconds of A-4, then two of a slightly sharp E-5 at half volume, then noise
        var frames = new List<byte[]>();
        for (int i = 0; i < 100; i++) frames.Add(Frame(a: 284, mixer: 0x3E, va: 15));
        for (int i = 0; i < 100; i++) frames.Add(Frame(a: 187, mixer: 0x3E, va: 13));   // 668 Hz: E-5 + about 4/16
        for (int i = 0; i < 50; i++) frames.Add(Frame(noise: 8, mixer: 0x37, va: 15));
        for (int i = 0; i < 20; i++) frames.Add(Frame(va: 16, env: 18, shape: i == 0 ? 8 : 0xFF));
        var ym = new YmFile { Frames = frames.ToArray() };
        var song = YmImport.Import(ym, "test", analyse: false).Song;

        Assert.Equal((1, 125), (song.Speed, song.Tempo));                // one row per frame, 50 a second
        Assert.Equal(frames.Count, song.Patterns.Sum(p => p.Rows));
        var p0 = song.Patterns[0];
        Assert.Equal("A-4 01 40 ...", p0[0, 0].ToString());
        Assert.True(p0[1, 0].IsEmpty, "nothing changes, nothing is written");
        Assert.Equal(76, p0[100, 0].Note);
        Assert.Equal(('E', 0x14), (p0[100, 0].Effect, p0[100, 0].Param));   // a fine slide up the rest of the way
        Assert.InRange(p0[100, 0].Volume, 30, 34);                          // two steps of 3 dB down: half
        Assert.Equal(2, p0[200, 0].Instrument);                            // noise
        Assert.Equal(4, p0[250, 0].Instrument);                            // the buzzer as a sawtooth
        Assert.Equal(69, p0[250, 0].Note);                                 // 434 Hz is A-4 a little flat

        // The song and the chip give the same pitch
        var chip = new float[SR];
        new YmPlayer(ym, SR).Render(chip);
        S.TrackPlay(song, loop: false);
        var ours = new float[SR];
        S.Render(ours);
        Assert.InRange(Frequency(ours.AsSpan(4410, 30000).ToArray()), 435, 445);
        Assert.InRange(Rms(ours) / Rms(chip), 0.7, 1.4);
    }

    [Fact]
    public void A_loop_in_the_middle_jumps_back_there()
    {
        var frames = Enumerable.Range(0, 300).Select(i => Frame(a: 284, mixer: 0x3E, va: 15)).ToArray();
        var song = YmImport.Import(new YmFile { Frames = frames, LoopFrame = 100 }, "loop", analyse: false).Song;
        // The part before the loop is a pattern of its own, so the jump lands on the loop frame
        Assert.Equal(100, song.Patterns[0].Rows);
        var last = song.Patterns[^1];
        Assert.Equal(('B', 1), (last[last.Rows - 1, 3].Effect, last[last.Rows - 1, 3].Param));
    }

    // A tune as an ST music routine plays it: a note every 6 frames, each an arpeggio (0, 4, 7) that
    // fades, with the melody going round eight times
    private static YmFile Arpeggios()
    {
        int[] melody = { 284, 253, 225, 213, 284, 190, 213, 253, 284, 253, 225, 213, 190, 213, 253, 284 };
        var frames = new List<byte[]>();
        for (int round = 0; round < 8; round++)
            foreach (int period in melody)
                for (int f = 0; f < 6; f++)
                {
                    // An arpeggio up 4 and 7 semitones, and the volume one step down each frame
                    double up = f % 3 == 1 ? 4 : f % 3 == 2 ? 7 : 0;
                    int p = (int)Math.Round(period / Math.Pow(2, up / 12));
                    frames.Add(Frame(a: p, b: period * 2, mixer: 0x3C, va: 15 - f, vb: 12));
                }
        return new YmFile { Frames = frames.ToArray() };
    }

    [Fact]
    public void The_analysis_finds_the_rows_the_instruments_and_the_repeats()
    {
        var ym = Arpeggios();
        var r = YmImport.Import(ym, "arp");
        var song = r.Song;
        Assert.Equal(6, song.Speed);                                    // a note every 6 frames
        // One arpeggio instrument (whatever the note), one held note for the bass
        Assert.Equal(2, song.Instruments.Count);
        var arp = song.Instruments.Single(i => i.Name.StartsWith("Arp"));
        Assert.Equal(new[] { "SQUARE", "SQUARE +4", "SQUARE +7" }, arp.Table.Take(3).Select(t => t.Split(' ', 2) is var w && w.Length > 1 ? w[0] + " " + double.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture).ToString("+0;-0") : w[0]));
        Assert.Equal(64, arp.Envelope[0].Level);
        Assert.True(arp.Envelope[^1].Level < 64, "it fades");
        // The melody: A-4 on the first row, and the second round is the same pattern again
        Assert.Equal(69, song.Patterns[song.Order[0]][0, 0].Note);
        Assert.Equal(128, song.Order.Sum(o => song.Patterns[o].Rows));
        Assert.True(song.Patterns.Count < song.Order.Count, "the repeat is written once");

        // It sounds as the frame-by-frame song does
        float[] Play(TrackerSong t)
        {
            S.TrackPlay(t, loop: false);
            var o = new float[SR * 2];
            S.Render(o);
            S.TrackStop();
            S.Render(new float[2205]);
            return o;
        }
        var analysed = Play(TrackerSong.FromJson(song.ToJson()));
        var dump = Play(YmImport.Import(ym, "arp", analyse: false).Song);
        Assert.InRange(Rms(analysed) / Rms(dump), 0.9, 1.1);
        Assert.InRange(Frequency(analysed.AsSpan(0, SR).ToArray()) / Frequency(dump.AsSpan(0, SR).ToArray()), 0.97, 1.03);
    }

    // An LHA-packed YM file from the Atari: only on a computer that has it
    [Fact]
    public void A_packed_YM_file_is_unpacked()
    {
        const string file = "/Users/mattias/Projects/Atari/Common/Music/dragon.ym";
        if (!File.Exists(file)) Assert.Skip("needs dragon.ym");
        var ym = YmFile.Read(File.ReadAllBytes(file));
        Assert.Equal((15744, "Dragon", 2000000, 50), (ym.Frames.Length, ym.Title, ym.Clock, ym.FrameRate));
    }
}
