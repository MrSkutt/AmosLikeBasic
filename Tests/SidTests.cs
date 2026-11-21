using System.Text;

namespace AmosLikeBasic.Tests;

// C64 music: the 6502, the SID chip, SID files played live and read into the tracker
public class SidTests
{
    private sealed class Ram : IMos6502Bus
    {
        public readonly byte[] M = new byte[65536];
        public byte Read(ushort a) => M[a];
        public void Write(ushort a, byte v) => M[a] = v;
    }

    // Runs code put at $0200 until it reaches BRK
    private static (Mos6502 cpu, Ram ram) Run(params byte[] code)
    {
        var ram = new Ram();
        code.CopyTo(ram.M, 0x200);
        var cpu = new Mos6502(ram) { PC = 0x200 };
        for (int i = 0; i < 1000 && ram.M[cpu.PC] != 0x00; i++) cpu.Step();
        return (cpu, ram);
    }

    [Fact]
    public void A_6502_adds_in_decimal_and_runs_the_undocumented_opcodes_players_use()
    {
        // SED; CLC; LDA #$15; ADC #$27 → $42 in decimal
        var (cpu, _) = Run(0xF8, 0x18, 0xA9, 0x15, 0x69, 0x27);
        Assert.Equal(0x42, cpu.A);

        // LAX $10 (A and X from memory); SAX $11 (A AND X); DCP $12 (decrement, then compare with A)
        var ram = new Ram();
        ram.M[0x10] = 0x3C; ram.M[0x12] = 0x3D;
        new byte[] { 0xA7, 0x10, 0x87, 0x11, 0xC7, 0x12 }.CopyTo(ram.M, 0x200);
        var c = new Mos6502(ram) { PC = 0x200 };
        c.Step(); c.Step(); c.Step();
        Assert.Equal((0x3C, 0x3C, 0x3C, 0x3C), (c.A, c.X, ram.M[0x11], ram.M[0x12]));
        Assert.Equal(Mos6502.Z | Mos6502.C, c.P & (Mos6502.Z | Mos6502.C));   // equal after the decrement

        // JMP ($02FF) takes its high byte from $0200, not $0300 (the 6502's bug)
        var bug = new Ram();
        bug.M[0x2FF] = 0x34; bug.M[0x200] = 0x12; bug.M[0x300] = 0x56;
        bug.M[0x400] = 0x6C; bug.M[0x401] = 0xFF; bug.M[0x402] = 0x02;
        var j = new Mos6502(bug) { PC = 0x400 };
        j.Step();
        Assert.Equal(0x1234, j.PC);
    }

    // A tiny PSID tune at $1000: init sets the volume, an envelope and a pulse of 50 %; play strikes
    // A-4 and E-5 in turn every 6 calls (gate off, the pitch, gate on)
    internal static byte[] TinyTune()
    {
        var code = new byte[0x84];
        void At(int address, params int[] bytes) { for (int i = 0; i < bytes.Length; i++) code[address - 0x1000 + i] = (byte)bytes[i]; }
        At(0x1000,
            0xA9, 0x0F, 0x8D, 0x18, 0xD4,       // LDA #$0F; STA $D418  volume
            0xA9, 0x09, 0x8D, 0x05, 0xD4,       // attack 0, decay 9
            0xA9, 0xF0, 0x8D, 0x06, 0xD4,       // sustain 15, release 0
            0xA9, 0x00, 0x8D, 0x02, 0xD4,       // pulse width $800
            0xA9, 0x08, 0x8D, 0x03, 0xD4,
            0xA9, 0x00, 0x8D, 0x00, 0x11,       // the counter and the note
            0x8D, 0x01, 0x11,
            0x60);
        At(0x1030,
            0xCE, 0x00, 0x11,                   // DEC counter
            0x10, 0x24,                         // BPL done
            0xA9, 0x05, 0x8D, 0x00, 0x11,       // the counter from 5
            0xA9, 0x40, 0x8D, 0x04, 0xD4,       // pulse, gate off
            0xAE, 0x01, 0x11,                   // LDX note
            0xBD, 0x80, 0x10, 0x8D, 0x00, 0xD4, // the frequency from the tables
            0xBD, 0x82, 0x10, 0x8D, 0x01, 0xD4,
            0x8A, 0x49, 0x01, 0x8D, 0x01, 0x11, // the other note next time
            0xA9, 0x41, 0x8D, 0x04, 0xD4,       // pulse, gate on
            0x60);                              // done: RTS
        At(0x1080, 0x45, 0xDA, 0x1D, 0x2B);    // 440 Hz ($1D45) and 659 Hz ($2BDA) at the PAL clock

        var header = new byte[0x7C];
        Encoding.ASCII.GetBytes("PSID").CopyTo(header, 0);
        void Word(int at, int w) { header[at] = (byte)(w >> 8); header[at + 1] = (byte)w; }
        Word(4, 2); Word(6, 0x7C); Word(10, 0x1000); Word(12, 0x1030); Word(14, 1); Word(16, 1);
        Encoding.Latin1.GetBytes("Tiny").CopyTo(header, 0x16);
        Encoding.Latin1.GetBytes("Nobody").CopyTo(header, 0x36);
        Word(0x76, 0x14);                       // PAL, 6581
        return header.Concat(new byte[] { 0x00, 0x10 }).Concat(code).ToArray();
    }

    [Fact]
    public void The_header_says_where_the_tune_goes_and_how_it_is_called()
    {
        var sid = SidFile.Read(TinyTune());
        Assert.Equal(("Tiny", "Nobody", false, false), (sid.Title, sid.Author, sid.Rsid, sid.Mos8580));
        Assert.Equal((0x1000, 0x1000, 0x1030, 1), (sid.LoadAddress, sid.InitAddress, sid.PlayAddress, sid.Tunes));
        Assert.True(YmFile.IsChipMusic(TinyTune()));
        Assert.True(YmFile.IsChipMusic(TinyTune()[..17]));   // the tracker tells a file's kind from its first bytes
    }

    [Fact]
    public void A_SID_tune_plays_on_the_C64()
    {
        var sound = new SidSound(SidFile.Read(TinyTune()), 44100, seconds: 1);
        var b = new float[44200];   // a little past its second
        sound.Render(b);
        // The first note (A-4) for the first 6 frames of 20 ms, then E-5
        int Crossings(double t0, double t1)
        {
            int n = 0;
            for (int i = (int)(t0 * 44100) + 1; i < t1 * 44100; i++) if (b[i - 1] < 0 && b[i] >= 0) n++;
            return n;
        }
        Assert.InRange(Crossings(0.02, 0.10), 33, 37);    // 440 Hz × 0.08 s
        Assert.InRange(Crossings(0.14, 0.22), 50, 55);    // 659 Hz
        Assert.True(sound.Ended);
    }

    [Fact]
    public void A_SID_tune_is_read_into_the_tracker_with_its_rows_and_notes()
    {
        var r = YmImport.ImportSid(SidFile.Read(TinyTune()), "tiny", seconds: 4);
        var song = r.Song;
        Assert.Equal(6, song.Speed);                    // a gate every 6 frames
        var p = song.Patterns[song.Order[0]];
        Assert.Equal((69, 76, 69), (p[0, 0].Note, p[1, 0].Note, p[2, 0].Note));
        var inst = song.Instruments[p[0, 0].Instrument - 1];
        Assert.StartsWith("PULSE P50", inst.Table[0]);
        Assert.Contains("Speed 6", r.Report);
    }
}
