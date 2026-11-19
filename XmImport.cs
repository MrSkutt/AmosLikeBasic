using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AmosLikeBasic;

// Reads a FastTracker 2 module (.xm) into a tracker song, as ModImport does for MOD files. XM has
// more than the tracker, so some of it is made simpler:
//  - an instrument can have several samples over the keys; each sample becomes its own instrument,
//    and a note gets the one its key plays;
//  - the volume envelope becomes an ADSR, and the fadeout part of the release;
//  - the volume column goes into the effect column when that is free; a volume for a new note
//    when it is not becomes a copy of the instrument that starts at that volume, and a volume
//    slide with a glide or vibrato that carries on becomes 5xy or 6xy;
//  - ping-pong loops are played forwards and backwards in the WAV file, so they sound the same.
// The notes are kept: XM's C-4 is C-4 here, and a sample with no relative note plays at 8363 Hz there.
public static class XmImport
{
    private const double C4Rate = 8363;
    private const int MaxInstruments = 255;

    private readonly record struct Cell(int Note, int Instrument, int Volume, int Effect, int Param);

    private sealed class Sample
    {
        public string Name = "";
        public float[] Data = Array.Empty<float>();
        public int LoopStart, LoopEnd;        // frames; LoopEnd 0 = no loop
        public int Volume, RelativeNote, Finetune;
        public int Panning = 128;
        public int Tracker;                   // the tracker instrument it became, 0 = none
    }

    private sealed class Instrument
    {
        public string Name = "";
        public byte[] KeyMap = new byte[96];
        public List<Sample> Samples = new();
        public (int x, int y)[] Envelope = Array.Empty<(int, int)>();
        public int Sustain = -1, LoopStart = -1, LoopEnd = -1;
        public bool EnvelopeOn;
        public int Fadeout;
    }

    public static bool IsXm(byte[] head) =>
        head.Length >= 17 && Encoding.ASCII.GetString(head, 0, 17) == "Extended Module: ";

    public static ModImport.Result Import(byte[] xm, string name, string sampleFolder, string sampleFolderInSong)
    {
        if (xm.Length < 80 || !IsXm(xm)) throw new InvalidDataException("not a FastTracker 2 XM file");
        var notes = new List<string>();
        string title = ModImport.Text(xm, 17, 20);
        int headerSize = I32(xm, 60);
        int songLength = Math.Clamp(U16(xm, 64), 1, 256);
        int channels = U16(xm, 68);
        int patternCount = U16(xm, 70);
        int instrumentCount = U16(xm, 72);
        bool linear = (U16(xm, 74) & 1) != 0;
        int speed = Math.Clamp(U16(xm, 76), 1, 31), bpm = Math.Clamp(U16(xm, 78), 32, 255);
        if (channels is < 1 or > 64) throw new InvalidDataException($"the XM file says it has {channels} channels");
        var order = Enumerable.Range(0, songLength).Select(i => (int)xm[80 + i]).ToList();

        // Patterns
        int pos = 60 + headerSize;
        var patterns = new List<Cell[,]>();
        for (int p = 0; p < patternCount; p++)
        {
            Need(xm, pos + 9);
            int length = I32(xm, pos), rows = Math.Clamp(U16(xm, pos + 5), 1, TrackerSong.MaxRows), packed = U16(xm, pos + 7);
            int d = pos + length, end = d + packed;
            Need(xm, end);
            var cells = new Cell[rows, channels];
            if (packed > 0)
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < channels; c++)
                    {
                        if (d >= end) break;
                        int b = xm[d++], note = 0, inst = 0, vol = 0, eff = 0, par = 0;
                        if ((b & 0x80) != 0)
                        {
                            if ((b & 1) != 0) note = xm[d++];
                            if ((b & 2) != 0) inst = xm[d++];
                            if ((b & 4) != 0) vol = xm[d++];
                            if ((b & 8) != 0) eff = xm[d++];
                            if ((b & 16) != 0) par = xm[d++];
                        }
                        else
                        {
                            note = b; inst = xm[d++]; vol = xm[d++]; eff = xm[d++]; par = xm[d++];
                        }
                        cells[r, c] = new Cell(note, inst, vol, eff, par);
                    }
            patterns.Add(cells);
            pos = end;
        }

        // Instruments and their samples
        var instruments = new List<Instrument>();
        for (int i = 0; i < instrumentCount; i++)
        {
            Need(xm, pos + 29);
            int size = I32(xm, pos);
            var inst = new Instrument { Name = ModImport.Text(xm, pos + 4, 22) };
            int sampleCount = U16(xm, pos + 27);
            int sampleHeaderSize = 40;
            if (sampleCount > 0)
            {
                Need(xm, pos + 243);
                sampleHeaderSize = I32(xm, pos + 29);
                Array.Copy(xm, pos + 33, inst.KeyMap, 0, 96);
                int points = Math.Min((int)xm[pos + 225], 12);
                inst.Envelope = Enumerable.Range(0, points).Select(k => (U16(xm, pos + 129 + k * 4), Math.Min(U16(xm, pos + 131 + k * 4), 64))).ToArray();
                int type = xm[pos + 233];
                inst.EnvelopeOn = (type & 1) != 0 && points > 0;
                if ((type & 2) != 0) inst.Sustain = Math.Min((int)xm[pos + 227], points - 1);
                if ((type & 4) != 0) { inst.LoopStart = Math.Min((int)xm[pos + 228], points - 1); inst.LoopEnd = Math.Min((int)xm[pos + 229], points - 1); }
                inst.Fadeout = U16(xm, pos + 239);
            }
            pos += size;

            var headers = new List<(int length, int loopStart, int loopLength, int type, Sample s)>();
            for (int k = 0; k < sampleCount; k++)
            {
                Need(xm, pos + 40);
                var s = new Sample
                {
                    Volume = Math.Min((int)xm[pos + 12], 64),
                    Finetune = (sbyte)xm[pos + 13],
                    Panning = xm[pos + 15],
                    RelativeNote = (sbyte)xm[pos + 16],
                    Name = ModImport.Text(xm, pos + 18, 22)
                };
                headers.Add((I32(xm, pos), I32(xm, pos + 4), I32(xm, pos + 8), xm[pos + 14], s));
                pos += sampleHeaderSize;
            }
            foreach (var (length, loopStart, loopLength, type, s) in headers)
            {
                int bytes = Math.Min(Math.Max(length, 0), Math.Max(0, xm.Length - pos));
                bool wide = (type & 16) != 0;
                int frames = wide ? bytes / 2 : bytes;
                var data = new float[frames];
                if (wide)
                {
                    short last = 0;
                    for (int f = 0; f < frames; f++) { last += (short)(xm[pos + f * 2] | (xm[pos + f * 2 + 1] << 8)); data[f] = last / 32768f; }
                }
                else
                {
                    sbyte last = 0;
                    for (int f = 0; f < frames; f++) { last += (sbyte)xm[pos + f]; data[f] = last / 128f; }
                }
                pos += Math.Max(length, 0);

                int ls = wide ? loopStart / 2 : loopStart, ll = wide ? loopLength / 2 : loopLength;
                int loop = type & 3;
                if (loop != 0 && ll > 1 && ls < frames)
                {
                    int le = Math.Min(ls + ll, frames);
                    if (loop == 2)
                    {
                        // Ping-pong: the loop forwards and then backwards, played forwards
                        var back = new float[le - ls - 2 > 0 ? le - ls - 2 : 0];
                        for (int f = 0; f < back.Length; f++) back[f] = data[le - 2 - f];
                        data = data.Take(le).Concat(back).ToArray();
                        le = data.Length;
                    }
                    s.LoopStart = ls;
                    s.LoopEnd = le;
                }
                s.Data = data;
                inst.Samples.Add(s);
            }
            instruments.Add(inst);
        }

        // Each sample with sound becomes a tracker instrument
        var song = new TrackerSong { Title = title.Length > 0 ? title : name, Speed = speed, Tempo = bpm };
        song.Instruments.Clear();
        double tickMs = 2500.0 / bpm;
        int tooMany = 0;
        for (int i = 0; i < instruments.Count; i++)
            for (int k = 0; k < instruments[i].Samples.Count; k++)
            {
                var inst = instruments[i];
                var s = inst.Samples[k];
                if (s.Data.Length <= 2) continue;
                if (song.Instruments.Count >= MaxInstruments) { tooMany++; continue; }
                string label = inst.Samples.Count(x => x.Data.Length > 2) > 1
                    ? $"{inst.Name} {(s.Name.Length > 0 ? s.Name : (k + 1).ToString())}".Trim()
                    : inst.Name.Length > 0 ? inst.Name : s.Name;
                if (label.Length == 0) label = $"Instrument {i + 1}";
                string file = $"{i + 1:00}-{k + 1:00} {ModImport.Safe(label)}.wav";
                int rate = (int)Math.Round(C4Rate * Math.Pow(2, s.Finetune / (128.0 * 12)));
                SampleBank.WriteWav(Path.Combine(sampleFolder, file), new SampleData(s.Data, rate));
                string inSong = sampleFolderInSong.TrimEnd('/') + "/" + file;
                SampleBank.Forget(inSong);
                var t = new TrackerInstrument
                {
                    Name = label, Wave = "Sample", Sample = inSong,
                    SampleNote = Math.Clamp(60 - s.RelativeNote, 0, 127),
                    LoopStart = s.LoopEnd > 0 ? s.LoopStart : 0, LoopEnd = s.LoopEnd,
                    DefaultVolume = s.Volume, Volume = 1,
                    Pan = Math.Round((s.Panning - 128) / 128.0 * 100)
                };
                Envelope(inst, t, tickMs);
                song.Instruments.Add(t);
                s.Tracker = song.Instruments.Count;
            }
        if (tooMany > 0) notes.Add($"{tooMany} samples were left out (the tracker has at most {MaxInstruments} instruments).");

        // The channels that play something; at most 8 are kept
        var used = Enumerable.Range(0, channels)
            .Where(c => patterns.Any(p => Enumerable.Range(0, p.GetLength(0)).Any(r => p[r, c].Note != 0 || p[r, c].Effect != 0 || p[r, c].Param != 0 || p[r, c].Volume != 0)))
            .ToList();
        if (used.Count == 0) used.Add(0);
        if (used.Count > TrackerSong.MaxChannels)
        {
            notes.Add($"The module plays {used.Count} channels; the first {TrackerSong.MaxChannels} were kept.");
            used = used.Take(TrackerSong.MaxChannels).ToList();
        }
        song.Channels = Math.Max(TrackerSong.DefaultChannels, used.Count);
        double level = Math.Round(0.26 * 4 / Math.Max(4, used.Count), 3);
        foreach (var t in song.Instruments) t.Volume = Math.Round(t.Volume * level, 3);

        // The patterns, in the order they are played, so that effect memory carries from one to the next
        var skipped = new SortedSet<string>();
        int volumeLost = 0;
        var shared = new Shared { Speed = speed };
        var state = used.Select(_ => new ChannelState { Song = shared }).ToArray();
        var copies = new Dictionary<(int, int), int>();
        var converted = new TrackerPattern?[Math.Max(patternCount, 1)];
        foreach (int p in order.Concat(Enumerable.Range(0, patternCount)).Where(p => p < patternCount))
        {
            if (converted[p] != null) continue;
            var cells = patterns[p];
            var pattern = new TrackerPattern(cells.GetLength(0));
            for (int r = 0; r < cells.GetLength(0); r++)
                for (int c = 0; c < used.Count; c++)
                    pattern[r, c] = Convert(cells[r, used[c]], state[c], instruments, linear, skipped, ref volumeLost, song, copies);
            converted[p] = pattern;
        }
        song.Patterns.Clear();
        for (int p = 0; p < converted.Length; p++) song.Patterns.Add(converted[p] ?? new TrackerPattern(64));
        song.Order.Clear();
        song.Order.AddRange(order.Select(p => p < song.Patterns.Count ? p : song.Patterns.Count - 1));

        if (skipped.Count > 0) notes.Add("Effects the tracker does not have were left out: " + string.Join(", ", skipped) + ".");
        if (volumeLost > 0) notes.Add($"{volumeLost} volume column entries were left out, where the effect column was taken.");
        return new ModImport.Result(song, song.Instruments.Count, notes);
    }

    // The volume envelope as an ADSR: up to its highest point, down to the sustain point, and after the
    // note is let go, the rest of the envelope or the fadeout, whichever ends first
    private static void Envelope(Instrument inst, TrackerInstrument t, double tickMs)
    {
        // Without an envelope a note sounds until it is let go, and then stops at once
        double fade = inst.Fadeout > 0 ? 32768.0 / inst.Fadeout * tickMs : double.PositiveInfinity;
        if (!inst.EnvelopeOn)
        {
            (t.Attack, t.Decay, t.Sustain, t.Release) = (0, 0, 100, 5);
            return;
        }
        var e = inst.Envelope;
        int hold = inst.Sustain >= 0 ? inst.Sustain : inst.LoopStart >= 0 ? inst.LoopStart : e.Length - 1;
        int peak = 0;
        for (int k = 1; k <= hold; k++) if (e[k].y > e[peak].y) peak = k;
        double top = Math.Max(1, e[peak].y);
        t.Volume = e[peak].y / 64.0;                       // multiplied by the song's level afterwards
        t.Attack = Math.Round(e[peak].x * tickMs);
        t.Decay = Math.Round(Math.Max(0, e[hold].x - e[peak].x) * tickMs);
        t.Sustain = Math.Round(e[hold].y / top * 100);
        double rest = e[^1].y < 4 ? (e[^1].x - e[hold].x) * tickMs : double.PositiveInfinity;
        double release = Math.Min(rest, fade);
        t.Release = Math.Round(double.IsInfinity(release) ? 5000 : Math.Clamp(release, 5, 10000));
        if (inst.Sustain < 0 && inst.LoopStart < 0 && e[^1].y < 4) t.Sustain = 0;   // dies away by itself
    }

    private sealed class Shared
    {
        public int Speed;                      // ticks per row, as the song has got to
    }

    private sealed class ChannelState
    {
        public required Shared Song;
        public double Period = AmigaPeriod(60);   // in Amiga mode: where slides have taken the pitch
        public Instrument? Instrument;
        public int Emitted;                    // the tracker instrument the channel has
        public int LastNote = 60;              // XM note numbers, 1 = C-0
        public readonly Dictionary<string, int> Memory = new();
    }

    private static TrackerCell Convert(Cell x, ChannelState ch, List<Instrument> instruments, bool linear,
        ISet<string> skipped, ref int volumeLost, TrackerSong song, Dictionary<(int, int), int> copies)
    {
        bool glide = x.Effect is 3 or 5 || (x.Volume >> 4) == 0xF;
        int note = TrackerCell.NoNote, inst = 0;
        if (x.Instrument > 0)
            ch.Instrument = x.Instrument <= instruments.Count ? instruments[x.Instrument - 1] : null;
        if (x.Note == 97) note = TrackerCell.NoteOff;
        else if (x.Note is >= 1 and <= 96)
        {
            note = Math.Clamp(x.Note - 1 + 12, 0, 119);
            if (!glide) ch.LastNote = x.Note;
            if (!glide) ch.Period = AmigaPeriod(note);
        }

        // The sample a note plays depends on its key; a change of sample is a change of instrument here
        if ((x.Note is >= 1 and <= 96 && !glide) || x.Instrument > 0)
        {
            int key = (x.Note is >= 1 and <= 96 ? x.Note : ch.LastNote) - 1;
            int want = 0;
            if (ch.Instrument != null && ch.Instrument.Samples.Count > 0)
            {
                int k = ch.Instrument.KeyMap[Math.Clamp(key, 0, 95)];
                want = k < ch.Instrument.Samples.Count ? ch.Instrument.Samples[k].Tracker : 0;
            }
            if (glide) want = ch.Emitted;
            if (x.Instrument > 0 || want != ch.Emitted) inst = want;
            if (want == 0 && note >= 0) note = TrackerCell.NoteOff;   // a sample with no sound
            if (inst > 0) ch.Emitted = inst;
            else if (x.Instrument > 0) ch.Emitted = 0;
        }

        var (eff, prm) = Effect(x.Effect, x.Param, ch, linear, skipped);
        if (x.Effect == 0xF && x.Param is > 0 and < 0x20) ch.Song.Speed = x.Param;
        if (x.Effect == 0x14 && x.Param == 0 && note == TrackerCell.NoNote) { note = TrackerCell.NoteOff; (eff, prm) = ('\0', 0); }

        // The volume column, where the effect column is free
        if (x.Volume >= 0x10)
        {
            var (veff, vprm) = VolumeColumn(x.Volume, ch, linear);
            if (veff != '\0')
            {
                if (eff == '\0') (eff, prm) = (veff, vprm);
                else if (veff == 'A' && x.Param == 0 && eff is '3' or '4') eff = eff == '3' ? '5' : '6';
                else if (veff == 'C' && note >= 0 && (inst > 0 ? inst : ch.Emitted) is var of && of > 0
                         && WithVolume(song, copies, of, vprm) is var copy && copy > 0)
                    inst = copy;
                else volumeLost++;
            }
            else skipped.Add("volume column " + ((x.Volume >> 4) == 0xD ? "pan slide left" : "pan slide right"));
        }
        return new TrackerCell(note, inst, eff, prm);
    }

    // A copy of an instrument that starts at another volume (0 if there is no room for more)
    private static int WithVolume(TrackerSong song, Dictionary<(int, int), int> copies, int of, int volume)
    {
        var original = song.Instruments[of - 1];
        if (original.DefaultVolume == volume) return of;
        if (copies.TryGetValue((of, volume), out int n)) return n;
        if (song.Instruments.Count >= MaxInstruments) return 0;
        var copy = InstrumentLibrary.Copy(original);
        copy.DefaultVolume = volume;
        copy.Name = $"{original.Name} vol {volume:X2}";
        song.Instruments.Add(copy);
        return copies[(of, volume)] = song.Instruments.Count;
    }

    private static (char, int) VolumeColumn(int v, ChannelState ch, bool linear)
    {
        int x = v >> 4, y = v & 15;
        switch (x)
        {
            case >= 1 and <= 4: return ('C', v - 0x10);
            case 5: return v == 0x50 ? ('C', 0x40) : ('\0', 0);
            case 6: return ('A', y);
            case 7: return ('A', y << 4);
            case 8: return ('E', 0xB0 | y);
            case 9: return ('E', 0xA0 | y);
            case 0xA: return ('4', (y << 4) | Remember(ch, "4y", 0));
            case 0xB: return ('4', (Remember(ch, "4x", 0) << 4) | y);
            case 0xC: return ('8', y * 17);
            case 0xF: return ('3', y == 0 ? 0 : Math.Min(255, Slide(y * 16, linear, ch.Period)));
        }
        return ('\0', 0);
    }

    private static int Remember(ChannelState ch, string key, int value)
    {
        if (value != 0) ch.Memory[key] = value;
        return ch.Memory.TryGetValue(key, out int v) ? v : 0;
    }

    // FT2's Amiga periods (four times a MOD file's): C-4 is 1712
    private static double AmigaPeriod(int note) => 4 * 428 * Math.Pow(2, (60 - note) / 12.0);

    // A slide of xx period units per tick, in the tracker's sixteenths of a semitone. In linear
    // mode these are the same; in Amiga mode they depend on the pitch, as in a MOD file.
    private static int Slide(int units, bool linear, double period)
    {
        if (units == 0) return 0;
        if (linear) return Math.Clamp(units, 1, 255);
        double sixteenths = 16 * 12 * Math.Log2(period / Math.Max(1, period - 4 * units));
        return Math.Clamp((int)Math.Round(sixteenths), 1, 255);
    }

    // In Amiga mode a slide up, followed through its ticks, so that the next one starts where it got to
    private static int SlideUp(ChannelState ch, int units, bool linear, int ticks)
    {
        int six = Slide(units, linear, ch.Period);
        ch.Period = Math.Clamp(ch.Period - 4.0 * units * ticks, 28, 32000);
        return six;
    }

    private static int SlideDown(ChannelState ch, int units, bool linear, int ticks)
    {
        // Down: the period grows, so the semitones are counted from the lower end
        int six = linear || units == 0 ? Slide(units, linear, ch.Period) : Slide(units, linear, ch.Period + 4.0 * units);
        ch.Period = Math.Clamp(ch.Period + 4.0 * units * ticks, 28, 32000);
        return six;
    }

    private static (char, int) Effect(int effect, int param, ChannelState ch, bool linear, ISet<string> skipped)
    {
        int x = param >> 4, y = param & 15;
        int ticks = Math.Max(0, ch.Song.Speed - 1);
        switch (effect)
        {
            case 0x0: return param == 0 ? ('\0', 0) : ('0', param);
            case 0x1: return ('1', SlideUp(ch, Remember(ch, "1", param), linear, ticks));
            case 0x2: return ('2', SlideDown(ch, Remember(ch, "2", param), linear, ticks));
            case 0x3: return ('3', Math.Min(255, Slide(param, linear, ch.Period)));
            case 0x4: return ('4', (Remember(ch, "4x", x) << 4) | Remember(ch, "4y", y));
            case 0x5: return ('5', Remember(ch, "A", param));
            case 0x6: return ('6', Remember(ch, "A", param));
            case 0x7: return ('7', param);
            case 0x8: return ('8', param);
            case 0x9: return ('9', param);
            case 0xA: return ('A', Remember(ch, "A", param));
            case 0xB: return ('B', param);
            case 0xC: return ('C', Math.Min(param, 64));
            case 0xD: return ('D', param);
            case 0xF: return param == 0 ? ('\0', 0) : ('F', param);
            case 0xE:
                switch (x)
                {
                    case 0x1: return ('E', 0x10 | Math.Min(15, SlideUp(ch, Remember(ch, "E1", y), linear, 1)));
                    case 0x2: return ('E', 0x20 | Math.Min(15, SlideDown(ch, Remember(ch, "E2", y), linear, 1)));
                    case 0xA: return ('E', 0xA0 | Remember(ch, "EA", y));
                    case 0xB: return ('E', 0xB0 | Remember(ch, "EB", y));
                    case 0x6: case 0x9: case 0xC: case 0xD: case 0xE: return ('E', param);
                    default:
                        skipped.Add($"E{x:X}x");
                        return ('\0', 0);
                }
            case 0x14:   // Kxx: let go of the note on tick xx
                return param == 0 ? ('\0', 0) : ('E', 0xC0 | Math.Min(param, 15));
            case 0x1B:   // Rxy: retrigger (the volume change is left out)
                int r = Remember(ch, "R", y);
                return r == 0 ? ('\0', 0) : ('E', 0x90 | r);
            case 0x21:   // X1x / X2x: extra fine slides, a quarter of E1x / E2x
                if (x is 1 or 2)
                {
                    int units = Remember(ch, "X" + x, y);
                    int six = linear ? (units + 2) / 4 : Slide(units, linear, ch.Period) / 4;
                    return six == 0 ? ('\0', 0) : ('E', (x << 4) | Math.Min(15, Math.Max(1, six)));
                }
                return ('\0', 0);
        }
        skipped.Add(effect < 36 ? ((char)(effect < 10 ? '0' + effect : 'A' + effect - 10)).ToString() + "xx" : $"#{effect}");
        return ('\0', 0);
    }

    private static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    private static int I32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

    private static void Need(byte[] b, int end)
    {
        if (end > b.Length) throw new InvalidDataException("the XM file ends too early");
    }
}
