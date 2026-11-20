using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AmosLikeBasic;

// Turns a YM file into a tracker song. Each frame is first read as a sound per channel: tone, noise,
// both or the "buzzer" (the hardware envelope repeated fast), its exact pitch and its volume step.
//
// The song is then made in one of two ways:
//  - Analysed (the normal way): the rows are found (how many frames a row lasts, and where the notes
//    start), and each note plays what the chip did from its start, frame by frame, as an instrument:
//    the pitch and the wave in the instrument's table, the volume in its envelope. Notes that do the
//    same become one instrument, and patterns that repeat are written once. Since every note plays
//    its frames exactly, this sounds as the file does; the analysis only decides how readable it is.
//  - A dump, one row per frame (speed 1): the nearest note and a fine slide (E1x / E2x), the volume
//    in the volume column, and five instruments for the kinds of sound. Used when the analysis
//    would need more instruments than a song can have.
// The YM5/YM6 special effects: a digidrum becomes a sampled instrument (its sample written as a
// WAV file), a note started where the drum is hit; a SID voice a square wave at the SID's pitch; and
// a sync buzzer the buzzer at the timer's pitch. MUSIC PLAY plays them exactly.
public static class YmImport
{
    private enum Mode { Silent, Tone, Noise, ToneNoise, BuzzSaw, BuzzTriangle }

    // What a channel does in a frame: Step is the chip's volume step 0 – 31
    private readonly record struct Sound(Mode Mode, double Semis, int Step, bool Retrigger)
    {
        public bool Sounds => Mode != Mode.Silent && Step > 0;
    }

    private static readonly double[] Level = Enumerable.Range(0, 32).Select(i => i == 0 ? 0 : Math.Pow(10, (i - 31) * 1.5 / 20)).ToArray();
    private static int Volume(int step) => (int)Math.Round(Level[Math.Clamp(step, 0, 31)] * 64);

    // sampleFolder: where the digidrums are written as WAV files, sampleFolderInSong how the song
    // refers to it (without them the drums are left out)
    public static ModImport.Result Import(YmFile ym, string name, bool analyse = true,
        string? sampleFolder = null, string? sampleFolderInSong = null)
    {
        var notes = new List<string>();
        // Faster than the tracker's top tempo (255, about 100 rows a second): frames taken together
        if (ym.FrameRate * 2.5 > 255) ym = Slower(ym, (int)Math.Ceiling(ym.FrameRate * 2.5 / 255));
        var fx = ReadEffects(ym);
        var sounds = Sounds(ym, fx);
        var drums = sampleFolder != null ? DrumInstruments(ym, fx, sampleFolder, sampleFolderInSong ?? sampleFolder) : new Drums();
        var buzzers = sampleFolder != null ? BuzzerSamples(sampleFolder, sampleFolderInSong ?? sampleFolder) : ((string, string)?)null;
        var found = new Found();
        TrackerSong? song = analyse ? Analyse(ym, sounds, notes, found, drums, buzzers) : null;
        bool analysed = song != null;
        song ??= Dump(ym, sounds, drums, buzzers);
        song.Title = ym.Title.Length > 0 ? ym.Title : name;
        song.Tempo = Math.Clamp((int)Math.Round(125.0 * ym.FrameRate / 50), 32, 255);
        if (fx.Hits.Count > 0 && sampleFolder == null) notes.Add("The digidrums were left out.");
        return new ModImport.Result(song, drums.Instruments.Count, notes, Report(ym, song, analysed, found, analyse, fx, drums));
    }

    // Every `n` frames as one: the registers of the last, the envelope started again if any of them did,
    // and the effects of all of them
    private static YmFile Slower(YmFile ym, int n)
    {
        int frames = (ym.Frames.Length + n - 1) / n;
        var regs = new byte[frames][];
        List<YmPlayer.Effect>[]? effects = ym.FrameEffects != null || ym.Version >= 5 ? new List<YmPlayer.Effect>[frames] : null;
        for (int f = 0; f < frames; f++)
        {
            int last = Math.Min(ym.Frames.Length, (f + 1) * n) - 1;
            regs[f] = (byte[])ym.Frames[last].Clone();
            byte shape = 0xFF;
            for (int k = f * n; k <= last; k++) if (ym.Frames[k][13] != 0xFF) shape = ym.Frames[k][13];
            regs[f][13] = shape;
            if (effects == null) continue;
            effects[f] = new List<YmPlayer.Effect>();
            for (int k = f * n; k <= last; k++) effects[f].AddRange(YmPlayer.Effects(ym, k));
        }
        return new YmFile
        {
            Frames = regs, FrameRate = (int)Math.Round(ym.FrameRate / (double)n), Clock = ym.Clock,
            LoopFrame = ym.LoopFrame / n, Title = ym.Title, Author = ym.Author, Comment = ym.Comment,
            UsesSpecialEffects = ym.UsesSpecialEffects, Version = ym.Version, Drums = ym.Drums, FrameEffects = effects
        };
    }

    // ── The effects of YM5 and YM6 ─────────────────────────────────────

    private readonly record struct DrumHit(int Frame, int Channel, int Drum, double Frequency);

    // The buzzer's sound: one round of the chip's envelope, 32 steps of its volume down (a sawtooth) or
    // down and up (a triangle), written as WAV files that loop. The steps are the chip's own, about
    // 1.5 dB apart, so the sound has the buzzer's edge that a smooth wave lacks.
    private static (string saw, string triangle) BuzzerSamples(string folder, string inSong)
    {
        string Write(string file, float[] levels)
        {
            float mean = levels.Average();
            var data = levels.Select(l => Math.Clamp((l - mean) * 2, -1f, 1f)).ToArray();
            // The note C-4 (261.63 Hz) plays one round per cycle
            SampleBank.WriteWav(System.IO.Path.Combine(folder, file), new SampleData(data, (int)Math.Round(261.63 * levels.Length)));
            string path = inSong.TrimEnd('/') + "/" + file;
            SampleBank.Forget(path);
            return path;
        }
        // Each step 8 frames long, so that the sampler's smoothing between frames keeps the steps sharp
        var down = Enumerable.Range(0, 32 * 8).Select(k => (float)Level[31 - k / 8]).ToArray();
        var downUp = down.Concat(down.Reverse()).ToArray();
        return (Write("ym buzzer saw.wav", down), Write("ym buzzer triangle.wav", downUp));
    }

    // An instrument that is only the buzzer plays its sample instead of a smooth wave
    private static void UseBuzzerSample(TrackerInstrument inst, Mode mode, (string saw, string triangle)? buzzers)
    {
        if (buzzers is not { } b) return;
        inst.Wave = "Sample";
        inst.Sample = mode == Mode.BuzzTriangle ? b.triangle : b.saw;
        inst.SampleNote = 60;
        inst.LoopStart = 0;
        inst.LoopEnd = mode == Mode.BuzzTriangle ? 512 : 256;
        inst.Volume = 0.23;
    }

    private sealed class Effects
    {
        public double[,] SidFrequency = new double[0, 3];
        public int[,] SidVolume = new int[0, 3];
        public double[] SyncFrequency = Array.Empty<double>();
        public bool[,] DrumPlaying = new bool[0, 3];
        public List<DrumHit> Hits = new();
        public int SidFrames, SyncFrames;
    }

    private static Effects ReadEffects(YmFile ym)
    {
        int frames = ym.Frames.Length;
        var fx = new Effects
        {
            SidFrequency = new double[frames, 3], SidVolume = new int[frames, 3],
            SyncFrequency = new double[frames], DrumPlaying = new bool[frames, 3]
        };
        if (ym.Version < 5 && ym.FrameEffects == null) return fx;
        for (int f = 0; f < frames; f++)
            foreach (var e in YmPlayer.Effects(ym, f))
                switch (e.Kind)
                {
                    case YmPlayer.EffectKind.Sid:
                    case YmPlayer.EffectKind.SineSid:
                        fx.SidFrequency[f, e.Channel] = e.Frequency;
                        fx.SidVolume[f, e.Channel] = e.Value;
                        fx.SidFrames++;
                        break;
                    case YmPlayer.EffectKind.SyncBuzzer:
                        fx.SyncFrequency[f] = e.Frequency;
                        fx.SyncFrames++;
                        break;
                    case YmPlayer.EffectKind.Drum:
                        if (e.Value >= ym.Drums.Count || e.Frequency <= 0) break;
                        fx.Hits.Add(new DrumHit(f, e.Channel, e.Value, e.Frequency));
                        // The channel plays the drum until it ends: its own sound is not heard meanwhile
                        int length = (int)Math.Ceiling(ym.Drums[e.Value].Length / e.Frequency * ym.FrameRate);
                        for (int k = f; k < Math.Min(frames, f + Math.Max(1, length)); k++) fx.DrumPlaying[k, e.Channel] = true;
                        break;
                }
        return fx;
    }

    // The digidrums as sampled instruments: a WAV file each, at the rate it is first played at
    private sealed class Drums
    {
        public List<TrackerInstrument> Instruments = new();
        public Dictionary<int, (int index, double rate)> Of = new();

        // The drum's note: 60 (C-4) at the rate of its file, other rates as other notes
        public TrackerCell Cell(DrumHit hit, int firstNumber, int delay = 0)
        {
            var (index, rate) = Of[hit.Drum];
            int note = Math.Clamp(60 + (int)Math.Round(12 * Math.Log2(hit.Frequency / rate)), 12, 119);
            return delay > 0
                ? new TrackerCell(note, firstNumber + index, 'E', 0xD0 | Math.Min(delay, 15))
                : new TrackerCell(note, firstNumber + index, '\0', 0);
        }
    }

    private static Drums DrumInstruments(YmFile ym, Effects fx, string folder, string inSong)
    {
        var drums = new Drums();
        foreach (var hit in fx.Hits)
        {
            if (drums.Of.ContainsKey(hit.Drum)) continue;
            var levels = ym.Drums[hit.Drum];
            if (levels.Length == 0) continue;
            // The chip's levels go one way from silence; the sample sits around 0
            float mean = levels.Average();
            var data = levels.Select(l => Math.Clamp((l - mean) * 2, -1f, 1f)).ToArray();
            int rate = (int)Math.Round(Math.Clamp(hit.Frequency, 1000, 96000));
            string file = $"digidrum {hit.Drum + 1:00}.wav";
            SampleBank.WriteWav(System.IO.Path.Combine(folder, file), new SampleData(data, rate));
            string path = inSong.TrimEnd('/') + "/" + file;
            SampleBank.Forget(path);
            drums.Of[hit.Drum] = (drums.Instruments.Count, rate);
            drums.Instruments.Add(new TrackerInstrument
            {
                Name = $"Digidrum {hit.Drum + 1}", Wave = "Sample", Sample = path, SampleNote = 60,
                Volume = 0.23, Attack = 0, Decay = 0, Sustain = 100, Release = 5
            });
        }
        return drums;
    }

    // What the analysis found, for the report
    private sealed class Found
    {
        public int Speed = 1;
        public Dictionary<string, int> Kinds = new();
        public int LoopOrder = -1;
    }

    private static string Report(YmFile ym, TrackerSong song, bool analysed, Found found, bool triedAnalysis, Effects fx, Drums drums)
    {
        var lines = new List<string>();
        string by = ym.Author.Length > 0 ? " by " + ym.Author : "";
        lines.Add($"\"{song.Title}\"{by}");
        if (ym.Comment.Length > 0) lines.Add(ym.Comment);
        var length = TimeSpan.FromSeconds(ym.Seconds);
        lines.Add($"{(int)length.TotalMinutes}:{length.Seconds:00} long, {ym.Frames.Length} frames at {ym.FrameRate} Hz, " +
                  $"{(ym.Clock == 2000000 ? "Atari ST" : ym.Clock is > 1700000 and < 1800000 ? "ZX Spectrum" : ym.Clock + " Hz")} clock.");
        lines.Add("");
        int rows = song.Order.Sum(o => song.Patterns[o].Rows);
        if (analysed)
        {
            lines.Add($"Speed {found.Speed}: a row is {found.Speed} frame{(found.Speed == 1 ? "" : "s")}, {rows} rows in all" +
                      (found.Speed == 1 ? " (no steady row length was found)." : "."));
            string kinds = string.Join(", ", found.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}"));
            lines.Add($"{song.Instruments.Count} instruments, made from the notes: {kinds}.");
            lines.Add($"{song.Patterns.Count} patterns, played {song.Order.Count} times in the order list" +
                      (song.Patterns.Count < song.Order.Count ? " (the repeats are written once)." : "."));
            if (found.LoopOrder > 0) lines.Add($"At the end it jumps back to place {found.LoopOrder} of the order list, where the music loops.");
            lines.Add("");
            lines.Add("Every note plays the chip's frames exactly, so the song sounds as the file does.");
        }
        else
        {
            lines.Add(triedAnalysis
                ? "The notes differ too much to share instruments (it would need more than 255), so the song was read one row per frame (speed 1)."
                : "Read one row per frame (speed 1).");
            lines.Add($"{rows} rows in {song.Patterns.Count} patterns; the pitch is the nearest note and a fine slide, the volume is in the volume column.");
        }
        var effects = new List<string>();
        if (drums.Instruments.Count > 0)
            effects.Add($"{drums.Instruments.Count} digidrum{(drums.Instruments.Count == 1 ? "" : "s")} became sampled instruments (Digidrum), hit {fx.Hits.Count} times.");
        else if (fx.Hits.Count > 0) effects.Add("The digidrums were left out.");
        if (fx.SidFrames > 0) effects.Add("The SID voice is played as a square wave at its pitch (MUSIC PLAY plays the real thing).");
        if (fx.SyncFrames > 0) effects.Add("The sync buzzer is played as a buzzer at the timer's pitch (MUSIC PLAY plays the real thing).");
        if (effects.Count > 0) { lines.Add(""); lines.AddRange(effects); }
        return string.Join("\n", lines);
    }

    // ── Reading the frames ─────────────────────────────────────────────

    private static Sound[,] Sounds(YmFile ym, Effects fx)
    {
        int frames = ym.Frames.Length;
        var s = new Sound[frames, 3];
        int envShape = 0, envStart = 0;   // the shape last written, and the frame it was written in
        for (int f = 0; f < frames; f++)
        {
            var r = ym.Frames[f];
            // Register 13 is 0xFF in frames where it was not written; a write starts the envelope again
            bool written = r[13] != 0xFF;
            if (written) { envShape = r[13] & 15; envStart = f; }
            for (int c = 0; c < 3; c++)
                s[f, c] = fx.DrumPlaying[f, c] ? default
                    : Read(ym, r, c, envShape, f - envStart, written, fx.SidFrequency[f, c], fx.SidVolume[f, c], fx.SyncFrequency[f]);
        }
        return s;
    }

    private static Sound Read(YmFile ym, byte[] r, int c, int envShape, int framesSinceEnv, bool envWritten,
        double sidFrequency = 0, int sidVolume = 0, double syncFrequency = 0)
    {
        bool tone = (r[7] & (1 << c)) == 0, noise = (r[7] & (8 << c)) == 0;
        int v = r[8 + c];
        bool env = (v & 16) != 0;
        int envPeriod = Math.Max(1, r[11] | r[12] << 8);
        bool repeating = envShape >= 8 && envShape is not (9 or 11 or 13 or 15);

        Mode mode;
        double freq;
        int step;
        if (sidFrequency > 0 && !env)
        {
            // SID voice: the volume switched on and off; with the tone off that is the sound (a square
            // wave at half the timer's frequency), with it on the tone is what is heard
            mode = tone ? noise ? Mode.ToneNoise : Mode.Tone : noise ? Mode.ToneNoise : Mode.Tone;
            int tonePeriod = Math.Max(1, r[c * 2] | (r[c * 2 + 1] & 15) << 8);
            freq = tone ? ym.Clock / 16.0 / tonePeriod : sidFrequency / 2;
            step = sidVolume == 0 ? 0 : sidVolume * 2 + 1;
            if (freq > 12000) mode = Mode.Silent;
        }
        else if (syncFrequency > 0 && env)
        {
            // Sync buzzer: the envelope started again at the timer's rate gives that pitch
            mode = envShape is 10 or 14 ? Mode.BuzzTriangle : Mode.BuzzSaw;
            freq = syncFrequency;
            step = 31;
        }
        else if (env && repeating && envPeriod < 4096)
        {
            mode = envShape is 10 or 14 ? Mode.BuzzTriangle : Mode.BuzzSaw;
            freq = ym.Clock / (envShape is 10 or 14 ? 512.0 : 256.0) / envPeriod;
            // A tone too high to hear, switched on with it, lets the envelope through half the time: 6 dB down
            int tonePeriod = Math.Max(1, r[c * 2] | (r[c * 2 + 1] & 15) << 8);
            step = tone && ym.Clock / 16.0 / tonePeriod > 12000 ? 27 : 31;
        }
        else
        {
            mode = tone && noise ? Mode.ToneNoise : tone ? Mode.Tone : noise ? Mode.Noise : Mode.Silent;
            int tonePeriod = Math.Max(1, r[c * 2] | (r[c * 2 + 1] & 15) << 8);
            int noisePeriod = Math.Max(1, r[6] & 31);
            // The noise oscillator is clocked at 16 × the note
            freq = mode == Mode.Noise ? ym.Clock / 16.0 / noisePeriod / 16 : ym.Clock / 16.0 / tonePeriod;
            step = env ? OneShotLevel(envShape, framesSinceEnv, envPeriod, ym) : (v & 15) == 0 ? 0 : (v & 15) * 2 + 1;
            if (freq > 12000) mode = Mode.Silent;   // above hearing: used for sample tricks
        }
        if (mode == Mode.Silent) step = 0;
        return new Sound(mode, 69 + 12 * Math.Log2(freq / 440.0), step, env && envWritten);
    }

    // A single envelope (it rises or falls once and stays): its level some frames after it started
    private static int OneShotLevel(int shape, int frames, int envPeriod, YmFile ym)
    {
        double steps = frames * (ym.Clock / 8.0 / ym.FrameRate) / envPeriod;
        bool attack = (shape & 4) != 0;
        if (steps >= 32) return shape is 11 or 13 ? 31 : 0;   // after the first ramp: 0, or held at the top
        int s = (int)steps;
        return attack ? s : 31 - s;
    }

    // ── One row per frame ──────────────────────────────────────────────

    private sealed class DumpChannel
    {
        public Mode Mode = Mode.Silent;
        public int Note = -1, Fine, Volume = -1;
    }

    private static TrackerSong Dump(YmFile ym, Sound[,] sounds, Drums drums, (string, string)? buzzers)
    {
        var song = new TrackerSong { Speed = 1, Channels = TrackerSong.DefaultChannels };
        song.Instruments.Clear();
        song.Instruments.Add(Instrument("YM tone", "Square"));
        song.Instruments.Add(Instrument("YM noise", "Noise"));
        song.Instruments.Add(Instrument("YM tone + noise", "Square", "Noise"));
        song.Instruments.Add(Instrument("YM buzzer saw", "Sawtooth"));
        song.Instruments.Add(Instrument("YM buzzer triangle", "Triangle"));
        UseBuzzerSample(song.Instruments[3], Mode.BuzzSaw, buzzers);
        UseBuzzerSample(song.Instruments[4], Mode.BuzzTriangle, buzzers);
        int firstDrum = song.Instruments.Count + 1;
        song.Instruments.AddRange(drums.Instruments);
        var hits = new Dictionary<(int, int), DrumHit>();
        foreach (var h in ReadHits(ym, drums)) hits[(h.Frame, h.Channel)] = h;

        // Patterns of up to 256 rows; a loop back to the middle of the song starts a pattern of its own
        int frames = ym.Frames.Length;
        int loop = Math.Clamp(ym.LoopFrame, 0, Math.Max(0, frames - 1));
        var starts = new List<int>();
        for (int f = 0; f < loop; f += TrackerSong.MaxRows) starts.Add(f);
        int loopPattern = starts.Count;
        for (int f = loop; f < frames; f += TrackerSong.MaxRows) starts.Add(f);
        if (starts.Count == 0) starts.Add(0);

        var state = new DumpChannel[3];
        for (int c = 0; c < 3; c++) state[c] = new DumpChannel();
        song.Patterns.Clear();
        for (int p = 0; p < starts.Count; p++)
        {
            int from = starts[p];
            int to = p + 1 < starts.Count ? starts[p + 1] : frames;
            var pattern = new TrackerPattern(Math.Max(1, to - from));
            for (int f = from; f < to; f++)
                for (int c = 0; c < 3; c++)
                {
                    if (hits.TryGetValue((f, c), out var hit))
                    {
                        // A digidrum: its note, and the channel's own sound starts afresh after it
                        pattern[f - from, c] = drums.Cell(hit, firstDrum);
                        state[c] = new DumpChannel();
                        continue;
                    }
                    pattern[f - from, c] = DumpCell(sounds[f, c], state[c]);
                }
            song.Patterns.Add(pattern);
        }
        song.Order.Clear();
        song.Order.AddRange(Enumerable.Range(0, song.Patterns.Count));
        // Back to the loop at the end (to the start the song goes by itself)
        if (loopPattern > 0 && loopPattern < song.Patterns.Count)
        {
            var last = song.Patterns[^1];
            last[last.Rows - 1, 3] = new TrackerCell(TrackerCell.NoNote, 0, 'B', loopPattern);
        }
        return song;
    }

    private static TrackerInstrument Instrument(string name, string wave, string osc2 = "Off") => new()
    {
        Name = name, Wave = wave, Volume = 0.21, PulseWidth = 50, Attack = 0, Decay = 0, Sustain = 100, Release = 2,
        Osc2Wave = osc2, Osc2Mix = 50
    };

    private static TrackerCell DumpCell(Sound s, DumpChannel ch)
    {
        int volume = Volume(s.Step);
        if (!s.Sounds || volume == 0)
        {
            if (ch.Volume == 0 || ch.Mode == Mode.Silent && ch.Note < 0) return TrackerCell.Empty;
            ch.Volume = 0;
            return new TrackerCell(TrackerCell.NoNote, 0, '\0', 0, 0);
        }

        int nearest = Math.Clamp((int)Math.Round(s.Semis), 12, 119);   // C-0 – B-8
        int fine = Math.Clamp((int)Math.Round((s.Semis - nearest) * 16), -8, 8);
        int note = TrackerCell.NoNote, inst = 0;
        char effect = '\0';
        int param = 0;
        if (ch.Note < 0 || s.Mode != ch.Mode || nearest != ch.Note)
        {
            note = nearest;
            if (s.Mode != ch.Mode || ch.Note < 0) inst = (int)s.Mode;   // instruments 1 – 5 in mode order
            if (fine != 0) (effect, param) = fine > 0 ? ('E', 0x10 | fine) : ('E', 0x20 | -fine);
            ch.Note = nearest;
            ch.Mode = s.Mode;
            ch.Fine = fine;
        }
        else if (fine != ch.Fine)
        {
            int delta = fine - ch.Fine;
            (effect, param) = delta > 0 ? ('E', 0x10 | Math.Min(15, delta)) : ('E', 0x20 | Math.Min(15, -delta));
            ch.Fine = fine;
        }
        int vol = TrackerCell.NoVolume;
        if (volume != ch.Volume || inst > 0)
        {
            vol = volume;
            ch.Volume = volume;
        }
        if (note == TrackerCell.NoNote && effect == '\0' && vol == TrackerCell.NoVolume) return TrackerCell.Empty;
        return new TrackerCell(note, inst, effect, param, vol);
    }

    // ── The analysis ──────────────────────────────────────────────────

    // One frame of a note: the sound, the pitch from the note, and the volume below the note's loudest.
    // Pitches within 15 cents count as the same: the chip's periods are whole numbers, so the same
    // arpeggio comes out a little differently on each note.
    private readonly record struct Step(Mode Mode, double Offset, int Down)
    {
        public bool Same(Step o) => Mode == o.Mode && (Mode == Mode.Silent || (Down == o.Down && Math.Abs(Offset - o.Offset) < 0.15));
    }

    private sealed class Note
    {
        public int Frame, Channel, Pitch, Loudest;
        public Step[] Steps = Array.Empty<Step>();
        public int Instrument;
    }

    private static IEnumerable<DrumHit> ReadHits(YmFile ym, Drums drums) =>
        ReadEffects(ym).Hits.Where(h => drums.Of.ContainsKey(h.Drum));

    private static TrackerSong? Analyse(YmFile ym, Sound[,] sounds, List<string> notes, Found report, Drums drums,
        (string, string)? buzzers)
    {
        int frames = sounds.GetLength(0);
        if (frames == 0) return null;
        var (speed, phase) = FindSpeed(sounds);
        report.Speed = speed;

        // The frames a row starts at: one a row before the phase (at speed 1), then one every `speed`
        var rowOf = new int[frames];   // the row a frame belongs to; -1 when it is not the row's first
        int rows = 0;
        for (int f = 0; f < frames; f++)
            rowOf[f] = f < phase || (f - phase) % speed == 0 ? rows++ : -1;

        // The notes, row by row. A channel starts one on a row where it sounds again after a silence
        // (from the row's start, even if the sound comes a frame or two later), or where at the row's
        // start it gets louder, changes its kind of sound, has its envelope restarted, or plays
        // another note than the one it started with.
        var rowStarts = Enumerable.Range(0, frames).Where(f => rowOf[f] >= 0).ToArray();
        var found = new List<Note>();
        for (int c = 0; c < 3; c++)
        {
            int start = -1;          // the frame the current note starts at
            double pitch = 0;        // the pitch it started with
            for (int k = 0; k < rowStarts.Length; k++)
            {
                int rs = rowStarts[k], re = k + 1 < rowStarts.Length ? rowStarts[k + 1] : frames;
                int firstSound = -1;
                for (int f = rs; f < re; f++)
                    if (sounds[f, c].Sounds) { firstSound = f; break; }
                bool silenced = start < 0 || !sounds[rs - 1, c].Sounds;   // no note, or it has fallen silent

                bool begins = false;
                if (firstSound >= 0)
                {
                    if (silenced || firstSound > rs) begins = true;
                    else
                    {
                        var now = sounds[rs, c];
                        var before = sounds[rs - 1, c];
                        begins = !before.Sounds || now.Step > before.Step || now.Mode != before.Mode || now.Retrigger
                                 || Math.Abs(now.Semis - pitch) > 0.5;
                    }
                }
                if (!begins) continue;
                if (start >= 0) found.Add(Finish(new Note { Frame = start, Channel = c }, sounds, start, rs));
                start = rs;
                pitch = sounds[firstSound, c].Semis;
            }
            if (start >= 0) found.Add(Finish(new Note { Frame = start, Channel = c }, sounds, start, frames));
        }

        // Notes that do the same (one is the start of the other) share an instrument
        var shapes = new List<Step[]>();
        foreach (var n in found.OrderByDescending(n => n.Steps.Length))
        {
            int k = shapes.FindIndex(sh => IsStartOf(n.Steps, sh));
            if (k < 0)
            {
                if (shapes.Count >= 255)
                {
                    notes.Add("The music changes too much between notes for instruments; it was read one row per frame.");
                    return null;
                }
                shapes.Add(n.Steps);
                k = shapes.Count - 1;
            }
            n.Instrument = k + 1;
        }

        var song = new TrackerSong { Speed = phase > 0 ? 1 : speed, Channels = TrackerSong.DefaultChannels };
        song.Instruments.Clear();
        var kinds = new Dictionary<string, int>();
        foreach (var shape in shapes) song.Instruments.Add(MakeInstrument(shape, kinds, buzzers));
        report.Kinds = kinds;
        int firstDrum = song.Instruments.Count + 1;
        if (firstDrum - 1 + drums.Instruments.Count > 255)
        {
            notes.Add("The music changes too much between notes for instruments; it was read one row per frame.");
            return null;
        }
        song.Instruments.AddRange(drums.Instruments);

        // The rows, then patterns that repeat written once
        var cells = new TrackerCell[rows, 4];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < 4; c++) cells[r, c] = TrackerCell.Empty;
        foreach (var n in found)
        {
            int volume = Volume(n.Loudest);
            cells[rowOf[n.Frame], n.Channel] = new TrackerCell(n.Pitch, n.Instrument, '\0', 0, volume >= 64 ? TrackerCell.NoVolume : volume);
        }
        // The digidrums on the row they are hit in, a few ticks late (EDx) when that is not its start
        foreach (var hit in ReadHits(ym, drums))
        {
            int start = hit.Frame;
            while (rowOf[start] < 0) start--;
            cells[rowOf[start], hit.Channel] = drums.Cell(hit, firstDrum, hit.Frame - start);
        }
        if (phase > 0) cells[phase, 3] = new TrackerCell(TrackerCell.NoNote, 0, 'F', speed);
        int loopRow = 0;
        if (ym.LoopFrame > 0 && ym.LoopFrame < frames)
        {
            int f = ym.LoopFrame;
            while (f > 0 && rowOf[f] < 0) f--;
            loopRow = rowOf[f];
        }
        report.LoopOrder = BuildPatterns(song, cells, loopRow);
        notes.Add($"Speed {speed}, {song.Instruments.Count} instruments, {song.Patterns.Count} patterns in an order of {song.Order.Count}.");
        return song;
    }

    // The note's frames up to the next note; a silent end is cut after its first frame (the envelope
    // stays at 0), silences in the middle are kept
    private static Note Finish(Note n, Sound[,] sounds, int from, int to)
    {
        int firstSound = from, lastSound = from;
        while (firstSound < to - 1 && !sounds[firstSound, n.Channel].Sounds) firstSound++;
        for (int f = from; f < to; f++) if (sounds[f, n.Channel].Sounds) lastSound = f;
        n.Pitch = Math.Clamp((int)Math.Round(sounds[firstSound, n.Channel].Semis), 12, 119);
        int end = Math.Min(to, lastSound + 2), loudest = 0;
        for (int f = from; f < end; f++) loudest = Math.Max(loudest, sounds[f, n.Channel].Step);
        n.Loudest = Math.Max(1, loudest);
        n.Steps = new Step[end - from];
        for (int f = from; f < end; f++)
        {
            var s = sounds[f, n.Channel];
            n.Steps[f - from] = s.Sounds ? new Step(s.Mode, s.Semis - n.Pitch, n.Loudest - s.Step) : new Step(Mode.Silent, 0, 0);
        }
        return n;
    }

    private static bool IsStartOf(Step[] part, Step[] whole)
    {
        if (part.Length > whole.Length) return false;
        for (int i = 0; i < part.Length; i++)
            if (!part[i].Same(whole[i])) return false;
        return true;
    }

    // How many frames a row lasts: the largest speed (up to 16) that has nearly every place where a
    // channel starts sounding, gets clearly louder (by 3 of the chip's 16 volume steps, so that an
    // instrument's own small swells do not count) or has its envelope restarted at the start of a
    // row; and where in it the rows start
    private static (int speed, int phase) FindSpeed(Sound[,] sounds)
    {
        int frames = sounds.GetLength(0);
        var starts = new List<int>();
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < 3; c++)
            {
                var s = sounds[f, c];
                var before = f > 0 ? sounds[f - 1, c] : default;
                if (s.Sounds && (!before.Sounds || s.Step >= before.Step + 6 || s.Retrigger)) { starts.Add(f); break; }
            }
        if (starts.Count < 8) return (1, 0);
        for (int speed = 16; speed >= 2; speed--)
        {
            var count = new int[speed];
            foreach (int f in starts) count[f % speed]++;
            int best = Array.IndexOf(count, count.Max());
            if (count[best] < starts.Count * 0.85) continue;
            // Rows of more than 8 frames are split (12 → 6, 15 → 5), as a tracker song would have them
            int rowSpeed = speed;
            if (speed > 8)
                for (int d = 8; d >= 3; d--)
                    if (speed % d == 0) { rowSpeed = d; break; }
            return (rowSpeed, best % rowSpeed);
        }
        return (1, 0);
    }

    // An instrument that plays the steps: the waves and pitches in its table, the volume in its envelope.
    // A repeating end (an arpeggio or vibrato held on) becomes a loop.
    private static TrackerInstrument MakeInstrument(Step[] steps, Dictionary<string, int> kinds, (string, string)? buzzers)
    {
        var (loopStart, loopLength) = FindLoop(steps);
        int length = loopLength > 0 ? loopStart + loopLength : steps.Length;
        var table = new List<string>();
        for (int i = 0; i < length; i++)
        {
            var s = steps[i];
            string wave = s.Mode switch
            {
                Mode.Noise or Mode.ToneNoise => "NOISE",
                Mode.BuzzSaw => "SAWTOOTH",
                Mode.BuzzTriangle => "TRIANGLE",
                _ => "SQUARE"
            };
            // Whole semitones as such; the rest (vibrato, detune) to a hundredth
            double offset = Math.Abs(s.Offset - Math.Round(s.Offset)) < 0.03 ? Math.Round(s.Offset) : Math.Round(s.Offset, 2);
            table.Add(offset == 0 ? wave : wave + " " + offset.ToString("+0.##;-0.##", CultureInfo.InvariantCulture));
        }

        // The envelope: a point per frame, without the ones on a straight line between their neighbours
        var level = new int[length];
        for (int i = 0; i < length; i++) level[i] = steps[i].Mode == Mode.Silent ? 0 : Volume(31 - steps[i].Down);
        var points = new List<TrackerEnvelopePoint>();
        for (int i = 0; i < length; i++)
        {
            bool keep = i == 0 || i == length - 1 || (loopLength > 0 && (i == loopStart || i == loopStart + loopLength - 1))
                        || level[i] - level[i - 1] != level[i + 1] - level[i];
            if (keep) points.Add(new TrackerEnvelopePoint(i, level[i]));
        }
        if (points.Count < 2) points.Add(new TrackerEnvelopePoint(1, level[^1]));

        var inst = Instrument(Kind(steps, kinds), "Square");
        var modes = steps.Where(s => s.Mode != Mode.Silent).Select(s => s.Mode).Distinct().ToList();
        if (modes.Count == 1 && modes[0] is Mode.BuzzSaw or Mode.BuzzTriangle) UseBuzzerSample(inst, modes[0], buzzers);
        inst.Table = table;
        inst.TableLoop = loopLength > 0 ? loopStart : -1;
        inst.Envelope = points;
        if (loopLength > 0)
        {
            inst.EnvelopeLoopStart = points.FindIndex(p => p.Tick == loopStart);
            inst.EnvelopeLoopEnd = points.FindIndex(p => p.Tick == loopStart + loopLength - 1);
        }
        return inst;
    }

    // The shortest repeating end: from loopStart the steps go round every loopLength (0: none)
    private static (int start, int length) FindLoop(Step[] steps)
    {
        int n = steps.Length;
        int bestStart = 0, bestLength = 0, bestCost = n;
        for (int p = 1; p <= n / 2; p++)
        {
            // The earliest start from which every step equals the one p before it
            int start = n - p;
            while (start > 0 && steps[start - 1 + p].Same(steps[start - 1])) start--;
            // It must go round at least twice to count as a loop
            if (n - start < 2 * p) continue;
            int cost = start + p;
            if (cost < bestCost) { bestCost = cost; bestStart = start; bestLength = p; }
        }
        return (bestStart, bestLength);
    }

    // A name from what the instrument does: Drum (noise and tone), Arp, Vibrato, Buzzer, Noise or Tone
    private static string Kind(Step[] steps, Dictionary<string, int> kinds)
    {
        var sounding = steps.Where(s => s.Mode != Mode.Silent).ToList();
        bool noise = sounding.Any(s => s.Mode is Mode.Noise or Mode.ToneNoise);
        bool tone = sounding.Any(s => s.Mode == Mode.Tone);
        string kind = sounding.Any(s => s.Mode is Mode.BuzzSaw or Mode.BuzzTriangle) ? "Buzzer"
            : noise && tone ? "Drum"
            : noise ? "Noise"
            : sounding.Select(s => Math.Round(s.Offset)).Distinct().Count() > 1 ? "Arp"
            : sounding.Any(s => Math.Abs(s.Offset - sounding[0].Offset) > 0.05) ? "Vibrato"
            : "Tone";
        kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        return $"{kind} {kinds[kind]}";
    }

    // Cuts the rows into patterns of the length that repeats best, and writes a repeat once. The loop
    // starts a pattern, and the last pattern is its own, as it jumps back.
    // Returns the place in the order list the end jumps back to (0: the start)
    private static int BuildPatterns(TrackerSong song, TrackerCell[,] cells, int loopRow)
    {
        int rows = cells.GetLength(0);
        string RowKey(int r) => string.Join("|", Enumerable.Range(0, 4).Select(c => cells[r, c].ToString()));
        var keys = Enumerable.Range(0, rows).Select(RowKey).ToArray();

        List<(int from, int to)> Blocks(int length)
        {
            var list = new List<(int, int)>();
            for (int r = 0; r < loopRow; r += length) list.Add((r, Math.Min(loopRow, r + length)));
            for (int r = loopRow; r < rows; r += length) list.Add((r, Math.Min(rows, r + length)));
            return list;
        }
        string BlockKey((int from, int to) b) => string.Join("\n", keys[b.from..b.to]);

        // The length that leaves the fewest rows to write, with a long order list counting against it
        int bestLength = 64;
        long bestCost = long.MaxValue;
        foreach (int length in new[] { 16, 32, 48, 64, 96, 128, 192, 256 })
        {
            var blocks = Blocks(length);
            // The jump back to the loop (Bxx) reaches places 0 – 255 of the order list
            if (blocks.Count > 256 && length < 256) continue;
            long unique = blocks.Take(blocks.Count - 1).Select(BlockKey).Distinct().Sum(k => k.Count(ch => ch == '\n') + 1)
                          + (blocks[^1].to - blocks[^1].from);
            long cost = unique + 8L * blocks.Count;
            if (cost < bestCost) { bestCost = cost; bestLength = length; }
        }

        var chosen = Blocks(bestLength);
        var patternOf = new Dictionary<string, int>();
        song.Patterns.Clear();
        song.Order.Clear();
        int loopOrder = 0;
        for (int b = 0; b < chosen.Count; b++)
        {
            var (from, to) = chosen[b];
            if (from == loopRow) loopOrder = b;
            bool last = b == chosen.Count - 1;
            string key = BlockKey(chosen[b]);
            if (last || !patternOf.TryGetValue(key, out int p))
            {
                var pattern = new TrackerPattern(to - from);
                for (int r = from; r < to; r++)
                    for (int c = 0; c < 4; c++) pattern[r - from, c] = cells[r, c];
                song.Patterns.Add(pattern);
                p = song.Patterns.Count - 1;
                if (!last) patternOf[key] = p;
            }
            song.Order.Add(p);
        }
        // Back to the loop at the end (to the start the song goes by itself)
        if (loopRow > 0)
        {
            var end = song.Patterns[song.Order[^1]];
            var cell = end[end.Rows - 1, 3];
            end[end.Rows - 1, 3] = new TrackerCell(cell.Note, cell.Instrument, 'B', loopOrder, cell.Volume);
        }
        return loopRow > 0 ? loopOrder : 0;
    }
}
