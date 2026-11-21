using System;

namespace AmosLikeBasic;

// The SID (MOS 6581, or the newer 8580) of the Commodore 64, clocked a cycle at a time as the C64
// runs it: three voices, each a 24-bit oscillator with triangle, saw, pulse and noise waves (ring
// modulation and hard sync from the voice before it) and an ADSR envelope counting as the chip's
// does; a filter (low, band and high pass with resonance) the voices can be routed through; and the
// main volume, which on the 6581 also moves the output's DC level, so that writing it fast plays
// samples ("volume register digis"). Run with Clock() up to the moment of each write; Output() then
// gives the sound since the last call, averaged, as one sample.
public sealed class SidChip
{
    public const double PalClock = 985248, NtscClock = 1022727;

    // Cycles between envelope steps for each of the 16 rates
    private static readonly int[] RatePeriod = { 9, 32, 63, 95, 149, 220, 267, 313, 392, 977, 1954, 3126, 3907, 11720, 19532, 31251 };

    private sealed class Voice
    {
        public uint Acc;                  // 24 bits
        public int Freq, PulseWidth;      // 16 and 12 bits
        public int Control, AttackDecay, SustainRelease;
        public uint Noise = 0x7FFFF8;     // the 23-bit noise shift register
        public bool MsbRose;              // the oscillator's top bit went up this cycle (for sync)
        // The envelope
        public int Env, RateCounter, ExpCounter;
        public int State;                 // 0 attack, 1 decay and sustain, 2 release
        public bool Gate;
        public int GateOns;               // times the gate went on since the last look (a note struck)
    }

    private readonly Voice[] _v = { new(), new(), new() };
    private readonly byte[] _regs = new byte[32];
    private readonly bool _mos8580;
    private readonly double _sampleRate;
    private int _cutoff, _resonance, _route, _mode, _volume;
    private byte _bus;                    // the last value written: write-only registers read as it

    // The sound since the last Output(): the filtered voices and the rest (both times the volume)
    private double _sumFiltered, _sumDirect, _sumVolume;
    private int _cycles;
    private double _low, _band, _dcIn, _dcOut;

    public SidChip(double sampleRate, bool mos8580 = false)
    {
        _sampleRate = sampleRate;
        _mos8580 = mos8580;
    }

    public byte Read(int register)
    {
        register &= 31;
        var v3 = _v[2];
        return register switch
        {
            0x19 or 0x1A => 0xFF,                                    // the paddles
            0x1B => (byte)(Wave(v3, _v[1]) >> 4),                    // voice 3's wave, top 8 bits
            0x1C => (byte)v3.Env,                                    // and its envelope
            _ => _bus
        };
    }

    public void Write(int register, byte value)
    {
        register &= 31;
        _bus = value;
        _regs[register] = value;
        if (register < 21)
        {
            var v = _v[register / 7];
            switch (register % 7)
            {
                case 0: v.Freq = (v.Freq & 0xFF00) | value; break;
                case 1: v.Freq = (v.Freq & 0x00FF) | value << 8; break;
                case 2: v.PulseWidth = (v.PulseWidth & 0xF00) | value; break;
                case 3: v.PulseWidth = (v.PulseWidth & 0x0FF) | (value & 15) << 8; break;
                case 4:
                    bool gate = (value & 1) != 0;
                    if (gate && !v.Gate) { v.State = 0; v.GateOns++; }   // attack
                    else if (!gate && v.Gate) v.State = 2;           // release
                    v.Gate = gate;
                    if ((value & 8) != 0) { v.Acc = 0; v.Noise = 0x7FFFF8; }   // test bit
                    v.Control = value;
                    break;
                case 5: v.AttackDecay = value; break;
                case 6: v.SustainRelease = value; break;
            }
            return;
        }
        switch (register)
        {
            case 0x15: _cutoff = (_cutoff & 0x7F8) | (value & 7); break;
            case 0x16: _cutoff = (_cutoff & 7) | value << 3; break;
            case 0x17: _resonance = value >> 4; _route = value & 15; break;
            case 0x18: _mode = value >> 4; _volume = value & 15; break;
        }
    }

    // For reading a tune into the tracker: a register as last written, a voice's envelope (0 – 255),
    // and how often its gate went on since the last time this was asked
    public byte Register(int register) => _regs[register & 31];
    public int Envelope(int voice) => _v[voice].Env;
    public int TakeGateOns(int voice)
    {
        int n = _v[voice].GateOns;
        _v[voice].GateOns = 0;
        return n;
    }

    // Runs the chip for some cycles, adding up what it plays
    public void Clock(int cycles)
    {
        for (int n = 0; n < cycles; n++)
        {
            for (int i = 0; i < 3; i++)
            {
                var v = _v[i];
                if ((v.Control & 8) != 0) { v.MsbRose = false; }
                else
                {
                    uint before = v.Acc;
                    v.Acc = (v.Acc + (uint)v.Freq) & 0xFFFFFF;
                    v.MsbRose = (before & 0x800000) == 0 && (v.Acc & 0x800000) != 0;
                    // The noise register moves on when bit 19 goes up
                    if ((before & 0x080000) == 0 && (v.Acc & 0x080000) != 0)
                        v.Noise = ((v.Noise << 1) | ((v.Noise >> 22) ^ (v.Noise >> 17)) & 1) & 0x7FFFFF;
                }
                Envelope(v);
            }
            // Hard sync: a voice starts again when the one before it wraps round
            for (int i = 0; i < 3; i++)
                if ((_v[i].Control & 2) != 0 && _v[(i + 2) % 3].MsbRose) _v[i].Acc = 0;

            double filtered = 0, direct = 0;
            for (int i = 0; i < 3; i++)
            {
                var v = _v[i];
                double s = (Wave(v, _v[(i + 2) % 3]) - 0x800) * v.Env;
                if ((_route & (1 << i)) != 0) filtered += s;
                else if (i < 2 || (_mode & 8) == 0) direct += s;     // 3OFF mutes voice 3 unless it is filtered
            }
            _sumFiltered += filtered;
            _sumDirect += direct;
            _sumVolume += _volume;
        }
        _cycles += cycles;
    }

    // A voice's wave, 12 bits: the waves chosen are ANDed together, as the chip mixes them
    private static int Wave(Voice v, Voice before)
    {
        int waves = v.Control >> 4;
        if (waves == 0) return 0x800;
        int output = 0xFFF;
        if ((waves & 1) != 0)
        {
            // Triangle: the accumulator folded; ring modulation flips it with the voice before's top bit
            uint msb = ((v.Control & 4) != 0 ? v.Acc ^ before.Acc : v.Acc) & 0x800000;
            output &= (int)(((msb != 0 ? ~v.Acc : v.Acc) >> 11) & 0xFFF);
        }
        if ((waves & 2) != 0) output &= (int)(v.Acc >> 12);
        if ((waves & 4) != 0) output &= (v.Control & 8) != 0 || (v.Acc >> 12) >= v.PulseWidth ? 0xFFF : 0;
        if ((waves & 8) != 0)
        {
            uint s = v.Noise;
            output &= (int)(((s & 0x400000) >> 11) | ((s & 0x100000) >> 10) | ((s & 0x010000) >> 7) | ((s & 0x002000) >> 5)
                | ((s & 0x000800) >> 4) | ((s & 0x000080) >> 1) | ((s & 0x000010) << 1) | ((s & 0x000004) << 2));
        }
        return output;
    }

    // One cycle of a voice's envelope: a step up (attack) or down at its rate, the steps down slower
    // the lower it is, as the chip's exponential counter makes them
    private static void Envelope(Voice v)
    {
        int rate = v.State switch { 0 => v.AttackDecay >> 4, 1 => v.AttackDecay & 15, _ => v.SustainRelease & 15 };
        if (++v.RateCounter < RatePeriod[rate]) return;
        v.RateCounter = 0;
        if (v.State == 0)
        {
            v.ExpCounter = 0;
            if (++v.Env >= 255) { v.Env = 255; v.State = 1; }
            return;
        }
        int expPeriod = v.Env > 0x5D ? 1 : v.Env > 0x36 ? 2 : v.Env > 0x1A ? 4 : v.Env > 0x0E ? 8 : v.Env > 0x06 ? 16 : 30;
        if (++v.ExpCounter < expPeriod) return;
        v.ExpCounter = 0;
        if (v.State == 1)
        {
            if (v.Env > (v.SustainRelease >> 4) * 0x11) v.Env--;
        }
        else if (v.Env > 0) v.Env--;
    }

    // The filter's cutoff in Hz (the 8580's is nearly straight, the 6581's curves up), and its resonance 0 – 15
    public double CutoffHz => _mos8580 ? 30 + _cutoff * (12000.0 / 2047) : 200 + 9800 * Math.Pow(_cutoff / 2047.0, 1.4);
    public int ResonanceSetting => _resonance;

    // The filter's Q: 0.707 with no resonance, up to about 3 (the 8580 less)
    public double FilterQ => 0.707 + _resonance * (_mos8580 ? 0.11 : 0.15);

    // The sound since the last call as one sample, about -1 – 1
    public double Output()
    {
        if (_cycles == 0) return 0;
        double filteredIn = _sumFiltered / _cycles, direct = _sumDirect / _cycles, volume = _sumVolume / _cycles / 15;
        _sumFiltered = _sumDirect = _sumVolume = 0;
        _cycles = 0;

        // The filter: a state-variable filter (in its stable "trapezoid" form) at the cutoff's frequency
        double fc = Math.Min(CutoffHz, _sampleRate * 0.45);
        double g = Math.Tan(Math.PI * fc / _sampleRate);
        double k = 1 / FilterQ;
        double a1 = 1 / (1 + g * (g + k)), a2 = g * a1, a3 = g * a2;
        double v3 = filteredIn - _low;
        double v1 = a1 * _band + a2 * v3;
        double v2 = _low + a2 * _band + a3 * v3;
        _band = 2 * v1 - _band;
        _low = 2 * v2 - _low;
        double high = filteredIn - k * v1 - v2;
        double filtered = ((_mode & 1) != 0 ? v2 : 0) + ((_mode & 2) != 0 ? v1 : 0) + ((_mode & 4) != 0 ? high : 0);

        // On the 6581 the volume also scales a DC level, which is what makes volume samples heard
        double bias = _mos8580 ? 0.05 * 0x800 * 255 : 0x800 * 255;
        double x = (filtered + direct + bias) * volume / (3.0 * 0x800 * 255);
        double y = x - _dcIn + 0.998 * _dcOut;
        _dcIn = x;
        _dcOut = y;
        return y * 0.55;
    }
}
