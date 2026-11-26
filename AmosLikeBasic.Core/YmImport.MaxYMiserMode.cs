using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

// A maxYMiser song in the tracker as maxYMiser has it (YM mode), and back again:
//  - Open: its 32 instruments as the tracker's instruments 1 – 32, its sequences by their numbers, each
//    of its patterns as the tracker's pattern of that number (the same lines in every column), and its
//    order as each channel's own order: at each place, the pattern each track plays. Its "nothing" ($FF)
//    and "note off" ($FE) in the order are two patterns more. The file itself is kept.
//  - ToMaxYMiser: the song read from that file again, with the tracker's song written over it: the
//    instruments, sequences, speed and order, and the patterns as the tracks now have them. A pattern
//    that is as it was keeps its number; one that a track has changed takes it with it, or a new number
//    when another track plays it as it was.
//  - The DMA track (the fourth) is the fourth channel when the song uses it; otherwise it is kept as it
//    was in the file.
public static partial class YmImport
{
    public static TrackerSong OpenMaxYMiser(byte[] sndhData, MaxYMiserSong m, string title)
    {
        var song = new TrackerSong
        {
            Title = title,
            Speed = Math.Clamp(m.Speed, 1, 31),
            Tempo = Math.Clamp((int)Math.Round(m.Rate * 2.5), 32, 255),
            Channels = m.DmaUsed ? 4 : 3,
            Ym = new TrackerYmSong
            {
                Version = m.Version, Rate = m.Rate, Speed = m.Speed, Timers = m.TimerEnables, Repeat = m.Repeat, StartPlace = m.StartPlace,
                Sequences = m.Sequences.Select(q => new TrackerYmSequence { Steps = q.Steps.ToList(), Repeat = q.Repeat }).ToList(),
                Original = Convert.ToBase64String(sndhData),
                OpenedTitle = title,
                OldReplay = IsOldReplay(sndhData)
            }
        };
        song.Instruments.Clear();
        for (int i = 0; i < 32; i++)
        {
            var x = m.Instruments[i];
            song.Instruments.Add(new TrackerInstrument
            {
                Name = x.Name, Wave = "Square", Volume = 0.21, Attack = 0, Decay = 0, Sustain = 100, Release = 2,
                Ym = new TrackerYmInstrument
                {
                    Speed = x.SequenceSpeed, Volume = x.Volume, Portamento = x.Portamento, Arpeggio = x.Arpeggio,
                    Vibrato = x.Vibrato, Transpose = x.Transpose, FixedFrequency = x.FixedFrequency, FixedDetune = x.FixedDetune,
                    DetuneCoarse = x.DetuneCoarse, DetuneFine = x.DetuneFine, PulseWidth = x.PulseWidth, BuzzerWave = x.BuzzerWave,
                    StartSync = x.BuzzerStartSync, DigiSample = x.DigiSample, DigiRate = x.DigiRate, FrequencyResolution = x.FrequencyResolution,
                    VolumeSeq = x.VolumeSeq, ArpeggioSeq = x.ArpeggioSeq, VibratoSeq = x.VibratoSeq, MixerSeq = x.MixerSeq,
                    NoiseSeq = x.NoiseSeq, FixedSeq = x.FixedSeq, TimerSeq = x.TimerSeq, PwmSeq = x.PwmSeq
                }
            });
        }

        song.Patterns.Clear();
        foreach (var lines in m.Patterns)
        {
            var p = new TrackerPattern(MaxYMiserSong.Lines);
            for (int r = 0; r < MaxYMiserSong.Lines; r++)
            {
                var cell = CellOf(lines[r]);
                for (int c = 0; c < MaxYMiserSong.Tracks; c++) p[r, c] = cell;
            }
            song.Patterns.Add(p);
        }
        song.Ym.EmptyPattern = song.Patterns.Count;
        song.Patterns.Add(new TrackerPattern(MaxYMiserSong.Lines));
        song.Ym.NoteOffPattern = song.Patterns.Count;
        var off = new TrackerPattern(MaxYMiserSong.Lines);
        for (int c = 0; c < MaxYMiserSong.Tracks; c++) off[0, c] = new TrackerCell(TrackerCell.NoteOff, 0, '\0', 0);
        song.Patterns.Add(off);
        song.Ym.LoopMarkPattern = song.Patterns.Count;
        song.Patterns.Add(new TrackerPattern(MaxYMiserSong.Lines));

        int PatternOf(int n) => n == 0xFE ? song.Ym.NoteOffPattern : n == 0xFD ? song.Ym.LoopMarkPattern : n < m.Patterns.Count ? n : song.Ym.EmptyPattern;
        song.Order.Clear();
        song.ChannelOrder.Clear();
        for (int p = 0; p < m.Length; p++)
        {
            var own = Enumerable.Range(0, MaxYMiserSong.Tracks).Select(t => PatternOf(m.Order[p, t])).ToArray();
            song.Order.Add(own[0]);
            song.ChannelOrder.Add(own);
        }
        return song;
    }

    // The older replay, known by its volume routine: the mixer read first (move.w $4E(a2),d0), and
    // the buzzer's bit tested before the volume (btst #4,d0)
    private static bool IsOldReplay(byte[] d)
    {
        for (int i = 0; i + 10 <= d.Length; i += 2)
            if (d[i] == 0x30 && d[i + 1] == 0x2A && d[i + 2] == 0x00 && d[i + 3] == 0x4E
                && d[i + 6] == 0x08 && d[i + 7] == 0x00 && d[i + 8] == 0x00 && d[i + 9] == 0x04)
                return true;
        return false;
    }

    // A line of maxYMiser as the tracker's cell: its note (1 a note off), instrument, volume (as maxYMiser
    // writes it, 1 – $10) and two effects (only letters and digits: the editor's leftovers do nothing)
    private static TrackerCell CellOf(MaxYMiserSong.Line l)
    {
        int note = l.Note == 0 ? TrackerCell.NoNote : l.Note == 1 ? TrackerCell.NoteOff : Math.Max(12, l.Note);
        static bool Valid(char e) => e is >= '0' and <= '9' or >= 'A' and <= 'Z';
        return new TrackerCell(note, l.Instrument, Valid(l.Effect1) ? l.Effect1 : '\0', l.Param1,
            l.Volume == 0 ? TrackerCell.NoVolume : Math.Min(l.Volume, 64), Valid(l.Effect2) ? l.Effect2 : '\0', l.Param2);
    }

    private static MaxYMiserSong.Line LineOf(TrackerCell c) => new(
        c.Note == TrackerCell.NoNote ? 0 : c.Note == TrackerCell.NoteOff ? 1 : Math.Clamp(c.Note, 2, 255),
        c.Instrument, c.Volume < 0 ? 0 : c.Volume,
        c.Effect, c.Effect == '\0' ? 0 : c.Param, c.Effect2, c.Effect2 == '\0' ? 0 : c.Param2);

    // The song as maxYMiser's again, over the file it came from (see above)
    public static MaxYMiserSong ToMaxYMiser(TrackerSong song)
    {
        var ym = song.Ym ?? throw new InvalidOperationException("The song is not a maxYMiser song");
        var m = MaxYMiserSong.Find(Convert.FromBase64String(ym.Original))
                ?? throw new InvalidOperationException("The song's maxYMiser file cannot be read");
        int readPatterns = m.Patterns.Count;
        var readOrder = m.Order;
        int readLength = m.Length;

        for (int i = 0; i < 32; i++)
        {
            var x = m.Instruments[i];
            var t = i < song.Instruments.Count ? song.Instruments[i] : null;
            var y = t?.Ym ?? new TrackerYmInstrument { BuzzerWave = 0, DigiRate = 0x20, Speed = 1 };
            x.Name = t?.Name ?? "";
            x.SequenceSpeed = y.Speed; x.Volume = y.Volume; x.Portamento = y.Portamento; x.Arpeggio = y.Arpeggio;
            x.Vibrato = y.Vibrato; x.Transpose = y.Transpose; x.FixedFrequency = y.FixedFrequency; x.FixedDetune = y.FixedDetune;
            x.DetuneCoarse = y.DetuneCoarse; x.DetuneFine = y.DetuneFine; x.PulseWidth = y.PulseWidth; x.BuzzerWave = y.BuzzerWave;
            x.BuzzerStartSync = y.StartSync; x.DigiSample = y.DigiSample; x.DigiRate = y.DigiRate; x.FrequencyResolution = y.FrequencyResolution;
            x.VolumeSeq = y.VolumeSeq; x.ArpeggioSeq = y.ArpeggioSeq; x.VibratoSeq = y.VibratoSeq; x.MixerSeq = y.MixerSeq;
            x.NoiseSeq = y.NoiseSeq; x.FixedSeq = y.FixedSeq; x.TimerSeq = y.TimerSeq; x.PwmSeq = y.PwmSeq;
        }
        m.Sequences = ym.Sequences.Select(q => new MaxYMiserSong.Sequence { Steps = q.Steps.ToArray(), Repeat = q.Repeat }).ToList();
        // (the song's speed in the tracker is at most 31; changed there, it is that)
        m.Speed = Math.Min(31, ym.Speed) == song.Speed ? ym.Speed : song.Speed;
        m.Rate = ym.Rate;
        m.TimerEnables = ym.Timers;

        // Each place's tracks: the lines each plays, and the pattern it played when read
        int places = Math.Min(song.Order.Count, 256);
        // (a GHOST song's channel 4 is the DMA's samples, which maxYMiser does not have: its DMA track stays as it was)
        int tracks = Math.Min(GhostMode.Is(song) ? 3 : song.Channels, MaxYMiserSong.Tracks);
        var lines = new MaxYMiserSong.Line[places, MaxYMiserSong.Tracks][];
        var was = new int[places, MaxYMiserSong.Tracks];
        for (int p = 0; p < places; p++)
            for (int c = 0; c < tracks; c++)
            {
                int index = song.PatternAt(p, c);
                var pattern = song.Patterns[index];
                var l = new MaxYMiserSong.Line[MaxYMiserSong.Lines];
                for (int r = 0; r < MaxYMiserSong.Lines; r++) l[r] = r < pattern.Rows ? LineOf(pattern[r, c]) : default;
                lines[p, c] = l;
                was[p, c] = index == ym.EmptyPattern ? 0xFF : index == ym.NoteOffPattern ? 0xFE : index == ym.LoopMarkPattern ? 0xFD
                    : index < readPatterns ? index : -1;
            }

        // The numbers: a pattern as it was keeps its own; changed, it takes it along if every track that
        // played it changed it alike, else the changed ones get numbers of their own (alike ones one)
        var order = new int[places, MaxYMiserSong.Tracks];
        var added = new List<MaxYMiserSong.Line[]>();
        int NewNumber(MaxYMiserSong.Line[] l)
        {
            for (int k = 0; k < added.Count; k++) if (added[k].AsSpan().SequenceEqual(l)) return readPatterns + k;
            added.Add(l);
            return readPatterns + added.Count - 1;
        }
        bool Empty(MaxYMiserSong.Line[] l) => l.All(x => x.Empty);
        bool NoteOff(MaxYMiserSong.Line[] l) => l[0].Note == 1 && l[0].Instrument == 0 && l[0].Volume == 0 && l[0].Effect1 == '\0'
                                                && l[0].Effect2 == '\0' && l.Skip(1).All(x => x.Empty);
        var groups = new Dictionary<int, List<(int p, int c)>>();
        for (int p = 0; p < places; p++)
            for (int c = 0; c < tracks; c++)
            {
                int n = was[p, c];
                if (n is 0xFF or 0xFD) order[p, c] = Empty(lines[p, c]) ? n : NewNumber(lines[p, c]);
                else if (n == 0xFE) order[p, c] = NoteOff(lines[p, c]) ? 0xFE : NewNumber(lines[p, c]);
                else if (n < 0) order[p, c] = NewNumber(lines[p, c]);
                else (groups.TryGetValue(n, out var g) ? g : groups[n] = new()).Add((p, c));
            }
        foreach (var (n, refs) in groups)
        {
            // As it was when opened (what the tracker shows of it: leftovers in its effect columns, which
            // do nothing, are not shown), it stays as it was, leftovers and all
            var original = m.Patterns[n];
            var shown = original.Select(l => LineOf(CellOf(l))).ToArray();
            bool Unchanged(MaxYMiserSong.Line[] l) => l.AsSpan().SequenceEqual(shown);
            var changed = refs.Where(r => !Unchanged(lines[r.p, r.c])).ToList();
            MaxYMiserSong.Line[] keep = original;
            if (changed.Count == refs.Count)
            {
                // Every track that played it changed it: the first change takes the number along
                keep = lines[changed[0].p, changed[0].c];
                m.Patterns[n] = keep;
            }
            foreach (var (p, c) in refs)
                order[p, c] = Unchanged(lines[p, c]) && keep == original || lines[p, c].AsSpan().SequenceEqual(keep) ? n : NewNumber(lines[p, c]);
        }
        if (readPatterns + added.Count > 0xF0)
            throw new InvalidOperationException($"maxYMiser has room for 240 patterns; the song would need {readPatterns + added.Count}");
        m.Patterns.AddRange(added);
        // The DMA track, when the song does not show it, as it was
        for (int p = 0; p < places; p++)
            for (int c = tracks; c < MaxYMiserSong.Tracks; c++)
                order[p, c] = p < readLength ? readOrder[p, c] : 0xFF;
        m.Order = order;
        m.Length = places;
        m.Repeat = Math.Clamp(ym.Repeat, 0, Math.Max(0, places - 1));
        return m;
    }

    // Chip music read into the tracker: a maxYMiser song (an SNDH file of one tune) as maxYMiser has it,
    // anything else as before (a maxYMiser file of several tunes made the tracker's own)
    public static ModImport.Result OpenChipMusic(byte[] bytes, string name, int tune = 0,
        string? sampleFolder = null, string? sampleFolderInSong = null, Action<double>? progress = null)
    {
        if (SndhFile.IsSndh(bytes))
        {
            var sndh = SndhFile.Read(bytes);
            if (sndh.Tunes == 1 && MaxYMiserSong.Count(sndh.Data) == 1 && MaxYMiserSong.Find(sndh.Data) is { } m)
            {
                var song = OpenMaxYMiser(sndh.Data, m, sndh.Title.Length > 0 ? sndh.Title : name);
                int used = song.Instruments.Count(i => i.Name.Length > 0 || i.Ym?.VolumeSeq > 0);
                string report = $"\"{song.Title}\"" + (sndh.Composer.Length > 0 ? " by " + sndh.Composer : "") + "\n" +
                    $"Made in maxYMiser (its format {m.Version}), and opened as maxYMiser has it: {used} of its 32 instruments in use, " +
                    $"{m.Sequences.Count - 1} sequences, {m.Patterns.Count} patterns in an order of {m.Length}, {m.Rate} Hz.\n\n" +
                    "Channels 1 – 3 are its tracks, played by the Atari ST's sound chip as maxYMiser plays them, with its two effects " +
                    "on each line." + (m.DmaUsed ? " Channel 4 is its DMA track, kept but not played." : "") + "\n\n" +
                    "File → Save as SNDH for maxYMiser saves it in its own file with the changes made, to open in maxYMiser again. " +
                    "Song → Convert to tracker format makes it the tracker's own (then it can no longer be saved for maxYMiser).";
                return new ModImport.Result(song, 0, new List<string>(), report);
            }
        }
        return ImportChipMusic(bytes, name, tune, sampleFolder, sampleFolderInSong, progress);
    }

    // The song as an SNDH file maxYMiser opens: its own file with the song as it is now, and its title
    // in the file's header when it has been changed
    public static byte[] SaveMaxYMiser(TrackerSong song)
    {
        var bytes = ToMaxYMiser(song).Write();
        if (song.Ym != null && song.Title != song.Ym.OpenedTitle) SetSndhTag(bytes, "TITL", song.Title);
        return bytes;
    }

    // A tag's text in an SNDH header, written over it and the zeros after it (up to the next tag, one
    // zero kept); a text longer than that room is cut
    internal static void SetSndhTag(byte[] d, string tag, string text)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(tag);
        int end = Math.Min(d.Length, 1024);
        for (int i = 16; i + 4 <= end; i++)
        {
            if (d[i] != t[0] || d[i + 1] != t[1] || d[i + 2] != t[2] || d[i + 3] != t[3]) continue;
            if (i > 0 && d[i - 1] != 0 && !(d[i - 4] == 'S' && d[i - 1] == 'H')) continue;
            int at = i + 4, room = at;
            while (room < end && d[room] != 0) room++;
            while (room < end && d[room] == 0) room++;
            int size = room - at - 1;
            if (size <= 0) return;
            var b = System.Text.Encoding.Latin1.GetBytes(text);
            Array.Clear(d, at, size);
            Array.Copy(b, 0, d, at, Math.Min(b.Length, size));
            return;
        }
    }

    // A new maxYMiser song from a file maxYMiser saved (an empty song, or any song: its instruments and
    // sequences are kept, its notes are not). Its replay is that file's. With no instrument in use, it
    // gets three to start with: a square, a bass with a SID on it, and a noise drum.
    public static TrackerSong NewMaxYMiser(byte[] file, string title)
    {
        var data = SndhFile.IsSndh(file) ? SndhFile.Read(file).Data : file;
        var m = MaxYMiserSong.Find(data) ?? throw new FormatException("It is not a song saved by maxYMiser");
        m.Patterns.Clear();
        m.Patterns.Add(new MaxYMiserSong.Line[MaxYMiserSong.Lines]);
        m.Order = new int[,] { { 0, 0, 0, 0xFF } };
        m.Length = 1;
        m.Repeat = 0;
        m.StartPlace = 0;
        m.DmaUsed = false;
        if (m.Instruments.All(i => i.Name.Trim().Length == 0 && i.VolumeSeq == 0))
        {
            if (m.Sequences.Count == 0) m.Sequences.Add(new MaxYMiserSong.Sequence { Steps = new[] { 0 } });
            int Seq(int repeat, params int[] steps)
            {
                m.Sequences.Add(new MaxYMiserSong.Sequence { Steps = steps, Repeat = repeat });
                return m.Sequences.Count - 1;
            }
            void Make(int i, string name, int volume, int mixer, int noise = 0, int timer = 0)
            {
                var x = m.Instruments[i];
                x.Name = name;
                x.SequenceSpeed = 1;
                x.VolumeSeq = volume; x.MixerSeq = mixer; x.NoiseSeq = noise; x.TimerSeq = timer;
                x.ArpeggioSeq = x.VibratoSeq = x.FixedSeq = x.PwmSeq = 0;
            }
            Make(0, "Square", Seq(3, 0xF, 0xE, 0xD, 0xC), Seq(0, 0x100));
            Make(1, "SID bass", Seq(0, 0xF), Seq(0, 0x105), timer: Seq(0, 0xF, 0));
            Make(2, "Noise drum", Seq(7, 0xF, 0xD, 0xB, 0x9, 0x7, 0x5, 0x3, 0x0), Seq(0, 0x1000), noise: Seq(0, 8));
            m.TimerEnables |= 7;
        }
        var bytes = m.Write();
        var song = OpenMaxYMiser(bytes, MaxYMiserSong.Find(bytes)!, title);
        song.Ym!.OpenedTitle = "";
        return song;
    }
}
