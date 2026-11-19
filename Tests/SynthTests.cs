using AmosLikeBasic;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// The synth is rendered straight into buffers and measured
public class SynthTests : IDisposable
{
    private const int SR = S.SampleRate;

    public SynthTests()
    {
        S.ManualRendering = true;
        S.Reset();
        Render(0.05);   // let the reset commands run
    }

    public void Dispose()
    {
        S.Reset();
        Render(0.05);
        S.ManualRendering = false;
    }

    private static float[] Render(double seconds)
    {
        var buffer = new float[(int)(seconds * SR)];
        S.Render(buffer);
        return buffer;
    }

    // Average pitch from upward zero crossings, interpolated between samples
    private static double Pitch(float[] b, double t0, double t1)
    {
        int a = (int)(t0 * SR), e = (int)(t1 * SR);
        double first = -1, last = -1;
        int n = 0;
        for (int i = a + 1; i < e; i++)
        {
            if (b[i - 1] <= 0 && b[i] > 0)
            {
                double x = i - 1 + b[i - 1] / (b[i - 1] - b[i]);
                if (first < 0) first = x; else n++;
                last = x;
            }
        }
        return n > 0 ? SR * n / (last - first) : 0;
    }

    private static double Rms(float[] b, double t0, double t1)
    {
        int a = (int)(t0 * SR), e = (int)(t1 * SR);
        double s = 0;
        for (int i = a; i < e; i++) s += b[i] * b[i];
        return Math.Sqrt(s / (e - a));
    }

    private static double Semitones(double hz, double baseHz) => 12 * Math.Log2(hz / baseHz);

    [Fact]
    public void FourQuartersAt120BpmTakeTwoSeconds()
    {
        S.SetBpm(120);
        S.PlayMusic(1, "C4:4 C4 C4 C4");
        Render(1.95);
        Assert.True(S.IsPlaying(1));
        Render(0.1);
        Assert.False(S.IsPlaying(1));
    }

    [Fact]
    public void A4IsTuned()
    {
        S.SetWave(1, "Sine");
        S.SetGate(1, 100);
        S.PlayMusic(1, "A4:1");
        var b = Render(0.6);
        Assert.InRange(Pitch(b, 0.1, 0.5), 438, 442);
    }

    [Fact]
    public void EffectsInTheNoteStringTakeNoTime()
    {
        S.SetBpm(120);
        S.PlayMusic(1, "{WAVE SAW; FILTER LOW 300 20} C4:4 {CUT 6000} C4 {ARP 0 4 7} C4 {ARP} C4");
        Render(1.95);
        Assert.True(S.IsPlaying(1));
        Render(0.1);
        Assert.False(S.IsPlaying(1));
    }

    [Fact]
    public void BadNoteStringEffectsGiveClearErrors()
    {
        Assert.Contains("unknown effect", Assert.Throws<FormatException>(() => S.PlayMusic(1, "{FOO 1} C4")).Message);
        Assert.Contains("needs a value", Assert.Throws<FormatException>(() => S.PlayMusic(1, "{CUT} C4")).Message);
        Assert.Throws<FormatException>(() => S.PlayMusic(1, "{CUT 300 C4"));
    }

    [Fact]
    public void BendMovesThePitch()
    {
        S.SetWave(1, "Sine");
        S.SetGate(1, 100);
        S.SetBend(1, 12);
        S.PlayMusic(1, "A4:1");
        var b = Render(0.6);
        Assert.InRange(Pitch(b, 0.1, 0.5), 875, 885);
    }

    private static TrackerSong OneChannelSong(params string[] rows)
    {
        var json = "{\"Speed\":6,\"Tempo\":125,\"Instruments\":[{\"Wave\":\"Sine\",\"Volume\":0.5,\"Attack\":0,\"Decay\":0,\"Sustain\":100}]," +
                   "\"Order\":[0],\"Patterns\":[[" + string.Join(",", rows.Select(r => "\"" + r + "\"")) + "]]}";
        return TrackerSong.FromJson(json);
    }

    [Fact]
    public void TrackerSlideUpMovesOneSemitonePerTick()
    {
        // Tempo 125 gives 50 ticks a second, so a tick is 20 ms. 110 slides one semitone per tick
        // on every tick except the first of each row.
        S.TrackPlay(OneChannelSong("A-4 01 110", "--- .. 110", "--- .. 110"), loop: false);
        var b = Render(0.36);
        Assert.InRange(Semitones(Pitch(b, 0.002, 0.02), 440), -0.1, 0.1);   // tick 0
        Assert.InRange(Semitones(Pitch(b, 0.342, 0.36), 440), 14.9, 15.1);  // tick 17: 15 slides
    }

    [Fact]
    public void TrackerTonePortamentoStopsAtTheTarget()
    {
        S.TrackPlay(OneChannelSong("A-4 01 ...", "A-5 01 3FF", "--- .. 300"), loop: false);
        var b = Render(0.36);
        Assert.InRange(Semitones(Pitch(b, 0.30, 0.36), 440), 11.9, 12.1);
    }

    [Fact]
    public void TrackerPatternBreakAndJumpFollowTheOrderList()
    {
        var song = TrackerSong.FromJson("{\"Speed\":6,\"Tempo\":125,\"Order\":[0,1,1]," +
            "\"Patterns\":[[\"A-4 01 D02\",\"--- .. ...\"],[\"--- .. ...\",\"--- .. ...\",\"C-5 01 ...\",\"--- .. B00\"]]}");
        S.TrackPlay(song, loop: true);
        var positions = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            Render(0.12);
            positions.Add($"{S.TrackPosition}:{S.TrackRow}");
        }
        Assert.Equal(new[] { "1:2", "1:3", "0:0", "1:2", "1:3", "0:0" }, positions);
    }

    [Fact]
    public void TrackerSongStopsWhenNotLooping()
    {
        S.TrackPlay(OneChannelSong("A-4 01 ...", "---", "---", "---"), loop: false);
        Render(0.45);
        Assert.True(S.IsTrackPlaying());
        Render(0.1);
        Assert.False(S.IsTrackPlaying());
    }

    public static IEnumerable<object[]> SongFiles() =>
        Directory.GetFiles(Repo.PathOf("Sound/Tracker"), "*.amostrk").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(SongFiles))]
    public void SongFilesLoadSaveAndPlay(string file)
    {
        var song = TrackerSong.Load(Repo.PathOf("Sound/Tracker/" + file));
        string json = song.ToJson();
        Assert.Equal(json, TrackerSong.FromJson(json).ToJson());

        S.TrackPlay(song);
        var b = Render(3);
        float peak = b.Max(Math.Abs);
        Assert.InRange(peak, 0.05f, 1.0f);
    }

    [Fact]
    public void BadTrackerFilesGiveClearErrors()
    {
        Assert.Contains("is not a note", Assert.Throws<FormatException>(() =>
            TrackerSong.FromJson("{\"Patterns\":[[\"H-4 01 ...\"]]}")).Message);
        Assert.Contains("pattern 3", Assert.Throws<FormatException>(() =>
            TrackerSong.FromJson("{\"Order\":[3]}")).Message);
    }

    [Fact]
    public void SoundEffectsUseChannelsFiveToEightAndGiveThemBack()
    {
        var bank = TrackerSong.Load(Repo.PathOf("Sound/Tracker/sfx.amostrk"));
        var jump = bank.Instruments.First(i => i.Name == "Jump");

        // Four sounds at once get one channel each
        var channels = Enumerable.Range(0, 4).Select(_ => S.PlayTrackSfx(jump, 1, 60)).ToArray();
        Assert.Equal(new[] { 5, 6, 7, 8 }, channels);
        Render(1);

        // Channel 5 had a sawtooth set up with PLAY before the effect borrowed it
        S.SetWave(5, "Sawtooth");
        S.SetVolume(5, 0.4);
        S.SetGate(5, 100);
        S.PlayMusic(5, "A3:1");
        var before = Render(0.5);
        S.StopMusic();
        Render(0.3);
        S.PlayTrackSfx(bank.Instruments.First(i => i.Name == "Coin"), 4, 83, channel: 5);
        Render(1);
        S.PlayMusic(5, "A3:1");
        var after = Render(0.5);
        Assert.InRange(Rms(after, 0.1, 0.4), Rms(before, 0.1, 0.4) * 0.95, Rms(before, 0.1, 0.4) * 1.05);
        Assert.InRange(Pitch(after, 0.1, 0.4), 218, 222);
    }

    [Fact]
    public void SoundEffectsLeaveTheSongAlone()
    {
        var song = TrackerSong.Load(Repo.PathOf("Sound/Tracker/forest_song.amostrk"));
        var bank = TrackerSong.Load(Repo.PathOf("Sound/Tracker/sfx.amostrk"));
        S.TrackPlay(song);
        Render(0.5);
        int row = S.TrackRow;
        S.PlayTrackSfx(bank.Instruments.First(i => i.Name == "Explosion"), 5, 60);
        Render(0.5);
        Assert.True(S.IsTrackPlaying());
        Assert.True(S.TrackRow > row);
    }

    // A song with one sine instrument with extra settings (JSON) and one row with A-4
    private static TrackerSong SongWith(string instrumentExtra, params string[] rows)
    {
        if (rows.Length == 0) rows = new[] { "A-4 01 ...", "--- .. ...", "--- .. ...", "--- .. ...", "--- .. ...", "--- .. ..." };
        var json = "{\"Speed\":6,\"Tempo\":125,\"Instruments\":[{\"Wave\":\"Sine\",\"Volume\":0.5,\"Attack\":0,\"Decay\":0,\"Sustain\":100," +
                   instrumentExtra + "}],\"Order\":[0],\"Patterns\":[[" + string.Join(",", rows.Select(r => "\"" + r + "\"")) + "]]}";
        return TrackerSong.FromJson(json);
    }

    private static double Loudness(float[] b, double t0, double t1)
    {
        double sum = 0;
        int a = (int)(t0 * SR), e = (int)(t1 * SR);
        for (int i = a; i < e; i++) sum += b[i] * b[i];
        return Math.Sqrt(sum / (e - a));
    }

    [Fact]
    public void OscillatorTwoPlaysAtItsOwnPitch()
    {
        // All of the sound from oscillator 2, an octave up
        S.TrackPlay(SongWith("\"Osc2Wave\":\"Sine\",\"Osc2Semitones\":12,\"Osc2Mix\":100"), loop: false);
        var b = Render(0.3);
        Assert.InRange(Semitones(Pitch(b, 0.05, 0.25), 440), 11.9, 12.1);
    }

    [Fact]
    public void OscillatorTwoOffIsTheOldSound()
    {
        S.TrackPlay(SongWith("\"Osc2Wave\":\"Off\",\"Osc2Semitones\":12,\"Osc2Mix\":100"), loop: false);
        var b = Render(0.3);
        Assert.InRange(Semitones(Pitch(b, 0.05, 0.25), 440), -0.1, 0.1);
    }

    [Fact]
    public void PitchLfoMovesThePitchUpAndDown()
    {
        // A square LFO at 2 Hz, 100 cents: a semitone up for 0.25 s, then a semitone down
        S.TrackPlay(SongWith("\"Lfo1\":{\"Shape\":\"Square\",\"Target\":\"Pitch\",\"Rate\":2,\"Depth\":100}"), loop: false);
        var b = Render(0.5);
        Assert.InRange(Semitones(Pitch(b, 0.03, 0.22), 440), 0.9, 1.1);
        Assert.InRange(Semitones(Pitch(b, 0.28, 0.47), 440), -1.1, -0.9);
    }

    [Fact]
    public void LfoInStepWithTheSongFollowsTheRows()
    {
        // Speed 6 at tempo 125 is 0.12 s a row; two rows a period: up for one row, down the next
        S.TrackPlay(SongWith("\"Lfo1\":{\"Shape\":\"Square\",\"Target\":\"Pitch\",\"SyncRows\":2,\"Depth\":200}"), loop: false);
        var b = Render(0.24);
        Assert.InRange(Semitones(Pitch(b, 0.02, 0.11), 440), 1.9, 2.1);
        Assert.InRange(Semitones(Pitch(b, 0.14, 0.23), 440), -2.1, -1.9);
    }

    [Fact]
    public void VolumeLfoIsTremolo()
    {
        // Square at 2 Hz, full depth: loud for 0.25 s, silent the next 0.25 s
        S.TrackPlay(SongWith("\"Lfo2\":{\"Shape\":\"Square\",\"Target\":\"Volume\",\"Rate\":2,\"Depth\":100}"), loop: false);
        var b = Render(0.5);
        Assert.True(Loudness(b, 0.05, 0.2) > 0.1);
        Assert.True(Loudness(b, 0.3, 0.45) < 0.01);
    }

    [Fact]
    public void LfoDelayLetsItGrowIn()
    {
        // Vibrato that has grown to full depth only after 400 ms: almost no change at first
        S.TrackPlay(SongWith("\"Lfo1\":{\"Shape\":\"Square\",\"Target\":\"Pitch\",\"Rate\":2,\"Depth\":100,\"Delay\":400}"), loop: false);
        var b = Render(0.1);
        Assert.InRange(Semitones(Pitch(b, 0.005, 0.03), 440), -0.1, 0.15);
    }

    [Fact]
    public void EchoComesBackAfterItsTimeAndDiesAway()
    {
        // A short click of a note, an echo after 200 ms at half the loudness, feedback 50 %
        S.TrackPlay(SongWith("\"Decay\":30,\"Sustain\":0,\"Release\":10,\"EchoMix\":50,\"EchoTime\":200,\"EchoFeedback\":50",
            "A-4 01 ...", "=== .. ...", "--- .. ...", "--- .. ..."), loop: false);
        var b = Render(0.7);
        double first = Loudness(b, 0.0, 0.04), gap = Loudness(b, 0.1, 0.18);
        double echo1 = Loudness(b, 0.2, 0.24), echo2 = Loudness(b, 0.4, 0.44);
        Assert.True(gap < first * 0.05, $"gap {gap:0.000}");
        Assert.InRange(echo1 / first, 0.4, 0.6);
        Assert.InRange(echo2 / echo1, 0.4, 0.6);
    }

    [Fact]
    public void EchoInStepWithTheSongFollowsTheRows()
    {
        // Two rows at speed 6, tempo 125 is 0.24 s
        S.TrackPlay(SongWith("\"Decay\":30,\"Sustain\":0,\"Release\":10,\"EchoMix\":50,\"EchoSyncRows\":2,\"EchoFeedback\":0",
            "A-4 01 ...", "=== .. ...", "--- .. ...", "--- .. ..."), loop: false);
        var b = Render(0.4);
        Assert.True(Loudness(b, 0.24, 0.27) > Loudness(b, 0.15, 0.2) * 10);
    }

    [Fact]
    public void TheChannelThatPlaysAnInstrumentIsFound()
    {
        // Instrument 1 on channel 1, instrument 2 on channel 3
        var song = TrackerSong.FromJson("{\"Speed\":6,\"Tempo\":125,\"Instruments\":[{\"Wave\":\"Sine\",\"Sustain\":100}," +
            "{\"Wave\":\"Square\",\"Sustain\":100}],\"Order\":[0],\"Patterns\":[[\"A-4 01 ...|--- .. ...|C-4 02 ...|--- .. ...\"," +
            "\"--- .. ...|--- .. ...|--- .. ...|--- .. ...\"]]}");
        S.TrackPlay(song, loop: true);
        Render(0.05);
        Assert.Equal(1, S.ChannelWithInstrument(1));
        Assert.Equal(3, S.ChannelWithInstrument(2));
        Assert.Equal(0, S.ChannelWithInstrument(5));
    }

    // Loudness of the left and right side of a stereo buffer between two times
    private static (double left, double right) Sides(float[] b, double t0, double t1)
    {
        double l = 0, r = 0;
        int a = (int)(t0 * SR), e = (int)(t1 * SR);
        for (int i = a; i < e; i++) { l += b[2 * i] * b[2 * i]; r += b[2 * i + 1] * b[2 * i + 1]; }
        return (Math.Sqrt(l / (e - a)), Math.Sqrt(r / (e - a)));
    }

    private static float[] RenderStereo(double seconds)
    {
        var buffer = new float[(int)(seconds * SR) * 2];
        S.RenderStereo(buffer);
        return buffer;
    }

    [Fact]
    public void InTheMiddleBothSidesAreTheOldSound()
    {
        S.TrackPlay(SongWith(""), loop: false);
        var b = RenderStereo(0.2);
        var (l, r) = Sides(b, 0.05, 0.2);
        Assert.True(l > 0.1);
        Assert.Equal(l, r, 4);
    }

    [Fact]
    public void PanToTheRightLeavesTheLeftSideQuiet()
    {
        S.TrackPlay(SongWith("\"Pan\":100"), loop: false);
        var (l, r) = Sides(RenderStereo(0.2), 0.05, 0.2);
        Assert.True(r > 0.1);
        Assert.True(l < r * 0.01);
    }

    [Fact]
    public void Effect8PansAsInProTracker()
    {
        // 800 is hard left
        S.TrackPlay(SongWith("", "A-4 01 800", "--- .. ...", "--- .. ..."), loop: false);
        var (l, r) = Sides(RenderStereo(0.2), 0.05, 0.2);
        Assert.True(l > 0.1);
        Assert.True(r < l * 0.02);
    }

    [Fact]
    public void PanLfoMovesTheSoundFromSideToSide()
    {
        // A square LFO at 2 Hz, full depth: right for 0.25 s, then left
        S.TrackPlay(SongWith("\"Lfo1\":{\"Shape\":\"Square\",\"Target\":\"Pan\",\"Rate\":2,\"Depth\":100}"), loop: false);
        var b = RenderStereo(0.5);
        var first = Sides(b, 0.03, 0.22);
        var second = Sides(b, 0.28, 0.47);
        Assert.True(first.right > first.left * 10);
        Assert.True(second.left > second.right * 10);
    }

    [Fact]
    public void PlayOsc2LfoEchoAndPanWorkInAMelody()
    {
        // Oscillator 2 an octave up takes the whole sound, panned right
        S.SetWave(1, "SINE");
        S.PlayMusic(1, "{OSC2 SINE 12 0 100; PAN 100} A4:1");
        var b = RenderStereo(0.3);
        var mono = new float[b.Length / 2];
        for (int i = 0; i < mono.Length; i++) mono[i] = b[2 * i + 1];
        Assert.InRange(Semitones(Pitch(mono, 0.05, 0.25), 440), 11.9, 12.1);
        var (l, r) = Sides(b, 0.05, 0.25);
        Assert.True(l < r * 0.01);
    }

    [Fact]
    public void PlayLfoCommandMovesThePitch()
    {
        S.SetWave(1, "SINE");
        S.SetLfo(1, 1, "PITCH", "SQUARE", 100, 2);
        S.PlayMusic(1, "A4:1");
        var b = Render(0.5);
        Assert.InRange(Semitones(Pitch(b, 0.03, 0.22), 440), 0.9, 1.1);
        Assert.InRange(Semitones(Pitch(b, 0.28, 0.47), 440), -1.1, -0.9);
    }

    [Fact]
    public void PlayEchoRepeatsTheSound()
    {
        S.SetWave(1, "SINE");
        S.SetAdsr(1, 0, 30, 0, 10);
        S.SetEcho(1, 50, 200, 0);
        S.PlayMusic(1, "A4:16");
        var b = Render(0.5);
        Assert.True(Loudness(b, 0.2, 0.23) > Loudness(b, 0.12, 0.18) * 5);
    }

    [Fact]
    public void ChangingTheInstrumentChangesTheNoteThatSounds()
    {
        // A held A-4; half way the instrument gets oscillator 2 an octave up, and the note goes on with it
        var song = SongWith("");
        S.TrackPreview(song, 1, 69, 1);
        var before = Render(0.2);
        song.Instruments[0].Osc2Wave = "Sine";
        song.Instruments[0].Osc2Semitones = 12;
        song.Instruments[0].Osc2Mix = 100;
        S.TrackInstrumentChanged(song, 1);
        var after = Render(0.2);
        Assert.InRange(Semitones(Pitch(before, 0.05, 0.19), 440), -0.1, 0.1);
        Assert.InRange(Semitones(Pitch(after, 0.02, 0.19), 440), 11.9, 12.1);
        // The note was not started again: it is as loud straight after the change as before it
        Assert.InRange(Loudness(after, 0.0, 0.02) / Loudness(before, 0.15, 0.2), 0.8, 1.25);
        S.TrackPreviewRelease(1);
        S.TrackPreviewStop();
    }

    [Fact]
    public void AShorterTableDoesNotBreakANoteThatSounds()
    {
        var song = SongWith("\"Table\":[\"0\",\"+4\",\"+7\",\"+12\",\"+7\",\"+4\"],\"TableLoop\":0");
        S.TrackPreview(song, 1, 69, 1);
        Render(0.09);   // well into the table
        song.Instruments[0].Table = new() { "0" };
        S.TrackInstrumentChanged(song, 1);
        var after = Render(0.2);
        Assert.InRange(Semitones(Pitch(after, 0.05, 0.19), 440), -0.1, 0.1);
        S.TrackPreviewRelease(1);
        S.TrackPreviewStop();
    }

    // ── Sampled instruments ──────────────────────────────────────────

    // A WAV file with a 220 Hz sine (A-3), 16-bit mono, written to the temp folder
    private static string SineWav(double seconds, int rate = 44100)
    {
        int n = (int)(seconds * rate);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 220 * i / rate) * 20000));
        string path = Path.Combine(Path.GetTempPath(), "amos-sample-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static string SampleInstrument(string file, string extra = "") =>
        $"\"Wave\":\"Sample\",\"Sample\":{System.Text.Json.JsonSerializer.Serialize(file)},\"SampleNote\":57" + (extra.Length > 0 ? "," + extra : "");

    [Fact]
    public void ASampleIsPlayedAtTheNotesPitch()
    {
        // The file is A-3 (220 Hz); played as A-4 it sounds an octave up
        var file = SineWav(1);
        S.TrackPlay(SongWith(SampleInstrument(file)), loop: false);
        var b = Render(0.3);
        Assert.InRange(Semitones(Pitch(b, 0.05, 0.25), 440), -0.1, 0.1);
        File.Delete(file);
    }

    [Fact]
    public void ASampleWithoutALoopEndsAndOneWithALoopGoesOn()
    {
        var file = SineWav(0.1);   // 0.1 s at A-3, 0.05 s at A-4
        S.TrackPlay(SongWith(SampleInstrument(file)), loop: false);
        var once = Render(0.3);
        Assert.True(Loudness(once, 0.01, 0.04) > 0.05);
        Assert.True(Loudness(once, 0.1, 0.2) < 0.001);

        SampleBank.Forget(file);
        S.TrackPlay(SongWith(SampleInstrument(file, "\"LoopStart\":0,\"LoopEnd\":4400")), loop: false);
        var looped = Render(0.3);
        Assert.True(Loudness(looped, 0.15, 0.25) > 0.05);
        File.Delete(file);
    }

    [Fact]
    public void Effect9StartsTheSampleFurtherIn()
    {
        // A sample that is silent for its first 0.5 s: from the start nothing is heard at first, with
        // 9xx (xx × 256 frames in) the sound is there at once
        var file = SineWav(1);
        var sample = SampleBank.ReadWav(File.ReadAllBytes(file));
        Array.Clear(sample.Data, 0, 22050);
        string silentStart = Path.ChangeExtension(file, ".start.wav");
        S.WriteWav(silentStart, sample.Data.SelectMany(x => new[] { x, x }).ToArray());   // stereo, read back as mono
        S.TrackPlay(SongWith(SampleInstrument(silentStart), "A-4 01 ...", "--- .. ..."), loop: false);
        Assert.True(Loudness(Render(0.1), 0.02, 0.08) < 0.001);
        S.TrackStop(); Render(0.02);
        S.TrackPlay(SongWith(SampleInstrument(silentStart), "A-4 01 958", "--- .. ..."), loop: false);   // 0x58 × 256 = 22528
        Assert.True(Loudness(Render(0.1), 0.02, 0.08) > 0.05);
        File.Delete(file); File.Delete(silentStart);
    }

    [Fact]
    public void WavFilesInEveryCommonFormatAreRead()
    {
        // 8-bit unsigned, and 24-bit stereo: both come back as the same mono wave
        byte[] Wav(short channels, short bits, byte[] data) =>
            "RIFF"u8.ToArray().Concat(BitConverter.GetBytes(36 + data.Length)).Concat("WAVEfmt "u8.ToArray())
                .Concat(BitConverter.GetBytes(16)).Concat(BitConverter.GetBytes((short)1)).Concat(BitConverter.GetBytes(channels))
                .Concat(BitConverter.GetBytes(8000)).Concat(BitConverter.GetBytes(8000 * channels * bits / 8))
                .Concat(BitConverter.GetBytes((short)(channels * bits / 8))).Concat(BitConverter.GetBytes(bits))
                .Concat("data"u8.ToArray()).Concat(BitConverter.GetBytes(data.Length)).Concat(data).ToArray();
        var eight = SampleBank.ReadWav(Wav(1, 8, new byte[] { 128, 255, 0 }));
        Assert.Equal(8000, eight.Rate);
        Assert.Equal(new[] { 0f, 127 / 128f, -1f }, eight.Data);
        // 24-bit stereo: left 0x400000 (0.5), right 0 → 0.25
        var twentyFour = SampleBank.ReadWav(Wav(2, 24, new byte[] { 0, 0, 0x40, 0, 0, 0 }));
        Assert.Equal(0.25f, twentyFour.Data[0], 4);
    }

    // ── More ProTracker effects ──────────────────────────────────────

    [Fact]
    public void FineSlideMovesOnceOnTheRow()
    {
        // E18: up half a semitone once (8 sixteenths), and it stays there
        S.TrackPlay(SongWith("", "A-4 01 E18", "--- .. ...", "--- .. ..."), loop: false);
        var b = Render(0.3);
        Assert.InRange(Semitones(Pitch(b, 0.03, 0.11), 440), 0.4, 0.6);
        Assert.InRange(Semitones(Pitch(b, 0.15, 0.28), 440), 0.4, 0.6);
    }

    [Fact]
    public void FineVolumeChangesTheVolumeOnce()
    {
        // C20 is half volume; EA8 on the next row brings it to 40 (of 64)
        S.TrackPlay(SongWith("", "A-4 01 C20", "--- .. EA8", "--- .. ..."), loop: false);
        var b = Render(0.3);
        double ratio = Loudness(b, 0.16, 0.22) / Loudness(b, 0.04, 0.1);
        Assert.InRange(ratio, 40.0 / 32 * 0.95, 40.0 / 32 * 1.05);
    }

    [Fact]
    public void RetriggerStrikesTheNoteAgain()
    {
        // A note that dies quickly, struck again every 2 ticks (40 ms) by E92
        S.TrackPlay(SongWith("\"Decay\":15,\"Sustain\":0,\"Release\":5", "A-4 01 E92", "--- .. ...", "--- .. ..."), loop: false);
        var b = Render(0.12);
        Assert.True(Loudness(b, 0.040, 0.046) > Loudness(b, 0.030, 0.038) * 3);
    }

    [Fact]
    public void PatternLoopPlaysRowsAgain()
    {
        // E60 marks row 0, E62 on row 1 goes back twice: rows 0 1 0 1 0 1 2
        S.TrackPlay(SongWith("", "A-4 01 E60", "--- .. E62", "C-5 01 ...", "--- .. ..."), loop: false);
        var rows = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            Render(0.12);
            rows.Add(S.TrackRow);
        }
        Assert.Equal(new[] { 1, 0, 1, 0, 1, 2, 3 }, rows);
    }

    [Fact]
    public void PatternDelayHoldsTheRow()
    {
        // EE2: row 0 lasts three rows (0.36 s) before row 1
        S.TrackPlay(SongWith("", "A-4 01 EE2", "--- .. ...", "--- .. ..."), loop: false);
        Render(0.3);
        Assert.Equal(0, S.TrackRow);
        Render(0.1);
        Assert.Equal(1, S.TrackRow);
    }

    [Fact]
    public void TremoloMovesTheVolume()
    {
        // 74F: the volume swings by a lot; the loudness differs much between parts of a row
        S.TrackPlay(SongWith("", "A-4 01 78F", "--- .. 700", "--- .. 700"), loop: false);
        var b = Render(0.36);
        var parts = Enumerable.Range(0, 12).Select(k => Loudness(b, 0.02 + k * 0.025, 0.035 + k * 0.025)).ToList();
        Assert.True(parts.Max() > parts.Min() * 2);
    }

    [Fact]
    public void AJumpBackEndsASongPlayedOnce()
    {
        // B00 on the last row goes back to the start: the song loops, so played once it ends there
        S.TrackPlay(SongWith("", "A-4 01 ...", "--- .. B00"), loop: false);
        Render(0.3);
        Assert.False(S.IsTrackPlaying());
        S.TrackPlay(SongWith("", "A-4 01 ...", "--- .. B00"), loop: true);
        Render(0.3);
        Assert.True(S.IsTrackPlaying());
    }
}
