using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AmosLikeBasic
{
    // A song in YM mode saved as GHOST: data for a small 68000 player called once
    // a VBL (50 Hz), with no other interrupts. Channels 1 – 3 are the YM's registers frame by frame;
    // timer effects (SID, PWM, sync buzzer, digidrums) need interrupts faster than 50 Hz and are played
    // as the plain sound under them. Channel 4 is the STe's DMA sound: a note starts its instrument's
    // sample (at the sample's own pitch, whatever the note), note off stops it, and a note with Q follows
    // the sample playing without a gap (a sequence of samples). Effects on channel 4
    // set the volume chip (LMC1992) through the Microwire:
    //   Mxx master 0 – 28 (0 – 40), Lxx left and Rxx right 0 – 14 (0 – 20), Bxx bass and Txx treble
    //   0 – 0C (0 – 12, 6 flat), Xxx the YM in the mix (0: −12 dB, 1: heard, 2: not heard)
    //
    // The file (big-endian, all offsets from the file's start):
    //   0  "GHS1"
    //   4  L the frames
    //   8  L the frame the song loops back to (0: it stops at its end)
    //  12  L the sample table (0: none)
    //  16  W the number of samples
    //  18  W flags: bit 0 the DMA is used, bit 1 the LMC1992 is used (both need an STe)
    //  20  L the number of frames
    //  24  32 bytes the title (ASCII, zeros after)
    //  56  8 bytes reserved
    // A frame: W mask, bits 0 – 13 the YM registers written, bit 14 a DMA event, bit 15 an LMC command;
    // $FFFF ends the song. Then the LMC command (W, the Microwire's 11 bits) if bit 15, then a byte for
    // each register in the mask from 0 up (register 7: bits 0 – 5, the player keeps the ports' bits),
    // then the DMA event if bit 14 (B: 0 stops the DMA, n starts sample n, $80 + n queues sample n to
    // follow the one playing without a gap), and a zero byte if needed so that the next frame starts on an
    // even address. A frame of only the DMA event $80 ($4000, $80, 0) is a sync: the player waits there
    // (a VBL or two at most) until the sample queued last has begun.
    // A sample (16 bytes): L start, L end, L loop start (0: played once; the loop goes to the end),
    // W the DMA mode ($FF8920: bits 0 – 1 the rate, bit 7 mono), W reserved. Samples are 8-bit signed.
    public partial class ChiptuneSynth
    {
        public static readonly int[] DmaRates = { 6258, 12517, 25033, 50066 };

        public sealed class GhostResult
        {
            public byte[] Data = Array.Empty<byte>();
            public int Frames, Samples;
            public List<string> Warnings = new();
        }

        private sealed class GhostFrame
        {
            public byte[] Regs = new byte[14];
            public bool Envelope;
            public int Order, Row;
            public bool RowStart;
        }

        // What a song played from the VBL is: the YM's registers each 1/50 s, and what the rows on channel 4
        // ask for (samples started, queued or stopped; LMC1992 commands). GHOST and DHS are both made from it.
        private sealed class VblPlan
        {
            public List<GhostFrame> Frames = new();
            public int Loop;
            public List<(int Frame, int Sample, bool Queue, int Order, int Row)> Asks = new();   // Sample 0: note off
            public List<ushort>[] Lmc = Array.Empty<List<ushort>>();
            public List<(sbyte[] Data, int LoopStart, int Mode)> Samples = new();
            public List<string> Warnings = new();
        }

        private static VblPlan PlanVbl(TrackerSong song, string format, double maxSeconds, CancellationToken token)
        {
            if (song.Ym == null) throw new InvalidOperationException($"Only a song in YM mode (maxYMiser) can be saved as {format}.");
            var result = new VblPlan();
            var frames = RecordYmFrames(song, maxSeconds, token, out bool timers);
            if (frames.Count == 0) throw new InvalidOperationException("The song has nothing to play.");
            if (timers) result.Warnings.Add($"Timer effects (SID, PWM, sync buzzer, digidrums) are played as plain sound: {format} has only the VBL.");

            // A replay faster than 50 Hz: one frame of each 1/50 s, the envelope started if it was in any of them
            int rate = Math.Max(1, song.Ym.Rate);
            if (rate != 50)
            {
                result.Warnings.Add($"The song's replay runs at {rate} Hz; {format} plays at 50 Hz, so it is taken at 50 Hz.");
                var fifty = new List<GhostFrame>();
                bool env = false;
                bool rowStart = false;
                for (int i = 0; i < frames.Count; i++)
                {
                    env |= frames[i].Envelope;
                    rowStart |= frames[i].RowStart;
                    int at = (int)((long)i * 50 / rate), next = (int)((long)(i + 1) * 50 / rate);
                    if (next == at && i < frames.Count - 1) continue;
                    var f = frames[i];
                    f.Envelope = env;
                    f.RowStart = rowStart;
                    fifty.Add(f);
                    env = rowStart = false;
                }
                frames = fifty;
            }

            // Where it loops back to: the first frame of the repeat place
            int loop = frames.FindIndex(f => f.RowStart && f.Row == 0 && f.Order >= song.Ym.Repeat);
            if (loop < 0) loop = 0;

            // Channel 4: DMA samples and the LMC1992, what each row asks for
            var lmc = new List<ushort>[frames.Count];
            for (int i = 0; i < frames.Count; i++) lmc[i] = new List<ushort>();
            var samples = result.Samples;
            var sampleOf = new Dictionary<int, int>();   // instrument → sample number (from 1)
            var asks = result.Asks;
            int lastInst = 0;
            var offNote = new HashSet<int>();
            var missing = new HashSet<int>();
            if (song.Channels >= 4)
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    if (!f.RowStart) continue;
                    int p = song.PatternAt(f.Order, 3);
                    if (p < 0 || p >= song.Patterns.Count || f.Row >= song.Patterns[p].Rows) continue;
                    var cell = song.Patterns[p][f.Row, 3];
                    if (cell.Instrument > 0) lastInst = cell.Instrument;
                    bool queued = cell.Effect == 'Q' || cell.Effect2 == 'Q';
                    if (cell.Note == TrackerCell.NoteOff) asks.Add((i, 0, false, f.Order, f.Row));
                    else if (cell.Note >= 0 && lastInst > 0)
                    {
                        if (!sampleOf.TryGetValue(lastInst, out int n))
                        {
                            var inst = lastInst <= song.Instruments.Count ? song.Instruments[lastInst - 1] : null;
                            var s = inst == null ? null : DmaSample(inst);
                            if (s == null) { missing.Add(lastInst); n = -1; }
                            else { samples.Add(s.Value); n = samples.Count; }
                            sampleOf[lastInst] = n;
                        }
                        if (n > 0)
                        {
                            asks.Add((i, n, queued, f.Order, f.Row));
                            var inst = song.Instruments[lastInst - 1];
                            if (cell.Note != inst.SampleNote) offNote.Add(lastInst);
                        }
                    }
                    foreach (var (e, v) in new[] { (cell.Effect, cell.Param), (cell.Effect2, cell.Param2) })
                    {
                        ushort? cmd = e switch
                        {
                            'X' => Lmc(0, Math.Min(v, 2)),
                            'B' => Lmc(1, Math.Min(v, 12)),
                            'T' => Lmc(2, Math.Min(v, 12)),
                            'M' => Lmc(3, Math.Min(v, 40)),
                            'R' => Lmc(4, Math.Min(v, 20)),
                            'L' => Lmc(5, Math.Min(v, 20)),
                            _ => null
                        };
                        if (cmd != null) lmc[i].Add(cmd.Value);
                    }
                }
            }
            foreach (int m in missing) result.Warnings.Add($"Instrument {m} on channel 4 has no sample (a WAV file) and is left out.");
            foreach (int m in offNote) result.Warnings.Add($"Instrument {m} on channel 4 is played at its sample's own pitch: {format} has no other notes.");
            result.Frames = frames;
            result.Loop = loop;
            result.Lmc = lmc;
            return result;
        }

        public static GhostResult ExportGhost(TrackerSong song, double maxSeconds = 1200, CancellationToken token = default)
        {
            var plan = PlanVbl(song, "GHOST", maxSeconds, token);
            var result = new GhostResult();
            result.Warnings.AddRange(plan.Warnings);
            var frames = plan.Frames;
            int loop = plan.Loop;
            var asks = plan.Asks;
            var lmc = plan.Lmc;
            var samples = plan.Samples.ToList();
            if (samples.Count > 126) throw new InvalidOperationException($"GHOST has room for 126 samples; the song has {samples.Count}.");
            var dma = new int?[frames.Count];
            var sync = new bool[frames.Count];

            // The DMA's events. A note starts its sample at once. A queued note (Q) follows the sample playing
            // without a gap: the next sample is written into the DMA's registers the frame after the one
            // playing began, the DMA goes on with it when that one ends, and at the queued note's row the
            // song waits (a sync) until the DMA has: the YM keeps time with the DMA whatever its clock. The
            // last sample of a chain is followed by silence, or the DMA would play it again.
            int silence = 0;
            int Silence(int mode)
            {
                if (silence == 0) { samples.Add((new sbyte[16], -1, mode)); silence = samples.Count; }
                return silence;
            }
            void Put(int at, int value, int limit)
            {
                while (at < limit && at < dma.Length && dma[at] != null) at++;
                if (at < limit && at < dma.Length) dma[at] = value;
            }
            int playing = 0, playingFrom = 0;
            bool chained = false;
            for (int k = 0; k < asks.Count; k++)
            {
                var a = asks[k];
                int next = k + 1 < asks.Count ? asks[k + 1].Frame : frames.Count;
                bool nextQueued = k + 1 < asks.Count && asks[k + 1].Queue;
                if (a.Sample == 0) { dma[a.Frame] = 0; playing = 0; chained = false; continue; }
                var (data, loopStart, mode) = samples[a.Sample - 1];
                if (a.Queue && playing > 0 && a.Frame > playingFrom + 1)
                {
                    // When the sample before ends: at most at the shortest rate the STe's clocks give, at least at the longest
                    var (before, _, beforeMode) = samples[playing - 1];
                    double perVbl = DmaBytesPerVbl(beforeMode);
                    double shortest = before.Length / perVbl, longest = before.Length / (perVbl * 250.07 / 250.4);
                    int span = a.Frame - playingFrom;
                    if (span > shortest + 0.5 || longest - span > 2)
                        result.Warnings.Add($"Channel 4, place {a.Order}, row {a.Row}: the sample before lasts {shortest:0.0} – {longest:0.0} VBL, " +
                                            $"the rows from its start {span} VBL. For it to follow without a gap, make the rows " +
                                            $"{Math.Floor(shortest)} VBL long (or the sample {(int)Math.Round(span * perVbl) & ~1} bytes).");
                    if ((mode & 3) != (beforeMode & 3) || (mode & 0x80) != (beforeMode & 0x80))
                        result.Warnings.Add($"Channel 4, place {a.Order}, row {a.Row}: a queued sample plays at the rate of the one before.");
                    Put(playingFrom + 1, 0x80 | a.Sample, a.Frame);
                    sync[a.Frame] = true;
                    chained = true;
                }
                else
                {
                    dma[a.Frame] = a.Sample;
                    chained = false;
                }
                playing = a.Sample;
                playingFrom = a.Frame;
                // The song's last sample, when the song loops back to a queued note: that note's sample follows it
                var loopAsk = asks.FirstOrDefault(x => x.Frame == loop && x.Queue && x.Sample > 0);
                if (k == asks.Count - 1 && loopAsk.Sample > 0 && loopAsk.Frame != a.Frame)
                {
                    Put(a.Frame + 1, 0x80 | loopAsk.Sample, frames.Count);
                    double perVbl = DmaBytesPerVbl(mode), shortest = data.Length / perVbl;
                    int span = frames.Count - a.Frame;
                    if (span > shortest + 0.5 || data.Length / (perVbl * 250.07 / 250.4) - span > 2)
                        result.Warnings.Add($"Channel 4: the last sample lasts {shortest:0.0} VBL, the rows from its start to the song's end {span}: " +
                                            $"for the loop to follow it without a gap, make them {Math.Floor(shortest)} VBL.");
                }
                // The end of a chain: silence queued after the last, unless it loops or something comes before it ends
                else if (chained && !nextQueued && loopStart < 0)
                {
                    double ends = a.Frame + data.Length / DmaBytesPerVbl(mode) * 250.07 / 250.4;
                    if (next > ends - 1) Put(a.Frame + 1, 0x80 | Silence(mode), next);
                }
            }
            if (asks.Any(a => a.Queue) && !sync.Any(x => x))
                result.Warnings.Add("Channel 4: Q queues a sample after the one playing; with nothing playing before it, it is started.");

            bool usesLmc = lmc.Any(l => l.Count > 0);
            bool usesDma = dma.Any(d => d != null);

            // The frames as bytes
            var o = new List<byte>(frames.Count * 8);
            o.AddRange(new byte[64]);
            int streamAt = o.Count, loopAt = 0;
            var prev = new byte[14];
            var queue = new Queue<ushort>();
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                bool full = i == 0 || i == loop;
                if (i == loop) loopAt = o.Count;
                if (sync[i]) { o.Add(0x40); o.Add(0); o.Add(0x80); o.Add(0); }   // a frame of its own: wait for the DMA
                int mask = 0;
                for (int r = 0; r < 13; r++)
                {
                    byte v = r == 7 ? (byte)(f.Regs[r] & 0x3F) : f.Regs[r];
                    if (full || v != prev[r]) mask |= 1 << r;
                    prev[r] = v;
                }
                if (f.Envelope) mask |= 1 << 13;
                if (dma[i] != null) mask |= 1 << 14;
                foreach (var c in lmc[i]) queue.Enqueue(c);
                // (at the loop, commands still waiting are sent before it)
                bool sendLmc = queue.Count > 0 && (mask | 0x8000) != 0xFFFF;
                if (sendLmc) mask |= 0x8000;
                o.Add((byte)(mask >> 8)); o.Add((byte)mask);
                if (sendLmc) { ushort c = queue.Dequeue(); o.Add((byte)(c >> 8)); o.Add((byte)c); }
                for (int r = 0; r < 14; r++)
                    if ((mask & 1 << r) != 0) o.Add(r == 7 ? (byte)(f.Regs[7] & 0x3F) : f.Regs[r]);
                if (dma[i] != null) o.Add((byte)dma[i]!.Value);
                if (o.Count % 2 != 0) o.Add(0);
            }
            // LMC commands left over: frames of their own after the last
            while (queue.Count > 0)
            {
                ushort c = queue.Dequeue();
                o.Add(0x80); o.Add(0); o.Add((byte)(c >> 8)); o.Add((byte)c);
            }
            o.Add(0xFF); o.Add(0xFF);
            if (o.Count % 2 != 0) o.Add(0);

            // The sample table and the samples
            int tableAt = samples.Count > 0 ? o.Count : 0;
            o.AddRange(new byte[samples.Count * 16]);
            for (int n = 0; n < samples.Count; n++)
            {
                var (data, loopStart, mode) = samples[n];
                int start = o.Count;
                o.AddRange(data.Select(b => (byte)b));
                int end = o.Count;
                if (o.Count % 2 != 0) o.Add(0);
                int at = tableAt + n * 16;
                PutLong(o, at, start);
                PutLong(o, at + 4, end);
                PutLong(o, at + 8, loopStart >= 0 ? start + loopStart : 0);
                o[at + 12] = 0; o[at + 13] = (byte)mode;
            }

            var bytes = o.ToArray();
            Encoding.ASCII.GetBytes("GHS1").CopyTo(bytes, 0);
            PutLong(bytes, 4, streamAt);
            PutLong(bytes, 8, loopAt);
            PutLong(bytes, 12, tableAt);
            bytes[16] = (byte)(samples.Count >> 8); bytes[17] = (byte)samples.Count;
            bytes[19] = (byte)((usesDma ? 1 : 0) | (usesLmc ? 2 : 0));
            PutLong(bytes, 20, frames.Count);
            var title = Encoding.ASCII.GetBytes(new string((song.Title ?? "").Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray()));
            Array.Copy(title, 0, bytes, 24, Math.Min(31, title.Length));

            result.Data = bytes;
            result.Frames = frames.Count;
            result.Samples = samples.Count;
            return result;
        }

        // The 68000 player's sources, for Devpac and for rmac (the same code)
        public static readonly string[] GhostSources = { "GHSPLAY.S", "GHSTEST.S", "GHSPLAY_RMAC.S", "GHSTEST_RMAC.S" };

        // The 68000 player's source (GHSPLAY.S) or its test program (GHSTEST.S, which plays the file named),
        // or the same for rmac (…_RMAC.S). The test program has the player pasted in where it includes it, so
        // that it is one file: Devpac on the Atari looks for an included file in its own folder, not in the
        // source's.
        public static string GhostSource(string name, string song = "SONG.GHS")
        {
            string Read(string resource)
            {
                using var stream = typeof(ChiptuneSynth).Assembly.GetManifestResourceStream(resource)
                    ?? throw new FileNotFoundException(resource);
                return new StreamReader(stream).ReadToEnd().Replace("\r\n", "\n");
            }
            string text = Read(name).Replace("SONG.GHS", song);
            if (name.StartsWith("GHSTEST"))
            {
                string player = name.Replace("GHSTEST", "GHSPLAY");
                text = text.Replace($"\tinclude\t\"{player}\"\n",
                    $";----- {player}, pasted in (the player as it is in its own file) -----\n" + Read(player) +
                    $";----- end of {player} -----\n");
            }
            return text.Replace("\n", "\r\n");   // the Atari's line ends
        }

        // The bytes the DMA plays in a VBL of a 50 Hz screen in a mode ($FF8921): a sample each 160, 320, 640
        // or 1280 cycles of the 160 256 a frame has (if the DMA's clock is the CPU's; else a little less)
        public static double DmaBytesPerVbl(int mode) => 160256.0 / (160 << (3 - (mode & 3))) * ((mode & 0x80) != 0 ? 1 : 2);

        // An LMC1992 command for the Microwire: its address (%10), the function, the value
        private static ushort Lmc(int function, int value) => (ushort)(2 << 9 | function << 6 | value);

        private static void PutLong(IList<byte> o, int at, int v)
        {
            o[at] = (byte)(v >> 24); o[at + 1] = (byte)(v >> 16); o[at + 2] = (byte)(v >> 8); o[at + 3] = (byte)v;
        }

        // An instrument's sample at the DMA rate nearest its own, as 8-bit signed mono; a loop goes to the
        // sample's end, so the sample is cut at the loop's end
        internal static (sbyte[] Data, int LoopStart, int Mode)? DmaSample(TrackerInstrument inst)
        {
            var s = SampleBank.Get(inst.Sample);
            if (s == null || s.Data.Length == 0) return null;
            int rateIndex = Enumerable.Range(0, 4).OrderBy(i => Math.Abs(Math.Log(DmaRates[i] / (double)s.Rate))).First();
            double ratio = DmaRates[rateIndex] / (double)s.Rate;
            int srcEnd = inst.LoopEnd > inst.LoopStart && inst.LoopEnd <= s.Data.Length ? inst.LoopEnd : s.Data.Length;
            int n = Math.Max(2, (int)Math.Round(srcEnd * ratio));
            n += n % 2;
            var data = new sbyte[n];
            float peak = 0;
            for (int i = 0; i < srcEnd; i++) peak = Math.Max(peak, Math.Abs(s.Data[i]));
            for (int i = 0; i < n; i++)
            {
                double x = i / ratio;
                int a = (int)x;
                double t = x - a;
                float v0 = a < srcEnd ? s.Data[a] : 0, v1 = a + 1 < srcEnd ? s.Data[a + 1] : v0;
                data[i] = (sbyte)Math.Clamp((int)Math.Round((v0 + (v1 - v0) * t) * 128), -128, 127);
            }
            int loop = inst.LoopEnd > inst.LoopStart ? Math.Min(n - 2, (int)Math.Round(inst.LoopStart * ratio) & ~1) : -1;
            return (data, loop, rateIndex | 0x80);
        }

        // The song played through once, faster than it plays, with the YM's registers taken each frame
        private static List<GhostFrame> RecordYmFrames(TrackerSong song, double maxSeconds, CancellationToken token, out bool timers)
        {
            var frames = new List<GhostFrame>();
            bool anyTimer = false;
            bool was = ManualRendering;
            ManualRendering = true;
            Thread.Sleep(30);
            var chunk = new float[SampleRate / 10 * 2];
            var muted = (bool[])_trackMute.Clone();
            try
            {
                Reset();
                RenderStereo(chunk);
                YmRecorder = (regs, env, timer) =>
                {
                    anyTimer |= timer;
                    frames.Add(new GhostFrame
                    {
                        Regs = (byte[])regs.Clone(),
                        Envelope = env,
                        Order = _trackOrder,
                        Row = _trackRow,
                        RowStart = _trackTick == 0 && !_trackRepeatingRow
                    });
                };
                TrackPlay(song, loop: false);
                int maxFrames = (int)(maxSeconds * Math.Max(50, song.Ym!.Rate));
                do
                {
                    token.ThrowIfCancellationRequested();
                    RenderStereo(chunk);
                } while (IsTrackPlaying() && frames.Count < maxFrames);
                TrackStop();
                RenderStereo(chunk);
            }
            finally
            {
                YmRecorder = null;
                Reset();
                Array.Copy(muted, _trackMute, muted.Length);
                RenderStereo(chunk);
                ManualRendering = was;
            }
            timers = anyTimer;
            return frames;
        }
    }
}
