using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

// Turns a YM file into a tracker song, one row per frame (speed 1), so that it plays as the
// original but can be edited. The chip's three channels become channels 1 – 3:
//  - the pitch is the nearest note, and the rest of it a fine slide (E1x / E2x) in sixteenths of
//    a semitone, so vibratos and slides follow;
//  - the volume goes in the volume column, with the chip's steps turned into 0 – 40;
//  - what the mixer lets through picks the instrument: tone (a square wave), noise, or both;
//  - the hardware envelope as a repeating "buzzer" becomes a sawtooth or triangle at its pitch,
//    and as a single fade, the volume it gives in each frame.
// The YM5/YM6 special effects (digidrums, SID voice, sync buzzer) are left out.
public static class YmImport
{
    private enum Mode { Silent, Tone, Noise, ToneNoise, BuzzSaw, BuzzTriangle }

    // Instrument numbers in the song, by mode
    private static readonly int[] InstrumentOf = { 0, 1, 2, 3, 4, 5 };

    private static readonly double[] Level = Enumerable.Range(0, 32).Select(i => i == 0 ? 0 : Math.Pow(10, (i - 31) * 1.5 / 20)).ToArray();

    public static ModImport.Result Import(YmFile ym, string name)
    {
        var notes = new List<string>();
        var song = new TrackerSong
        {
            Title = ym.Title.Length > 0 ? ym.Title : name,
            Speed = 1,
            Tempo = Math.Clamp((int)Math.Round(125.0 * ym.FrameRate / 50), 32, 255),
            Channels = TrackerSong.DefaultChannels
        };
        if (ym.FrameRate * 2.5 > 255) notes.Add($"The file plays {ym.FrameRate} frames a second, faster than the tracker's top tempo; it plays slower.");
        song.Instruments.Clear();
        TrackerInstrument Inst(string n, string wave, string osc2 = "Off") => new()
        {
            Name = n, Wave = wave, Volume = 0.21, PulseWidth = 50, Attack = 0, Decay = 0, Sustain = 100, Release = 2,
            Osc2Wave = osc2, Osc2Mix = 50
        };
        song.Instruments.Add(Inst("YM tone", "Square"));
        song.Instruments.Add(Inst("YM noise", "Noise"));
        song.Instruments.Add(Inst("YM tone + noise", "Square", "Noise"));
        song.Instruments.Add(Inst("YM buzzer saw", "Sawtooth"));
        song.Instruments.Add(Inst("YM buzzer triangle", "Triangle"));

        // Patterns of up to 256 rows; a loop back to the middle of the song starts a pattern of its own
        int frames = ym.Frames.Length;
        int loop = Math.Clamp(ym.LoopFrame, 0, Math.Max(0, frames - 1));
        var starts = new List<int>();
        for (int f = 0; f < loop; f += TrackerSong.MaxRows) starts.Add(f);
        int loopPattern = starts.Count;
        for (int f = loop; f < frames; f += TrackerSong.MaxRows) starts.Add(f);
        if (starts.Count == 0) starts.Add(0);

        var state = new Channel[3];
        for (int c = 0; c < 3; c++) state[c] = new Channel();
        int envShape = 0, envStart = 0;   // the shape last written, and the frame it was written in
        song.Patterns.Clear();
        for (int p = 0; p < starts.Count; p++)
        {
            int from = starts[p];
            int to = p + 1 < starts.Count ? starts[p + 1] : frames;
            var pattern = new TrackerPattern(Math.Max(1, to - from));
            for (int f = from; f < to; f++)
            {
                var r = ym.Frames[f];
                // Register 13 is 0xFF in frames where it was not written; a write starts the envelope again
                if (r[13] != 0xFF) { envShape = r[13] & 15; envStart = f; }
                for (int c = 0; c < 3; c++)
                    pattern[f - from, c] = Row(ym, r, c, state[c], envShape, f - envStart);
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
        if (ym.UsesSpecialEffects) notes.Add("The file uses YM5/YM6 special effects (digidrums or SID voice); they were left out.");
        return new ModImport.Result(song, 0, notes);
    }

    private sealed class Channel
    {
        public Mode Mode = Mode.Silent;
        public int Note = -1, Fine, Volume = -1;
    }

    private static TrackerCell Row(YmFile ym, byte[] r, int c, Channel ch, int envShape, int framesSinceEnv)
    {
        bool tone = (r[7] & (1 << c)) == 0, noise = (r[7] & (8 << c)) == 0;
        int v = r[8 + c];
        bool env = (v & 16) != 0;
        int envPeriod = Math.Max(1, r[11] | r[12] << 8);
        bool repeating = envShape >= 8 && envShape is not (9 or 11 or 13 or 15);

        Mode mode;
        double freq;
        int level;   // 0 – 31
        if (env && repeating && envPeriod < 4096)
        {
            mode = envShape is 10 or 14 ? Mode.BuzzTriangle : Mode.BuzzSaw;
            freq = ym.Clock / (envShape is 10 or 14 ? 512.0 : 256.0) / envPeriod;
            level = 31;
        }
        else
        {
            mode = tone && noise ? Mode.ToneNoise : tone ? Mode.Tone : noise ? Mode.Noise : Mode.Silent;
            int tonePeriod = Math.Max(1, r[c * 2] | (r[c * 2 + 1] & 15) << 8);
            int noisePeriod = Math.Max(1, r[6] & 31);
            // The noise oscillator is clocked at 16 × the note
            freq = mode == Mode.Noise ? ym.Clock / 16.0 / noisePeriod / 16 : ym.Clock / 16.0 / tonePeriod;
            level = env ? OneShotLevel(envShape, framesSinceEnv, envPeriod, ym) : (v & 15) == 0 ? 0 : (v & 15) * 2 + 1;
            if (freq > 12000) mode = Mode.Silent;   // above hearing: used for sample tricks
        }
        int volume = (int)Math.Round(Level[level] * 64);

        if (mode == Mode.Silent || volume == 0)
        {
            if (ch.Volume == 0 || ch.Mode == Mode.Silent && ch.Note < 0) return TrackerCell.Empty;
            ch.Volume = 0;
            return new TrackerCell(TrackerCell.NoNote, 0, '\0', 0, 0);
        }

        double semis = 69 + 12 * Math.Log2(freq / 440.0);
        int nearest = Math.Clamp((int)Math.Round(semis), 12, 119);   // C-0 – B-8
        int fine = Math.Clamp((int)Math.Round((semis - nearest) * 16), -8, 8);
        int note = TrackerCell.NoNote, inst = 0;
        char effect = '\0';
        int param = 0;
        if (ch.Note < 0 || mode != ch.Mode || nearest != ch.Note)
        {
            note = nearest;
            if (mode != ch.Mode || ch.Note < 0) inst = InstrumentOf[(int)mode];
            if (fine != 0) (effect, param) = fine > 0 ? ('E', 0x10 | fine) : ('E', 0x20 | -fine);
            ch.Note = nearest;
            ch.Mode = mode;
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

    // A single envelope (it rises or falls once and stays): its level some frames after it started
    private static int OneShotLevel(int shape, int frames, int envPeriod, YmFile ym)
    {
        double steps = frames * (ym.Clock / 8.0 / ym.FrameRate) / envPeriod;
        bool attack = (shape & 4) != 0;
        if (steps >= 32)
        {
            // After the first ramp: 0, or held at the top for shapes 11 and 13
            return shape is 11 or 13 ? 31 : 0;
        }
        int s = (int)steps;
        return attack ? s : 31 - s;
    }
}
