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
                            if (c == 3 && !LmcEffects.Contains(e))
                                found.Add($"the effect {e} on channel 4 (there: M, L, R, B, T, X for the LMC1992)");
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
                        bool Drop(char e) => e != '\0' && (c < 3 ? TimerEffects.Contains(e) : !LmcEffects.Contains(e));
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
            "the note; note off stops them. Its effects set the volume chip (LMC1992):\n" +
            "  Mxx master 00 – 28    Lxx left 00 – 14    Rxx right 00 – 14\n" +
            "  Bxx bass 00 – 0C      Txx treble 00 – 0C (06 flat)\n" +
            "  Xxx the YM in the mix: 00 −12 dB, 01 heard, 02 not heard\n\n" +
            "Anything else asks first whether the song should become an ordinary maxYMiser song. File → Save as DHS " +
            "saves it for DHSPLAY.S; it can also be saved for maxYMiser (channels 1 – 3).";
    }
}
