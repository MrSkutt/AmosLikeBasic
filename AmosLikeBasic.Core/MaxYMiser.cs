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
    public List<Line[]> Patterns = new();
    public List<sbyte[]> Digidrums = new();

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

    private static MaxYMiserSong Read(byte[] d, int instTag, int trakTag)
    {
        var song = new MaxYMiserSong { Version = d[instTag + 3] - '0' };
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
        for (int at = bank + 2048 + 32; at + size <= trakTag; at += size)
        {
            int length = Math.Min((int)d[at + size - 2], size / 2 - 1);
            var steps = new int[length];
            for (int k = 0; k < length; k++) steps[k] = (short)U16(at + k * 2);
            song.Sequences.Add(new Sequence { Steps = steps, Repeat = d[at + size - 1] });
        }

        int state = trakTag + 8;
        song.Speed = Math.Max(1, (int)d[state + 13]);
        song.Rate = d[state + 14] is >= 25 and <= 250 ? d[state + 14] : 50;
        song.TimerEnables = d[state + 36];
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

        // The patterns: lines until 64 are counted (a line and the empty ones after it)
        int pos = state + 1090;
        for (int p = 0; p < patterns; p++)
        {
            var lines = new Line[Lines];
            int line = 0;
            while (line < Lines)
            {
                if (pos + 8 > d.Length) throw new ArgumentException("The patterns run past the end");
                char Fx(byte b) => b == 0 ? '\0' : (char)b;
                lines[line] = new Line(d[pos], d[pos + 1], d[pos + 2], Fx(d[pos + 3]), d[pos + 4], Fx(d[pos + 5]), d[pos + 6]);
                line += 1 + d[pos + 7];
                pos += 8;
            }
            song.Patterns.Add(lines);
        }

        song.DmaUsed = d[state + 49] != 0 && Enumerable.Range(0, song.Length)
            .Any(p => song.Order[p, 3] < song.Patterns.Count && song.Patterns[song.Order[p, 3]].Any(l => l.Note > 0));

        // The digidrums, one after another after their tag
        int digi = Tag(d, "DIGI", trakTag);
        int next = Tag(d, "INST", trakTag);
        if (next >= 0 && digi > next) digi = -1;
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
