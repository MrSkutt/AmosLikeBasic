using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// The ready-made instruments in Sound/Instruments: each one is read, and sounds without clipping
public class InstrumentLibraryTests : IDisposable
{
    public InstrumentLibraryTests()
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

    public static IEnumerable<object[]> Files() =>
        Directory.GetFiles(Repo.PathOf("Sound/Instruments"), "*.amosinst", SearchOption.AllDirectories)
            .Select(f => new object[] { Path.GetRelativePath(Repo.PathOf("Sound/Instruments"), f) });

    // Peak and loudness (RMS) of a note held for 0.6 s and then let go, at C-4 (C-2 for basses)
    public static (double peak, double rms) Measure(TrackerInstrument inst, bool bass)
    {
        var song = new TrackerSong { Speed = 6, Tempo = 125 };
        song.Instruments.Add(inst);
        int note = bass ? 36 : 60;
        S.TrackPreview(song, 1, note, 1);
        var held = new float[(int)(0.6 * S.SampleRate)];
        S.Render(held);
        S.TrackPreviewRelease(1);
        var after = new float[(int)(0.3 * S.SampleRate)];
        S.Render(after);
        S.TrackPreviewStop();
        var all = held.Concat(after).ToArray();
        return (all.Max(x => Math.Abs(x)), Math.Sqrt(held.Average(x => (double)x * x)));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_instrument_sounds_and_does_not_clip(string file)
    {
        var inst = InstrumentLibrary.Load(Path.Combine(Repo.PathOf("Sound/Instruments"), file));
        var (peak, rms) = Measure(inst, file.StartsWith("Bass"));
        Assert.True(rms > 0.005, $"{file} is (almost) silent: rms {rms:0.0000}");
        Assert.True(peak < 0.75, $"{file} is too loud: peak {peak:0.00}");
    }

    [Fact]
    public void Categories_come_in_synth_order()
    {
        var categories = InstrumentLibrary.Categories();
        Assert.Equal("Bass", categories[0]);
        Assert.Contains("Drums", categories);
        Assert.True(categories.IndexOf("Lead") < categories.IndexOf("FX"));
    }

    [Fact]
    public void A_saved_instrument_reads_back_the_same()
    {
        string file = Path.Combine(Path.GetTempPath(), "amos-inst-" + Guid.NewGuid().ToString("N") + InstrumentLibrary.Extension);
        try
        {
            var inst = new TrackerInstrument
            {
                Name = "Test", Wave = "Sawtooth", Osc2Wave = "Square", Osc2Detune = 9,
                Lfo1 = new TrackerLfo { Target = "Cutoff", Shape = "Random", Depth = 12, SyncRows = 4 },
                Table = new() { "+12", "0" }
            };
            InstrumentLibrary.Save(inst, file);
            var back = InstrumentLibrary.Load(file);
            Assert.Equal(("Square", 9.0, "Random", 4, 2), (back.Osc2Wave, back.Osc2Detune, back.Lfo1.Shape, back.Lfo1.SyncRows, back.Table.Count));
        }
        finally { File.Delete(file); }
    }

    // The songs made with the library (Tools/build_songs.py) play to the end without clipping
    [Theory]
    [InlineData("acid_sunrise")]
    [InlineData("deep_space")]
    [InlineData("pixel_rush")]
    public void Library_songs_play_without_clipping(string name)
    {
        var song = TrackerSong.Load(Repo.PathOf($"Sound/Tracker/{name}.amostrk"));
        S.TrackPlay(song, loop: false);
        var buf = new float[4410];
        double peak = 0, sum = 0;
        long count = 0;
        for (int i = 0; i < 1000 && S.IsTrackPlaying(); i++)
        {
            S.Render(buf);
            foreach (var x in buf) { peak = Math.Max(peak, Math.Abs(x)); sum += x * x; count++; }
        }
        Assert.False(S.IsTrackPlaying(), $"{name} did not end");
        Assert.True(peak < 0.95, $"{name} peaks at {peak:0.00}");
        Assert.InRange(Math.Sqrt(sum / count), 0.1, 0.3);
    }
}
