using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AmosLikeBasic
{
    // A GHOST song saved as DHS: the three files of the DHS spec ("GHOST-STE-TRACKER-THING"), for a player
    // called from the VBL. Each can be left out when the song does not need it. Big-endian.
    //
    //   .GYM  "GHOSTYM!", B major, B minor, L frames, L the loop frame; then 14 bytes a frame: registers
    //         0 – 13, register 7 with bits 6 and 7 cleared, register 13 $FF when it is not to be written
    //         (a write starts the envelope again)
    //   .GPC  "GHOSTPCM", B major, B minor, L the sample data's offset in the file, L its size; then 6 bytes
    //         an entry: W the VBL to start at, L the offset in the sample data to start from; last
    //         W the VBL, W -1, W the VBL to go back to. The DMA plays to the sample data's end, so a sample
    //         plays on into the one after it in the data until the next entry: made for a long sound in
    //         parts, laid out one after another. All samples have one rate (the file does not say which).
    //   .GLM  "GHOSTLMC", B major, B minor; then 4 bytes an entry: W the VBL, W the Microwire's word; last
    //         W the VBL, W -1, W the VBL to go back to. One command a VBL.
    public partial class ChiptuneSynth
    {
        public sealed class DhsResult
        {
            public byte[] Ym = Array.Empty<byte>();
            public byte[]? Pcm, Lmc;   // null: not used
            public int Frames, Samples, Rate;
            public List<string> Warnings = new();
        }

        public static DhsResult ExportDhs(TrackerSong song, double maxSeconds = 1200, CancellationToken token = default)
        {
            var plan = PlanVbl(song, "DHS", maxSeconds, token);
            var result = new DhsResult { Frames = plan.Frames.Count };
            result.Warnings.AddRange(plan.Warnings);
            var frames = plan.Frames;
            int loop = plan.Loop;
            if (frames.Count > 0xFFFE) throw new InvalidOperationException($"DHS counts VBLs in a word: the song is {frames.Count} VBLs, 65534 at most.");

            // The YM: all 14 registers each frame
            var ym = new List<byte>(18 + frames.Count * 14);
            Header(ym, "GHOSTYM!");
            AddLong(ym, frames.Count);
            AddLong(ym, loop);
            foreach (var f in frames)
                for (int r = 0; r < 14; r++)
                    ym.Add(r == 7 ? (byte)(f.Regs[7] & 0x3F) : r == 13 ? (f.Envelope ? f.Regs[13] : (byte)0xFF) : f.Regs[r]);
            result.Ym = ym.ToArray();

            // The samples: those the song starts, one after another in their order, each on an even offset
            var used = plan.Asks.Where(a => a.Sample > 0).Select(a => a.Sample).Distinct().OrderBy(n => n).ToList();
            if (used.Count > 0)
            {
                var data = new List<byte>();
                var offset = new Dictionary<int, int>();
                foreach (int n in used)
                {
                    offset[n] = data.Count;
                    data.AddRange(plan.Samples[n - 1].Data.Select(b => (byte)b));
                    if (data.Count % 2 != 0) data.Add(0);
                }
                int mode = plan.Samples[used[0] - 1].Mode;
                result.Rate = DmaRates[mode & 3];
                result.Samples = used.Count;
                if (used.Any(n => (plan.Samples[n - 1].Mode & 0x83) != (mode & 0x83)))
                    result.Warnings.Add($"Channel 4: DHS plays all samples at one rate, {result.Rate} Hz (the first's); the others are not at it.");
                if (plan.Asks.Any(a => a.Sample == 0))
                    result.Warnings.Add("Channel 4: DHS has no note off; the sample plays on.");
                if (used.Any(n => plan.Samples[n - 1].LoopStart >= 0))
                    result.Warnings.Add("Channel 4: DHS does not loop a sample; it plays on into the samples after it.");

                var entries = plan.Asks.Where(a => a.Sample > 0).Select(a => (a.Frame, Offset: offset[a.Sample])).ToList();
                double perVbl = DmaBytesPerVbl(mode);
                // Where the song loops back to with no start of its own: the sample playing there, from as far as it has got
                if (loop > 0 && entries.All(e => e.Frame != loop))
                {
                    var before = plan.Asks.LastOrDefault(a => a.Frame < loop);
                    if (before.Sample > 0)
                    {
                        int into = (int)Math.Round((loop - before.Frame) * perVbl) & ~1;
                        if (into < plan.Samples[before.Sample - 1].Data.Length)
                            entries.Add((loop, offset[before.Sample] + into));
                    }
                }
                entries = entries.GroupBy(e => e.Frame).Select(g => g.Last()).OrderBy(e => e.Frame).ToList();
                // A sample that ends long before the next start plays on into the data after it
                var warned = new HashSet<int>();
                foreach (var a in plan.Asks.Where(a => a.Sample > 0))
                {
                    int next = plan.Asks.Where(x => x.Frame > a.Frame).Select(x => x.Frame).DefaultIfEmpty(frames.Count).First();
                    double lasts = plan.Samples[a.Sample - 1].Data.Length / perVbl;
                    if (next - a.Frame > lasts + 1 && a.Sample != used[^1] && warned.Add(a.Sample))
                        result.Warnings.Add($"Channel 4, place {a.Order}, row {a.Row}: the sample lasts {lasts:0.0} VBL and nothing starts for " +
                                            $"{next - a.Frame}; DHS plays on into the sample after it in the data.");
                }

                var pcm = new List<byte>();
                Header(pcm, "GHOSTPCM");
                int dataAt = 18 + entries.Count * 6 + 6;
                AddLong(pcm, dataAt);
                AddLong(pcm, data.Count);
                foreach (var (frame, at) in entries) { AddWord(pcm, frame); AddLong(pcm, at); }
                AddWord(pcm, frames.Count); AddWord(pcm, 0xFFFF); AddWord(pcm, loop);
                pcm.AddRange(data);
                result.Pcm = pcm.ToArray();
            }

            // The LMC1992: one command a VBL; more on a row go out on the VBLs after it
            if (plan.Lmc.Any(l => l.Count > 0))
            {
                var lmc = new List<byte>();
                Header(lmc, "GHOSTLMC");
                var waiting = new Queue<ushort>();
                for (int i = 0; i < frames.Count; i++)
                {
                    foreach (var c in plan.Lmc[i]) waiting.Enqueue(c);
                    if (waiting.Count > 0) { AddWord(lmc, i); AddWord(lmc, waiting.Dequeue()); }
                }
                int left = waiting.Count;
                if (left > 0) result.Warnings.Add($"Channel 4: {left} LMC1992 command(s) at the song's very end are left out (one a VBL).");
                AddWord(lmc, frames.Count); AddWord(lmc, 0xFFFF); AddWord(lmc, loop);
                result.Lmc = lmc.ToArray();
            }
            return result;
        }

        private static void Header(List<byte> o, string id)
        {
            o.AddRange(Encoding.ASCII.GetBytes(id));
            o.Add(1); o.Add(0);   // version 1.0
        }

        private static void AddWord(List<byte> o, int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }

        private static void AddLong(List<byte> o, int v) { AddWord(o, v >> 16); AddWord(o, v); }
    }
}
