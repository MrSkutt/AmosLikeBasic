using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

// A CIA 6526 of the C64: two 16-bit timers counting the system clock (B can count A's underflows
// instead), and the interrupt they raise, CIA 1's as an IRQ, CIA 2's as an NMI. Players use timer A
// to call themselves, and some for samples.
public sealed class Cia
{
    private int _counterA = 0xFFFF, _counterB = 0xFFFF, _latchA = 0xFFFF, _latchB = 0xFFFF;
    private int _cra, _crb, _flags, _mask;
    private readonly byte[] _regs = new byte[16];

    public bool Interrupt => (_flags & _mask) != 0;
    public int LatchA => _latchA;
    public bool TimerAStarted => (_cra & 1) != 0;

    // Timer A running round with its interrupt on, as the C64's operating system leaves it
    public void StartTimerA(int latch, bool interrupt)
    {
        _latchA = _counterA = latch;
        _cra = 1;
        if (interrupt) _mask |= 1;
    }

    public byte Read(int register)
    {
        register &= 15;
        switch (register)
        {
            case 0: case 1: return 0xFF;                        // the ports: no keys, no joystick
            case 4: return (byte)_counterA;
            case 5: return (byte)(_counterA >> 8);
            case 6: return (byte)_counterB;
            case 7: return (byte)(_counterB >> 8);
            case 13:
            {
                int value = _flags | (Interrupt ? 0x80 : 0);
                _flags = 0;                                     // reading acknowledges
                return (byte)value;
            }
            case 14: return (byte)(_cra & ~0x10);
            case 15: return (byte)(_crb & ~0x10);
            default: return _regs[register];
        }
    }

    public void Write(int register, byte value)
    {
        register &= 15;
        _regs[register] = value;
        switch (register)
        {
            case 4: _latchA = (_latchA & 0xFF00) | value; break;
            case 5:
                _latchA = (_latchA & 0x00FF) | value << 8;
                if ((_cra & 1) == 0) _counterA = _latchA;       // a stopped timer takes it at once
                break;
            case 6: _latchB = (_latchB & 0xFF00) | value; break;
            case 7:
                _latchB = (_latchB & 0x00FF) | value << 8;
                if ((_crb & 1) == 0) _counterB = _latchB;
                break;
            case 13:
                if ((value & 0x80) != 0) _mask |= value & 0x1F;
                else _mask &= ~value & 0x1F;
                break;
            case 14:
                _cra = value;
                if ((value & 0x10) != 0) _counterA = _latchA;   // force load
                break;
            case 15:
                _crb = value;
                if ((value & 0x10) != 0) _counterB = _latchB;
                break;
        }
    }

    // Lets the timers run for some cycles
    public void Run(int cycles)
    {
        int underflowsA = 0;
        if ((_cra & 1) != 0 && (_cra & 0x20) == 0)
        {
            _counterA -= cycles;
            while (_counterA < 0)
            {
                underflowsA++;
                _flags |= 1;
                if ((_cra & 8) != 0) { _cra &= ~1; _counterA = _latchA; break; }   // one shot
                _counterA += _latchA + 1;
            }
        }
        if ((_crb & 1) == 0) return;
        int mode = (_crb >> 5) & 3;
        int counts = mode == 0 ? cycles : mode == 2 ? underflowsA : 0;
        if (counts == 0) return;
        _counterB -= counts;
        while (_counterB < 0)
        {
            _flags |= 2;
            if ((_crb & 8) != 0) { _crb &= ~1; _counterB = _latchB; break; }
            _counterB += _latchB + 1;
        }
    }

    // Cycles until timer A next underflows (B is not looked at), to sleep until then
    public int CyclesToNextEvent() =>
        (_cra & 1) != 0 && (_cra & 0x20) == 0 ? _counterA + 1 : int.MaxValue;
}

// The VIC-II, only as far as music needs it: the raster line, and the interrupt at a chosen line
public sealed class Vic
{
    public int CyclesPerLine = 63, Lines = 312;
    private long _cycle;
    private int _line, _compare, _flags, _enable;
    private readonly byte[] _regs = new byte[64];

    public bool Interrupt => (_flags & _enable & 15) != 0;
    public int FrameCycles => CyclesPerLine * Lines;

    public byte Read(int register)
    {
        register &= 63;
        return register switch
        {
            0x11 => (byte)((_regs[0x11] & 0x7F) | (_line & 0x100) >> 1),
            0x12 => (byte)_line,
            0x19 => (byte)(_flags | 0x70 | (Interrupt ? 0x80 : 0)),
            0x1A => (byte)(_enable | 0xF0),
            _ => _regs[register]
        };
    }

    public void Write(int register, byte value)
    {
        register &= 63;
        _regs[register] = value;
        switch (register)
        {
            case 0x11: _compare = (_compare & 0xFF) | (value & 0x80) << 1; break;
            case 0x12: _compare = (_compare & 0x100) | value; break;
            case 0x19: _flags &= ~value & 15; break;            // writing a 1 acknowledges
            case 0x1A: _enable = value & 15; break;
        }
    }

    public void Run(int cycles)
    {
        _cycle += cycles;
        int line = (int)(_cycle / CyclesPerLine % Lines);
        if (line == _line) return;
        // Every line passed is looked at, so a long sleep does not miss the one compared with
        int passed = Math.Min((line - _line + Lines) % Lines, Lines);
        for (int k = 1; k <= passed; k++)
            if ((_line + k) % Lines == _compare) _flags |= 1;
        _line = line;
    }

    public int CyclesToNextEvent()
    {
        int lines = (_compare - _line + Lines - 1) % Lines + 1;
        return (int)(lines * CyclesPerLine - _cycle % CyclesPerLine);
    }
}

// The C64's memory as the 6510 sees it: 64 KB of RAM, the chips at $D000 – $DFFF and a small stand-in
// for the operating system's ROM at $E000, banked in and out by the processor port at $01. The real
// ROMs cannot come with the program, so the stand-in has just what players use: the interrupt entry
// that goes on through the vectors at $0314 and $0318, and the routines that end an interrupt
// ($EA31, $EA81, $FEBC); every other routine in it only returns.
public sealed class C64 : IMos6502Bus
{
    public readonly byte[] Ram = new byte[65536];
    public readonly Cia Cia1 = new(), Cia2 = new();
    public readonly Vic Vic = new();
    public readonly List<(ushort address, SidChip chip)> Sids = new();
    public Action? BeforeSidAccess;              // brings the SID chips up to now
    private readonly byte[] _kernal = new byte[8192];

    public C64()
    {
        Array.Fill(_kernal, (byte)0x60);         // RTS
        void Code(int address, params byte[] code) => code.CopyTo(_kernal, address - 0xE000);
        Code(0xFF48, 0x48, 0x8A, 0x48, 0x98, 0x48, 0xBA, 0xBD, 0x04, 0x01, 0x29, 0x10,   // PHA TXA PHA TYA PHA; TSX; LDA $0104,X; AND #$10
             0xF0, 0x03, 0x6C, 0x16, 0x03, 0x6C, 0x14, 0x03);                          // BEQ +3; JMP ($0316); JMP ($0314)
        Code(0xEA31, 0x4C, 0x7E, 0xEA);                                                // JMP $EA7E
        Code(0xEA7E, 0xAD, 0x0D, 0xDC, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40);           // LDA $DC0D; PLA TAY PLA TAX PLA; RTI
        Code(0xFE43, 0x78, 0x6C, 0x18, 0x03);                                          // SEI; JMP ($0318)
        Code(0xFE47, 0x40);                                                            // RTI
        Code(0xFE66, 0x4C, 0x81, 0xEA);                                                // JMP $EA81
        Code(0xFEBC, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40);
        Code(0xFFFA, 0x43, 0xFE, 0xE2, 0xFC, 0x48, 0xFF);                              // NMI, reset, IRQ
        Ram[0x0314] = 0x31; Ram[0x0315] = 0xEA;
        Ram[0x0316] = 0x66; Ram[0x0317] = 0xFE;
        Ram[0x0318] = 0x47; Ram[0x0319] = 0xFE;
        Ram[0] = 0x2F;
        Ram[1] = 0x37;
    }

    private bool IoVisible => (Ram[1] & 4) != 0 && (Ram[1] & 3) != 0;
    private bool KernalVisible => (Ram[1] & 2) != 0;

    public byte Read(ushort a)
    {
        if (a >= 0xD000 && a < 0xE000 && IoVisible) return ReadIo(a);
        if (a >= 0xE000 && KernalVisible) return _kernal[a - 0xE000];
        return Ram[a];
    }

    public void Write(ushort a, byte v)
    {
        if (a >= 0xD000 && a < 0xE000 && IoVisible) { WriteIo(a, v); return; }
        Ram[a] = v;
    }

    private SidChip? SidAt(ushort a)
    {
        foreach (var (address, chip) in Sids)
            if (address == 0xD400 ? a is >= 0xD400 and < 0xD800 && !OtherSidAt(a) : a >= address && a < address + 0x20) return chip;
        return null;
    }

    // The first SID is mirrored all through $D400 – $D7FF, except where another one sits
    private bool OtherSidAt(ushort a)
    {
        for (int i = 1; i < Sids.Count; i++)
            if (a >= Sids[i].address && a < Sids[i].address + 0x20) return true;
        return false;
    }

    private byte ReadIo(ushort a)
    {
        if (a < 0xD400) return Vic.Read(a);
        if (SidAt(a) is { } sid) { BeforeSidAccess?.Invoke(); return sid.Read(a & 31); }
        if (a < 0xDC00) return a >= 0xD800 ? Ram[a] : (byte)0;
        if (a < 0xDD00) return Cia1.Read(a);
        if (a < 0xDE00) return Cia2.Read(a);
        return 0;
    }

    private void WriteIo(ushort a, byte v)
    {
        if (a < 0xD400) { Vic.Write(a, v); return; }
        if (SidAt(a) is { } sid) { BeforeSidAccess?.Invoke(); sid.Write(a & 31, v); return; }
        if (a < 0xDC00) { if (a >= 0xD800) Ram[a] = v; return; }
        if (a < 0xDD00) { Cia1.Write(a, v); return; }
        if (a < 0xDE00) Cia2.Write(a, v);
    }
}

// A SID tune running on the C64: init is called with the tune's number, then play as often as the
// tune asks (each screen, 50 times a second, or at the rate of CIA 1's timer A), as the PSID players
// do. A tune that installs its own interrupts (RSID, or play at 0) gets them from the timers and the
// raster as on the C64. Between calls the 6510 waits at an address in the unused I/O area.
public sealed class SidMachine
{
    private const ushort Idle = 0xDE00;           // where the 6510 waits
    private const ushort PlayReturn = 0xDE01;     // play returns here, as from an interrupt
    private const ushort InitReturn = 0xDE02;

    public readonly C64 C64 = new();
    public readonly Mos6502 Cpu;
    public readonly SidFile Sid;
    public readonly double Clock;
    public readonly bool CiaSpeed;
    public long SidCycle;                          // how far the SID chips have been run
    private readonly List<SidChip> _chips = new();
    private long _nextPlay;
    private bool _inPlay;
    private long _ioCycle;

    public SidMachine(SidFile sid, double sampleRate, int tune = 0)
    {
        Sid = sid;
        if (tune < 1 || tune > sid.Tunes) tune = sid.StartTune;
        Clock = sid.Ntsc ? SidChip.NtscClock : SidChip.PalClock;
        if (sid.Ntsc) { C64.Vic.CyclesPerLine = 65; C64.Vic.Lines = 263; }
        CiaSpeed = sid.Rsid || (sid.Speed >> Math.Min(tune - 1, 31) & 1) != 0;

        foreach (var address in new[] { (ushort)0xD400 }.Concat(sid.ExtraSids))
        {
            var chip = new SidChip(sampleRate, sid.Mos8580);
            _chips.Add(chip);
            C64.Sids.Add((address, chip));
        }
        C64.BeforeSidAccess = CatchUpSids;
        Cpu = new Mos6502(C64);
        for (int i = 0; i < sid.Data.Length && sid.LoadAddress + i < 0x10000; i++) C64.Ram[sid.LoadAddress + i] = sid.Data[i];

        // As after the C64 starts: CIA 1's timer A at 60 Hz. Its interrupt is on for tunes that bring
        // their own (the operating system's routine at $EA31 handles it otherwise)
        bool ownInterrupts = sid.Rsid || sid.PlayAddress == 0;
        int timer = sid.Ntsc ? 0x4295 : 0x4025;
        C64.Cia1.StartTimerA(timer, ownInterrupts);

        // init, with the memory banked for where it is
        C64.Ram[1] = Bank(sid.Rsid ? (ushort)0 : sid.InitAddress);
        Cpu.S = 0xFF;
        Cpu.Push16(InitReturn - 1);
        Cpu.A = (byte)(tune - 1);
        Cpu.X = Cpu.Y = 0;
        Cpu.P = (byte)(Mos6502.U | (ownInterrupts ? 0 : Mos6502.I));
        Cpu.PC = sid.InitAddress;
        // A tune with a BASIC program needs the C64's BASIC ROM, which is not here: it stays silent
        if (sid.BasicTune) Cpu.PC = InitReturn;
        long limit = Cpu.Cycles + (long)(Clock * 5);
        while (Cpu.PC != InitReturn && Cpu.Cycles < limit && !Cpu.Jammed) Step();
        if (Cpu.PC == InitReturn)
        {
            Cpu.PC = Idle;
            Cpu.P = Mos6502.U;     // interrupts on while it waits
        }
        _nextPlay = Cpu.Cycles;
    }

    // The bank a routine is called with: the operating system's ROM away from where it is
    private static byte Bank(ushort address) =>
        address == 0 || address < 0xA000 ? (byte)0x37 : address < 0xD000 ? (byte)0x36 : address >= 0xE000 ? (byte)0x35 : (byte)0x34;

    private int PlayPeriod =>
        CiaSpeed ? C64.Cia1.LatchA + 1 : C64.Vic.FrameCycles;

    private void CatchUpSids()
    {
        int cycles = (int)(Cpu.Cycles - SidCycle);
        if (cycles <= 0) return;
        foreach (var chip in _chips) chip.Clock(cycles);
        SidCycle = Cpu.Cycles;
    }

    // Runs until the 6510's clock reaches a cycle, calling play on time
    public void RunUntil(long cycle)
    {
        while (Cpu.Cycles < cycle)
        {
            if (Sid.PlayAddress != 0 && !Sid.Rsid && Cpu.Cycles >= _nextPlay)
            {
                _nextPlay += Math.Max(1000, PlayPeriod);
                if (!_inPlay) CallPlay();
            }
            if (Cpu.PC == Idle)
            {
                // Waiting: on to the next thing that can happen
                long next = Math.Min(cycle, Sid.PlayAddress != 0 && !Sid.Rsid ? _nextPlay : long.MaxValue);
                long soon = Cpu.Cycles + Math.Min(Math.Min(C64.Cia1.CyclesToNextEvent(), C64.Cia2.CyclesToNextEvent()), C64.Vic.CyclesToNextEvent());
                Cpu.Cycles = Math.Max(Cpu.Cycles + 1, Math.Min(next, soon));
                RunChips();
                continue;
            }
            if (Cpu.PC == PlayReturn)
            {
                // Back from play, as from an interrupt
                Cpu.P = (byte)((Cpu.Pull() & ~Mos6502.B) | Mos6502.U);
                Cpu.PC = Cpu.Pull16();
                _inPlay = false;
                continue;
            }
            Step();
        }
    }

    private void Step()
    {
        Cpu.Step();
        RunChips();
    }

    private bool _nmiLine;

    private void RunChips()
    {
        int elapsed = (int)(Cpu.Cycles - _ioCycle);
        _ioCycle = Cpu.Cycles;
        C64.Cia1.Run(elapsed);
        C64.Cia2.Run(elapsed);
        C64.Vic.Run(elapsed);
        // The NMI is taken when its line goes active; an IRQ whenever it is and I is clear
        bool nmi = C64.Cia2.Interrupt;
        if (nmi && !_nmiLine) Cpu.Nmi();
        _nmiLine = nmi;
        if (C64.Cia1.Interrupt || C64.Vic.Interrupt) Cpu.Irq();
    }

    // play as an interrupt would call it: the place and flags pushed, then a call that returns to
    // PlayReturn, with the memory banked for it
    private void CallPlay()
    {
        _inPlay = true;
        Cpu.Push16(Cpu.PC);
        Cpu.Push((Cpu.P & ~Mos6502.B) | Mos6502.U);
        Cpu.P |= Mos6502.I;
        Cpu.Push16(PlayReturn - 1);
        C64.Ram[1] = Bank(Sid.PlayAddress);
        Cpu.PC = Sid.PlayAddress;
        Cpu.Jammed = false;
    }
}

// A SID file played live, a sample at a time: the C64 runs alongside, and the SID chips are run up
// to every write it makes, so what it plays between the calls of play is heard too
public sealed class SidSound : IChipMusic
{
    private readonly SidMachine _machine;
    private readonly double _cyclesPerSample;
    private double _cycle;
    private readonly double _endCycle;
    private readonly List<SidChip> _chips = new();
    public bool Loop { get; set; }
    public bool Ended { get; private set; }

    public SidSound(SidFile sid, int sampleRate, int tune = 0, int seconds = 0)
    {
        _machine = new SidMachine(sid, sampleRate, tune);
        foreach (var (_, chip) in _machine.C64.Sids) _chips.Add(chip);
        _cyclesPerSample = _machine.Clock / sampleRate;
        _cycle = _machine.Cpu.Cycles;
        _machine.SidCycle = _machine.Cpu.Cycles;
        if (tune < 1 || tune > sid.Tunes) tune = sid.StartTune;
        if (seconds <= 0) seconds = sid.Seconds.Length >= tune && sid.Seconds[tune - 1] > 0 ? sid.Seconds[tune - 1] : 180;
        _endCycle = _cycle + seconds * _machine.Clock;
    }

    public void Render(Span<float> dest)
    {
        for (int i = 0; i < dest.Length; i++)
        {
            if (!Loop && _cycle >= _endCycle)
            {
                Ended = true;
                dest[i..].Clear();
                return;
            }
            _cycle += _cyclesPerSample;
            _machine.RunUntil((long)_cycle);
            int cycles = (int)(_machine.Cpu.Cycles - _machine.SidCycle);
            double sum = 0;
            foreach (var chip in _chips)
            {
                if (cycles > 0) chip.Clock(cycles);
                sum += chip.Output();
            }
            if (cycles > 0) _machine.SidCycle = _machine.Cpu.Cycles;
            dest[i] = (float)(sum / Math.Sqrt(_chips.Count));
        }
    }
}
