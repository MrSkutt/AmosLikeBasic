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

    private bool InService(int channel)
    {
        int bit = channel & 7, service = channel >= 8 ? Isra : Isrb;
        return (_r[service] & (1 << bit)) != 0;
    }
}

// Enough of an Atari ST for its music routines: 4 MB of memory, the YM2149 at $FF8800 (an even address
// picks a register, $FF8802 writes it) and the MFP at $FFFA01.
public sealed class AtariSt : IM68000Bus
{
    public readonly byte[] Ram = new byte[4 << 20];
    public readonly byte[] Ym = new byte[16];
    public readonly Mfp Mfp = new();
    public bool EnvelopeWritten;            // register 13 was written (which starts the envelope again)
    public Action<int, byte>? YmWritten;     // each write to the sound chip, as it happens
    private int _select;

    public byte Read8(uint a)
    {
        if (a < Ram.Length) return Ram[a];
        if (a is >= 0xFF8800 and < 0xFF8900) return (a & 2) == 0 ? Ym[_select] : (byte)0xFF;
        if (a is >= 0xFFFA00 and < 0xFFFA40) return (a & 1) != 0 ? Mfp.Read((int)(a - 0xFFFA01) / 2) : (byte)0xFF;
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
        if (a is >= 0xFFFA00 and < 0xFFFA40 && (a & 1) != 0) Mfp.Write((int)(a - 0xFFFA01) / 2, v);
    }

    public void Write16(uint a, ushort v) { Write8(a, (byte)(v >> 8)); Write8(a + 1, (byte)v); }

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

    public readonly AtariSt St = new();
    public readonly M68000 Cpu;
    public readonly SndhFile Sndh;
    public readonly int Rate;
    private readonly double _cyclesPerCall;
    private double _nextCall;
    private bool _inPlay;
    private uint _heap = 0x200000;
    private long _mfpCycles;                  // the 68000 cycle the MFP's timers have run to

    public SndhMachine(SndhFile sndh, int tune = 0)
    {
        Sndh = sndh;
        if (tune < 1 || tune > sndh.Tunes) tune = sndh.DefaultTune;
        Rate = Math.Clamp(sndh.TimerHz, 1, 1000);
        _cyclesPerCall = CpuClock / Rate;
        Cpu = new M68000(St);
        Cpu.Trap = SystemCall;
        St.Load(LoadAddress, sndh.Data);

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
            Step();
        }
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
            dest[i] = (float)_chip.Next();
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

    public static YmFile Record(SndhFile sndh, int tune = 0, int seconds = 0)
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
