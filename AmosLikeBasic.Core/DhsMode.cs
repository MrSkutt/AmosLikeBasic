using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic
{
    // A maxYMiser song kept within what DHS plays (TrackerSong.Mode "DHS"): a player called from the VBL
    // only, so nothing that needs a timer.
    //   Channels 1 – 3  maxYMiser's instruments 1 – 32, without timer effects (the mixer's timer digit:
    //                   5 SID, 9 PWM, B sync buzzer, D digidrum) and without the effects for them (7, 8, 9
    //                   PWM; G digidrum; I timer sequence; R digi rate)
    //   Channel 4       the STe's DMA: sample instruments (33 and up, a WAV file each), played at their own
    //                   pitch; its effects set the LMC1992: M master, L left, R right, B bass, T treble,
    //                   X the YM in the mix
    // The song's timers are off (TrackerYmSong.Timers 0), so the tracker plays it as DHS will.
    public static class DhsMode
    {
        public const string Name = "DHS";
        public const int YmInstruments = 32;
        public const string TimerEffects = "789GIR";
        public const string LmcEffects = "MLRBTX";
        public const string Channel4Effects = LmcEffects + "QS";  // and Q: queued after the sample playing; S: the speed

        public static bool Is(TrackerSong song) => song.Mode == Name && song.Ym != null;

        // What the song has that DHS cannot play, each said once (where it is does not matter for asking)
        public static SortedSet<string> Problems(TrackerSong song)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var ym = song.Ym;
            if (ym == null) return found;

            bool HasTimer(int seq) =>
                seq > 0 && seq < ym.Sequences.Count && ym.Sequences[seq].Steps.Any(v => (v & 15) != 0);

            for (int i = 0; i < song.Instruments.Count; i++)
            {
                var inst = song.Instruments[i];
                if (i < YmInstruments)
                {
                    if (inst.Ym != null && HasTimer(inst.Ym.MixerSeq))
                        found.Add($"a timer effect (SID, PWM, sync buzzer or digidrum) in instrument {i + 1}'s mixer sequence");
                }
                else if (inst.Wave != "Sample")
                    found.Add($"instrument {i + 1}, which is not a sample (instruments from 33 are the DMA's samples)");
            }

            var seen = new HashSet<int>();
            for (int place = 0; place < song.Order.Count; place++)
                for (int c = 0; c < Math.Min(song.Channels, 4); c++)
                {
                    int p = song.PatternAt(place, c);
                    if (p < 0 || p >= song.Patterns.Count || !seen.Add(p * 8 + c)) continue;
                    var pattern = song.Patterns[p];
                    for (int r = 0; r < pattern.Rows; r++)
                    {
                        var cell = pattern[r, c];
                        foreach (var (e, v) in new[] { (cell.Effect, cell.Param), (cell.Effect2, cell.Param2) })
                        {
                            if (e == '\0') continue;
                            if (c < 3 && TimerEffects.Contains(e))
                                found.Add($"the effect {e} on channels 1 – 3 (it is for a timer effect)");
                            if (c < 3 && e == 'M' && HasTimer(v))
                                found.Add($"the effect M{v:X2}: mixer sequence {v} has a timer effect");
                            if (c == 3 && !Channel4Effects.Contains(e))
                                found.Add($"the effect {e} on channel 4 (there: Q, and M, L, R, B, T, X for the LMC1992)");
                        }
                        if (cell.Instrument > 0)
                        {
                            if (c < 3 && cell.Instrument > YmInstruments)
                                found.Add("a sample instrument on channels 1 – 3 (the samples are played by the DMA, channel 4)");
                            if (c == 3 && cell.Instrument <= YmInstruments)
                                found.Add("a maxYMiser instrument on channel 4 (the DMA plays samples: instruments from 33)");
                        }
                    }
                }
            return found;
        }

        // A maxYMiser song made one for DHS: timers off, their digits taken out of the mixer sequences,
        // the effects that need them taken away, channel 4 the DMA's. What was taken is told.
        public static List<string> Convert(TrackerSong song)
        {
            var ym = song.Ym ?? throw new InvalidOperationException("Only a maxYMiser song can be made a DHS song");
            var told = new List<string>();
            var mixers = new HashSet<int>(song.Instruments.Take(YmInstruments).Where(i => i.Ym != null).Select(i => i.Ym!.MixerSeq));
            foreach (var pattern in song.Patterns)
                for (int r = 0; r < pattern.Rows; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        var cell = pattern[r, c];
                        if (cell.Effect == 'M') mixers.Add(cell.Param);
                        if (cell.Effect2 == 'M') mixers.Add(cell.Param2);
                    }
            foreach (int n in mixers.Where(n => n > 0 && n < ym.Sequences.Count).OrderBy(n => n))
            {
                var steps = ym.Sequences[n].Steps;
                var kinds = steps.Select(v => v & 15).Where(t => t != 0).Distinct().Select(TimerName).ToList();
                if (kinds.Count == 0) continue;
                for (int k = 0; k < steps.Count; k++) steps[k] &= ~15;
                var users = Enumerable.Range(0, Math.Min(YmInstruments, song.Instruments.Count))
                    .Where(i => song.Instruments[i].Ym?.MixerSeq == n).Select(i => (i + 1).ToString()).ToList();
                told.Add($"Mixer sequence {n}" + (users.Count > 0 ? $" (instrument {string.Join(", ", users)})" : "") +
                         $": {string.Join(", ", kinds)} taken out; the tone and noise under it stay.");
            }
            int effects = 0, channel4 = 0;
            for (int pi = 0; pi < song.Patterns.Count; pi++)
            {
                var pattern = song.Patterns[pi];
                for (int r = 0; r < pattern.Rows; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        var cell = pattern[r, c];
                        bool Drop(char e) => e != '\0' && (c < 3 ? TimerEffects.Contains(e) : !Channel4Effects.Contains(e));
                        var keep = cell;
                        if (Drop(cell.Effect)) keep = keep.WithEffect('\0', 0);
                        if (Drop(cell.Effect2)) keep = keep.WithEffect2('\0', 0);
                        if (c == 3 && keep.Instrument is > 0 and <= YmInstruments)
                        {
                            keep = new TrackerCell(keep.Note == TrackerCell.NoteOff ? keep.Note : TrackerCell.NoNote, 0, keep.Effect, keep.Param, keep.Volume, keep.Effect2, keep.Param2);
                            channel4++;
                        }
                        if (!keep.Equals(cell)) { if (c < 3) effects++; pattern[r, c] = keep; }
                    }
            }
            if (effects > 0) told.Add($"{effects} timer effects (7, 8, 9, G, I, R) taken off the lines.");
            if (channel4 > 0) told.Add($"{channel4} notes of maxYMiser's DMA track taken off channel 4: it is the DMA's samples now.");
            if (ym.Rate != 50) told.Add($"The replay runs at {ym.Rate} Hz; DHS plays at 50 Hz, so the song is taken at 50 Hz when saved.");
            ym.Timers = 0;
            song.Channels = 4;
            song.Mode = Name;
            return told;
        }

        // A long sound in parts (a sample sequence): each part an instrument from 33 on, and with chain, the
        // parts on channel 4 one after another, each in a place of its own of rowsPerPart rows: the first
        // started, the others queued (Q) after it. Each row's speed (S on channel 4, 5 or 6 VBL and so on) makes
        // the rows of a part as long as the part, or a little shorter: the song waits for the DMA at each part,
        // so it never runs ahead of it. The places go after the song's, or from its start if it has nothing yet.
        public static List<string> AddSampleSequence(TrackerSong song, IReadOnlyList<(string Name, string Sample)> parts,
                                                     int rowsPerPart, bool chain)
        {
            if (!Is(song)) throw new InvalidOperationException("A sample sequence is for a DHS song's channel 4.");
            if (parts.Count == 0) return new List<string>();
            var told = new List<string>();
            while (song.Instruments.Count < YmInstruments) song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
            int first = song.Instruments.Count + 1;
            if (first + parts.Count - 1 > 33 + 125) throw new InvalidOperationException("DHS has room for 126 samples.");
            var lengths = new List<(int Bytes, int Mode)>();
            foreach (var (name, sample) in parts)
            {
                var inst = new TrackerInstrument { Name = name, Wave = "Sample", Sample = sample, SampleNote = 60, Attack = 0, Release = 0 };
                var dma = ChiptuneSynth.DmaSample(inst) ?? throw new InvalidOperationException($"{name}: the sample cannot be read.");
                lengths.Add((dma.Data.Length, dma.Mode));
                song.Instruments.Add(inst);
            }
            int last = song.Instruments.Count;
            told.Add($"Instruments {first:X2} – {last:X2}: the {parts.Count} parts, played by the DMA at " +
                     $"{ChiptuneSynth.DmaRates[lengths[0].Mode & 3]} Hz.");
            if (!chain) return told;

            // Where the places go: from the start of a song that has nothing yet, else after its places
            bool fresh = song.Order.Count == 1 && Enumerable.Range(0, 4).All(c => IsEmpty(song.Patterns[song.PatternAt(0, c)]));
            int place = fresh ? 0 : song.Order.Count;
            int firstPlace = place;
            var speeds = new HashSet<string>();
            for (int k = 0; k < parts.Count; k++, place++)
            {
                var (bytes, mode) = lengths[k];
                double vbls = bytes / ChiptuneSynth.DmaBytesPerVbl(mode);
                int span = (int)Math.Floor(vbls);
                if (span < rowsPerPart * 2)
                    throw new InvalidOperationException($"{parts[k].Name} lasts {vbls:0.0} VBL: too short for {rowsPerPart} rows (2 VBL a row at least).");
                var speedOf = Enumerable.Range(0, rowsPerPart)
                    .Select(r => (int)Math.Round((r + 1) * (double)span / rowsPerPart) - (int)Math.Round(r * (double)span / rowsPerPart)).ToArray();
                var pattern = new TrackerPattern(rowsPerPart);
                song.Patterns.Add(pattern);
                int index = song.Patterns.Count - 1;
                if (place < song.Order.Count) song.Order[place] = index; else song.Order.Add(index);
                if (place < song.ChannelOrder.Count) song.ChannelOrder[place] = null;   // all channels the place's pattern
                int previous = -1;
                for (int r = 0; r < rowsPerPart; r++)
                {
                    char speed = speedOf[r] != previous ? 'S' : '\0';
                    int speedParam = speed == 'S' ? speedOf[r] : 0;
                    previous = speedOf[r];
                    if (r == 0)
                        pattern[r, 3] = new TrackerCell(60, first + k, k == 0 ? '\0' : 'Q', 0, TrackerCell.NoVolume, speed, speedParam);
                    else if (speed != '\0')
                        pattern[r, 3] = new TrackerCell(TrackerCell.NoNote, 0, speed, speedParam);
                }
                speeds.Add(string.Join(" ", speedOf.Distinct().OrderByDescending(v => v)) + $" VBL ({span} VBL)");
            }
            if (fresh) song.Speed = song.Ym!.Speed = Math.Max(2, (int)Math.Round(lengths[0].Bytes / ChiptuneSynth.DmaBytesPerVbl(lengths[0].Mode) / rowsPerPart));
            told.Add($"Places {firstPlace} – {place - 1}: one part each on channel 4, {rowsPerPart} rows, the first started and " +
                     "the others queued (Q) after it, so the DMA goes from part to part without a gap.");
            told.Add($"The rows' speed (S on channel 4) makes each part's rows as long as the part: rows of {string.Join("; ", speeds)}. " +
                     "The song waits for the DMA at each part, a VBL at most.");
            told.Add("Write the YM's channels 1 – 3 in these places. (A song saved for maxYMiser has channels 1 – 3 only, " +
                     "without the speeds on channel 4.)");
            return told;
        }

        private static bool IsEmpty(TrackerPattern p)
        {
            for (int r = 0; r < p.Rows; r++)
                for (int c = 0; c < TrackerSong.MaxChannels; c++)
                    if (!p[r, c].IsEmpty) return false;
            return true;
        }

        // Out of DHS: an ordinary maxYMiser song, its timers on again
        public static void Leave(TrackerSong song)
        {
            if (song.Mode != Name) return;
            song.Mode = null;
            if (song.Ym != null) song.Ym.Timers = 7;
        }

        private static string TimerName(int t) => t switch
        {
            5 => "SID", 9 => "PWM", 0xB => "sync buzzer", 0xD => "digidrum", _ => $"timer {t:X}"
        };

        // What DHS plays, for the help
        public const string Help =
            "A DHS song is a maxYMiser song kept within what a player called from the VBL alone (50 Hz, no other " +
            "interrupts) can play.\n\n" +
            "Channels 1 – 3, the YM: maxYMiser's instruments 1 – 32 and its effects, but nothing that needs a timer. " +
            "The mixer's timer digit (5 SID, 9 PWM, B sync buzzer, D digidrum) and the effects 7, 8, 9 (PWM), G (digidrum), " +
            "I (timer sequence) and R (digi rate) are not there. Tone, noise, the buzzer (envelope), arpeggio, vibrato, " +
            "portamento and the other effects are.\n\n" +
            "Channel 4, the STe's DMA: sample instruments (from 33, a WAV file each), played at their own pitch whatever " +
            "the note; note off stops them. A note with Q (Q00 in an effect column) follows the sample playing without " +
            "a gap, for a long sound in parts: the song waits for the DMA there, so the YM keeps time with it. The rows " +
            "from the sample before must be about as long as it (the save says by how much if not). The tracker " +
            "plays it so too, and shows a queued note in blue with a line down its side. Sxx on channel 4 sets the " +
            "speed as on channels 1 – 3 (Instrument → Load sample sequence uses it to fit each part's rows to it). The other " +
            "effects set the volume chip (LMC1992):\n" +
            "  Mxx master 00 – 28    Lxx left 00 – 14    Rxx right 00 – 14\n" +
            "  Bxx bass 00 – 0C      Txx treble 00 – 0C (06 flat)\n" +
            "  Xxx the YM in the mix: 00 −12 dB, 01 heard, 02 not heard\n\n" +
            "Anything else asks first whether the song should become an ordinary maxYMiser song. File → Save as DHS " +
            "saves it for DHSPLAY.S; it can also be saved for maxYMiser (channels 1 – 3).";
    }
}
