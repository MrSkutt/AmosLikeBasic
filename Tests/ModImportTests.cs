using System.Numerics;
using System.Runtime.InteropServices;
using ManagedBass;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// ProTracker modules read into tracker songs
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
        Assert.Equal("C-4 01 C20", p[0, 0].ToString());
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
    public void A_module_sounds_and_lasts_as_in_BASS(string file)
    {
        var bytes = File.ReadAllBytes(Repo.PathOf(file));
        Bass.Init(0, 44100);   // the "no sound" device, for decoding
        int h = Bass.MusicLoad(bytes, 0, bytes.Length, BassFlags.Decode | BassFlags.Float | BassFlags.Mono | BassFlags.Prescan | BassFlags.MusicPT1Mod, 44100);
        Assert.NotEqual(0, h);
        double bassSeconds = Bass.ChannelBytes2Seconds(h, Bass.ChannelGetLength(h));
        var bass = new float[N];
        var pin = GCHandle.Alloc(bass, GCHandleType.Pinned);
        Bass.ChannelGetData(h, pin.AddrOfPinnedObject(), N * 4);
        pin.Free();
        Bass.MusicFree(h);

        var song = ModImport.Import(bytes, Path.GetFileNameWithoutExtension(file), _temp, _temp).Song;
        S.TrackPlay(song, loop: false);
        var ours = new float[N];
        S.Render(ours);
        var chunk = new float[4410];
        int chunks = N / 4410;
        while (S.IsTrackPlaying() && chunks < 6000) { S.Render(chunk); chunks++; }

        Assert.InRange(chunks * 0.1, bassSeconds - 0.3, bassSeconds + 0.3);   // tempo, jumps and loops
        Assert.True(Correlation(Bands(bass), Bands(ours)) > 0.95, "the pitches sound as in BASS");
    }
}
