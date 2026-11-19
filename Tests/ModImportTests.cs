using System.Numerics;
using System.Runtime.InteropServices;
using ManagedBass;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// ProTracker and FastTracker 2 modules read into tracker songs
public class ModImportTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "amos-modtest-" + Guid.NewGuid().ToString("N"));

    public ModImportTests()
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
        try { Directory.Delete(_temp, true); } catch { /* best effort */ }
    }

    // A small "M.K." module: sample 1 a looping square, sample 2 empty; one pattern
    private static byte[] TinyMod(Action<byte[], int, int, int, int, int> cell)
    {
        var m = new byte[1084 + 1024 + 64];
        "tiny tune"u8.CopyTo(m);
        void Sample(int s, string name, int words, int vol, int loopStart, int loopLen)
        {
            int o = 20 + (s - 1) * 30;
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(m, o);
            m[o + 22] = (byte)(words >> 8); m[o + 23] = (byte)words;
            m[o + 25] = (byte)vol;
            m[o + 26] = (byte)(loopStart >> 8); m[o + 27] = (byte)loopStart;
            m[o + 28] = (byte)(loopLen >> 8); m[o + 29] = (byte)loopLen;
        }
        Sample(1, "square", 32, 48, 0, 32);   // 64 frames, looped all through
        m[950] = 1;                           // song length 1, pattern 0
        "M.K."u8.CopyTo(m.AsSpan(1080));
        // Writes a cell: row, channel, sample, period, effect, param
        void Put(byte[] b, int row, int ch, int sample, int period, int effect, int param)
        {
            int o = 1084 + (row * 4 + ch) * 4;
            b[o] = (byte)((sample & 0xF0) | (period >> 8)); b[o + 1] = (byte)period;
            b[o + 2] = (byte)(((sample & 15) << 4) | effect); b[o + 3] = (byte)param;
        }
        cell(m, 0, 0, 0, 0, 0);
        for (int i = 0; i < 64; i++) m[1084 + 1024 + i] = (byte)(i % 16 < 8 ? 100 : 156);   // the square: +100, -100
        return m;
    }

    private static void Put(byte[] b, int row, int ch, int sample, int period, int effect, int param)
    {
        int o = 1084 + (row * 4 + ch) * 4;
        b[o] = (byte)((sample & 0xF0) | (period >> 8)); b[o + 1] = (byte)period;
        b[o + 2] = (byte)(((sample & 15) << 4) | effect); b[o + 3] = (byte)param;
    }

    [Fact]
    public void Notes_samples_and_effects_are_read()
    {
        var mod = TinyMod((_, _, _, _, _, _) => { });
        Put(mod, 0, 0, 1, 428, 0xC, 0x20);   // C-2 (period 428), volume 32
        Put(mod, 1, 0, 0, 214, 0x1, 0x02);   // an octave up, slide up 2 periods a tick
        Put(mod, 2, 1, 2, 428, 0, 0);        // sample 2 is empty: silence
        Put(mod, 3, 2, 0, 428, 0, 0);        // a note on a channel that has had no sample: silence
        Put(mod, 4, 0, 0, 0, 0xE, 0x93);     // E93 retrigger
        Put(mod, 5, 0, 0, 0, 0xE, 0x01);     // E01 (the Amiga's filter) is not in the tracker

        var r = ModImport.Import(mod, "tiny", _temp, _temp);
        var song = r.Song;
        Assert.Equal("tiny tune", song.Title);
        Assert.Equal(1, r.Samples);
        var inst = Assert.Single(song.Instruments);
        Assert.Equal(("square", "Sample", 60, 48, 0, 64), (inst.Name, inst.Wave, inst.SampleNote, inst.DefaultVolume, inst.LoopStart, inst.LoopEnd));
        Assert.True(File.Exists(inst.Sample));

        var p = song.Patterns[0];
        Assert.Equal("C-4 01 .. C20", p[0, 0].ToString());
        Assert.Equal(72, p[1, 0].Note);                    // an octave up
        Assert.Equal('1', p[1, 0].Effect);
        Assert.InRange(p[1, 0].Param, 2, 4);                // 2 periods at period 214 ≈ 0.16 semitone ≈ 3 sixteenths
        Assert.Equal(TrackerCell.NoteOff, p[2, 1].Note);
        Assert.Equal(TrackerCell.NoteOff, p[3, 2].Note);
        Assert.Equal(('E', 0x93), (p[4, 0].Effect, p[4, 0].Param));
        Assert.Equal('\0', p[5, 0].Effect);
        Assert.Contains(r.Notes, n => n.Contains("E0x"));
    }

    [Fact]
    public void An_XM_with_22_channels_keeps_them_all()
    {
        var r = XmImport.Import(File.ReadAllBytes(Repo.PathOf("Sound/Tracker/XM/Cube.XM")), "cube", _temp, _temp);
        Assert.Equal(22, r.Song.Channels);
        Assert.DoesNotContain(r.Notes, n => n.Contains("were kept"));
    }

    [Fact]
    public void Not_a_mod_file_says_so()
    {
        var e = Assert.Throws<InvalidDataException>(() => ModImport.Import(new byte[2000], "x", _temp, _temp));
        Assert.Contains("not a ProTracker MOD", e.Message);
    }

    // ── Played as BASS plays the same module ───────────────────────────

    private const int N = 1 << 18;   // about 6 s

    private static double[] Bands(float[] x)
    {
        var c = new Complex[N];
        for (int i = 0; i < N; i++) c[i] = i < x.Length ? x[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)) : 0;
        for (int i = 1, j = 0; i < N; i++) { int bit = N >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) (c[i], c[j]) = (c[j], c[i]); }
        for (int len = 2; len <= N; len <<= 1)
        {
            var w = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (int i = 0; i < N; i += len) { var wn = Complex.One; for (int k = 0; k < len / 2; k++) { var u = c[i + k]; var v = c[i + k + len / 2] * wn; c[i + k] = u + v; c[i + k + len / 2] = u - v; wn *= w; } }
        }
        var bands = new List<double>();
        for (double f = 50; f < 10000; f *= Math.Pow(2, 1 / 3.0))
        {
            int a = (int)(f * N / 44100), b = (int)(f * Math.Pow(2, 1 / 3.0) * N / 44100);
            double e = 0;
            for (int k = a; k < b; k++) e += c[k].Magnitude * c[k].Magnitude;
            bands.Add(Math.Log10(e + 1e-9));
        }
        return bands.ToArray();
    }

    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), s = 0, sa = 0, sb = 0;
        for (int i = 0; i < a.Length; i++) { s += (a[i] - ma) * (b[i] - mb); sa += (a[i] - ma) * (a[i] - ma); sb += (b[i] - mb) * (b[i] - mb); }
        return s / Math.Sqrt(sa * sb);
    }

    [Theory]
    [InlineData("Sound/Tracker/MODs/Introgasm.MOD")]
    [InlineData("Sound/Tracker/MODs/GruftSong.MOD")]
    [InlineData("Sound/Tracker/XM/Arcadian.XM")]
    [InlineData("Sound/Tracker/XM/Introghost.XM")]   // Amiga periods
    public void A_module_sounds_and_lasts_as_in_BASS(string file)
    {
        var bytes = File.ReadAllBytes(Repo.PathOf(file));
        Bass.Init(0, 44100);   // the "no sound" device, for decoding
        var flags = BassFlags.Decode | BassFlags.Float | BassFlags.Mono | BassFlags.Prescan | (XmImport.IsXm(bytes) ? 0 : BassFlags.MusicPT1Mod);
        int h = Bass.MusicLoad(bytes, 0, bytes.Length, flags, 44100);
        Assert.NotEqual(0, h);
        double bassSeconds = Bass.ChannelBytes2Seconds(h, Bass.ChannelGetLength(h));
        var bass = new float[N];
        var pin = GCHandle.Alloc(bass, GCHandleType.Pinned);
        Bass.ChannelGetData(h, pin.AddrOfPinnedObject(), N * 4);
        pin.Free();
        Bass.MusicFree(h);

        var song = (XmImport.IsXm(bytes) ? XmImport.Import(bytes, "xm", _temp, _temp) : ModImport.Import(bytes, "mod", _temp, _temp)).Song;
        S.TrackPlay(song, loop: false);
        var ours = new float[N];
        S.Render(ours);
        var chunk = new float[4410];
        int chunks = N / 4410;
        while (S.IsTrackPlaying() && chunks < 6000) { S.Render(chunk); chunks++; }

        Assert.InRange(chunks * 0.1, bassSeconds - 0.3, bassSeconds + 0.3);   // tempo, jumps and loops
        Assert.True(Correlation(Bands(bass), Bands(ours)) > 0.94, "the pitches sound as in BASS");
    }

    // ── XM ─────────────────────────────────────────────────────────────

    // A small XM: one instrument with two samples (keys below C-4 play the first), an envelope,
    // and one pattern of four rows, every cell written out in full
    private static byte[] TinyXm(Action<(int note, int inst, int vol, int eff, int par)[,]> fill)
    {
        var b = new List<byte>();
        void Bytes(params int[] v) { foreach (var x in v) b.Add((byte)x); }
        void Text(string t, int n) { var a = new byte[n]; System.Text.Encoding.ASCII.GetBytes(t).CopyTo(a, 0); b.AddRange(a); }
        void U16(int v) => Bytes(v & 255, v >> 8);
        void I32(int v) => Bytes(v & 255, (v >> 8) & 255, (v >> 16) & 255, v >> 24);

        Text("Extended Module: ", 17); Text("tiny xm", 20); Bytes(0x1A); Text("test", 20); U16(0x0104);
        I32(276); U16(1); U16(0); U16(4); U16(1); U16(1); U16(1); U16(6); U16(125);
        b.AddRange(new byte[256]);                                    // order: pattern 0

        var cells = new (int note, int inst, int vol, int eff, int par)[4, 4];
        fill(cells);
        I32(9); Bytes(0); U16(4); U16(4 * 4 * 5);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++) Bytes(cells[r, c].note, cells[r, c].inst, cells[r, c].vol, cells[r, c].eff, cells[r, c].par);

        int start = b.Count;
        I32(263); Text("lead", 22); Bytes(0); U16(2); I32(40);
        for (int k = 0; k < 96; k++) Bytes(k < 48 ? 0 : 1);           // keymap
        int[] env = { 0, 64, 10, 32, 20, 0 };
        for (int k = 0; k < 24; k++) U16(k < env.Length ? env[k] : 0);   // volume envelope
        b.AddRange(new byte[48]);                                        // panning envelope
        Bytes(3, 0, 1, 0, 0, 0, 0, 0, 1 | 2, 0, 0, 0, 0, 0); U16(0x400);
        while (b.Count < start + 263) Bytes(0);

        // "low": 8-bit, 16 frames, a forward loop; "high": 16-bit, 16 frames, ping-pong 4 – 12, an octave up
        I32(16); I32(0); I32(16); Bytes(48, 0, 1, 128, 0, 0); Text("low", 22);
        I32(32); I32(8); I32(16); Bytes(64, 0, 2 | 16, 255, 12, 0); Text("high", 22);
        int last = 0;
        for (int f = 0; f < 16; f++) { int v = f % 4 < 2 ? 80 : -80; Bytes((v - last) & 255); last = v; }
        last = 0;
        for (int f = 0; f < 16; f++) { int v = f * 1000; U16((v - last) & 0xFFFF); last = v; }
        return b.ToArray();
    }

    [Fact]
    public void An_XM_file_is_read()
    {
        var xm = TinyXm(c =>
        {
            c[0, 0] = (37, 1, 0x30, 0, 0);       // C-3, the low sample, volume 20 from the volume column
            c[1, 0] = (61, 0, 0, 1, 8);          // C-5 without an instrument: the high sample; slide up 8
            c[2, 0] = (97, 0, 0, 0, 0);          // key off
            c[0, 1] = (49, 1, 0x18, 0, 0x37);    // C-4 (the high sample), volume 8 and an arpeggio
            c[1, 1] = (0, 0, 0x65, 0, 0);        // volume slide down 5 (in the effect column)
            c[3, 1] = (0, 0, 0, 0x10, 0x20);     // Gxx (global volume) is not in the tracker
        });

        var r = XmImport.Import(xm, "tiny", _temp, _temp);
        var song = r.Song;
        Assert.Equal(("tiny xm", 6, 125), (song.Title, song.Speed, song.Tempo));
        Assert.Equal(new[] { "lead low", "lead high" }, song.Instruments.Select(i => i.Name));

        var low = song.Instruments[0];
        Assert.Equal((60, 48, 0, 16), (low.SampleNote, low.DefaultVolume, low.LoopStart, low.LoopEnd));
        // The envelope as it is; the fadeout of 0x400 takes 32 ticks of 20 ms
        Assert.Equal(new[] { (0, 64), (10, 32), (20, 0) }, low.Envelope.Select(p => (p.Tick, p.Level)));
        Assert.Equal((1, -1, -1, 640.0), (low.EnvelopeSustain, low.EnvelopeLoopStart, low.EnvelopeLoopEnd, low.FadeOut));

        var high = song.Instruments[1];
        Assert.Equal(48, high.SampleNote);                        // relative note +12
        Assert.Equal((4, 18), (high.LoopStart, high.LoopEnd));    // 4 – 12 and back: 12 + 6 frames
        var wav = SampleBank.ReadWav(File.ReadAllBytes(high.Sample));
        Assert.Equal(18, wav.Data.Length);
        Assert.Equal(3000 / 32768f, wav.Data[3], 3);              // 16-bit deltas added up
        Assert.Equal(wav.Data[10], wav.Data[12], 3);              // and back again

        var p = song.Patterns[0];
        Assert.Equal("C-3 01 20 ...", p[0, 0].ToString());
        Assert.Equal((72, 2, '1', 8), (p[1, 0].Note, p[1, 0].Instrument, p[1, 0].Effect, p[1, 0].Param));
        Assert.Equal(TrackerCell.NoteOff, p[2, 0].Note);
        Assert.Equal("C-4 02 08 037", p[0, 1].ToString());
        Assert.Equal("--- .. .. A05", p[1, 1].ToString());
        Assert.Contains(r.Notes, n => n.Contains("Gxx"));
    }
}
