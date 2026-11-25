using System.Diagnostics;

namespace AmosLikeBasic.Tests;

// The 68000 player for DHS songs (Atari/DHS/DHSPLAY.S), assembled with vasm in Devpac mode and run on the
// emulated 68000: frame by frame it must leave the YM, the DMA and the Microwire as the file says, keep
// all registers, and take well under 1 % of a 50 Hz frame. Runs only where vasm is found: the environment
// variable VASM (the vasmm68k_mot program) or vasmm68k_mot on the PATH.
public class DhsPlayerTests
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
        while (dir != null && !File.Exists(Path.Combine(dir, "Atari", "DHS", "DHSPLAY.S"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("Atari/DHS"), "Atari", "DHS");
    }

    // The player and the song as one binary: init, stop, play at 0, 4, 8
    private static byte[]? Assemble(byte[] song)
    {
        var vasm = Vasm();
        if (vasm == null) return null;
        var dir = Directory.CreateTempSubdirectory("dhs").FullName;
        try
        {
            File.Copy(Path.Combine(PlayerFolder(), "DHSPLAY.S"), Path.Combine(dir, "DHSPLAY.S"));
            File.WriteAllBytes(Path.Combine(dir, "SONG.DHS"), song);
            File.WriteAllText(Path.Combine(dir, "WRAP.S"),
                "\tbra.w\tinit\n\tbra.w\tdhs_stop\n\tbra.w\tdhs_play\ninit:\tlea\tsong(pc),a0\n\tbra\tdhs_init\n" +
                "\tinclude\t\"DHSPLAY.S\"\n\teven\nsong:\tincbin\t\"SONG.DHS\"\n");
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
    private static long Check(byte[] dhs)
    {
        var bin = Assemble(dhs);
        if (bin == null) return -1;
        var m = new Machine(bin);
        m.Call(Load);
        Assert.Equal(0u, m.Cpu.D[0]);
        uint song = Load + (uint)bin.AsSpan().LastIndexOf("DHS1"u8);
        int pos = Long(dhs, 4), loop = Long(dhs, 8), table = Long(dhs, 12), frames = Long(dhs, 20);
        var regs = new byte[14];
        var starts = new List<(uint Start, uint End, double Rate)>();
        m.St.Dma.FrameStarted = (s, e, rate, stereo, again) => starts.Add((s, e, rate));
        long worst = 0;
        for (int f = 0; f < frames + 100; f++)
        {
            int mask = Word(dhs, pos);
            if (mask == 0xFFFF)
            {
                if (loop == 0) break;
                pos = loop;
                mask = Word(dhs, pos);
            }
            int q = pos + 2;
            int? lmc = null, dma = null;
            if ((mask & 0x8000) != 0) { lmc = Word(dhs, q); q += 2; }
            for (int r = 0; r < 14; r++) if ((mask & 1 << r) != 0) regs[r] = dhs[q++];
            if ((mask & 0x4000) != 0) dma = dhs[q++];
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
                Assert.Equal(song + (uint)Long(dhs, t), starts[0].Start);
                Assert.Equal(song + (uint)Long(dhs, t + 4), starts[0].End);
                Assert.Equal(ChiptuneSynth.DmaRates[dhs[t + 13] & 3], starts[0].Rate);
                if (Long(dhs, t + 8) != 0)
                {
                    // Looped: round and round, the loop's start waiting in the start registers
                    Assert.Equal(3, m.St.Read8(0xFF8901) & 3);
                    uint next = (uint)(m.St.Read8(0xFF8903) << 16 | m.St.Read8(0xFF8905) << 8 | m.St.Read8(0xFF8907));
                    Assert.Equal(song + (uint)Long(dhs, t + 8), next);
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

    [Fact]
    public void The_player_writes_the_YM_as_the_file_says_in_under_1_percent()
    {
        long worst = Check(ChiptuneSynth.ExportDhs(Open()).Data);
        if (worst < 0) return;   // no vasm here
        Assert.InRange(worst, 1, FrameCycles / 100);
    }

    [Fact]
    public void The_player_starts_DMA_samples_and_sends_LMC1992_commands()
    {
        var song = Open();
        string wav = Path.Combine(Path.GetTempPath(), "dhs-player-" + Guid.NewGuid() + ".wav");
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
            long worst = Check(ChiptuneSynth.ExportDhs(song).Data);
            if (worst < 0) return;
            Assert.InRange(worst, 1, FrameCycles / 100);
        }
        finally { File.Delete(wav); SampleBank.Forget(wav); }
    }
}
