using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// A song in YM mode saved as DHS: a YM register dump at 50 Hz, STe DMA samples and LMC1992 commands, for
// a 68000 player called from the VBL (Atari/DHS/DHSPLAY.S). The file is read back here as that player
// reads it.
public class DhsTests
{
    private sealed record Frame(byte[] Regs, int Mask, int? Dma, int? Lmc, int Offset);

    private static int Long(byte[] d, int at) => d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3];
    private static int Word(byte[] d, int at) => d[at] << 8 | d[at + 1];

    // The frames as the player goes through them, with the YM's registers as they stand after each
    private static List<Frame> Read(byte[] d)
    {
        var frames = new List<Frame>();
        var regs = new byte[14];
        int p = Long(d, 4);
        while (true)
        {
            int at = p;
            int mask = Word(d, p); p += 2;
            if (mask == 0xFFFF) break;
            int? lmc = null, dma = null;
            if ((mask & 0x8000) != 0) { lmc = Word(d, p); p += 2; }
            for (int r = 0; r < 14; r++)
                if ((mask & 1 << r) != 0) regs[r] = d[p++];
            if ((mask & 0x4000) != 0) dma = d[p++];
            p += p & 1;
            frames.Add(new Frame((byte[])regs.Clone(), mask, dma, lmc, at));
        }
        return frames;
    }

    private static TrackerSong Open()
    {
        var file = MaxYMiserTests.Song();
        return YmImport.OpenMaxYMiser(file, MaxYMiserSong.Find(file)!, "Test");
    }

    [Fact]
    public void A_maxYMiser_song_becomes_YM_registers_frame_by_frame()
    {
        var song = Open();
        var r = S.ExportDhs(song);
        var d = r.Data;
        Assert.Equal("DHS1", System.Text.Encoding.ASCII.GetString(d, 0, 4));
        Assert.Equal(0, Long(d, 12));               // no samples
        Assert.Equal(0, Word(d, 18));               // no DMA or LMC: any ST plays it
        var frames = Read(d);
        Assert.Equal(Long(d, 20), frames.Count);
        Assert.True(frames.Count > 50);
        // The first frame sets the whole chip but the envelope's start; the mixer's port bits are left
        Assert.Equal(0x1FFF, frames[0].Mask & 0x1FFF);
        Assert.All(frames, f => Assert.True(f.Regs[7] <= 0x3F));
        Assert.All(frames, f => Assert.Equal(0, f.Mask & 0xC000));
        // The loop is a frame's start, and sets the whole chip again
        int loop = Long(d, 8);
        var loopFrame = frames.Single(f => f.Offset == loop);
        Assert.Equal(0x1FFF, loopFrame.Mask & 0x1FFF);
        // Something sounds: a volume (or the envelope) on and a tone on in the mixer
        Assert.Contains(frames, f => (f.Regs[8] | f.Regs[9] | f.Regs[10]) != 0 && (f.Regs[7] & 7) != 7);
        // Frames are on even places and small: a register dump that sends only changes
        Assert.All(frames, f => Assert.Equal(0, f.Offset & 1));
        Assert.True(d.Length < 64 + frames.Count * 18);
    }

    [Fact]
    public void Channel_4_plays_samples_on_the_DMA_and_sets_the_LMC1992()
    {
        var song = Open();
        string wav = Path.Combine(Path.GetTempPath(), "dhs-test-" + Guid.NewGuid() + ".wav");
        var tone = Enumerable.Range(0, 12517 / 4).Select(i => (float)Math.Sin(i * 0.1) * 0.5f).ToArray();
        SampleBank.WriteWav(wav, new SampleData(tone, 12000));
        try
        {
            song.Instruments.Add(new TrackerInstrument { Name = "Sample", Wave = "Sample", Sample = wav, SampleNote = 60, LoopStart = 1000, LoopEnd = 3000 });
            int inst = song.Instruments.Count;
            song.Channels = 4;
            song.Patterns.Add(new TrackerPattern(song.Patterns[song.PatternAt(0, 0)].Rows));
            int p = song.Patterns.Count - 1;
            song.SetPatternAt(0, 3, p);
            song.Patterns[p][0, 3] = new TrackerCell(60, inst, 'M', 20, effect2: 'L', param2: 10);
            song.Patterns[p][8, 3] = new TrackerCell(TrackerCell.NoteOff, 0, '\0', 0);

            var r = S.ExportDhs(song);
            var d = r.Data;
            Assert.Equal(3, Word(d, 18));            // DMA and LMC used
            Assert.Equal(1, Word(d, 16));
            var frames = Read(d);
            // Row 0: the sample starts and the master volume is set; the second command waits a frame
            Assert.Equal(1, frames[0].Dma);
            Assert.Equal(2 << 9 | 3 << 6 | 20, frames[0].Lmc);
            Assert.Equal(2 << 9 | 5 << 6 | 10, frames[1].Lmc);
            Assert.Contains(frames, f => f.Dma == 0);
            // The sample: 12517 Hz mono, 8-bit, cut at its loop end, its loop start inside it
            int t = Long(d, 12);
            int start = Long(d, t), end = Long(d, t + 4), loopStart = Long(d, t + 8);
            Assert.Equal(0x81, d[t + 13]);
            Assert.Equal(0, start & 1);
            Assert.InRange(end - start, 3000 * 12517 / 12000 - 2, 3000 * 12517 / 12000 + 2);
            Assert.InRange(loopStart - start, 1000 * 12517 / 12000 - 2, 1000 * 12517 / 12000 + 2);
            Assert.True(end <= d.Length);
        }
        finally { File.Delete(wav); SampleBank.Forget(wav); }
    }

    [Fact]
    public void The_dump_sounds_as_the_tracker_plays_the_song()
    {
        var song = Open();
        var d = S.ExportDhs(song).Data;
        var frames = Read(d);
        // The dump on the chip, a frame each 1/50 s, register 13 only when the frame writes it
        var ym = new Ym2149(2000000, S.SampleRate);
        int per = S.SampleRate / 50;
        var dump = new float[frames.Count * per];
        for (int i = 0; i < frames.Count; i++)
        {
            for (int r = 0; r < 14; r++)
                if ((frames[i].Mask & 1 << r) != 0) ym.WriteLive(r, frames[i].Regs[r]);
            for (int k = 0; k < per; k++) dump[i * per + k] = (float)ym.Next();
        }
        // The tracker's own playback (mono)
        S.ManualRendering = true;
        S.Reset();
        S.TrackPlay(TrackerSong.FromJson(song.ToJson()), loop: false);
        var live = new float[dump.Length];
        S.Render(live);
        S.TrackStop();
        S.Reset();
        // Loudness frame by frame follows the same shape
        double[] Rms(float[] b) => Enumerable.Range(0, frames.Count)
            .Select(i => Math.Sqrt(Enumerable.Range(i * per, per).Average(j => (double)b[j] * b[j]))).ToArray();
        var a = Rms(dump);
        var b = Rms(live);
        double ma = a.Average(), mb = b.Average();
        double cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
        double corr = cov / Math.Sqrt(a.Sum(x => (x - ma) * (x - ma)) * b.Sum(y => (y - mb) * (y - mb)));
        Assert.True(corr > 0.8, $"correlation {corr:0.000}");
    }

    // A song with a timer effect in an instrument's mixer and a PWM effect on a line
    private static TrackerSong WithTimers()
    {
        var song = Open();
        var ym = song.Ym!;
        ym.Sequences.Add(new TrackerYmSequence { Steps = new List<int> { 0x105, 0x105 }, Repeat = 1 });
        song.Instruments[0].Ym!.MixerSeq = ym.Sequences.Count - 1;
        var p = song.Patterns[song.PatternAt(0, 0)];
        p[4, 1] = p[4, 1].WithEffect('9', 0x40);
        return song;
    }

    [Fact]
    public void A_maxYMiser_song_made_DHS_has_its_timer_effects_taken_out()
    {
        var song = WithTimers();
        Assert.NotEmpty(DhsMode.Problems(song));
        var told = DhsMode.Convert(song);
        Assert.True(DhsMode.Is(song));
        Assert.Equal(0, song.Ym!.Timers);
        Assert.Equal(4, song.Channels);
        Assert.Empty(DhsMode.Problems(song));
        Assert.Contains(told, t => t.Contains("SID"));
        Assert.Contains(told, t => t.Contains("timer effects"));
        Assert.Equal(0x100, song.Ym.Sequences[song.Instruments[0].Ym!.MixerSeq].Steps[0]);
        // Kept as DHS through the song's file; saved as DHS without a timer warning
        var back = TrackerSong.FromJson(song.ToJson());
        Assert.True(DhsMode.Is(back));
        Assert.DoesNotContain(S.ExportDhs(back).Warnings, w => w.Contains("Timer"));
    }

    [Fact]
    public void What_DHS_cannot_play_is_found()
    {
        var song = Open();
        DhsMode.Convert(song);
        Assert.Empty(DhsMode.Problems(song));
        var p = song.Patterns[song.PatternAt(0, 0)];
        p[2, 0] = p[2, 0].WithEffect('G', 1);
        Assert.Contains(DhsMode.Problems(song), x => x.Contains("effect G"));
        p[2, 0] = p[2, 0].WithEffect('\0', 0);

        while (song.Instruments.Count < 32) song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
        song.Instruments.Add(new TrackerInstrument { Name = "Drum", Wave = "Sample" });
        song.Patterns.Add(new TrackerPattern(p.Rows));
        int own = song.Patterns.Count - 1;
        song.SetPatternAt(0, 3, own);
        song.Patterns[own][0, 3] = new TrackerCell(60, 33, 'M', 0x28, effect2: 'X', param2: 1);
        Assert.Empty(DhsMode.Problems(song));            // a sample on channel 4 with LMC effects: fine
        song.Patterns[own][1, 3] = new TrackerCell(TrackerCell.NoNote, 0, 'Q', 1);
        Assert.Contains(DhsMode.Problems(song), x => x.Contains("on channel 4"));
        song.Patterns[own][1, 3] = new TrackerCell(60, 1, '\0', 0);
        Assert.Contains(DhsMode.Problems(song), x => x.Contains("maxYMiser instrument on channel 4"));
        song.Patterns[own][1, 3] = TrackerCell.Empty;
        p[3, 0] = new TrackerCell(60, 33, '\0', 0);
        Assert.Contains(DhsMode.Problems(song), x => x.Contains("channels 1 – 3"));
        p[3, 0] = TrackerCell.Empty;
        song.Instruments[32].Wave = "Square";
        Assert.Contains(DhsMode.Problems(song), x => x.Contains("instrument 33"));
        // Out of DHS: the timers on again
        DhsMode.Leave(song);
        Assert.Null(song.Mode);
        Assert.Equal(7, song.Ym!.Timers);
    }

    [Fact]
    public void A_DHS_song_plays_its_samples_on_channel_4_and_saves_channels_1_to_3_for_maxYMiser()
    {
        var song = Open();
        DhsMode.Convert(song);
        string wav = Path.Combine(Path.GetTempPath(), "dhs-ch4-" + Guid.NewGuid() + ".wav");
        SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, 20000).Select(i => (float)Math.Sin(i * 0.2) * 0.8f).ToArray(), 12517));
        try
        {
            while (song.Instruments.Count < 32) song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
            song.Instruments.Add(new TrackerInstrument { Name = "Drum", Wave = "Sample", Sample = wav, SampleNote = 48 });
            var rows = song.Patterns[song.PatternAt(0, 0)].Rows;
            song.Patterns.Add(new TrackerPattern(rows));
            int own = song.Patterns.Count - 1;
            song.SetPatternAt(0, 3, own);
            song.Patterns[own][0, 3] = new TrackerCell(72, 33, '\0', 0);   // any note: the sample's own pitch
            // Only channel 4 heard
            S.ManualRendering = true;
            S.Reset();
            for (int c = 1; c <= 3; c++) S.SetTrackMute(c, true);
            S.TrackPlay(TrackerSong.FromJson(song.ToJson()), loop: false);
            var b = new float[S.SampleRate / 5];
            S.Render(b);
            S.TrackStop();
            for (int c = 1; c <= 4; c++) S.SetTrackMute(c, false);
            S.Reset();
            Assert.True(b.Max(Math.Abs) > 0.05f);
            // At its own pitch: 0.2 rad a sample at 12517 Hz is about 398 Hz
            int crossings = 0;
            for (int i = 1; i < b.Length; i++) if (b[i - 1] < 0 && b[i] >= 0) crossings++;
            Assert.InRange(crossings * 5, 340, 460);

            // For maxYMiser: its DMA track as it was (empty), the samples left out
            var m = MaxYMiserSong.Find(YmImport.SaveMaxYMiser(song))!;
            Assert.All(Enumerable.Range(0, m.Length), place => Assert.Equal(0xFF, m.Order[place, 3]));
        }
        finally { File.Delete(wav); SampleBank.Forget(wav); }
    }

    [Fact]
    public void The_test_program_is_written_as_one_file_with_the_player_in_it()
    {
        string test = S.DhsSource("DHSTEST.S", "MYSONG.DHS");
        Assert.DoesNotContain(test.Split("\r\n"), l => l.Split(';')[0].Contains("include"));
        Assert.Contains("dhs_play:", test);
        Assert.Contains("incbin\t\"MYSONG.DHS\"", test);
        Assert.DoesNotContain("\n", test.Replace("\r\n", ""));
    }

    [Fact]
    public void Only_a_song_in_YM_mode_can_be_saved_as_DHS()
    {
        Assert.Throws<InvalidOperationException>(() => S.ExportDhs(new TrackerSong()));
    }
}
