using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

// Something that plays chip music a block of samples at a time: a YM file, or an SNDH file run live
public interface IChipMusic
{
    void Render(Span<float> dest);
    bool Ended { get; }
}

// The MFP 68901 of the Atari ST: four timers counting a 2.4576 MHz clock through a predivisor, and
// the interrupts they raise (CPU level 6). Music players use timers A and D for SID voices and
// digidrums: the interrupt writes a volume to the sound chip thousands of times a second.
public sealed class Mfp
{
    public const double Clock = 2457600;
    private static readonly int[] Predivisor = { 0, 4, 10, 16, 50, 64, 100, 200 };

    // Registers $FFFA01, $FFFA03 ... by number (0 – 23)
    private readonly byte[] _r = new byte[24];
    private const int Iera = 3, Ierb = 4, Ipra = 5, Iprb = 6, Isra = 7, Isrb = 8, Imra = 9, Imrb = 10, Vr = 11;
    private const int Tacr = 12, Tbcr = 13, Tcdcr = 14, Tadr = 15, Tbdr = 16, Tcdr = 17, Tddr = 18;

    // Timers A – D: what they count down from, where they are, and MFP clocks left to the next step
    private readonly int[] _reload = new int[4], _count = new int[4];
    private readonly double[] _left = new double[4];
    // Each timer's interrupt channel (a higher one has priority): A 13, B 8, C 5, D 4
    private static readonly int[] Channel = { 13, 8, 5, 4 };

    public Mfp()
    {
        _r[0] = 0xFF;   // GPIP: no lines active
        _r[Vr] = 0x40;
        for (int t = 0; t < 4; t++) _reload[t] = _count[t] = 256;
    }

    private int Mode(int timer) => timer switch
    {
        0 => _r[Tacr] & 15,
        1 => _r[Tbcr] & 15,
        2 => (_r[Tcdcr] >> 4) & 7,
        _ => _r[Tcdcr] & 7
    };

    // Only the delay modes (1 – 7) count the clock; event counting and pulse width are not used here
    private int Divide(int timer) => Mode(timer) is >= 1 and <= 7 ? Predivisor[Mode(timer)] : 0;

    public byte Read(int register)
    {
        if (register is >= Tadr and <= Tddr) return (byte)_count[register - Tadr];
        return register < _r.Length ? _r[register] : (byte)0;
    }

    public void Write(int register, byte value)
    {
        if (register >= _r.Length) return;
        switch (register)
        {
            case Ipra: case Iprb: case Isra: case Isrb:
                _r[register] &= value;   // writing 0 clears a bit
                return;
            case Iera: _r[Iera] = value; _r[Ipra] &= value; return;   // a channel turned off loses its request
            case Ierb: _r[Ierb] = value; _r[Iprb] &= value; return;
            case Tadr: case Tbdr: case Tcdr: case Tddr:
            {
                int t = register - Tadr;
                _reload[t] = value == 0 ? 256 : value;
                if (Divide(t) == 0) _count[t] = _reload[t];   // a stopped timer takes the value at once
                _r[register] = value;
                return;
            }
            case Tacr: case Tbcr:
            {
                int t = register - Tacr;
                bool wasStopped = Divide(t) == 0;
                _r[register] = value;
                if (wasStopped && Divide(t) != 0) _left[t] = Divide(t);
                return;
            }
            case Tcdcr:
            {
                bool cStopped = Divide(2) == 0, dStopped = Divide(3) == 0;
                _r[Tcdcr] = value;
                if (cStopped && Divide(2) != 0) _left[2] = Divide(2);
                if (dStopped && Divide(3) != 0) _left[3] = Divide(3);
                return;
            }
        }
        _r[register] = value;
    }

    // Lets the timers run for some MFP clocks
    public void Run(double clocks)
    {
        for (int t = 0; t < 4; t++)
        {
            int divide = Divide(t);
            if (divide == 0) continue;
            _left[t] -= clocks;
            while (_left[t] <= 0)
            {
                _left[t] += divide;
                if (--_count[t] > 0) continue;
                _count[t] = _reload[t];
                Request(Channel[t]);
            }
        }
    }

    // MFP clocks until a timer next reaches 0 (to sleep a stopped CPU until then)
    public double ClocksToNextEvent()
    {
        double next = double.MaxValue;
        for (int t = 0; t < 4; t++)
        {
            int divide = Divide(t);
            if (divide == 0) continue;
            next = Math.Min(next, _left[t] + (double)(_count[t] - 1) * divide);
        }
        return next;
    }

    private void Request(int channel)
    {
        int bit = channel & 7, reg = channel >= 8 ? Ipra : Iprb, enable = channel >= 8 ? Iera : Ierb;
        if ((_r[enable] & (1 << bit)) != 0) _r[reg] |= (byte)(1 << bit);
    }

    // The interrupt to take now, if any: its vector number; marks it in service
    public int? Acknowledge()
    {
        bool softwareEnd = (_r[Vr] & 8) != 0;
        for (int channel = 15; channel >= 0; channel--)
        {
            int bit = channel & 7;
            int pending = channel >= 8 ? Ipra : Iprb, mask = channel >= 8 ? Imra : Imrb, service = channel >= 8 ? Isra : Isrb;
            if (InService(channel)) return null;   // one of this priority or above is still being served
            if ((_r[pending] & _r[mask] & (1 << bit)) == 0) continue;
            _r[pending] &= (byte)~(1 << bit);
            if (softwareEnd) _r[service] |= (byte)(1 << bit);
            return (_r[Vr] & 0xF0) + channel;
        }
        return null;
    }

    // The STe's DMA sound at the end of a frame: timer A counts it in event mode (8), and GPIP 7 raises
    // its interrupt (channel 15), both as players use them to hear when a frame has played
    public void DmaFrameEnded()
    {
        if ((_r[Tacr] & 15) == 8 && --_count[0] <= 0)
        {
            _count[0] = _reload[0];
            Request(Channel[0]);
        }
        Request(15);
    }

    private bool InService(int channel)
    {
        int bit = channel & 7, service = channel >= 8 ? Isra : Isrb;
        return (_r[service] & (1 << bit)) != 0;
    }
}

// The STe's DMA sound: 8-bit samples played straight from memory, between a start and an end address,
// once or round and round, at 6, 12, 25 or 50 kHz, in mono or stereo; and the volume chip (LMC1992),
// set through the Microwire interface, which also says whether the YM is heard alongside
public sealed class SteSound
{
    private static readonly double[] Rates = { 6258, 12517, 25033, 50066 };
    private readonly byte[] _ram;
    private readonly Mfp _mfp;
    private byte _control, _mode;
    private uint _start, _end;              // the registers: the next frame
    private uint _frameEnd, _address;       // the frame playing
    private double _position;               // frames played since the last whole one, 0 – 1
    private ushort _wireData, _wireMask = 0x07FF;
    private double _wireCycles;             // 68000 cycles left of the 16 µs the Microwire shifts for
    private double _master = 1, _left = 1, _right = 1;
    public double YmLevel { get; private set; } = 1;

    public SteSound(byte[] ram, Mfp mfp) { _ram = ram; _mfp = mfp; }

    private bool Playing => (_control & 1) != 0;
    private bool Mono => (_mode & 0x80) != 0;

    public byte Read(uint a) => (a & 0x3F) switch
    {
        0x01 => _control,
        0x03 => (byte)(_start >> 16), 0x05 => (byte)(_start >> 8), 0x07 => (byte)_start,
        0x09 => (byte)(_address >> 16), 0x0B => (byte)(_address >> 8), 0x0D => (byte)_address,
        0x0F => (byte)(_end >> 16), 0x11 => (byte)(_end >> 8), 0x13 => (byte)_end,
        0x21 => _mode,
        0x22 => (byte)(Shifting(_wireData) >> 8), 0x23 => (byte)Shifting(_wireData),
        0x24 => (byte)(Shifting(_wireMask) >> 8), 0x25 => (byte)Shifting(_wireMask),
        _ => 0
    };

    // While the Microwire sends, its data and mask turn round a bit each µs, and are back after 16:
    // players wait for the mask to change and then to come back
    private const double WireCycles = 16 * SndhMachine.CpuClock / 1_000_000;
    private ushort Shifting(ushort v)
    {
        if (_wireCycles <= 0) return v;
        int bits = Math.Clamp(16 - (int)(_wireCycles / (WireCycles / 16)), 1, 15);
        return (ushort)(v << bits | v >> (16 - bits));
    }

    public void Write(uint a, byte v)
    {
        switch (a & 0x3F)
        {
            case 0x01:
                bool was = Playing;
                _control = (byte)(v & 3);
                if (!was && Playing) StartFrame();
                break;
            case 0x03: _start = (_start & 0x00FFFF) | (uint)(v & 0x3F) << 16; break;
            case 0x05: _start = (_start & 0xFF00FF) | (uint)v << 8; break;
            case 0x07: _start = (_start & 0xFFFF00) | (uint)(v & 0xFE); break;
            case 0x0F: _end = (_end & 0x00FFFF) | (uint)(v & 0x3F) << 16; break;
            case 0x11: _end = (_end & 0xFF00FF) | (uint)v << 8; break;
            case 0x13: _end = (_end & 0xFFFF00) | (uint)(v & 0xFE); break;
            case 0x21: _mode = (byte)(v & 0x83); break;
            case 0x22: _wireData = (ushort)(_wireData & 0x00FF | v << 8); break;
            case 0x23: _wireData = (ushort)(_wireData & 0xFF00 | v); Microwire(); _wireCycles = WireCycles; break;
            case 0x24: _wireMask = (ushort)(_wireMask & 0x00FF | v << 8); break;
            case 0x25: _wireMask = (ushort)(_wireMask & 0xFF00 | v); break;
        }
    }

    private void StartFrame()
    {
        _address = _start;
        _frameEnd = _end;
        _position = 0;
    }

    // The bits of the data where the mask has ones, first to last, are the command: 10 (the LMC1992),
    // what to set, and its value; volumes in steps of 2 dB
    private void Microwire()
    {
        int command = 0, bits = 0;
        for (int b = 15; b >= 0; b--)
            if ((_wireMask >> b & 1) != 0) { command = command << 1 | (_wireData >> b & 1); bits++; }
        if (bits < 11) return;
        command &= 0x7FF;
        if (command >> 9 != 2) return;
        int value = command & 63;
        double Db(int steps) => Math.Pow(10, -2.0 * steps / 20);
        switch ((command >> 6) & 7)
        {
            case 0: YmLevel = (value & 3) switch { 0 => Db(6), 1 => 1, _ => 0 }; break;   // mixing: the YM at -12 dB, as it is, or not at all
            case 3: _master = Db(40 - Math.Min(value, 40)); break;         // master volume 0 – 40
            case 4: _right = Db(20 - Math.Min(value, 20)); break;          // right 0 – 20
            case 5: _left = Db(20 - Math.Min(value, 20)); break;           // left 0 – 20
        }
    }

    // Plays for some 68000 cycles: the frame moves on, ends, and starts again or stops
    public void Run(double cycles)
    {
        _wireCycles -= cycles;
        if (!Playing) return;
        _position += cycles * Rates[_mode & 3] / SndhMachine.CpuClock;
        int frameBytes = Mono ? 1 : 2;
        while (_position >= 1 && Playing)
        {
            _position -= 1;
            _address += (uint)frameBytes;
            if (_address < _frameEnd) continue;
            _mfp.DmaFrameEnded();
            if ((_control & 2) != 0) StartFrame();
            else _control &= 0xFE;
        }
    }

    // The sample heard now (both sides together), -1 – 1
    public double Sample()
    {
        if (!Playing || _address >= _frameEnd) return 0;
        uint a = _address & 0x3FFFFF;
        double left = (sbyte)_ram[a] / 128.0;
        if (Mono) return left * _master * (_left + _right) / 2;
        double right = (sbyte)_ram[(a + 1) & 0x3FFFFF] / 128.0;
        return (left * _left + right * _right) / 2 * _master;
    }
}

// Enough of an Atari STe for its music routines: 4 MB of memory, the YM2149 at $FF8800 (an even address
// picks a register, $FF8802 writes it), the DMA sound at $FF8900 and the MFP at $FFFA01.
public sealed class AtariSt : IM68000Bus
{
    public AtariSt() => Dma = new SteSound(Ram, Mfp);

    public readonly byte[] Ram = new byte[4 << 20];
    public readonly byte[] Ym = new byte[16];
    public readonly Mfp Mfp = new();
    public readonly SteSound Dma;
    public bool EnvelopeWritten;            // register 13 was written (which starts the envelope again)
    public Action<int, byte>? YmWritten;     // each write to the sound chip, as it happens
    private int _select;

    public byte Read8(uint a)
    {
        if (a < Ram.Length) return Ram[a];
        if (a is >= 0xFF8800 and < 0xFF8900) return (a & 2) == 0 ? Ym[_select] : (byte)0xFF;
        if (a is >= 0xFFFA00 and < 0xFFFA40) return (a & 1) != 0 ? Mfp.Read((int)(a - 0xFFFA01) / 2) : (byte)0xFF;
        if (a is >= 0xFF8900 and < 0xFF8940) return Dma.Read(a);
        // The keyboard's and MIDI's ACIAs: always ready to send, nothing received (players wait on it)
        if (a is 0xFFFC00 or 0xFFFC04) return 0x02;
        return 0;
    }

    public ushort Read16(uint a) => (ushort)(Read8(a) << 8 | Read8(a + 1));

    public void Write8(uint a, byte v)
    {
        if (a < Ram.Length) { Ram[a] = v; return; }
        if (a is >= 0xFF8800 and < 0xFF8900)
        {
            // The YM sits on the upper half of the data bus, but the 68000 puts a byte it writes on both
            // halves: a byte to $FF8801 or $FF8803 reaches it as well
            if ((a & 2) == 0) _select = v & 15;
            else WriteYm(_select, v);
            return;
        }
        if (a is >= 0xFFFA00 and < 0xFFFA40 && (a & 1) != 0) Mfp.Write((int)(a - 0xFFFA01) / 2, v);
        if (a is >= 0xFF8900 and < 0xFF8940) Dma.Write(a, v);
    }

    // A word reaches the YM as its upper byte only, in one write
    public void Write16(uint a, ushort v)
    {
        if (a is >= 0xFF8800 and < 0xFF8900) { Write8(a, (byte)(v >> 8)); return; }
        Write8(a, (byte)(v >> 8));
        Write8(a + 1, (byte)v);
    }

    public void WriteYm(int register, byte value)
    {
        register &= 15;
        Ym[register] = value;
        if (register == 13) EnvelopeWritten = true;
        YmWritten?.Invoke(register, value);
    }

    public void Load(uint address, byte[] data) => Array.Copy(data, 0, Ram, address, Math.Min(data.Length, Ram.Length - (int)address));
    public uint Read32(uint a) => (uint)Read16(a) << 16 | Read16(a + 2);
    public void Write32(uint a, uint v) { Write16(a, (ushort)(v >> 16)); Write16(a + 2, (ushort)v); }
}

// An SNDH file's player running on the emulated ST: init once with the tune's number, then play called
// as often as the file says, as a timer would call it, while the MFP's timers raise their interrupts in
// between. Time is the 68000's: 8 MHz.
public sealed class SndhMachine
{
    public const double CpuClock = 8_000_000;
    private const uint LoadAddress = 0x10000;
    private const uint Stack = 0xF000;
    private const uint Idle = 0x500;          // STOP, waiting for the next interrupt, and round again
    private const uint Trampoline = 0x520;    // saves the registers, calls play, and returns from the "interrupt"
    private const uint AfterPlay = 0x52A;
    private const uint InitReturn = 0x540;
    private const uint CookieJar = 0x560;
    private const double VblHz = 50;

    public readonly AtariSt St = new();
    public readonly M68000 Cpu;
    public readonly SndhFile Sndh;
    public readonly int Rate;
    private readonly double _cyclesPerCall;
    private double _nextCall;
    private bool _inPlay;
    private uint _heap = 0x200000;
    private long _mfpCycles;                  // the 68000 cycle the MFP's timers have run to
    private double _nextVbl;
    // Dosound (XBIOS 32): the operating system plays a list of register writes, 50 times a second
    private uint _sound;
    private int _soundDelay;
    private byte _soundTemp;

    public SndhMachine(SndhFile sndh, int tune = 0)
    {
        Sndh = sndh;
        if (tune < 1 || tune > sndh.Tunes) tune = sndh.DefaultTune;
        Rate = Math.Clamp(sndh.TimerHz, 1, 1000);
        _cyclesPerCall = CpuClock / Rate;
        Cpu = new M68000(St);
        Cpu.Trap = SystemCall;
        St.Load(LoadAddress, sndh.Data);

        // The cookie jar at $5A0: an STe (_MCH 1.0) with the YM and 8-bit DMA sound (_SND bits 0 and 1),
        // which players for the STe look for before they use its sound
        St.Write32(0x5A0, CookieJar);
        uint jar = CookieJar;
        foreach (var (name, value) in new[] { ("_MCH", 0x00010000u), ("_SND", 3u), ("_CPU", 0u) })
        {
            St.Write32(jar, (uint)(name[0] << 24 | name[1] << 16 | name[2] << 8 | name[3]));
            St.Write32(jar + 4, value);
            jar += 8;
        }
        St.Write32(jar, 0);
        St.Write32(jar + 4, 8);   // room for 8

        // Every vector points at an RTE, so that a stray exception or interrupt returns
        St.Write16(0x408, 0x4E73);
        for (uint v = 2; v < 256; v++) St.Write32(v * 4, 0x408);
        // The idle loop, and the call of play with all the registers kept
        Code(Idle, 0x4E72, 0x2300, 0x60FA);                                        // STOP #$2300; BRA.S Idle
        Code(Trampoline, 0x48E7, 0xFFFE, 0x4EB9,
             (ushort)((LoadAddress + 8) >> 16), (ushort)((LoadAddress + 8) & 0xFFFF), // MOVEM.L D0-A6,-(SP); JSR play
             0x4CDF, 0x7FFF, 0x4E73);                                               // MOVEM.L (SP)+,D0-A6; RTE

        // init, run until it returns (interrupts it starts are taken meanwhile)
        Cpu.SetState(0x2700, Stack - 0x1000, Stack);
        Cpu.D[0] = (uint)tune;
        Cpu.Push32(InitReturn);
        Cpu.PC = LoadAddress;
        for (long i = 0; i < 50_000_000 && Cpu.PC != InitReturn; i++)
        {
            Step();
            if (Cpu.Stopped) break;
        }
        Cpu.PC = Idle;
        Cpu.Stopped = false;
        Cpu.SR = 0x2300;
        _nextCall = Cpu.Cycles;
        _mfpCycles = Cpu.Cycles;
        _nextVbl = Cpu.Cycles + CpuClock / VblHz;
    }

    private void Code(uint address, params ushort[] words)
    {
        for (int i = 0; i < words.Length; i++) St.Write16(address + (uint)(i * 2), words[i]);
    }

    // One instruction (or, when stopped, a jump to the next event), the timers, and their interrupts.
    // The timers run to wherever the 68000's clock is, so the cycles an interrupt takes count too.
    private void Step()
    {
        if (Cpu.Stopped)
        {
            double toTimer = St.Mfp.ClocksToNextEvent() * CpuClock / Mfp.Clock;
            double toCall = _nextCall - Cpu.Cycles;
            Cpu.Cycles += (long)Math.Max(4, Math.Min(Math.Min(toTimer, toCall), 2000));
        }
        else Cpu.Step();
        St.Mfp.Run((Cpu.Cycles - _mfpCycles) * Mfp.Clock / CpuClock);
        St.Dma.Run(Cpu.Cycles - _mfpCycles);
        _mfpCycles = Cpu.Cycles;
        if (Cpu.InterruptMask < 6 && St.Mfp.Acknowledge() is { } vector) Cpu.Interrupt(6, vector);
        if (_inPlay && Cpu.PC == AfterPlay) _inPlay = false;
    }

    // Runs until the 68000's clock reaches a cycle, calling play on time
    public void RunUntil(double cycle)
    {
        int guard = 0;
        while (Cpu.Cycles < cycle && guard++ < 10_000_000)
        {
            if (Cpu.Cycles >= _nextCall)
            {
                _nextCall += _cyclesPerCall;
                if (!_inPlay) CallPlay();
            }
            // What the operating system does every VBL that players count on: the frame counters go up, and
            // a Dosound list plays on (no player in the SNDH archive hooks the VBL itself, and an interrupt
            // there only moves the timing of the others)
            if (Cpu.Cycles >= _nextVbl)
            {
                _nextVbl += CpuClock / VblHz;
                St.Write32(0x466, St.Read32(0x466) + 1);
                St.Write32(0x462, St.Read32(0x462) + 1);
                DosoundTick();
            }
            Step();
        }
    }

    // One step of the Dosound list: registers set, a sweep of one register a step at a time (81), or a
    // wait (82 and above; 0 ends it), as the operating system does it
    private void DosoundTick()
    {
        if (_sound == 0) return;
        if (_soundDelay > 0) { _soundDelay--; return; }
        uint p = _sound;
        for (int guard = 0; guard < 256; guard++)
        {
            byte command = St.Read8(p++);
            if (command < 0x80) St.WriteYm(command & 15, St.Read8(p++));
            else if (command == 0x80) _soundTemp = St.Read8(p++);
            else if (command == 0x81)
            {
                int register = St.Read8(p) & 15;
                sbyte step = (sbyte)St.Read8(p + 1);
                byte last = St.Read8(p + 2);
                St.WriteYm(register, _soundTemp);
                _soundTemp = (byte)(_soundTemp + step);
                if (_soundTemp == last) p += 3;
                else p--;                                    // the same sweep again next time
                break;
            }
            else
            {
                _soundDelay = St.Read8(p++);
                if (_soundDelay == 0) p = 0;
                break;
            }
        }
        _sound = p;
    }

    // As an interrupt: the current place and status pushed, then the trampoline with interrupts allowed
    private void CallPlay()
    {
        _inPlay = true;
        Cpu.Stopped = false;
        ushort sr = Cpu.SR;
        Cpu.SR = (ushort)((sr | 0x2000) & ~0x0700 | 0x0300);
        Cpu.Push32(Cpu.PC);
        Cpu.A[7] -= 2;
        St.Write16(Cpu.A[7], sr);
        Cpu.PC = Trampoline;
    }

    // The few calls to the Atari's operating system that music players make: the stack holds the
    // function number and its arguments
    private bool SystemCall(int trap)
    {
        uint sp = Cpu.A[7];
        int function = St.Read16(sp);
        switch (trap)
        {
            case 1:   // GEMDOS
                switch (function)
                {
                    case 0x20: Cpu.D[0] = 0; break;                                   // Super: already supervisor
                    case 0x48:                                                         // Malloc
                    {
                        uint size = St.Read32(sp + 2);
                        if (size == 0xFFFFFFFF) { Cpu.D[0] = 0x100000; break; }
                        Cpu.D[0] = _heap;
                        _heap = (_heap + size + 15) & ~15u;
                        break;
                    }
                    default: Cpu.D[0] = 0; break;
                }
                return true;
            case 13:  // BIOS
                if (function == 5)                                                     // Setexc
                {
                    int vector = St.Read16(sp + 2);
                    uint address = St.Read32(sp + 4);
                    Cpu.D[0] = St.Read32((uint)vector * 4);
                    if (address != 0xFFFFFFFF) St.Write32((uint)vector * 4, address);
                }
                else Cpu.D[0] = 0;
                return true;
            case 14:  // XBIOS
                switch (function)
                {
                    case 28:                                                           // Giaccess: the YM's registers
                    {
                        int data = St.Read16(sp + 2) & 0xFF, register = St.Read16(sp + 4);
                        if ((register & 0x80) != 0) St.WriteYm(register & 15, (byte)data);
                        Cpu.D[0] = St.Ym[register & 15];
                        break;
                    }
                    case 31:                                                           // Xbtimer: a timer and its routine
                    {
                        int timer = St.Read16(sp + 2) & 3, control = St.Read16(sp + 4) & 0xFF, data = St.Read16(sp + 6) & 0xFF;
                        uint routine = St.Read32(sp + 8);
                        uint[] vectors = { 0x134, 0x120, 0x114, 0x110 };
                        St.Write32(vectors[timer], routine);
                        int[] dataRegister = { 15, 16, 17, 18 };
                        St.Mfp.Write(dataRegister[timer], (byte)data);
                        if (timer < 2) St.Mfp.Write(12 + timer, (byte)control);
                        else
                        {
                            byte cd = St.Mfp.Read(14);
                            St.Mfp.Write(14, timer == 2 ? (byte)((cd & 0x0F) | (control & 7) << 4) : (byte)((cd & 0xF0) | (control & 7)));
                        }
                        // Enable and unmask the timer's interrupt, as the system does
                        int[] channels = { 13, 8, 5, 4 };
                        int channel = channels[timer], bit = 1 << (channel & 7);
                        int enable = channel >= 8 ? 3 : 4, mask = channel >= 8 ? 9 : 10;
                        St.Mfp.Write(enable, (byte)(St.Mfp.Read(enable) | bit));
                        St.Mfp.Write(mask, (byte)(St.Mfp.Read(mask) | bit));
                        break;
                    }
                    case 32:                                                           // Dosound: a list of writes to play
                    {
                        uint list = St.Read32(sp + 2);
                        Cpu.D[0] = _sound;
                        if ((int)list >= 0) { _sound = list & 0xFFFFFF; _soundDelay = 0; }
                        break;
                    }
                    case 38:                                                           // Supexec: call a routine
                    {
                        uint routine = St.Read32(sp + 2);
                        Cpu.Push32(Cpu.PC);
                        Cpu.PC = routine;
                        break;
                    }
                    default: Cpu.D[0] = 0; break;
                }
                return true;
        }
        return false;
    }
}

// An SNDH file played live: the machine runs alongside a YM2149, and every write it makes to the chip
// is heard at the moment it is made (so SID voices and digidrums work)
public sealed class SndhSound : IChipMusic
{
    private readonly SndhMachine _machine;
    private readonly Ym2149 _chip;
    private readonly double _cyclesPerSample;
    private double _cycle;
    private readonly double _endCycle;
    public bool Loop { get; set; }
    public bool Ended { get; private set; }

    public SndhSound(SndhFile sndh, int sampleRate, int tune = 0, int seconds = 0)
    {
        _chip = new Ym2149(2000000, sampleRate);
        _machine = new SndhMachine(sndh, tune);
        _machine.St.YmWritten = (r, v) => _chip.WriteLive(r, v);
        for (int r = 0; r < 14; r++) _chip.WriteLive(r, _machine.St.Ym[r]);
        _cyclesPerSample = SndhMachine.CpuClock / sampleRate;
        _cycle = _machine.Cpu.Cycles;
        if (seconds <= 0) seconds = SndhPlayer.LengthOf(sndh, tune);
        _endCycle = _cycle + seconds * SndhMachine.CpuClock;
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
            _machine.RunUntil(_cycle);
            double ym = _chip.Next();
            var dma = _machine.St.Dma;
            dest[i] = (float)(ym * dma.YmLevel + dma.Sample() * 0.7);
        }
    }
}

// SNDH files as YM frames, for the tracker: the machine runs and the chip's registers are taken at
// every call of play
public static class SndhPlayer
{
    public static int LengthOf(SndhFile sndh, int tune, int otherwise = 180)
    {
        if (tune < 1 || tune > sndh.Tunes) tune = sndh.DefaultTune;
        return sndh.Seconds.Length >= tune && sndh.Seconds[tune - 1] > 0 ? sndh.Seconds[tune - 1] : otherwise;
    }

    // Writes to one register in a frame, more than a player makes in one call: done by a timer interrupt
    private const int TimerWrites = 6;

    // progress: how far it has come, 0 – 1, now and then
    public static YmFile Record(SndhFile sndh, int tune = 0, int seconds = 0, Action<double>? progress = null)
    {
        if (seconds <= 0) seconds = LengthOf(sndh, tune);
        var machine = new SndhMachine(sndh, tune);
        var st = machine.St;
        int frames = seconds * machine.Rate;
        var list = new List<byte[]>(frames);
        // Every write to the sound chip in each frame, to find what timer interrupts did between the frames
        var writes = new List<(int register, byte value)>[frames];
        List<(int, byte)> current = new();
        st.YmWritten = (r, v) => current.Add((r, v));
        double cycle = machine.Cpu.Cycles, perFrame = SndhMachine.CpuClock / machine.Rate;
        for (int f = 0; f < frames; f++)
        {
            if (progress != null && f % Math.Max(1, frames / 100) == 0) progress((double)f / frames);
            current = new List<(int, byte)>();
            writes[f] = current;
            st.EnvelopeWritten = false;
            cycle += perFrame;
            machine.RunUntil(cycle);
            var regs = new byte[16];
            Array.Copy(st.Ym, regs, 14);
            if (!st.EnvelopeWritten) regs[13] = 0xFF;   // not written this frame, as in a YM file
            list.Add(regs);
        }
        var ym = new YmFile
        {
            Frames = list.ToArray(),
            FrameRate = machine.Rate,
            Clock = 2000000,
            Title = sndh.Title,
            Author = sndh.Composer,
            Comment = sndh.Year
        };
        FindTimerEffects(ym, writes);
        return ym;
    }

    // What the timers did between the frames, as the effects of YM6 files, so that the tracker can make
    // them into instruments:
    //  - a channel's volume switched between two values many times a frame: a SID voice;
    //  - its volume written with ever other values: a digidrum, its values the sample (the same sample
    //    played again is the same drum);
    //  - the envelope's shape written many times a frame: a sync buzzer.
    private static void FindTimerEffects(YmFile ym, List<(int register, byte value)>[] writes)
    {
        int frames = writes.Length;
        var effects = new List<YmPlayer.Effect>[frames];
        for (int f = 0; f < frames; f++) effects[f] = new List<YmPlayer.Effect>();
        bool any = false;
        var drumOf = new Dictionary<string, int>();

        for (int c = 0; c < 3; c++)
        {
            List<byte>? drum = null;   // the volumes of a digidrum being played, over its frames
            int drumStart = 0;
            double drumRate = 0;
            for (int f = 0; f <= frames; f++)
            {
                var volumes = new List<byte>();
                if (f < frames)
                    foreach (var (r, v) in writes[f]) if (r == 8 + c) volumes.Add(v);
                bool timer = volumes.Count >= TimerWrites;
                bool toggling = timer && Alternates(volumes);
                if (drum != null && (!timer || toggling))
                {
                    // The drum ends: keep it (once) and mark where it started
                    string key = Convert.ToBase64String(drum.ToArray());
                    if (!drumOf.TryGetValue(key, out int number))
                    {
                        number = ym.Drums.Count;
                        drumOf[key] = number;
                        ym.Drums.Add(drum.Select(v => (float)Ym2149.VolumeLevel(v)).ToArray());
                    }
                    effects[drumStart].Add(new YmPlayer.Effect(YmPlayer.EffectKind.Drum, c, drumRate, number));
                    drum = null;
                    any = true;
                }
                if (f == frames || !timer) continue;
                double rate = volumes.Count * (double)ym.FrameRate;   // writes a second: the timer's frequency
                if (toggling)
                {
                    effects[f].Add(new YmPlayer.Effect(YmPlayer.EffectKind.Sid, c, rate, volumes.Max(v => v & 15)));
                    any = true;
                }
                else
                {
                    if (drum == null) { drum = new List<byte>(); drumStart = f; drumRate = rate; }
                    drum.AddRange(volumes.Select(v => (byte)(v & 15)));
                }
            }
        }
        for (int f = 0; f < frames; f++)
        {
            int shapes = writes[f].Count(w => w.register == 13);
            if (shapes < TimerWrites) continue;
            effects[f].Add(new YmPlayer.Effect(YmPlayer.EffectKind.SyncBuzzer, 0, shapes * (double)ym.FrameRate, 0));
            any = true;
        }
        if (!any) return;
        ym.FrameEffects = effects;
        ym.UsesSpecialEffects = true;
        ym.Version = 6;
    }

    // Switched on and off: every other value is the same (the "off", usually 0) and the ones between are
    // louder (the "on", which may change as the note fades)
    private static bool Alternates(List<byte> values)
    {
        for (int start = 0; start < 2; start++)
        {
            int off = values[start] & 31;
            bool same = true, louder = true;
            for (int i = start; i < values.Count && same; i += 2) same = (values[i] & 31) == off;
            for (int i = 1 - start; i < values.Count && louder; i += 2) louder = (values[i] & 15) > (off & 15);
            if (same && louder) return true;
        }
        return false;
    }
}
