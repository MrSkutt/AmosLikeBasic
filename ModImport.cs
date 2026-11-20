using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AmosLikeBasic;

// Reads a ProTracker module (.mod with 4 – 32 channels) into a tracker song: the patterns, the order
// list and the effects, and every sample as a sampled instrument (written as a WAV file, since the
// song refers to its samples by file). The pitches are kept: the Amiga's period 428 (C-2 in
// ProTracker) is note 60 here, played at the sample's own rate of 8287 Hz.
public static class ModImport
{
    // Report: what was found, in a few lines for a window (null: nothing to show)
    public sealed record Result(TrackerSong Song, int Samples, List<string> Notes, string? Report = null);

    private const double C2Rate = 8287.137;    // PAL: 7 093 789.2 / (2 × 428)
    private const int C2Period = 428;
    private const int C2Note = 60;

    // sampleFolder is where the WAV files go; sampleFolderInSong how the song refers to it
    // (relative to the working folder, so the song works on another computer too)
    public static Result Import(byte[] mod, string name, string sampleFolder, string sampleFolderInSong)
    {
        if (mod.Length < 1084) throw new InvalidDataException("the file is too short to be a MOD file");
        string tag = Encoding.ASCII.GetString(mod, 1080, 4);
        int channels = ChannelsOf(tag);
        if (channels == 0) throw new InvalidDataException($"not a ProTracker MOD file (its tag is \"{Printable(tag)}\"); XM, S3M and IT files cannot be read");
        if (channels > TrackerSong.MaxChannels) throw new InvalidDataException($"the module has {channels} channels; the tracker plays at most {TrackerSong.MaxChannels}");

        var notes = new List<string>();
        string title = Text(mod, 0, 20);

        // 31 sample headers of 30 bytes
        var headers = new List<(string name, int length, int finetune, int volume, int loopStart, int loopLength)>();
        for (int s = 0; s < 31; s++)
        {
            int o = 20 + s * 30;
            int finetune = mod[o + 24] & 15;
            headers.Add((Text(mod, o, 22), Word(mod, o + 22) * 2, finetune > 7 ? finetune - 16 : finetune,
                         Math.Min((int)mod[o + 25], 64), Word(mod, o + 26) * 2, Word(mod, o + 28) * 2));
        }

        int songLength = Math.Clamp((int)mod[950], 1, 128);
        var order = Enumerable.Range(0, songLength).Select(i => (int)mod[952 + i]).ToList();
        int patternCount = Enumerable.Range(0, 128).Max(i => (int)mod[952 + i]) + 1;
        int patternBytes = 64 * channels * 4;
        int sampleStart = 1084 + patternCount * patternBytes;
        if (sampleStart > mod.Length) throw new InvalidDataException("the file ends inside its patterns");

        // The samples: 8-bit signed, one after the other. Only those with sound become instruments.
        var song = new TrackerSong { Title = title.Length > 0 ? title : name, Speed = 6, Tempo = 125, Channels = Math.Max(TrackerSong.DefaultChannels, channels) };
        song.Instruments.Clear();
        var instrumentOf = new int[32];
        int pos = sampleStart, written = 0;
        for (int s = 0; s < 31; s++)
        {
            var h = headers[s];
            int length = Math.Min(h.length, Math.Max(0, mod.Length - pos));
            if (length > 2)
            {
                var data = new float[length];
                for (int i = 0; i < length; i++) data[i] = (sbyte)mod[pos + i] / 128f;
                // Finetune: eighths of a semitone, as a slightly different rate
                int rate = (int)Math.Round(C2Rate * Math.Pow(2, h.finetune / 96.0));
                string file = $"{s + 1:00} {Safe(h.name)}.wav";
                SampleBank.WriteWav(Path.Combine(sampleFolder, file), new SampleData(data, rate));
                string inSong = sampleFolderInSong.TrimEnd('/') + "/" + file;
                SampleBank.Forget(inSong);
                bool loops = h.loopLength > 2 && h.loopStart < length;
                song.Instruments.Add(new TrackerInstrument
                {
                    Name = h.name.Length > 0 ? h.name : $"Sample {s + 1}",
                    Wave = "Sample", Sample = inSong, SampleNote = C2Note,
                    LoopStart = loops ? h.loopStart : 0,
                    LoopEnd = loops ? Math.Min(h.loopStart + h.loopLength, length) : 0,
                    DefaultVolume = h.volume,
                    // Four channels at full volume must not clip; a short release so cut notes do not click
                    Volume = Math.Round(0.26 * 4 / Math.Max(4, channels), 3),
                    Attack = 0, Decay = 0, Sustain = 100, Release = 15
                });
                instrumentOf[s + 1] = song.Instruments.Count;
                written++;
            }
            pos += h.length;
        }

        // The patterns; the period each channel plays is kept to turn slides into semitones
        song.Patterns.Clear();
        var skipped = new SortedSet<string>();
        var period = Enumerable.Repeat(C2Period, channels).ToArray();
        var hasSample = new bool[channels];   // the channel has a sample with sound (a note without one is silent)
        for (int p = 0; p < patternCount; p++)
        {
            var pattern = new TrackerPattern(64);
            for (int row = 0; row < 64; row++)
                for (int c = 0; c < channels; c++)
                {
                    int o = 1084 + p * patternBytes + (row * channels + c) * 4;
                    int sample = (mod[o] & 0xF0) | (mod[o + 2] >> 4);
                    int per = ((mod[o] & 0x0F) << 8) | mod[o + 1];
                    int effect = mod[o + 2] & 0x0F, param = mod[o + 3];

                    int note = TrackerCell.NoNote;
                    if (per > 0)
                    {
                        note = Math.Clamp(C2Note + (int)Math.Round(12 * Math.Log2((double)C2Period / per)), 12, 119);
                        if (effect != 3 && effect != 5) period[c] = per;   // a glide goes towards it instead
                    }
                    var (eff, prm) = Effect(effect, param, effect is 3 or 5 && per > 0 ? per : period[c], skipped);
                    int inst = sample is > 0 and <= 31 ? instrumentOf[sample] : 0;
                    // An empty sample, or a note on a channel that has had none, is silence on the Amiga
                    if (sample > 0) hasSample[c] = inst > 0;
                    if (note >= 0 && !hasSample[c]) note = TrackerCell.NoteOff;
                    pattern[row, c] = new TrackerCell(note, inst, eff, prm);
                }
            song.Patterns.Add(pattern);
        }
        song.Order.Clear();
        song.Order.AddRange(order.Select(i => Math.Min(i, patternCount - 1)));

        if (skipped.Count > 0) notes.Add("Effects the tracker does not have were left out: " + string.Join(", ", skipped));
        return new Result(song, written, notes);
    }

    // The MOD effect as the tracker's; slides in periods become sixteenths of a semitone at the pitch
    private static (char effect, int param) Effect(int effect, int param, int period, ISet<string> skipped)
    {
        int x = param >> 4, y = param & 15;
        double Sixteenths(int periodUnits)
        {
            int p = Math.Max(period, 2);
            return 16 * 12 * Math.Log2((double)p / Math.Max(1, p - periodUnits));
        }
        int Slide(int units) => units == 0 ? 0 : Math.Clamp((int)Math.Round(Sixteenths(units)), 1, 255);

        switch (effect)
        {
            case 0x0: return param == 0 ? ('\0', 0) : ('0', param);
            case 0x1: return ('1', Slide(param));
            case 0x2: return ('2', Slide(param));
            case 0x3: return ('3', Slide(param));
            case 0x4: return ('4', param);
            case 0x5: return ('5', param);
            case 0x6: return ('6', param);
            case 0x7: return ('7', param);
            case 0x8: return ('8', param);
            case 0x9: return ('9', param);
            case 0xA: return ('A', param);
            case 0xB: return ('B', param);
            case 0xC: return ('C', Math.Min(param, 64));
            case 0xD: return ('D', param);
            case 0xF: return ('F', param);
            case 0xE:
                switch (x)
                {
                    case 0x1: case 0x2: return ('E', (x << 4) | Math.Clamp((int)Math.Round(Sixteenths(y)), y == 0 ? 0 : 1, 15));
                    case 0x6: case 0x9: case 0xA: case 0xB: case 0xC: case 0xD: case 0xE: return ('E', param);
                    default:
                        skipped.Add($"E{x:X}x");
                        return ('\0', 0);
                }
        }
        return ('\0', 0);
    }

    private static int ChannelsOf(string tag)
    {
        switch (tag)
        {
            case "M.K.": case "M!K!": case "FLT4": case "4CHN": return 4;
            case "6CHN": return 6;
            case "8CHN": case "FLT8": case "OCTA": case "CD81": return 8;
        }
        // "10CH", "16CN" ...
        if (tag.Length == 4 && (tag.EndsWith("CH") || tag.EndsWith("CN")) && int.TryParse(tag[..2], out int n)) return n;
        if (tag.Length == 4 && tag.EndsWith("CHN") && char.IsDigit(tag[0])) return tag[0] - '0';
        return 0;
    }

    private static int Word(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    internal static string Text(byte[] b, int o, int length)
    {
        var chars = new StringBuilder();
        for (int i = 0; i < length && b[o + i] != 0; i++)
            chars.Append(b[o + i] is >= 32 and < 127 ? (char)b[o + i] : ' ');
        return chars.ToString().Trim();
    }

    private static string Printable(string s) => new(s.Select(c => c is >= ' ' and < (char)127 ? c : '?').ToArray());

    internal static string Safe(string name)
    {
        var s = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "sample" : s.Length > 40 ? s[..40] : s;
    }
}
