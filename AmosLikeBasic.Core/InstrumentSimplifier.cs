using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AmosLikeBasic;

// Makes fewer instruments out of many that are the same sound with something put on top, as a YM
// file's are: each note an instrument of its own because its arpeggio chord or vibrato differs.
//  1. Each instrument (one with a table and an envelope of points) is taken apart: an arpeggio in its
//     table becomes the effect 0xy, a vibrato 4xy, and a note it is moved by goes into the notes.
//  2. What is left, the wave and the volume tick by tick, is compared, and instruments that are
//     nearly the same become one (the most used of them); a shorter one counts as the start of a longer one.
//  3. The patterns get the new instruments, the moved notes, and the effects on the rows a note sounds.
// The song then sounds nearly, not exactly, as before; undo brings the old one back.
public static class InstrumentSimplifier
{
    public sealed record Result(int Before, int After, int Arpeggios, int Vibratos);

    private sealed class Part
    {
        public int Number;                       // the instrument, from 1
        public string[] Waves = Array.Empty<string>();
        public int[] Levels = Array.Empty<int>();
        public double[] Offsets = Array.Empty<double>();   // what is left of the pitch after taking out the effect
        public int Shift;                        // semitones moved into the notes
        public (char effect, int param)? Effect;
        public bool TakenApart;
        public int Uses;
        public int NewNumber;
    }

    public static Result Simplify(TrackerSong song)
    {
        int before = song.Instruments.Count;
        var uses = CountUses(song);
        var parts = new Dictionary<int, Part>();
        for (int i = 0; i < song.Instruments.Count; i++)
            if (Read(song.Instruments[i], i + 1) is { } part)
            {
                part.Uses = uses.GetValueOrDefault(i + 1);
                parts[i + 1] = part;
            }
        if (parts.Count < 2) return new Result(before, before, 0, 0);

        // Group the parts, the most used first; each joins the first group it sounds like
        var groups = new List<List<Part>>();
        foreach (var p in parts.Values.OrderByDescending(p => p.Uses).ThenBy(p => p.Number))
        {
            var g = groups.FirstOrDefault(g => Alike(g[0], p));
            if (g == null) groups.Add(new List<Part> { p });
            else g.Add(p);
        }

        // The new instrument list: the ones not taken apart as they were, then one per group
        var instruments = new List<TrackerInstrument>();
        var newNumber = new Dictionary<int, int>();
        var kinds = new Dictionary<string, int>();
        for (int i = 0; i < song.Instruments.Count; i++)
        {
            if (parts.ContainsKey(i + 1)) continue;
            instruments.Add(song.Instruments[i]);
            newNumber[i + 1] = instruments.Count;
        }
        foreach (var g in groups)
        {
            instruments.Add(Build(song.Instruments[g[0].Number - 1], g[0], kinds));
            foreach (var p in g) newNumber[p.Number] = instruments.Count;
        }

        // The patterns, in the order they are played, so that a note going on into the next pattern keeps its effect
        int channels = song.Channels;
        var current = new Part?[channels];
        var done = new HashSet<int>();
        foreach (int pi in song.Order.Concat(Enumerable.Range(0, song.Patterns.Count)))
        {
            if (pi < 0 || pi >= song.Patterns.Count || !done.Add(pi)) continue;
            var pattern = song.Patterns[pi];
            for (int r = 0; r < pattern.Rows; r++)
                for (int c = 0; c < channels; c++)
                {
                    var cell = pattern[r, c];
                    int inst = cell.Instrument;
                    int note = cell.Note;
                    if (inst > 0) current[c] = parts.GetValueOrDefault(inst);
                    if (note == TrackerCell.NoteOff) current[c] = null;
                    var part = current[c];
                    if (inst > 0 && newNumber.TryGetValue(inst, out int n)) inst = n;
                    if (note >= 0 && part != null) note = Math.Clamp(note + part.Shift, 12, 119);
                    char effect = cell.Effect;
                    int param = cell.Param;
                    if (effect == '\0' && part?.Effect is { } e) (effect, param) = e;
                    pattern[r, c] = new TrackerCell(note, inst, effect, param, cell.Volume);
                }
        }
        song.Instruments.Clear();
        song.Instruments.AddRange(instruments);
        return new Result(before, instruments.Count, parts.Values.Count(p => p.Effect?.effect is '0' or 'J'), parts.Values.Count(p => p.Effect?.effect == '4'));
    }

    private static Dictionary<int, int> CountUses(TrackerSong song)
    {
        var uses = new Dictionary<int, int>();
        foreach (int pi in song.Order)
        {
            if (pi < 0 || pi >= song.Patterns.Count) continue;
            var p = song.Patterns[pi];
            for (int r = 0; r < p.Rows; r++)
                for (int c = 0; c < song.Channels; c++)
                    if (p[r, c].Instrument > 0) uses[p[r, c].Instrument] = uses.GetValueOrDefault(p[r, c].Instrument) + 1;
        }
        return uses;
    }

    // The instrument tick by tick, and its effect taken out; null when it has no envelope of points or
    // a table step that is more than a wave and a pitch
    private static Part? Read(TrackerInstrument inst, int number)
    {
        if (inst.Envelope == null || inst.Envelope.Count < 2) return null;
        var steps = new List<(string wave, double offset)>();
        foreach (var text in inst.Table ?? new List<string>())
        {
            string stepWave = "";
            double offset = 0;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n)) offset = n;
                else if (word.ToUpperInvariant() is "SQUARE" or "PULSE" or "TRIANGLE" or "SAWTOOTH" or "SAW" or "SINE" or "NOISE") stepWave = word.ToUpperInvariant();
                else return null;
            }
            steps.Add((stepWave, offset));
        }

        var env = inst.Envelope.OrderBy(p => p.Tick).ToList();
        int natural = Math.Max(Math.Max(steps.Count, env[^1].Tick + 1), 2), length = natural;
        // An envelope that ends above 0 holds its last level for as long as the note is held: its volume
        // is compared with others' for a while longer, as the held sound it is
        if (env[^1].Level > 0 || inst.EnvelopeLoopEnd >= 0) length = Math.Max(length, HeldTicks);
        var part = new Part { Number = number, Waves = new string[length], Levels = new int[length], Offsets = new double[length] };
        string baseWave = (inst.Wave ?? "Square").ToUpperInvariant();
        string wave = baseWave;
        int envTick = 0;
        for (int t = 0; t < length; t++)
        {
            // The table: a step a tick, then round its loop or stay on the last step
            if (steps.Count > 0)
            {
                int k = t < steps.Count ? t : inst.TableLoop >= 0 && inst.TableLoop < steps.Count
                    ? inst.TableLoop + (t - steps.Count) % (steps.Count - inst.TableLoop) : steps.Count - 1;
                if (steps[k].wave.Length > 0) wave = steps[k].wave;
                part.Offsets[t] = steps[k].offset;
            }
            part.Waves[t] = wave;
            part.Levels[t] = Level(env, envTick);
            envTick++;
            if (inst.EnvelopeLoopEnd >= 0 && inst.EnvelopeLoopStart >= 0 && inst.EnvelopeLoopEnd < env.Count && envTick > env[inst.EnvelopeLoopEnd].Tick)
                envTick = env[inst.EnvelopeLoopStart].Tick;
        }

        // Only a tone (no noise) is taken apart: a drum's pitches are its sound
        var sounding = Enumerable.Range(0, natural).Where(t => part.Levels[t] > 0).ToList();
        if (sounding.Count == 0 || sounding.Any(t => part.Waves[t] == "NOISE")) return part;
        var o = sounding.Select(t => part.Offsets[t]).ToArray();
        var whole = o.Select(v => Math.Round(v)).ToArray();
        bool steady = o.Zip(whole, (a, b) => Math.Abs(a - b)).Max() < 0.15;

        if (steady && whole.Distinct().Count() == 1)
            TakeOut(part, (int)whole[0], null);
        else if (steady && Arpeggio(whole) is var (low, x, y))
            TakeOut(part, low, ('0', x << 4 | y));
        else if (steady && TickArpeggio(whole) is var (tlow, tp))
            TakeOut(part, tlow, ('J', tp));
        else if (!steady && Vibrato(o) is var (centre, speed, depth))
            TakeOut(part, centre, ('4', speed << 4 | depth));
        return part;
    }

    private const int HeldTicks = 32;

    private static void TakeOut(Part part, int shift, (char, int)? effect)
    {
        part.Shift = shift;
        part.Effect = effect;
        part.TakenApart = true;
        Array.Clear(part.Offsets);
    }

    // Three pitches going round, one each tick: the lowest becomes the note, the others x and y above it
    private static (int low, int x, int y)? Arpeggio(double[] whole)
    {
        if (whole.Length < 6) return null;   // at least twice round
        int n = Math.Min(whole.Length, 24);
        int fits = Enumerable.Range(0, n).Count(i => whole[i] == whole[i % 3]);
        if (fits < n * 0.8) return null;
        var three = whole.Take(3).Select(v => (int)v).ToArray();
        int start = Array.IndexOf(three, three.Min());
        int low = three[start], x = three[(start + 1) % 3] - low, y = three[(start + 2) % 3] - low;
        return x is >= 0 and <= 15 && y is >= 0 and <= 15 ? (low, x, y) : null;
    }

    // Two pitches taking turns each tick (Jx0), or four going up with the octave last (Jxy). They go round
    // from the note's start, so only a round that starts on its lowest pitch is the same (short notes
    // would start on the wrong pitch otherwise)
    private static (int low, int param)? TickArpeggio(double[] whole)
    {
        if (whole.Length < 4) return null;
        int n = Math.Min(whole.Length, 24);
        foreach (int period in new[] { 2, 4 })
        {
            if (Enumerable.Range(0, n).Any(i => whole[i] != whole[i % period])) continue;
            var round = whole.Take(period).Select(v => (int)v).ToArray();
            int low = round[0];
            var above = round.Select(v => v - low).ToArray();
            if (period == 2 && above[1] is > 0 and <= 15) return (low, above[1] << 4);
            if (period == 4 && above[1] > 0 && above[2] > above[1] && above[2] < 12 && above[3] == 12) return (low, above[1] << 4 | above[2]);
        }
        return null;
    }

    // A pitch that swings around a centre, at most 1.5 semitones each way: speed and depth as in 4xy
    // (the vibrato goes once round in 64 / speed ticks, depth / 8 semitones each way)
    private static (int centre, int speed, int depth)? Vibrato(double[] o)
    {
        double max = o.Max(), min = o.Min();
        double centre = (max + min) / 2, swing = (max - min) / 2;
        if (swing < 0.04 || swing > 1.5) return null;
        // How often it passes the centre (values on the centre itself do not count either way)
        int crossings = 0, side = 0;
        foreach (double v in o)
        {
            int now = Math.Sign(Math.Round(v - centre, 3));
            if (now == 0) continue;
            if (side != 0 && now != side) crossings++;
            side = now;
        }
        if (crossings < 2) return null;
        double period = 2.0 * o.Length / crossings;
        int speed = Math.Clamp((int)Math.Round(64 / period), 1, 15);
        int depth = Math.Clamp((int)Math.Round(swing * 8), 1, 15);
        return ((int)Math.Round(centre), speed, depth);
    }

    private static int Level(List<TrackerEnvelopePoint> env, int t)
    {
        for (int k = 0; k < env.Count - 1; k++)
            if (t < env[k + 1].Tick)
            {
                int span = Math.Max(1, env[k + 1].Tick - env[k].Tick);
                return (int)Math.Round(env[k].Level + (env[k + 1].Level - env[k].Level) * Math.Clamp((t - env[k].Tick) / (double)span, 0, 1));
            }
        return env[^1].Level;
    }

    // The same waves, nearly the same volume (on average within 6 of 64, nowhere more than 20), and the
    // same pitches where they were not taken apart; over the ticks both have, since a shorter one is a
    // note the next note cut off, and that cut stays where it is (a held one is looked at for HeldTicks,
    // so that a sound that goes on is not taken for one that dies away)
    private static bool Alike(Part a, Part b)
    {
        int n = Math.Min(a.Levels.Length, b.Levels.Length);
        double sum = 0;
        for (int t = 0; t < n; t++)
        {
            bool sa = a.Levels[t] > 0, sb = b.Levels[t] > 0;
            if (sa && sb && a.Waves[t] != b.Waves[t]) return false;
            int diff = Math.Abs(a.Levels[t] - b.Levels[t]);
            if (diff > 20) return false;
            if (Math.Abs(a.Offsets[t] - b.Offsets[t]) > 0.15) return false;
            sum += diff;
        }
        return sum / n <= 6;
    }

    // The group's instrument: the most used one, without the pitches that are now effects
    private static TrackerInstrument Build(TrackerInstrument source, Part part, Dictionary<string, int> kinds)
    {
        var inst = InstrumentLibrary.Copy(source);
        if (part.TakenApart)
        {
            // One wave all through needs no table
            if (part.Waves.Distinct().Count() == 1)
            {
                inst.Table = new List<string>();
                inst.Wave = WaveName(part.Waves[0]);
            }
            else
            {
                // The waves tick by tick, up to the last change
                int lastChange = Enumerable.Range(1, part.Waves.Length - 1).LastOrDefault(t => part.Waves[t] != part.Waves[t - 1]);
                inst.Table = part.Waves.Take(lastChange + 1).ToList();
            }
            inst.TableLoop = -1;
        }
        bool noise = part.Waves.Contains("NOISE"), tone = part.Waves.Any(w => w is "SQUARE" or "PULSE");
        string kind = part.Waves.Any(w => w is "SAWTOOTH" or "SAW" or "TRIANGLE") ? "Buzzer"
            : noise && tone ? "Drum" : noise ? "Noise" : part.TakenApart ? "Tone" : (source.Name ?? "Sound").Split(' ')[0];
        kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        inst.Name = $"{kind} {kinds[kind]}";
        return inst;
    }

    private static string WaveName(string w) => w switch
    {
        "NOISE" => "Noise",
        "SAWTOOTH" or "SAW" => "Sawtooth",
        "TRIANGLE" => "Triangle",
        "SINE" => "Sine",
        "PULSE" => "Pulse",
        _ => "Square"
    };
}
