using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AmosLikeBasic;

// A song made in maxYMiser (gwEm's Atari ST tracker), as its SNDH files carry it after the replay
// routine: the instruments with their sequences, the song's order (a pattern for each track at each
// place), the patterns and the digidrums. Read so that the tracker gets the song as it was written,
// not as its sound chip was heard. The layout is the one the replay routine reads
// (github.com/gwEm303/maxYMiser_replay, replay.s/trakdata.s):
//   "MYM?INST", 32 instruments of 64 bytes, the 8 digidrums' lengths, then the sequences: words, then
//   their length and repeat in the last two bytes; 128 bytes each in version 1, 64 in version 0
//   "MYM?TRAK", 64 bytes of settings (song speed at +13, the replay rate at +14), the order (256 places
//   × 4 tracks), its length and repeat, then the patterns: 64 lines each, 8 bytes a line with a count
//   of the empty lines after it
//   "MYM?DIGI", the digidrums (8-bit signed)
public sealed class MaxYMiserSong
{
    public const int Tracks = 4;            // A, B, C on the YM; the fourth the STe's DMA sound
    public const int Lines = 64;

    public sealed class Instrument
    {
        public string Name = "";
        // What portamento, arpeggio, vibrato, transpose, fixed frequency and fixed detune act on: bit 2 the
        // tone, bit 1 the buzzer, bit 0 the timer
        public int Portamento, Arpeggio, Vibrato, Transpose, FixedFrequency, FixedDetune;
        public int SequenceSpeed, PulseWidth, BuzzerWave, BuzzerStartSync, DigiSample, DigiRate, Volume;
        public int DetuneCoarse, DetuneFine, FrequencyResolution;
        // The sequences it uses (0: none): volume, arpeggio, vibrato, mixer, noise, fixed frequency, timer, PWM
        public int VolumeSeq, ArpeggioSeq, VibratoSeq, MixerSeq, NoiseSeq, FixedSeq, TimerSeq, PwmSeq;
    }

    public sealed class Sequence
    {
        public int[] Steps = Array.Empty<int>();   // words; volumes, semitones, mixer words, ...
        public int Repeat;                         // the step it goes round from
    }

    public readonly struct Line
    {
        public readonly int Note, Instrument, Volume;
        public readonly char Effect1, Effect2;     // '\0', or a letter or digit
        public readonly int Param1, Param2;
        public Line(int note, int instrument, int volume, char e1, int p1, char e2, int p2)
        { Note = note; Instrument = instrument; Volume = volume; Effect1 = e1; Param1 = p1; Effect2 = e2; Param2 = p2; }
        public bool Empty => Note == 0 && Instrument == 0 && Volume == 0 && Effect1 == '\0' && Effect2 == '\0';
    }

    public int Version;                            // the digit in "MYM?": 0 or 1
    public Instrument[] Instruments = new Instrument[32];
    public List<Sequence> Sequences = new();
    public int Speed = 6, Rate = 50;               // ticks a line, and calls a second
    public int TimerEnables;                       // the timer effects each channel may use: bit 2 A, bit 1 B, bit 0 C
    public int Muted;                              // channels muted when it was saved: bit 0 A, 1 B, 2 C
    public bool DmaUsed;                           // the fourth track (the STe's DMA sound) is played and has notes
    public int[,] Order = new int[0, Tracks];      // [place, track]: a pattern, or $FD (loop mark), $FE (note off), $FF (empty)
    public int Length, Repeat;
    public int StartPlace;                         // where the replay starts (where the editor was left)
    public List<Line[]> Patterns = new();
    public List<sbyte[]> Digidrums = new();

    // Where it was read from: the whole file, and where its parts are, so that it can be written back
    // as it was with only what has been changed (the replay and all it does not know about kept)
    private byte[] _source = Array.Empty<byte>();
    private int _instTag, _trakTag, _digiTag = -1, _sequenceSize, _patternsEnd;
    private readonly List<(int start, int length, Line[] lines)> _readPatterns = new();
    private readonly List<int[]> _readSteps = new();
    private int _readSpeed, _readRate;

    // The song's data in an SNDH file's code, or null when it is not a maxYMiser song. A file with
    // several tunes has a song for each, one after another (tune counts from 0).
    public static MaxYMiserSong? Find(byte[] data, int tune = 0)
    {
        int inst = -1;
        for (int i = 0; i <= tune; i++)
        {
            inst = Tag(data, "INST", inst + 1);
            if (inst < 0) return null;
        }
        int trak = Tag(data, "TRAK", inst);
        if (trak < 0) return null;
        try { return Read(data, inst, trak); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException) { return null; }
    }

    // How many songs the code holds
    public static int Count(byte[] data)
    {
        int n = 0;
        for (int at = Tag(data, "INST"); at >= 0; at = Tag(data, "INST", at + 1)) n++;
        return n;
    }

    // "MYM" + a digit + the name, where it starts
    private static int Tag(byte[] d, string name, int from = 0)
    {
        var want = Encoding.ASCII.GetBytes(name);
        for (int i = Math.Max(0, from); i + 8 <= d.Length; i++)
            if (d[i] == 'M' && d[i + 1] == 'Y' && d[i + 2] == 'M' && d[i + 3] is >= (byte)'0' and <= (byte)'9'
                && d[i + 4] == want[0] && d[i + 5] == want[1] && d[i + 6] == want[2] && d[i + 7] == want[3])
                return i;
        return -1;
    }

    // The file again, with the song as it is now: the replay and everything before the song's data as
    // they were, the instruments, sequences, settings, order and patterns written from the song (what
    // it does not know about kept from the file), the digidrums as they were. The replay finds its
    // data with offsets from where they are written (the two before the data and the digidrums' eight,
    // at its start); those that point past the instruments are moved with what they point to.
    public byte[] Write()
    {
        var d = _source;
        if (d.Length == 0) throw new InvalidOperationException("The song was not read from a file");
        var o = new List<byte>(d.Length + 4096);
        int voice = _instTag - 32;
        o.AddRange(d.AsSpan(0, _instTag + 8).ToArray());

        // The instruments: their 64 bytes each, with what the song knows written in
        var bank = d.AsSpan(_instTag + 8, 2048).ToArray();
        for (int i = 0; i < 32; i++)
        {
            var x = Instruments[i];
            int at = i * 64;
            var name = Encoding.Latin1.GetBytes(x.Name);
            int old = Array.IndexOf(bank, (byte)0, at, 16) is var z && z >= 0 ? z - at : 16;
            if (Encoding.Latin1.GetString(bank, at, old).Trim() != x.Name)
            {
                Array.Clear(bank, at, 16);
                Array.Copy(name, 0, bank, at, Math.Min(name.Length, 15));
            }
            byte[] fields =
            {
                (byte)x.Portamento, (byte)x.Arpeggio, (byte)x.Vibrato, (byte)x.Transpose, (byte)x.FixedFrequency, (byte)x.FixedDetune
            };
            fields.CopyTo(bank, at + 16);
            bank[at + 32] = (byte)x.SequenceSpeed; bank[at + 33] = (byte)x.PulseWidth; bank[at + 34] = (byte)x.BuzzerWave;
            bank[at + 35] = (byte)x.BuzzerStartSync; bank[at + 36] = (byte)x.DigiSample; bank[at + 37] = (byte)x.DigiRate;
            bank[at + 38] = (byte)x.Volume; bank[at + 39] = (byte)x.DetuneCoarse; bank[at + 40] = (byte)x.DetuneFine;
            bank[at + 41] = (byte)x.FrequencyResolution;
            byte[] seqs = { (byte)x.VolumeSeq, (byte)x.ArpeggioSeq, (byte)x.VibratoSeq, (byte)x.MixerSeq, (byte)x.NoiseSeq, (byte)x.FixedSeq, (byte)x.TimerSeq, (byte)x.PwmSeq };
            seqs.CopyTo(bank, at + 48);
        }
        o.AddRange(bank);
        o.AddRange(d.AsSpan(_instTag + 8 + 2048, 32).ToArray());   // the digidrums' lengths, as they are

        // The sequences: each its words, its length and repeat (the words past its length kept)
        int size = _sequenceSize, seqStart = _instTag + 8 + 2048 + 32;
        int readSequences = (_trakTag - seqStart) / size;
        for (int k = 0; k < Sequences.Count; k++)
        {
            var block = k < readSequences ? d.AsSpan(seqStart + k * size, size).ToArray() : new byte[size];
            var q = Sequences[k];
            // As it was, when it is (its length byte may say more than it can hold)
            if (k < _readSteps.Count && _readSteps[k].AsSpan().SequenceEqual(q.Steps) && block[size - 1] == q.Repeat)
            {
                o.AddRange(block);
                continue;
            }
            int n = Math.Min(q.Steps.Length, size / 2 - 1);
            for (int j = 0; j < n; j++) { block[j * 2] = (byte)(q.Steps[j] >> 8); block[j * 2 + 1] = (byte)q.Steps[j]; }
            int oldLength = k < readSequences ? Math.Min((int)block[size - 2], size / 2 - 1) : 0;
            for (int j = n; j < oldLength; j++) { block[j * 2] = 0; block[j * 2 + 1] = 0; }
            block[size - 2] = (byte)n;
            block[size - 1] = (byte)q.Repeat;
            o.AddRange(block);
        }
        // (what was between the sequences and the tracker data)
        int gap = seqStart + readSequences * size;
        if (Sequences.Count <= readSequences) o.AddRange(d.AsSpan(gap, _trakTag - gap).ToArray());

        // The tracker data: its tag and settings, the order, the patterns
        int trak = o.Count;
        var state = d.AsSpan(_trakTag, 8 + 64).ToArray();
        if (Speed != _readSpeed) state[8 + 13] = (byte)Speed;
        if (Rate != _readRate) state[8 + 14] = (byte)Rate;
        state[8 + 36] = (byte)TimerEnables;
        state[8 + 37] = (byte)Muted;
        o.AddRange(state);
        var order = d.AsSpan(_trakTag + 8 + 64, 1026).ToArray();
        for (int p = 0; p < Math.Min(Length, 256); p++)
            for (int t = 0; t < Tracks; t++) order[p * 4 + t] = (byte)Order[p, t];
        order[1024] = (byte)Length;
        order[1025] = (byte)Repeat;
        o.AddRange(order);
        for (int p = 0; p < Patterns.Count; p++)
        {
            if (p < _readPatterns.Count && _readPatterns[p].lines.AsSpan().SequenceEqual(Patterns[p]))
                o.AddRange(d.AsSpan(_readPatterns[p].start, _readPatterns[p].length).ToArray());
            else
                o.AddRange(EncodePattern(Patterns[p]));
        }
        // (what was after the patterns, up to the digidrums)
        int digiOld = _digiTag >= 0 ? _digiTag : d.Length;
        o.AddRange(d.AsSpan(_patternsEnd, Math.Max(0, digiOld - _patternsEnd)).ToArray());
        int digi = o.Count;
        if (_digiTag >= 0) o.AddRange(d.AsSpan(_digiTag).ToArray());

        // The offsets that find the data, moved with it
        int trakMove = trak - _trakTag, digiMove = digi - digiOld;
        var bytes = o.ToArray();
        void Move(int at)
        {
            int v = bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];
            int target = at + v;
            int shift = target >= digiOld ? digiMove : target >= _trakTag ? trakMove : 0;
            if (shift == 0 || target < voice || target > d.Length) return;
            v += shift;
            bytes[at] = (byte)(v >> 24); bytes[at + 1] = (byte)(v >> 16); bytes[at + 2] = (byte)(v >> 8); bytes[at + 3] = (byte)v;
        }
        for (int at = voice - 8; at < voice + 32; at += 4)
            if (at >= 0) Move(at);
        return bytes;
    }

    // A pattern as maxYMiser keeps it: the first line, and each line after it that has something,
    // with the count of the empty lines that follow it
    private static byte[] EncodePattern(Line[] lines)
    {
        var o = new List<byte>();
        int line = 0;
        while (line < Lines)
        {
            int next = line + 1;
            while (next < Lines && lines[next].Empty) next++;
            var l = lines[line];
            o.AddRange(new[]
            {
                (byte)l.Note, (byte)l.Instrument, (byte)l.Volume, (byte)l.Effect1, (byte)l.Param1, (byte)l.Effect2, (byte)l.Param2,
                (byte)(next - line - 1)
            });
            line = next;
        }
        return o.ToArray();
    }

    private static MaxYMiserSong Read(byte[] d, int instTag, int trakTag)
    {
        var song = new MaxYMiserSong { Version = d[instTag + 3] - '0', _source = d, _instTag = instTag, _trakTag = trakTag };
        int U16(int at) => d[at] << 8 | d[at + 1];

        int bank = instTag + 8;
        for (int i = 0; i < 32; i++)
        {
            int at = bank + i * 64;
            int nameLength = Array.IndexOf(d, (byte)0, at, 16) is var z && z >= 0 ? z - at : 16;
            song.Instruments[i] = new Instrument
            {
                Name = Encoding.Latin1.GetString(d, at, nameLength).Trim(),
                Portamento = d[at + 16], Arpeggio = d[at + 17], Vibrato = d[at + 18],
                Transpose = d[at + 19], FixedFrequency = d[at + 20], FixedDetune = d[at + 21],
                SequenceSpeed = d[at + 32], PulseWidth = d[at + 33], BuzzerWave = d[at + 34], BuzzerStartSync = d[at + 35],
                DigiSample = d[at + 36], DigiRate = d[at + 37], Volume = d[at + 38],
                DetuneCoarse = (sbyte)d[at + 39], DetuneFine = (sbyte)d[at + 40], FrequencyResolution = d[at + 41],
                VolumeSeq = d[at + 48], ArpeggioSeq = d[at + 49], VibratoSeq = d[at + 50], MixerSeq = d[at + 51],
                NoiseSeq = d[at + 52], FixedSeq = d[at + 53], TimerSeq = d[at + 54], PwmSeq = d[at + 55]
            };
        }

        var digiLengths = new int[8];
        for (int i = 0; i < 8; i++) digiLengths[i] = U16(bank + 2048 + i * 4);

        // The sequences, as many as fit before the tracker data
        int size = song.Version == 0 ? 64 : 128;
        song._sequenceSize = size;
        for (int at = bank + 2048 + 32; at + size <= trakTag; at += size)
        {
            int length = Math.Min((int)d[at + size - 2], size / 2 - 1);
            var steps = new int[length];
            for (int k = 0; k < length; k++) steps[k] = (short)U16(at + k * 2);
            song.Sequences.Add(new Sequence { Steps = steps, Repeat = d[at + size - 1] });
            song._readSteps.Add((int[])steps.Clone());
        }

        int state = trakTag + 8;
        song.Speed = Math.Max(1, (int)d[state + 13]);
        song.Rate = d[state + 14] is >= 25 and <= 250 ? d[state + 14] : 50;
        song.TimerEnables = d[state + 36];
        song.StartPlace = d[state];
        song._readSpeed = song.Speed;
        song._readRate = song.Rate;
        song.Muted = d[state + 37];
        int order = state + 64;
        song.Length = Math.Max(1, (int)d[order + 1024]);
        song.Repeat = d[order + 1025];
        song.Order = new int[song.Length, Tracks];
        int patterns = 0;
        for (int p = 0; p < song.Length; p++)
            for (int t = 0; t < Tracks; t++)
            {
                int n = d[order + p * 4 + t];
                song.Order[p, t] = n;
                if (n < 0xF0) patterns = Math.Max(patterns, n + 1);
            }

        // The patterns: lines until 64 are counted (a line and the empty ones after it), as many as
        // there are before the digidrums (an order may name more than the file has)
        int digiAt = Tag(d, "DIGI", trakTag);
        int nextInst = Tag(d, "INST", trakTag);
        if (nextInst >= 0 && digiAt > nextInst) digiAt = -1;
        int patternsEnd = digiAt >= 0 ? digiAt : d.Length;
        int pos = state + 1090;
        for (int p = 0; p < patterns; p++)
        {
            if (pos + 8 > patternsEnd) break;
            var lines = new Line[Lines];
            int line = 0, start = pos;
            while (line < Lines)
            {
                if (pos + 8 > patternsEnd) throw new ArgumentException("The patterns run past the end");
                char Fx(byte b) => b == 0 ? '\0' : (char)b;
                lines[line] = new Line(d[pos], d[pos + 1], d[pos + 2], Fx(d[pos + 3]), d[pos + 4], Fx(d[pos + 5]), d[pos + 6]);
                line += 1 + d[pos + 7];
                pos += 8;
            }
            song.Patterns.Add(lines);
            song._readPatterns.Add((start, pos - start, (Line[])lines.Clone()));
        }
        song._patternsEnd = pos;

        song.DmaUsed = d[state + 49] != 0 && Enumerable.Range(0, song.Length)
            .Any(p => song.Order[p, 3] < song.Patterns.Count && song.Patterns[song.Order[p, 3]].Any(l => l.Note > 0));

        // The digidrums, one after another after their tag
        int digi = Tag(d, "DIGI", trakTag);
        int next = Tag(d, "INST", trakTag);
        if (next >= 0 && digi > next) digi = -1;
        song._digiTag = digi;
        if (digi >= 0)
        {
            int at = digi + 8;
            for (int i = 0; i < 8; i++)
            {
                int n = Math.Min(digiLengths[i], Math.Max(0, d.Length - at));
                var sample = new sbyte[n];
                for (int k = 0; k < n; k++) sample[k] = (sbyte)d[at + k];
                song.Digidrums.Add(sample);
                at += n;
            }
        }
        return song;
    }
}
