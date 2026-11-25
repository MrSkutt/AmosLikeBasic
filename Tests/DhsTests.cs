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
        song.Patterns[own][1, 3] = new TrackerCell(TrackerCell.NoNote, 0, 'A', 1);
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

    // The tracker plays queued samples (Q on channel 4) as the Atari does: each part goes on from where the one
    // before ends, not at its row, and the song waits for it at that row, so it keeps time with the parts
    [Fact]
    public void The_tracker_plays_queued_samples_in_a_chain_and_waits_for_them()
    {
        var song = Open();
        DhsMode.Convert(song);
        const int rows = 16, speed = 6, parts = 8;
        song.Speed = speed;
        song.Ym!.Speed = speed;
        int frames = (int)Math.Round((rows * speed + 0.3) * 250.4) & ~1;   // each part 0.3 VBL longer than its rows
        float[] levels = { 0.2f, 0.5f, 0.8f };
        var wavs = new List<string>();
        try
        {
            while (song.Instruments.Count < 32) song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
            for (int k = 0; k < 3; k++)
            {
                string wav = Path.Combine(Path.GetTempPath(), $"dhs-chain{k}-{Guid.NewGuid():N}.wav");
                SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, frames).Select(i => (float)Math.Sin(i * 0.3) * levels[k]).ToArray(), 12517));
                wavs.Add(wav);
                song.Instruments.Add(new TrackerInstrument { Name = "Part " + k, Wave = "Sample", Sample = wav, SampleNote = 60, Attack = 0, Release = 0 });
            }
            song.Patterns.Add(new TrackerPattern(rows));
            int empty = song.Patterns.Count - 1;
            song.Order = Enumerable.Repeat(empty, parts).ToList();
            song.ChannelOrder = new();
            for (int place = 0; place < parts; place++)
            {
                song.Patterns.Add(new TrackerPattern(rows));
                int own = song.Patterns.Count - 1;
                song.SetPatternAt(place, 3, own);
                song.Patterns[own][0, 3] = new TrackerCell(60, 33 + place % 3, place == 0 ? '\0' : 'Q', 0);
            }

            S.ManualRendering = true;
            S.Reset();
            for (int c = 1; c <= 3; c++) S.SetTrackMute(c, true);
            S.TrackPlay(TrackerSong.FromJson(song.ToJson()), loop: false);
            int perTick = S.SampleRate / 50, ticks = parts * rows * speed + 10;
            var audio = new float[ticks * perTick];
            var placeAt = new Dictionary<int, double>();   // place → when the song got to it, s
            var chunk = new float[perTick / 4];
            for (int i = 0; i < audio.Length / chunk.Length; i++)
            {
                S.Render(chunk);
                chunk.CopyTo(audio, i * chunk.Length);
                placeAt.TryAdd(S.TrackPosition, (i + 1) * chunk.Length / (double)S.SampleRate);
            }
            S.TrackStop();
            for (int c = 1; c <= 4; c++) S.SetTrackMute(c, false);
            S.Reset();

            // When each part is heard: its level in 2 ms windows
            int win = S.SampleRate / 500;
            double Rms(int at) => Math.Sqrt(Enumerable.Range(at, win).Average(j => (double)audio[j] * audio[j]));
            double length = frames / 12517.0;
            for (int k = 1; k < parts; k++)
            {
                double expected = k * length;   // the parts end to end
                // (the levels as heard: each part's middle)
                double want = Rms((int)((expected + length / 2) * S.SampleRate)), before = Rms((int)((expected - length / 2) * S.SampleRate));
                int from = (int)((expected - 0.03) * S.SampleRate);
                int at = Enumerable.Range(0, (int)(0.06 * S.SampleRate / win)).Select(w => from + w * win)
                    .First(x => Math.Abs(Rms(x) - want) < Math.Abs(Rms(x) - before));
                double heard = at / (double)S.SampleRate;
                Assert.InRange(heard - expected, -0.004, 0.004);
                // The song's row for the part: in the tick after it begins, not at its own time (k × 96 ticks)
                Assert.InRange(placeAt[k] - heard, -0.006, 0.026);
            }
        }
        finally { foreach (var w in wavs) { File.Delete(w); SampleBank.Forget(w); } }
    }

    // A drum loop: a kick on each beat and a hat between, beats of a part at a tempo, at 12517 Hz
    private static SampleData Loop(double bpm, int beats, int seed)
    {
        const int rate = 12517;
        var rnd = new Random(seed);
        double beat = 60 / bpm;
        var data = new float[(int)Math.Round(beats * beat * rate)];
        for (int b = 0; b < beats * 2; b++)
        {
            int at = (int)(b * beat / 2 * rate);
            bool kick = b % 2 == 0;
            for (int i = 0; i < rate / 8 && at + i < data.Length; i++)
            {
                double t = i / (double)rate;
                data[at + i] += kick ? (float)(Math.Sin(2 * Math.PI * 60 * t) * Math.Exp(-t * 20) * 0.7)
                                     : (float)((rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 60) * 0.3);
            }
        }
        // a pad under it all, so that it is not silence between the hits
        for (int i = 0; i < data.Length; i++) data[i] += (float)(Math.Sin(i * 0.05) * 0.1);
        return new SampleData(data, rate);
    }

    [Theory]
    [InlineData(128, 8)]
    [InlineData(100, 4)]
    [InlineData(140, 16)]
    public void The_tempo_of_a_sample_sequence_is_heard(double bpm, int beats)
    {
        var parts = Enumerable.Range(0, 4).Select(k => Loop(bpm, beats, k)).ToList();
        var tempo = SampleTempo.Detect(parts)!;
        Assert.Equal(beats, tempo.Beats);
        Assert.InRange(tempo.Bpm, bpm - 0.5, bpm + 0.5);
    }

    [Fact]
    public void A_sample_sequence_becomes_samples_chained_on_channel_4_with_rows_that_fit_them()
    {
        var song = Open();
        DhsMode.Convert(song);
        // A song with nothing in it yet: one empty place
        song.Patterns.Add(new TrackerPattern(64));
        song.Order = new List<int> { song.Patterns.Count - 1 };
        song.ChannelOrder = new();
        var wavs = new List<string>();
        try
        {
            for (int k = 0; k < 3; k++)
            {
                string wav = Path.Combine(Path.GetTempPath(), $"dhs-seq{k}-{Guid.NewGuid():N}.wav");
                SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, 46939).Select(i => (float)Math.Sin(i * 0.1) * 0.5f).ToArray(), 12517));
                wavs.Add(wav);
            }
            var told = DhsMode.AddSampleSequence(song, wavs.Select((w, k) => ($"part{k}", w)).ToList(), 32, chain: true);
            Assert.NotEmpty(told);
            Assert.Equal(new[] { "part0", "part1", "part2" }, song.Instruments.Skip(32).Select(i => i.Name));
            Assert.Equal(3, song.Order.Count);   // from the start of a song that had nothing
            for (int place = 0; place < 3; place++)
            {
                var p = song.Patterns[song.PatternAt(place, 3)];
                Assert.Equal(32, p.Rows);
                Assert.Equal(33 + place, p[0, 3].Instrument);
                Assert.Equal(place == 0 ? '\0' : 'Q', p[0, 3].Effect);
                // The rows' speeds: 187 VBL, as long as the part (46 940 bytes / 250.4) or a little less
                int speed = 0, total = 0;
                for (int r = 0; r < 32; r++)
                {
                    var c = p[r, 3];
                    if (c.Effect == 'S') speed = c.Param;
                    if (c.Effect2 == 'S') speed = c.Param2;
                    total += speed;
                }
                Assert.Equal(187, total);
            }
            Assert.Empty(DhsMode.Problems(song));
            var export = S.ExportDhs(TrackerSong.FromJson(song.ToJson()));
            Assert.DoesNotContain(export.Warnings, w => w.Contains("gap") || w.Contains("VBL"));
            Assert.Equal(3 * 187, export.Frames);
        }
        finally { foreach (var w in wavs) { File.Delete(w); SampleBank.Forget(w); } }
    }

    [Fact]
    public void Only_a_song_in_YM_mode_can_be_saved_as_DHS()
    {
        Assert.Throws<InvalidOperationException>(() => S.ExportDhs(new TrackerSong()));
    }
}
