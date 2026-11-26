using System.Text;
using S = AmosLikeBasic.ChiptuneSynth;

namespace AmosLikeBasic.Tests;

// A GHOST song saved as DHS: the DHS spec's three files, the YM's 14 registers a VBL (.GYM), the PCM sample
// sequence (.GPC) and the LMC1992 commands (.GLM), read back here as the spec says.
public class DhsTests
{
    private static int Long(byte[] d, int at) => d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3];
    private static int Word(byte[] d, int at) => d[at] << 8 | d[at + 1];

    [Fact]
    public void The_YM_file_has_all_14_registers_each_VBL_as_GHOST_plays_them()
    {
        var song = GhostTests.Open();
        GhostMode.Convert(song);
        var dhs = S.ExportDhs(TrackerSong.FromJson(song.ToJson()));
        var ghost = S.ExportGhost(TrackerSong.FromJson(song.ToJson()));
        var d = dhs.Ym;
        Assert.Equal("GHOSTYM!", Encoding.ASCII.GetString(d, 0, 8));
        Assert.Equal(new byte[] { 1, 0 }, d[8..10]);
        int frames = Long(d, 10);
        Assert.Equal(ghost.Frames, frames);
        Assert.Equal(18 + frames * 14, d.Length);
        Assert.Null(dhs.Pcm);   // no samples, no LMC: the YM file alone
        Assert.Null(dhs.Lmc);

        // The same registers as GHOST's frame by frame; register 13 only where the envelope starts again
        var g = GhostTests.Read(ghost.Data);
        int loopOffset = Long(ghost.Data, 8);
        Assert.Equal(g.FindIndex(f => f.Offset == loopOffset), Long(d, 14));
        for (int i = 0; i < frames; i++)
        {
            var regs = d.AsSpan(18 + i * 14, 14);
            for (int r = 0; r < 13; r++) Assert.Equal(g[i].Regs[r], regs[r]);
            Assert.True(regs[7] <= 0x3F);
            Assert.Equal((g[i].Mask & 1 << 13) != 0 ? g[i].Regs[13] : 0xFF, regs[13]);
        }
    }

    [Fact]
    public void A_sample_sequence_becomes_PCM_entries_into_one_block_and_LMC_commands_a_list()
    {
        var song = GhostTests.Open();
        GhostMode.Convert(song);
        song.Patterns.Add(new TrackerPattern(64));
        song.Order = new List<int> { song.Patterns.Count - 1 };
        song.ChannelOrder = new();
        var wavs = new List<string>();
        try
        {
            for (int k = 0; k < 3; k++)
            {
                string wav = Path.Combine(Path.GetTempPath(), $"dhs-seq{k}-{Guid.NewGuid():N}.wav");
                SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, 46939).Select(i => (float)Math.Sin(i * 0.1) * 0.5f).ToArray(), 12517));
                wavs.Add(wav);
            }
            GhostMode.AddSampleSequence(song, wavs.Select((w, k) => ($"part{k}", w)).ToList(), 32, chain: true);
            var first = song.Patterns[song.PatternAt(0, 3)];
            var c = first[0, 3];
            first[0, 3] = new TrackerCell(c.Note, c.Instrument, c.Effect, c.Param, effect2: 'M', param2: 0x28);
            first[1, 3] = new TrackerCell(-1, 0, 'L', 0x14, effect2: 'X', param2: 2);

            var r = S.ExportDhs(TrackerSong.FromJson(song.ToJson()));
            Assert.Equal(12517, r.Rate);
            Assert.Equal(3 * 187, r.Frames);

            // PCM: the three parts one after another, each started at its row's VBL
            var p = r.Pcm!;
            Assert.Equal("GHOSTPCM", Encoding.ASCII.GetString(p, 0, 8));
            int dataAt = Long(p, 10), size = Long(p, 14);
            Assert.Equal(p.Length, dataAt + size);
            Assert.Equal(3 * 46940, size);
            Assert.Equal(18 + 3 * 6 + 6, dataAt);
            for (int k = 0; k < 3; k++)
            {
                Assert.Equal(k * 187, Word(p, 18 + k * 6));
                Assert.Equal(k * 46940, Long(p, 20 + k * 6));
            }
            Assert.Equal(3 * 187, Word(p, 36));
            Assert.Equal(0xFFFF, Word(p, 38));
            Assert.Equal(0, Word(p, 40));
            // The sample data as the GHOST file has it
            var g = S.ExportGhost(TrackerSong.FromJson(song.ToJson())).Data;
            int table = Long(g, 12);
            Assert.Equal(g.AsSpan(Long(g, table), 46940).ToArray(), p.AsSpan(dataAt, 46940).ToArray());

            // LMC: one command a VBL, the words the Microwire takes (master $04C0 + v)
            var l = r.Lmc!;
            Assert.Equal("GHOSTLMC", Encoding.ASCII.GetString(l, 0, 8));
            var entries = Enumerable.Range(0, (l.Length - 10 - 6) / 4).Select(i => (Vbl: Word(l, 10 + i * 4), Word: Word(l, 12 + i * 4))).ToList();
            Assert.Equal(new[] { (0, 0x04C0 + 40) }, entries.Take(1));
            Assert.Equal(new[] { 0x0540 + 20, 0x0400 + 2 }, entries.Skip(1).Select(e => e.Word));
            Assert.Equal(entries.Select(e => e.Vbl).Distinct().Count(), entries.Count);
            Assert.Equal(new[] { 3 * 187, 0xFFFF, 0 }, new[] { Word(l, l.Length - 6), Word(l, l.Length - 4), Word(l, l.Length - 2) });
        }
        finally { foreach (var w in wavs) { File.Delete(w); SampleBank.Forget(w); } }
    }
}
