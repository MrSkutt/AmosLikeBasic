using System;
using System.Collections.Generic;

namespace AmosLikeBasic;

// Enough of an Atari ST for its music routines: 4 MB of memory, the YM2149 at $FF8800 (an even address
// picks a register, $FF8802 writes it) and the MFP's registers at $FFFA00 (kept, not run yet).
public sealed class AtariSt : IM68000Bus
{
    public readonly byte[] Ram = new byte[4 << 20];
    public readonly byte[] Ym = new byte[16];
    public bool EnvelopeWritten;            // register 13 was written (which starts the envelope again)
    private int _select;
    private readonly byte[] _mfp = new byte[0x40];

    public byte Read8(uint a)
    {
        if (a < Ram.Length) return Ram[a];
        if (a is >= 0xFF8800 and < 0xFF8900) return (a & 2) == 0 ? Ym[_select] : (byte)0xFF;
        if (a is >= 0xFFFA00 and < 0xFFFA40) return _mfp[a - 0xFFFA00];
        return 0;
    }

    public ushort Read16(uint a) => (ushort)(Read8(a) << 8 | Read8(a + 1));

    public void Write8(uint a, byte v)
    {
        if (a < Ram.Length) { Ram[a] = v; return; }
        if (a is >= 0xFF8800 and < 0xFF8900)
        {
            if ((a & 1) != 0) return;   // the YM sits on the upper half of the data bus
            if ((a & 2) == 0) _select = v & 15;
            else WriteYm(_select, v);
            return;
        }
        if (a is >= 0xFFFA00 and < 0xFFFA40) _mfp[a - 0xFFFA00] = v;
    }

    public void Write16(uint a, ushort v) { Write8(a, (byte)(v >> 8)); Write8(a + 1, (byte)v); }

    public void WriteYm(int register, byte value)
    {
        register &= 15;
        Ym[register] = value;
        if (register == 13) EnvelopeWritten = true;
    }

    public void Load(uint address, byte[] data) => Array.Copy(data, 0, Ram, address, Math.Min(data.Length, Ram.Length - (int)address));
    public uint Read32(uint a) => (uint)Read16(a) << 16 | Read16(a + 2);
    public void Write32(uint a, uint v) { Write16(a, (ushort)(v >> 16)); Write16(a + 2, (ushort)v); }
}

// Runs an SNDH file's player on the emulated ST and records what it writes to the sound chip, frame by
// frame, as a YM file: init is called once with the tune's number, then play as often as the file
// says (50 times a second for most). The rest of the program (MUSIC PLAY, the tracker) takes it from
// there.
public static class SndhPlayer
{
    private const uint LoadAddress = 0x10000;
    private const uint Stack = 0xF000;
    private const uint ReturnAddress = 0x400;      // where init and play return to: the run stops there
    private const long MaxInitSteps = 50_000_000, MaxPlaySteps = 2_000_000;

    // seconds: how long to record (the file's own length when it gives one, otherwise this)
    public static YmFile Record(SndhFile sndh, int tune = 0, int seconds = 180)
    {
        if (tune < 1 || tune > sndh.Tunes) tune = sndh.DefaultTune;
        if (sndh.Seconds.Length >= tune && sndh.Seconds[tune - 1] > 0) seconds = sndh.Seconds[tune - 1];

        var st = new AtariSt();
        st.Load(LoadAddress, sndh.Data);
        var cpu = new M68000(st);
        uint heap = 0x200000;
        cpu.Trap = n => SystemCall(cpu, st, n, ref heap);
        cpu.SetState(0x2700, Stack - 0x1000, Stack);

        // Every vector points at an RTE, so that a stray exception or interrupt returns
        st.Write16(0x408, 0x4E73);
        for (uint v = 2; v < 64; v++) st.Write32(v * 4, 0x408);
        // The 200 Hz counter some players read, and the hardware Atari ROM addresses they might poke
        Call(cpu, LoadAddress, MaxInitSteps, d0: (uint)tune);

        int rate = Math.Clamp(sndh.TimerHz, 1, 1000);
        int frames = seconds * rate;
        var list = new List<byte[]>(frames);
        for (int f = 0; f < frames; f++)
        {
            st.EnvelopeWritten = false;
            Call(cpu, LoadAddress + 8, MaxPlaySteps);
            var regs = new byte[16];
            Array.Copy(st.Ym, regs, 14);
            if (!st.EnvelopeWritten) regs[13] = 0xFF;   // not written this frame, as in a YM file
            list.Add(regs);
            // The 200 Hz system counter goes on
            st.Write32(0x4BA, st.Read32(0x4BA) + (uint)(200 / rate));
        }
        return new YmFile
        {
            Frames = list.ToArray(),
            FrameRate = rate,
            Clock = 2000000,
            Title = sndh.Title,
            Author = sndh.Composer,
            Comment = sndh.Year.Length > 0 ? sndh.Year : ""
        };
    }

    // Calls a routine as with JSR and runs it until it returns (or takes far too long)
    private static void Call(M68000 cpu, uint address, long maxSteps, uint? d0 = null)
    {
        if (d0 is { } v) cpu.D[0] = v;
        cpu.A[7] = Stack;
        cpu.Push32(ReturnAddress);
        cpu.PC = address;
        cpu.Stopped = false;
        for (long i = 0; i < maxSteps && cpu.PC != ReturnAddress; i++)
        {
            cpu.Step();
            if (cpu.Stopped) break;
        }
    }

    // The few calls to the Atari's operating system that music players make: the stack holds the
    // function number and its arguments
    private static bool SystemCall(M68000 cpu, AtariSt st, int trap, ref uint heap)
    {
        uint sp = cpu.A[7];
        int function = st.Read16(sp);
        switch (trap)
        {
            case 1:   // GEMDOS
                switch (function)
                {
                    case 0x20: cpu.D[0] = 0; break;                                   // Super: already supervisor
                    case 0x48:                                                         // Malloc
                    {
                        uint size = st.Read32(sp + 2);
                        if (size == 0xFFFFFFFF) { cpu.D[0] = 0x100000; break; }
                        cpu.D[0] = heap;
                        heap = (heap + size + 15) & ~15u;
                        break;
                    }
                    default: cpu.D[0] = 0; break;
                }
                return true;
            case 13:  // BIOS
                if (function == 5)                                                     // Setexc
                {
                    int vector = st.Read16(sp + 2);
                    uint address = st.Read32(sp + 4);
                    cpu.D[0] = st.Read32((uint)vector * 4);
                    if (address != 0xFFFFFFFF) st.Write32((uint)vector * 4, address);
                }
                else cpu.D[0] = 0;
                return true;
            case 14:  // XBIOS
                switch (function)
                {
                    case 28:                                                           // Giaccess: the YM's registers
                    {
                        int data = st.Read16(sp + 2) & 0xFF, register = st.Read16(sp + 4);
                        if ((register & 0x80) != 0) st.WriteYm(register & 15, (byte)data);
                        cpu.D[0] = st.Ym[register & 15];
                        break;
                    }
                    case 38:                                                           // Supexec: call a routine
                    {
                        uint routine = st.Read32(sp + 2);
                        cpu.Push32(cpu.PC);
                        cpu.PC = routine;
                        break;
                    }
                    default: cpu.D[0] = 0; break;
                }
                return true;
        }
        return false;
    }
}
