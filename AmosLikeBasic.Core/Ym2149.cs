using System;
using System.Collections.Generic;

namespace AmosLikeBasic;

// The YM2149 (and AY-3-8910) sound chip: three square-wave tones, one noise generator and one
// envelope, mixed per channel by register 7. It is run at its own rate (the clock / 8) and the
// output is averaged over each sample, so very high tones and volume tricks sound as they should.
// The Atari's tricks with a timer (YM5/YM6 effects) are here too: SID voice (the volume switched on
// and off), sinus SID, digidrums (a sample written to the volume) and sync buzzer (the envelope
// started again and again).
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
    private readonly double _clockTicks;   // chip ticks a second (the clock / 8)

    // Timer effects per channel: the timer's step per chip tick and where it is (0 – 1)
    private readonly double[] _sidStep = new double[3], _sidPhase = new double[3];
    private readonly int[] _sidVolume = new int[3];
    private readonly bool[] _sidSine = new bool[3];
    private readonly int[]?[] _sidLevels = new int[]?[3];   // a SID's own steps (maxYMiser's), or null for on / off
    private readonly double[] _sidDuty = new double[3];
    private readonly float[]?[] _drum = new float[]?[3];
    private readonly double[] _drumPos = new double[3], _drumStep = new double[3];
    private double _syncStep, _syncPhase;
    private int[]? _syncShapes;   // the shapes a sync buzzer writes in turn (maxYMiser's), or null: the same again
    private int _syncIndex;

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
        _clockTicks = clock / 8.0;
        _ticksPerSample = _clockTicks / sampleRate;
        // As after a reset: envelope shape 0, which falls once to silence and stays there
        Write(13, 0);
        _envStep = 0;
        _envHolding = true;
    }

    public static double VolumeLevel(int volume) => Level[(volume & 15) == 0 ? 0 : (volume & 15) * 2 + 1];

    // ── The timer effects (frequencies in Hz; a volume is 0 – 15) ──

    // The timer switches the volume on or off each time it runs, so the sound is half its frequency
    public void StartSid(int channel, double frequency, int volume, bool sine = false)
    {
        _sidStep[channel] = frequency / 2 / _clockTicks;
        _sidVolume[channel] = volume & 15;
        _sidSine[channel] = sine;
        _sidLevels[channel] = null;
    }

    // A SID that writes its own steps, the volumes 0 – 15 one after another, a round of them `frequency`
    // times a second. Two steps take `duty` and the rest of the round (a PWM); more take equal parts.
    // Started again only when the rate or the steps change, so that it runs on through the frames.
    public void StartSidSteps(int channel, double frequency, int[] levels, double duty = 0.5)
    {
        double step = frequency / _clockTicks;
        var old = _sidLevels[channel];
        bool same = old != null && old.AsSpan().SequenceEqual(levels);
        _sidStep[channel] = step;
        _sidLevels[channel] = levels;
        _sidDuty[channel] = Math.Clamp(duty, 0, 1);
        _sidSine[channel] = false;
        if (!same) _sidPhase[channel] = 0;
    }

    public void StopSid(int channel) { _sidStep[channel] = 0; _sidLevels[channel] = null; }

    public void StartDrum(int channel, float[] sample, double frequency)
    {
        _drum[channel] = sample;
        _drumPos[channel] = 0;
        _drumStep[channel] = frequency / _clockTicks;
    }

    public void StartSyncBuzzer(double frequency) { _syncStep = frequency / _clockTicks; _syncPhase = 0; _syncShapes = null; }

    // A sync buzzer that writes its own shapes, one after another, `frequency` times a second (each
    // write starts the envelope). It runs on through the frames while the shapes stay the same.
    public void StartSyncBuzzer(double frequency, int[] shapes)
    {
        bool same = _syncStep > 0 && _syncShapes != null && _syncShapes.AsSpan().SequenceEqual(shapes);
        _syncStep = frequency / _clockTicks;
        _syncShapes = shapes;
        if (!same) { _syncPhase = 0; _syncIndex = 0; }
    }

    public void StopSyncBuzzer() { _syncStep = 0; _syncShapes = null; }

    private void RestartEnvelope()
    {
        if (_syncShapes is { Length: > 0 } shapes)
        {
            Write(13, (byte)(shapes[_syncIndex] & 15));
            _syncIndex = (_syncIndex + 1) % shapes.Length;
        }
        else Write(13, (byte)(_r[13] & 15));
    }

    // A register from a YM file, where 0xFF in register 13 means "not written this frame"
    public void Write(int register, byte value)
    {
        if (register == 13 && value == 0xFF) return;
        WriteLive(register, value);
    }

    // A register written by a program as it runs: every write to register 13 starts the envelope again
    public void WriteLive(int register, byte value)
    {
        if (register is < 0 or > 13) return;
        if (register == 13)
        {
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
            if (_syncStep > 0 && (_syncPhase += _syncStep) >= 1)
            {
                _syncPhase -= 1;
                RestartEnvelope();
            }
            int envLevel = _envStep ^ _envAttack;
            for (int c = 0; c < 3; c++)
            {
                // A digidrum is the channel's volume, whatever the mixer says
                if (_drum[c] is { } drum)
                {
                    int i = (int)_drumPos[c];
                    if (i < drum.Length)
                    {
                        sum += drum[i];
                        _drumPos[c] += _drumStep[c];
                        continue;
                    }
                    _drum[c] = null;
                }
                bool tone = (mixer & (1 << c)) != 0 || _toneOut[c] != 0;
                bool noise = (mixer & (8 << c)) != 0 || _noiseOut != 0;
                double level;
                if (_sidStep[c] > 0)
                {
                    // SID voice: the volume on in the timer's first half and off in the other (or a sine)
                    _sidPhase[c] += _sidStep[c];
                    if (_sidPhase[c] >= 1) _sidPhase[c] -= 1;
                    if (_sidLevels[c] is { } steps)
                    {
                        int k = steps.Length == 2 ? (_sidPhase[c] < _sidDuty[c] ? 0 : 1)
                            : Math.Min(steps.Length - 1, (int)(_sidPhase[c] * steps.Length));
                        level = VolumeLevel(steps[k]);
                    }
                    else
                        level = VolumeLevel(_sidVolume[c]) * (_sidSine[c]
                            ? 0.5 - 0.5 * Math.Cos(_sidPhase[c] * 2 * Math.PI)
                            : _sidPhase[c] < 0.5 ? 1 : 0);
                }
                else
                {
                    int v = _r[8 + c];
                    level = (v & 16) != 0 ? Level[envLevel] : VolumeLevel(v);
                }
                if (tone && noise) sum += level;
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
public sealed class YmPlayer : IChipMusic
{
    // The Atari's MFP timer: 2.4576 MHz, divided by a predivisor (from three bits) and a count
    private const double MfpClock = 2457600;
    private static readonly int[] Predivisor = { 0, 4, 10, 16, 50, 64, 100, 200 };

    private static double TimerFrequency(int predivisorBits, int count)
    {
        int divide = Predivisor[predivisorBits & 7] * count;
        return divide > 0 ? MfpClock / divide : 0;
    }

    public enum EffectKind { Sid, SineSid, Drum, SyncBuzzer }

    // An effect a frame asks for: on which channel, at what frequency, and the volume (SID) or the
    // digidrum's number
    public readonly record struct Effect(EffectKind Kind, int Channel, double Frequency, int Value);

    // The effects of a frame: in YM5 the SID voice (register 1) and a digidrum (register 3), in YM6
    // two effects of any kind, described by registers 1 and 3
    public static List<Effect> Effects(YmFile ym, int frame)
    {
        if (ym.FrameEffects != null) return frame < ym.FrameEffects.Length ? ym.FrameEffects[frame] : new List<Effect>();
        var r = ym.Frames[frame];
        var list = new List<Effect>(2);
        if (ym.Version == 5)
        {
            int sid = (r[1] >> 4) & 3;
            if (sid > 0) list.Add(new Effect(EffectKind.Sid, sid - 1, TimerFrequency(r[6] >> 5, r[14]), r[8 + sid - 1] & 15));
            int drum = (r[3] >> 4) & 3;
            if (drum > 0) list.Add(new Effect(EffectKind.Drum, drum - 1, TimerFrequency(r[8] >> 5, r[15]), r[8 + drum - 1] & 31));
        }
        else if (ym.Version >= 6)
        {
            foreach (var (code, predivisor, count) in new[] { (r[1], r[6], r[14]), (r[3], r[8], r[15]) })
            {
                int voice = ((code & 0x30) >> 4) - 1;
                if (voice < 0) continue;
                double frequency = TimerFrequency(predivisor >> 5, count);
                var kind = (code & 0xC0) switch { 0x00 => EffectKind.Sid, 0x80 => EffectKind.SineSid, 0x40 => EffectKind.Drum, _ => EffectKind.SyncBuzzer };
                list.Add(new Effect(kind, voice, frequency, kind == EffectKind.Drum ? r[8 + voice] & 31 : r[8 + voice] & 15));
            }
        }
        return list;
    }

    // The SID voices and the sync buzzer only last while the frames ask for them; a digidrum plays to its end
    private void ApplyEffects(int frame)
    {
        for (int c = 0; c < 3; c++) _chip.StopSid(c);
        _chip.StopSyncBuzzer();
        foreach (var e in Effects(_ym, frame))
            switch (e.Kind)
            {
                case EffectKind.Sid: _chip.StartSid(e.Channel, e.Frequency, e.Value); break;
                case EffectKind.SineSid: _chip.StartSid(e.Channel, e.Frequency, e.Value, sine: true); break;
                case EffectKind.Drum: StartDrum(e.Channel, e.Value, e.Frequency); break;
                case EffectKind.SyncBuzzer: _chip.StartSyncBuzzer(e.Frequency); break;
            }
    }

    private void StartDrum(int voice, int number, double frequency)
    {
        if (number < _ym.Drums.Count && frequency > 0) _chip.StartDrum(voice, _ym.Drums[number], frequency);
    }

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
                int frame = _frame++;
                var regs = _ym.Frames[frame];
                _chip.WriteFrame(regs);
                ApplyEffects(frame);
                _frameLeft += _samplesPerFrame;
            }
            _frameLeft--;
            dest[i] = (float)_chip.Next();
        }
    }
}
