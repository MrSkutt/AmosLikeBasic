using System;

namespace AmosLikeBasic;

// The YM2149 (and AY-3-8910) sound chip: three square-wave tones, one noise generator and one
// envelope, mixed per channel by register 7. It is run at its own rate (the clock / 8) and the
// output is averaged over each sample, so very high tones and volume tricks sound as they should.
public sealed class Ym2149
{
    private readonly double _ticksPerSample;
    private double _tickLeft;
    private readonly int[] _toneCount = new int[3];
    private readonly int[] _toneOut = new int[3];
    private int _noiseCount, _noiseOut, _rng = 1;
    private int _envCount, _envStep, _envAttack;
    private bool _envHold, _envAlternate, _envHolding;
    private readonly byte[] _r = new byte[16];
    private double _dcIn, _dcOut;   // a gentle high-pass, as the chip's output has a DC level

    // The 32 volume steps of the YM2149, about 1.5 dB apart (a 4-bit volume v is step 2v + 1)
    private static readonly double[] Level = BuildLevels();

    private static double[] BuildLevels()
    {
        var l = new double[32];
        for (int i = 1; i < 32; i++) l[i] = Math.Pow(10, (i - 31) * 1.5 / 20);
        return l;
    }

    public Ym2149(int clock, int sampleRate)
    {
        _ticksPerSample = clock / 8.0 / sampleRate;
    }

    public void Write(int register, byte value)
    {
        if (register is < 0 or > 13) return;
        // Register 13 starts the envelope again; in YM files 0xFF there means "not written"
        if (register == 13)
        {
            if (value == 0xFF) return;
            int shape = value & 15;
            _envAttack = (shape & 4) != 0 ? 31 : 0;
            if ((shape & 8) == 0) { _envHold = true; _envAlternate = _envAttack != 0; }
            else { _envHold = (shape & 1) != 0; _envAlternate = (shape & 2) != 0; }
            _envStep = 31;
            _envHolding = false;
            _envCount = 0;
        }
        _r[register] = value;
    }

    public void WriteFrame(byte[] regs)
    {
        for (int r = 0; r < 14; r++) Write(r, regs[r]);
    }

    private int TonePeriod(int c) => Math.Max(1, _r[c * 2] | (_r[c * 2 + 1] & 15) << 8);

    // One sample, about -1 – 1
    public double Next()
    {
        _tickLeft += _ticksPerSample;
        int ticks = (int)_tickLeft;
        _tickLeft -= ticks;
        if (ticks == 0) ticks = 1;

        int noisePeriod = Math.Max(1, _r[6] & 31) * 2;
        int envPeriod = Math.Max(1, _r[11] | _r[12] << 8);
        int mixer = _r[7];
        double sum = 0;
        for (int t = 0; t < ticks; t++)
        {
            for (int c = 0; c < 3; c++)
                if (++_toneCount[c] >= TonePeriod(c)) { _toneCount[c] = 0; _toneOut[c] ^= 1; }
            if (++_noiseCount >= noisePeriod)
            {
                _noiseCount = 0;
                int bit = (_rng ^ (_rng >> 3)) & 1;
                _rng = (_rng >> 1) | (bit << 16);
                _noiseOut = _rng & 1;
            }
            if (++_envCount >= envPeriod)
            {
                _envCount = 0;
                StepEnvelope();
            }
            int envLevel = _envStep ^ _envAttack;
            for (int c = 0; c < 3; c++)
            {
                bool tone = (mixer & (1 << c)) != 0 || _toneOut[c] != 0;
                bool noise = (mixer & (8 << c)) != 0 || _noiseOut != 0;
                if (!(tone && noise)) continue;
                int v = _r[8 + c];
                sum += (v & 16) != 0 ? Level[envLevel] : Level[(v & 15) == 0 ? 0 : (v & 15) * 2 + 1];
            }
        }
        double x = sum / ticks / 3;
        // Remove the DC level so that the sound sits around 0
        double y = x - _dcIn + 0.995 * _dcOut;
        _dcIn = x;
        _dcOut = y;
        return y * 1.4;
    }

    private void StepEnvelope()
    {
        if (_envHolding) return;
        _envStep--;
        if (_envStep >= 0) return;
        if (_envHold)
        {
            if (_envAlternate) _envAttack ^= 31;
            _envHolding = true;
            _envStep = 0;
        }
        else
        {
            if (_envAlternate) _envAttack ^= 31;
            _envStep = 31;
        }
    }
}

// A YM file played frame by frame through the chip
public sealed class YmPlayer
{
    private readonly YmFile _ym;
    private readonly Ym2149 _chip;
    private readonly double _samplesPerFrame;
    private double _frameLeft;
    private int _frame;
    public bool Loop { get; set; }
    public bool Ended { get; private set; }

    public YmPlayer(YmFile ym, int sampleRate)
    {
        _ym = ym;
        _chip = new Ym2149(ym.Clock, sampleRate);
        _samplesPerFrame = sampleRate / (double)ym.FrameRate;
    }

    // Fills mono samples; after the end (without Loop) the rest is silent
    public void Render(Span<float> dest)
    {
        for (int i = 0; i < dest.Length; i++)
        {
            if (_frameLeft <= 0)
            {
                if (_frame >= _ym.Frames.Length)
                {
                    if (Loop && _ym.Frames.Length > 0) _frame = Math.Clamp(_ym.LoopFrame, 0, _ym.Frames.Length - 1);
                    else
                    {
                        Ended = true;
                        dest[i..].Clear();
                        return;
                    }
                }
                _chip.WriteFrame(_ym.Frames[_frame++]);
                _frameLeft += _samplesPerFrame;
            }
            _frameLeft--;
            dest[i] = (float)_chip.Next();
        }
    }
}
