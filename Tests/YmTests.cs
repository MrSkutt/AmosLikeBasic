using Avalonia.VisualTree;
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

    // A YM5 (or YM6) file with its header, digidrums (8-bit) and texts, registers interleaved
    private static byte[] Ym5(byte[][] frames, int loop = 0, byte[][]? drums = null, int version = 5)
    {
        var o = new List<byte>();
        void Text(string s) { o.AddRange(Encoding.ASCII.GetBytes(s)); o.Add(0); }
        void U32(int v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }
        void U16(int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
        o.AddRange(System.Text.Encoding.ASCII.GetBytes($"YM{version}!LeOnArD!"));
        drums ??= Array.Empty<byte[]>();
        U32(frames.Length); U32(1); U16(drums.Length); U32(2000000); U16(50); U32(loop); U16(0);
        foreach (var d in drums) { U32(d.Length); o.AddRange(d); }
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

    // ── YM5 / YM6 effects ──

    // A digidrum: 4000 frames of a square wave between levels 0.2 and 0.8, hit on channel C in frame 10.
    // In YM5 register 3's bits 4 – 5 pick the channel, the volume register the drum, and register 8's top
    // bits with register 15 the timer: predivisor 4 (bits 001) × count 61 = 2.4576 MHz / 244 ≈ 10 kHz.
    private static (YmFile ym, byte[] file) DrumTune()
    {
        var drum = Enumerable.Range(0, 4000).Select(i => (byte)(i / 20 % 2 == 0 ? 51 : 204)).ToArray();
        var frames = new List<byte[]>();
        for (int f = 0; f < 50; f++)
        {
            var r = Frame(a: 284, mixer: 0x3E, va: 15);
            if (f == 10) { r[3] |= 0x30; r[10] = 0; r[8] |= 1 << 5; r[15] = 61; }
            frames.Add(r);
        }
        var file = Ym5(frames.ToArray(), drums: new[] { drum });
        return (YmFile.Read(file), file);
    }

    [Fact]
    public void A_digidrum_is_read_and_played()
    {
        var (ym, _) = DrumTune();
        Assert.Equal(5, ym.Version);
        var drum = Assert.Single(ym.Drums);
        Assert.Equal(4000, drum.Length);
        Assert.Equal((0.2f, 0.8f), (drum[0], drum[20]), new Tolerance());

        var withDrum = new float[SR / 2];
        new YmPlayer(ym, SR).Render(withDrum);
        ym.Drums.Clear();
        var without = new float[SR / 2];
        new YmPlayer(YmFile.Read(Ym5(ym.Frames)), SR).Render(without);
        // Frame 10 is 0.2 s in and the drum lasts 4000 / 10 kHz = 0.4 s: heard on top of the tone then, not before
        float[] Difference(int from, int length) => Enumerable.Range(from, length).Select(i => withDrum[i] - without[i]).ToArray();
        Assert.True(Rms(Difference((int)(0.25 * SR), SR / 10)) > 0.05, "the drum is heard");
        Assert.True(Rms(Difference((int)(0.05 * SR), SR / 10)) < 1e-4, "and not before it is hit");
    }

    private sealed class Tolerance : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) a, (float, float) b) => Math.Abs(a.Item1 - b.Item1) < 0.01 && Math.Abs(a.Item2 - b.Item2) < 0.01;
        public int GetHashCode((float, float) o) => 0;
    }

    [Fact]
    public void A_digidrum_becomes_a_sampled_instrument_and_a_note()
    {
        var (ym, _) = DrumTune();
        string folder = Path.Combine(Path.GetTempPath(), "amos-ymdrum-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (bool analyse in new[] { false, true })
            {
                var r = YmImport.Import(ym, "drum", analyse, folder, folder);
                var song = r.Song;
                var drumInst = song.Instruments.FindIndex(i => i.Name == "Digidrum 1") + 1;
                Assert.True(drumInst > 0, "a Digidrum instrument");
                Assert.Equal("Sample", song.Instruments[drumInst - 1].Wave);
                Assert.True(File.Exists(song.Instruments[drumInst - 1].Sample));
                // The hit: the drum's note on channel 3, in the row of frame 10
                var cells = song.Order.SelectMany(o => Enumerable.Range(0, song.Patterns[o].Rows).Select(row => song.Patterns[o][row, 2])).ToList();
                var hit = cells.Single(c => c.Instrument == drumInst);
                Assert.Equal(60, hit.Note);
                Assert.Contains("digidrum", r.Report!);
            }
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    [Fact]
    public void A_SID_voice_sounds_at_half_its_timer()
    {
        // YM6: register 1's top bits 0x10 = a SID voice on channel A; register 6's top bits 001 (predivisor 4)
        // and register 14 = 140 make the timer 2.4576 MHz / 560 = 4389 Hz, heard as 2194 Hz. The tone is
        // off, so the SID voice is the sound.
        var frames = Enumerable.Range(0, 100).Select(_ =>
        {
            var r = Frame(mixer: 0x3F, va: 15);
            r[1] |= 0x10;
            r[6] = 1 << 5;
            r[14] = 140;
            return r;
        }).ToArray();
        var ym = YmFile.Read(Ym5(frames, version: 6));
        var b = new float[SR];
        new YmPlayer(ym, SR).Render(b);
        Assert.InRange(Frequency(b), 2160, 2230);

        var song = YmImport.Import(ym, "sid").Song;
        var first = song.Patterns[song.Order[0]][0, 0];
        Assert.InRange(first.Note, 96, 97);   // 2194 Hz is between C#7 (97) and C-7 (96)
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_YM_browser_lists_plays_and_opens_songs()
    {
        string root = Path.Combine(Path.GetTempPath(), "amos-ymbrowse-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tune = Ym5(Enumerable.Range(0, 100).Select(_ => Frame(a: 284, mixer: 0x3E, va: 15)).ToArray());
            Directory.CreateDirectory(Path.Combine(root, "Hippel", "Games"));
            Directory.CreateDirectory(Path.Combine(root, "Tao"));
            File.WriteAllBytes(Path.Combine(root, "Hippel", "Games", "Wings.ym"), tune);
            File.WriteAllBytes(Path.Combine(root, "Hippel", "Games", "Tiles.ym"), tune);
            File.WriteAllBytes(Path.Combine(root, "Tao", "Stars.ym"), tune);
            File.WriteAllText(Path.Combine(root, "Tao", "readme.txt"), "not a song");

            string? opened = null;
            var w = new YmBrowserWindow((p, _) => opened = p, root);
            w.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var lists = w.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().ToList();
            var folders = lists[0].ItemsSource!.Cast<object>().Select(o => o.ToString()).ToList();
            Assert.Equal(new[] { "Hippel/Games (2)", "Tao (1)" }, folders);
            Assert.Equal(new[] { "Tiles", "Wings" }, lists[1].ItemsSource!.Cast<object>().Select(o => o.ToString()));

            // Only SNDH files: these YM files go
            var boxes = w.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>().ToList();
            boxes.First(b => b.Content as string == "YM").IsChecked = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(lists[0].ItemsSource!.Cast<object>());
            boxes.First(b => b.Content as string == "YM").IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, lists[0].ItemsSource!.Cast<object>().Count());

            // A search looks through every folder
            w.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First().Text = "star";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Tao/Stars" }, lists[1].ItemsSource!.Cast<object>().Select(o => o.ToString()));

            // Choosing it plays it at once
            lists[1].SelectedIndex = 0;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var sound = new float[SR / 4];
            S.Render(sound);
            Assert.InRange(Frequency(sound), 430, 450);

            w.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => b.Content as string == "Open in tracker")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(Path.Combine(root, "Tao", "Stars.ym"), opened);
            S.Render(sound);
            Assert.True(Rms(sound) < 1e-3, "opening it stops the playing");
            w.Close();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
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
