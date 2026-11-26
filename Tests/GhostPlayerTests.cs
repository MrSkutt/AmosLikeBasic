using System.Diagnostics;

namespace AmosLikeBasic.Tests;

// The 68000 player for GHOST songs (Atari/GHOST/GHSPLAY.S), assembled with vasm in Devpac mode and run on the
// emulated 68000: frame by frame it must leave the YM, the DMA and the Microwire as the file says, keep
// all registers, and take well under 1 % of a 50 Hz frame. Runs only where vasm is found: the environment
// variable VASM (the vasmm68k_mot program) or vasmm68k_mot on the PATH.
public class GhostPlayerTests
{
    private const uint Load = 0x10000, Return = 0x500;
    private const int FrameCycles = 160000;   // 8 MHz at 50 Hz

    private static int Long(byte[] d, int at) => d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3];
    private static int Word(byte[] d, int at) => d[at] << 8 | d[at + 1];

    private static string? Vasm()
    {
        var env = Environment.GetEnvironmentVariable("VASM");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir, "vasmm68k_mot");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string PlayerFolder()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Atari", "GHOST", "GHSPLAY.S"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("Atari/GHOST"), "Atari", "GHOST");
    }

    // The player and the song as one binary: init, stop, play at 0, 4, 8
    private static byte[]? Assemble(byte[] song)
    {
        var vasm = Vasm();
        if (vasm == null) return null;
        var dir = Directory.CreateTempSubdirectory("ghost").FullName;
        try
        {
            File.Copy(Path.Combine(PlayerFolder(), "GHSPLAY.S"), Path.Combine(dir, "GHSPLAY.S"));
            File.WriteAllBytes(Path.Combine(dir, "SONG.GHS"), song);
            File.WriteAllText(Path.Combine(dir, "WRAP.S"),
                "\tbra.w\tinit\n\tbra.w\tghs_stop\n\tbra.w\tghs_play\ninit:\tlea\tsong(pc),a0\n\tbra\tghs_init\n" +
                "\tinclude\t\"GHSPLAY.S\"\n\teven\nsong:\tincbin\t\"SONG.GHS\"\n");
            var p = Process.Start(new ProcessStartInfo(vasm, "-devpac -Fbin -quiet -o WRAP.BIN WRAP.S")
                { WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true })!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, output);
            return File.ReadAllBytes(Path.Combine(dir, "WRAP.BIN"));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class Machine
    {
        public readonly AtariSt St = new();
        public readonly M68000 Cpu;
        public Machine(byte[] bin)
        {
            Cpu = new M68000(St);
            St.Load(Load, bin);
            St.Write16(Return, 0x4E71);
            Cpu.SetState(0x2700, 0x7000, 0x8000);
        }

        // A routine run to its end: the cycles it took. The rest of the frame passes (the Microwire shifts).
        public long Call(uint at)
        {
            Cpu.Push32(Return);
            Cpu.PC = at;
            long start = Cpu.Cycles;
            for (int i = 0; i < 1_000_000 && Cpu.PC != Return; i++) Cpu.Step();
            Assert.Equal(Return, Cpu.PC);
            long used = Cpu.Cycles - start;
            St.Dma.Run(FrameCycles - used);
            return used;
        }
    }

    // The song played through (and past its loop) by the player and read in C# side by side
    private static long Check(byte[] ghost)
    {
        var bin = Assemble(ghost);
        if (bin == null) return -1;
        var m = new Machine(bin);
        m.Call(Load);
        Assert.Equal(0u, m.Cpu.D[0]);
        uint song = Load + (uint)bin.AsSpan().LastIndexOf("GHS1"u8);
        int pos = Long(ghost, 4), loop = Long(ghost, 8), table = Long(ghost, 12), frames = Long(ghost, 20);
        var regs = new byte[14];
        var starts = new List<(uint Start, uint End, double Rate)>();
        m.St.Dma.FrameStarted = (s, e, rate, stereo, again) => starts.Add((s, e, rate));
        long worst = 0;
        for (int f = 0; f < frames + 100; f++)
        {
            int mask = Word(ghost, pos);
            if (mask == 0xFFFF)
            {
                if (loop == 0) break;
                pos = loop;
                mask = Word(ghost, pos);
            }
            int q = pos + 2;
            int? lmc = null, dma = null;
            if ((mask & 0x8000) != 0) { lmc = Word(ghost, q); q += 2; }
            for (int r = 0; r < 14; r++) if ((mask & 1 << r) != 0) regs[r] = ghost[q++];
            if ((mask & 0x4000) != 0) dma = ghost[q++];
            pos = q + (q & 1);

            m.St.EnvelopeWritten = false;
            starts.Clear();
            var d = (uint[])m.Cpu.D.Clone();
            var a = (uint[])m.Cpu.A.Clone();
            worst = Math.Max(worst, m.Call(Load + 8));
            Assert.Equal(d, m.Cpu.D);
            Assert.Equal(a, m.Cpu.A);
            for (int r = 0; r < 13; r++)
                Assert.True((r == 7 ? m.St.Ym[7] & 0x3F : m.St.Ym[r]) == (r == 7 ? regs[7] & 0x3F : regs[r]), $"frame {f}, register {r}");
            Assert.Equal((mask & 0x2000) != 0, m.St.EnvelopeWritten);
            if ((mask & 0x2000) != 0) Assert.Equal(regs[13], m.St.Ym[13]);
            if (lmc != null) Assert.Equal(lmc.Value, m.St.Read16(0xFF8922));
            if (dma is > 0)
            {
                int t = table + (dma.Value - 1) * 16;
                Assert.Single(starts);
                Assert.Equal(song + (uint)Long(ghost, t), starts[0].Start);
                Assert.Equal(song + (uint)Long(ghost, t + 4), starts[0].End);
                Assert.Equal(ChiptuneSynth.DmaRates[ghost[t + 13] & 3], starts[0].Rate);
                if (Long(ghost, t + 8) != 0)
                {
                    // Looped: round and round, the loop's start waiting in the start registers
                    Assert.Equal(3, m.St.Read8(0xFF8901) & 3);
                    uint next = (uint)(m.St.Read8(0xFF8903) << 16 | m.St.Read8(0xFF8905) << 8 | m.St.Read8(0xFF8907));
                    Assert.Equal(song + (uint)Long(ghost, t + 8), next);
                }
            }
            if (dma == 0) Assert.Equal(0, m.St.Read8(0xFF8901) & 1);
        }
        return worst;
    }

    private static TrackerSong Open()
    {
        var file = MaxYMiserTests.Song();
        return YmImport.OpenMaxYMiser(file, MaxYMiserSong.Find(file)!, "Test");
    }

    // A long sound in parts on channel 4, each queued (Q) after the one before, with an LMC marker (Mxx, xx
    // the part's number) on each part's row: the DMA goes from part to part without a gap, and the song
    // waits for it, so each marker comes within a VBL after its part has begun, the last as the first
    [Fact]
    public void Queued_samples_follow_without_a_gap_and_the_song_keeps_time_with_them()
    {
        var song = Open();
        GhostMode.Convert(song);
        const int rows = 16, speed = 6, parts = 12;
        song.Speed = speed;
        song.Ym!.Speed = speed;
        // Each part a little longer than its rows (0.3 VBL), as a real sample will be
        int bytes = (int)Math.Round((rows * speed + 0.3) * 250.4) & ~1;
        var wavs = new List<string>();
        try
        {
            while (song.Instruments.Count < 32) song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
            for (int k = 0; k < 3; k++)
            {
                string wav = Path.Combine(Path.GetTempPath(), $"ghost-part{k}-{Guid.NewGuid():N}.wav");
                SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, bytes).Select(i => (float)Math.Sin(i * (0.05 + k * 0.03)) * 0.5f).ToArray(), 12517));
                wavs.Add(wav);
                song.Instruments.Add(new TrackerInstrument { Name = "Part " + k, Wave = "Sample", Sample = wav, SampleNote = 60 });
            }
            song.Patterns.Add(new TrackerPattern(rows));
            int empty = song.Patterns.Count - 1;
            song.Order = Enumerable.Repeat(empty, parts).ToList();
            song.ChannelOrder = new();
            song.Ym.Repeat = 0;
            for (int place = 0; place < parts; place++)
            {
                song.Patterns.Add(new TrackerPattern(rows));
                int own = song.Patterns.Count - 1;
                song.SetPatternAt(place, 3, own);
                song.Patterns[own][0, 3] = new TrackerCell(60, 33 + place % 3, place == 0 ? '\0' : 'Q', 0, effect2: 'M', param2: place);
            }

            var export = ChiptuneSynth.ExportGhost(song);
            Assert.DoesNotContain(export.Warnings, w => w.Contains("gap"));
            var ghost = export.Data;
            var bin = Assemble(ghost);
            if (bin == null) return;   // no vasm here
            var m = new Machine(bin);
            m.Call(Load);
            uint songAt = Load + (uint)bin.AsSpan().LastIndexOf("GHS1"u8);
            int table = Long(ghost, 12);
            var partStart = Enumerable.Range(0, 3).Select(k => songAt + (uint)Long(ghost, table + k * 16)).ToArray();

            // VBL by VBL, the DMA run in 16 steps a frame to see when it goes on to the next part
            var switches = new List<(double At, uint Start, bool Again)>();
            int vbl = 0, step = 0;
            m.St.Dma.FrameStarted = (start, _, _, _, again) => switches.Add((vbl + step / 16.0, start, again));
            var markers = new Dictionary<int, int>();   // part → the VBL its marker came
            int lastMarker = -1;
            for (vbl = 0; vbl < parts * rows * speed + 20; vbl++)
            {
                m.Cpu.Push32(Return);
                m.Cpu.PC = Load + 8;
                long from = m.Cpu.Cycles;
                for (int i = 0; i < 1_000_000 && m.Cpu.PC != Return; i++) m.Cpu.Step();
                long used = m.Cpu.Cycles - from;
                Assert.InRange(used, 1, FrameCycles / 100);
                for (step = 0; step < 16; step++) m.St.Dma.Run((FrameCycles - used) / 16.0);
                step = 0;
                // (the Microwire word read when it has been sent: the emulated one shifts as the DMA runs)
                int word = m.St.Read16(0xFF8922);
                if ((word & 0x7C0) == (2 << 9 | 3 << 6) && (word & 63) != lastMarker && vbl > 0)
                {
                    lastMarker = word & 63;
                    markers.TryAdd(lastMarker, vbl);
                }
            }

            // The parts in their order, each after the one before without a restart (the DMA going on)
            var started = switches.Where(x => partStart.Contains(x.Start)).ToList();
            var firstRound = started.Take(parts).ToList();
            Assert.Equal(Enumerable.Range(0, parts).Select(k => partStart[k % 3]), firstRound.Select(x => x.Start));
            Assert.All(firstRound.Skip(1), x => Assert.True(x.Again, "a part was started again, with a gap"));
            // Each part's marker in the VBL after it begins (the song waited for it), the last as the first
            for (int k = 1; k < parts; k++)
            {
                Assert.True(markers.ContainsKey(k), $"no marker for part {k}");
                double late = markers[k] - firstRound[k].At;
                Assert.True(late is >= 0 and <= 1.0, $"part {k}: marker at {markers[k]}, part began at {firstRound[k].At}; " +
                    string.Join(", ", Enumerable.Range(1, parts - 1).Select(j => $"{j}:{markers.GetValueOrDefault(j)}/{firstRound[j].At}")));
            }
        }
        finally { foreach (var w in wavs) { File.Delete(w); SampleBank.Forget(w); } }
    }

    [Fact]
    public void The_player_writes_the_YM_as_the_file_says_in_under_1_percent()
    {
        long worst = Check(ChiptuneSynth.ExportGhost(Open()).Data);
        if (worst < 0) return;   // no vasm here
        Assert.InRange(worst, 1, FrameCycles / 100);
    }

    [Fact]
    public void The_player_starts_DMA_samples_and_sends_LMC1992_commands()
    {
        var song = Open();
        string wav = Path.Combine(Path.GetTempPath(), "ghost-player-" + Guid.NewGuid() + ".wav");
        SampleBank.WriteWav(wav, new SampleData(Enumerable.Range(0, 4000).Select(i => (float)Math.Sin(i * 0.1) * 0.5f).ToArray(), 12000));
        try
        {
            song.Instruments.Add(new TrackerInstrument { Name = "Looped", Wave = "Sample", Sample = wav, SampleNote = 60, LoopStart = 1000, LoopEnd = 3000 });
            song.Instruments.Add(new TrackerInstrument { Name = "Once", Wave = "Sample", Sample = wav, SampleNote = 60 });
            int looped = song.Instruments.Count - 1, once = song.Instruments.Count;
            song.Channels = 4;
            song.Patterns.Add(new TrackerPattern(song.Patterns[song.PatternAt(0, 0)].Rows));
            int p = song.Patterns.Count - 1;
            song.SetPatternAt(0, 3, p);
            song.Patterns[p][0, 3] = new TrackerCell(60, looped, 'M', 20, effect2: 'L', param2: 10);
            song.Patterns[p][8, 3] = new TrackerCell(TrackerCell.NoteOff, 0, '\0', 0);
            song.Patterns[p][16, 3] = new TrackerCell(60, once, 'X', 2);
            long worst = Check(ChiptuneSynth.ExportGhost(song).Data);
            if (worst < 0) return;
            Assert.InRange(worst, 1, FrameCycles / 100);
        }
        finally { File.Delete(wav); SampleBank.Forget(wav); }
    }
}
