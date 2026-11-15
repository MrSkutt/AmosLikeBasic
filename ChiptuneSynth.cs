using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;

namespace AmosLikeBasic
{
    // Realtidssynt i stil med C64:ans SID och NES:ens ljudkrets.
    //
    // Alla röster räknas ut sampel för sampel i en enda ljudström. Sequencern (PLAY MUSIC)
    // körs i samma ljudtråd, så kanalerna går exakt i takt. Inställningarna (vågform,
    // pulsbredd, filter, envelope, vibrato ...) läses för varje sampel, så de kan ändras
    // från BASIC medan ljudet spelar och hörs direkt.
    public class ChiptuneSynth
    {
        public const int SampleRate = 44100;
        public const int Channels = 8;
        private const int SfxSlots = 4;

        public enum WaveformType
        {
            Square, Triangle, Sawtooth, Noise, Pulse, Drums, Sine
        }

        public enum FilterMode
        {
            Off, Low, High, Band
        }

        // ── Sequencerns händelser ─────────────────────────────────────
        private readonly struct NoteEvent
        {
            public readonly double Freq;       // 0 = paus
            public readonly float[]? Drum;     // trumljud på en trumkanal
            public readonly double Length;     // del av helnot, 0.25 = fjärdedel

            public NoteEvent(double freq, float[]? drum, double length)
            {
                Freq = freq;
                Drum = drum;
                Length = length;
            }
        }

        private sealed class Sequence
        {
            public readonly NoteEvent[] Events;
            public readonly bool Loop;
            public Sequence(NoteEvent[] events, bool loop) { Events = events; Loop = loop; }
        }

        // Skickas som "ny sekvens" för att stoppa en kanal
        private static readonly Sequence StopSequence = new(Array.Empty<NoteEvent>(), false);

        private enum EnvStage { Idle, Attack, Decay, Sustain, Release }

        // ── En röst ──────────────────────────────────────────────────
        // Fälten under "Inställningar" skrivs av BASIC-tråden och läses av ljudtråden.
        // Resten ägs av ljudtråden.
        private sealed class Voice
        {
            // Inställningar
            public WaveformType Wave = WaveformType.Square;
            public double Volume = 0.3;
            public double PulseWidth = 0.25, PwmDepth, PwmRate;
            public double Attack = 0.02, Decay = 0.05, Sustain = 0.7, Release = 0.1;
            public FilterMode Filter = FilterMode.Off;
            public double Cutoff = 2000, Resonance;
            public double FilterEnvDepth, FilterEnvAttack = 0.005, FilterEnvDecay = 0.2;   // Hz, sekunder
            public double VibratoDepth, VibratoRate = 6;   // djup i halvtoner
            public int[] Arp = Array.Empty<int>();
            public double ArpRate = 50;
            public double Glide;                           // sekunder
            public double Bend;                            // halvtoner
            public int RingSource = -1, SyncSource = -1;
            public double Gate = 0.9;                      // del av notlängden som tonen hålls

            // Sequencer
            public Sequence? Pending;
            public Sequence? Seq;
            public int SeqIndex;
            public double SamplesLeft, GateLeft;

            // Ljudtillstånd
            public double Phase, Freq, TargetFreq;
            public bool Wrapped;
            public int Lfsr = 1;
            public double NoiseClock;
            public double NoiseOut = 1;
            public EnvStage Stage;
            public double Env;
            public double Gain = 0.3;
            public double LfoPhase, PwmPhase;
            public int ArpIndex;
            public double ArpCounter;
            public float[]? Drum;
            public int DrumPos;
            public double Ic1, Ic2, SmoothCutoff = 2000;
            public double FilterEnv;
            public bool FilterEnvRising;

            public void ResetSettings()
            {
                Wave = WaveformType.Square;
                Volume = 0.3;
                PulseWidth = 0.25; PwmDepth = 0; PwmRate = 0;
                Attack = 0.02; Decay = 0.05; Sustain = 0.7; Release = 0.1;
                Filter = FilterMode.Off; Cutoff = 2000; Resonance = 0;
                FilterEnvDepth = 0; FilterEnvAttack = 0.005; FilterEnvDecay = 0.2;
                VibratoDepth = 0; VibratoRate = 6;
                Arp = Array.Empty<int>(); ArpRate = 50;
                Glide = 0; Bend = 0;
                RingSource = -1; SyncSource = -1;
                Gate = 0.9;
            }
        }

        // Ljudeffekter (PLAY COIN ...) är färdiga ljudklipp som mixas in
        private sealed class SfxSlot
        {
            public float[]? Buffer;
            public int Pos;
        }

        private static readonly Voice[] _voices = CreateVoices();
        private static readonly SfxSlot[] _sfx = { new(), new(), new(), new() };
        private static int _nextSfx;
        private static double _bpm = 120;

        private static Voice[] CreateVoices()
        {
            var v = new Voice[Channels];
            for (int i = 0; i < Channels; i++) v[i] = new Voice();
            return v;
        }

        private static int ChIdx(int channel) => Math.Clamp(channel - 1, 0, Channels - 1);

        // ════════════════════════════════════════════════════════════════
        //  Utgång: BASS-ström, eller en tyst klocka om det inte finns ljud
        // ════════════════════════════════════════════════════════════════

        private static readonly object _startLock = new();
        private static bool _started;
        private static int _stream;
        private static StreamProcedure? _streamProc;   // hålls kvar så att GC inte tar den
        private static float[] _renderBuffer = new float[4096];

        // Sätts av tester som själva anropar Render
        public static bool ManualRendering { get; set; }

        private static void EnsureStarted()
        {
            if (_started || ManualRendering) return;
            lock (_startLock)
            {
                if (_started) return;
                _started = true;

                _streamProc = StreamCallback;
                _stream = Bass.CreateStream(SampleRate, 1, BassFlags.Float, _streamProc, IntPtr.Zero);
                if (_stream != 0)
                {
                    // Ingen extra buffring: ändringar från BASIC hörs inom några millisekunder
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Buffer, 0);
                    Bass.ChannelPlay(_stream);
                }
                else
                {
                    // Inget ljudkort (eller BASS ej startat): låt sequencern gå ändå,
                    // så att PLAYING och WAIT MUSIC fungerar som vanligt
                    var t = new Thread(SilentClock) { IsBackground = true, Name = "Chiptune clock" };
                    t.Start();
                }
            }
        }

        private static int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
        {
            int samples = length / sizeof(float);
            if (_renderBuffer.Length < samples) _renderBuffer = new float[samples];
            Render(_renderBuffer.AsSpan(0, samples));
            Marshal.Copy(_renderBuffer, 0, buffer, samples);
            return samples * sizeof(float);
        }

        private static void SilentClock()
        {
            var buf = new float[441];
            var sw = Stopwatch.StartNew();
            long rendered = 0;
            while (true)
            {
                long due = sw.ElapsedTicks * SampleRate / Stopwatch.Frequency;
                while (rendered < due)
                {
                    Render(buf);
                    rendered += buf.Length;
                }
                Thread.Sleep(5);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Inställningar från BASIC
        // ════════════════════════════════════════════════════════════════

        public static void SetBpm(int bpm) => _bpm = Math.Max(1, bpm);

        // Nollställer alla kanalers inställningar, när ett nytt program startar
        public static void Reset()
        {
            StopMusic();
            foreach (var v in _voices) v.ResetSettings();
            _bpm = 120;
        }

        public static void SetWave(int channel, string wave)
        {
            _voices[ChIdx(channel)].Wave = wave.Trim().ToUpperInvariant() switch
            {
                "SQUARE" => WaveformType.Square,
                "TRIANGLE" => WaveformType.Triangle,
                "SAWTOOTH" or "SAW" => WaveformType.Sawtooth,
                "NOISE" => WaveformType.Noise,
                "PULSE" => WaveformType.Pulse,
                "DRUMS" => WaveformType.Drums,
                "SINE" => WaveformType.Sine,
                _ => WaveformType.Square
            };
        }

        public static void SetVolume(int channel, double volume) =>
            _voices[ChIdx(channel)].Volume = Math.Clamp(volume, 0.0, 1.0);

        // Pulsbredd i procent, och valfri pulsbreddsmodulering (djup i procent, fart i Hz)
        public static void SetPulse(int channel, double widthPercent, double pwmDepthPercent = 0, double pwmRate = 0)
        {
            var v = _voices[ChIdx(channel)];
            v.PulseWidth = Math.Clamp(widthPercent / 100.0, 0.01, 0.99);
            v.PwmDepth = Math.Clamp(pwmDepthPercent / 100.0, 0, 0.49);
            v.PwmRate = Math.Max(0, pwmRate);
        }

        // Attack, decay och release i millisekunder, sustain 0 – 100
        public static void SetAdsr(int channel, double attackMs, double decayMs, double sustainPercent, double releaseMs)
        {
            var v = _voices[ChIdx(channel)];
            v.Attack = Math.Max(0, attackMs) / 1000.0;
            v.Decay = Math.Max(0, decayMs) / 1000.0;
            v.Sustain = Math.Clamp(sustainPercent / 100.0, 0, 1);
            v.Release = Math.Max(0, releaseMs) / 1000.0;
        }

        public static void SetFilter(int channel, string mode, double cutoffHz, double resonancePercent)
        {
            var v = _voices[ChIdx(channel)];
            v.Filter = mode.Trim().ToUpperInvariant() switch
            {
                "LOW" or "LOWPASS" or "LP" => FilterMode.Low,
                "HIGH" or "HIGHPASS" or "HP" => FilterMode.High,
                "BAND" or "BANDPASS" or "BP" => FilterMode.Band,
                _ => FilterMode.Off
            };
            v.Cutoff = Math.Clamp(cutoffHz, 20, 18000);
            v.Resonance = Math.Clamp(resonancePercent / 100.0, 0, 0.98);
        }

        // Filtret öppnas med upp till djup Hz vid varje ny not och stängs sedan igen.
        // Attack och decay i millisekunder. Negativt djup stänger filtret i stället.
        public static void SetFilterEnvelope(int channel, double depthHz, double attackMs, double decayMs)
        {
            var v = _voices[ChIdx(channel)];
            v.FilterEnvDepth = Math.Clamp(depthHz, -18000, 18000);
            v.FilterEnvAttack = Math.Max(0, attackMs) / 1000.0;
            v.FilterEnvDecay = Math.Max(1, decayMs) / 1000.0;
        }

        // Djup i hundradels halvton (cent), fart i Hz
        public static void SetVibrato(int channel, double depthCents, double rate)
        {
            var v = _voices[ChIdx(channel)];
            v.VibratoDepth = Math.Max(0, depthCents) / 100.0;
            v.VibratoRate = Math.Max(0, rate);
        }

        // Halvtonssteg, t.ex. "0 4 7" för ett durackord, och hur många steg per sekund
        public static void SetArpeggio(int channel, string offsets, double rate)
        {
            var list = new List<int>();
            foreach (var p in offsets.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(p, out var n)) list.Add(n);
            var v = _voices[ChIdx(channel)];
            v.ArpRate = Math.Max(1, rate);
            v.Arp = list.Count > 1 ? list.ToArray() : Array.Empty<int>();
        }

        public static void SetGlide(int channel, double ms) => _voices[ChIdx(channel)].Glide = Math.Max(0, ms) / 1000.0;

        public static void SetBend(int channel, double semitones) => _voices[ChIdx(channel)].Bend = semitones;

        public static void SetGate(int channel, double percent) =>
            _voices[ChIdx(channel)].Gate = Math.Clamp(percent / 100.0, 0.05, 1.0);

        // Ringmodulering och hård synk mot en annan kanal (0 stänger av), som på SID-kretsen
        public static void SetRing(int channel, int sourceChannel) =>
            _voices[ChIdx(channel)].RingSource = sourceChannel <= 0 ? -1 : ChIdx(sourceChannel);

        public static void SetSync(int channel, int sourceChannel) =>
            _voices[ChIdx(channel)].SyncSource = sourceChannel <= 0 ? -1 : ChIdx(sourceChannel);

        // ════════════════════════════════════════════════════════════════
        //  Musik
        // ════════════════════════════════════════════════════════════════

        public static void PlayMusic(int channel, string sequence, bool loop = false)
        {
            var v = _voices[ChIdx(channel)];
            var events = ParseSequence(sequence, v.Wave == WaveformType.Drums);
            Volatile.Write(ref v.Pending, events.Length == 0 ? StopSequence : new Sequence(events, loop));
            EnsureStarted();
        }

        public static void StopMusic()
        {
            foreach (var v in _voices)
                if (Volatile.Read(ref v.Seq) != null || Volatile.Read(ref v.Pending) != null)
                    Volatile.Write(ref v.Pending, StopSequence);
        }

        public static void StopMusic(int channel) =>
            Volatile.Write(ref _voices[ChIdx(channel)].Pending, StopSequence);

        public static bool IsPlaying(int channel) => IsPlaying(_voices[ChIdx(channel)]);

        private static bool IsPlaying(Voice v)
        {
            var pending = Volatile.Read(ref v.Pending);
            if (pending != null) return pending != StopSequence;
            return Volatile.Read(ref v.Seq) != null;
        }

        public static bool IsAnyPlaying()
        {
            foreach (var v in _voices)
                if (IsPlaying(v)) return true;
            return false;
        }

        // "C5:8 E5 G5:4. R:4 BD SD" → händelser. Längden gäller tills en ny anges,
        // och en punkt efter längden gör noten en halv gång längre.
        private static NoteEvent[] ParseSequence(string sequence, bool drums)
        {
            var result = new List<NoteEvent>();
            double length = 0.25;
            int octave = 4;
            foreach (var raw in sequence.Trim().Trim('"').Split(new[] { ' ', ',', '\t', '\n', '\r' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Split(':');
                string name = parts[0].Trim();
                if (parts.Length >= 2)
                {
                    string lenText = parts[1].Trim();
                    bool dotted = lenText.EndsWith('.');
                    if (int.TryParse(lenText.TrimEnd('.'), out int div) && div > 0)
                        length = (1.0 / div) * (dotted ? 1.5 : 1.0);
                }

                if (name.Equals("R", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("P", StringComparison.OrdinalIgnoreCase))
                    result.Add(new NoteEvent(0, null, length));
                else if (drums)
                    result.Add(new NoteEvent(0, DrumSound(name), length));
                else
                    result.Add(new NoteEvent(NoteNameToFreq(name, ref octave), null, length));
            }
            return result.ToArray();
        }

        private static readonly Dictionary<string, int> _noteMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["C"] = 0, ["C#"] = 1, ["Db"] = 1,
            ["D"] = 2, ["D#"] = 3, ["Eb"] = 3,
            ["E"] = 4,
            ["F"] = 5, ["F#"] = 6, ["Gb"] = 6,
            ["G"] = 7, ["G#"] = 8, ["Ab"] = 8,
            ["A"] = 9, ["A#"] = 10, ["Bb"] = 10,
            ["B"] = 11,
        };

        // Utan oktav används samma oktav som förra noten
        private static double NoteNameToFreq(string note, ref int octave)
        {
            int digits = note.Length;
            while (digits > 0 && char.IsDigit(note[digits - 1])) digits--;
            if (digits < note.Length) octave = int.Parse(note[digits..]);
            if (!_noteMap.TryGetValue(note[..digits], out int semitone)) return 440.0;
            int midi = (octave + 1) * 12 + semitone;
            return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
        }

        // ════════════════════════════════════════════════════════════════
        //  Ljudtråden
        // ════════════════════════════════════════════════════════════════

        public static void Render(Span<float> output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                double mix = 0;
                for (int c = 0; c < Channels; c++)
                    mix += RenderVoice(_voices[c]);

                foreach (var s in _sfx)
                {
                    var b = s.Buffer;
                    if (b == null) continue;
                    if (s.Pos < b.Length) mix += b[s.Pos++];
                    else s.Buffer = null;
                }

                output[i] = (float)SoftClip(mix);
            }
        }

        // Rör inte normala nivåer, men rundar av toppar i stället för att klippa dem hårt
        private static double SoftClip(double x)
        {
            double a = Math.Abs(x);
            if (a <= 0.75) return x;
            return Math.Sign(x) * (0.75 + 0.25 * Math.Tanh((a - 0.75) / 0.25));
        }

        private static double RenderVoice(Voice v)
        {
            StepSequencer(v);

            bool sounding = v.Stage != EnvStage.Idle || v.Drum != null;
            if (!sounding)
            {
                v.Wrapped = false;
                return 0;
            }

            double dt = 1.0 / SampleRate;

            // Volym utan knaster när den ändras
            v.Gain += (v.Volume - v.Gain) * 0.002;

            double sample;
            if (v.Drum != null)
            {
                sample = v.Drum[v.DrumPos++] * v.Gain;
                if (v.DrumPos >= v.Drum.Length) { v.Drum = null; v.Stage = EnvStage.Idle; }
                v.Wrapped = false;
            }
            else
            {
                // Glidning mellan toner
                if (v.Glide > 0 && v.Freq > 0)
                    v.Freq *= Math.Pow(v.TargetFreq / v.Freq, Math.Min(1.0, 4.0 * dt / v.Glide));
                else
                    v.Freq = v.TargetFreq;

                double semis = v.Bend;
                if (v.VibratoDepth > 0)
                {
                    v.LfoPhase += v.VibratoRate * dt;
                    if (v.LfoPhase >= 1) v.LfoPhase -= 1;
                    semis += v.VibratoDepth * Math.Sin(2 * Math.PI * v.LfoPhase);
                }
                var arp = v.Arp;
                if (arp.Length > 0)
                {
                    v.ArpCounter += v.ArpRate * dt;
                    if (v.ArpCounter >= 1) { v.ArpCounter -= 1; v.ArpIndex++; }
                    semis += arp[v.ArpIndex % arp.Length];
                }

                double freq = semis == 0 ? v.Freq : v.Freq * Math.Pow(2, semis / 12.0);
                double inc = Math.Min(freq * dt, 0.5);

                if (v.SyncSource >= 0 && _voices[v.SyncSource].Wrapped) v.Phase = 0;

                double t = v.Phase;
                sample = Oscillator(v, t, inc, dt);

                if (v.RingSource >= 0 && _voices[v.RingSource].Phase >= 0.5) sample = -sample;

                v.Phase += inc;
                v.Wrapped = v.Phase >= 1.0;
                if (v.Wrapped) v.Phase -= 1.0;

                sample *= NextEnvelope(v, dt) * v.Gain;
            }

            if (v.Filter != FilterMode.Off)
                sample = ApplyFilter(v, sample, NextFilterEnvelope(v, dt));

            return sample;
        }

        private static double Oscillator(Voice v, double t, double inc, double dt)
        {
            switch (v.Wave)
            {
                case WaveformType.Square:
                    return PulseWave(t, inc, 0.5);
                case WaveformType.Pulse:
                {
                    double w = v.PulseWidth;
                    if (v.PwmDepth > 0 && v.PwmRate > 0)
                    {
                        v.PwmPhase += v.PwmRate * dt;
                        if (v.PwmPhase >= 1) v.PwmPhase -= 1;
                        w += v.PwmDepth * Math.Sin(2 * Math.PI * v.PwmPhase);
                    }
                    return PulseWave(t, inc, Math.Clamp(w, 0.01, 0.99));
                }
                case WaveformType.Triangle:
                    return t < 0.5 ? 4.0 * t - 1.0 : 3.0 - 4.0 * t;
                case WaveformType.Sawtooth:
                    return 2.0 * t - 1.0 - PolyBlep(t, inc);
                case WaveformType.Sine:
                    return Math.Sin(2 * Math.PI * t);
                case WaveformType.Noise:
                {
                    // 15-bitars skiftregister som på NES, klockat i takt med tonhöjden
                    v.NoiseClock += inc * 16;
                    while (v.NoiseClock >= 1)
                    {
                        v.NoiseClock -= 1;
                        int bit = (v.Lfsr ^ (v.Lfsr >> 1)) & 1;
                        v.Lfsr = (v.Lfsr >> 1) | (bit << 14);
                        v.NoiseOut = (v.Lfsr & 1) != 0 ? 1.0 : -1.0;
                    }
                    return v.NoiseOut;
                }
                default:
                    return 0;
            }
        }

        // Pulsvåg med mjukade kanter (PolyBLEP), så att höga toner inte låter fula
        private static double PulseWave(double t, double inc, double width)
        {
            double s = t < width ? 1.0 : -1.0;
            s += PolyBlep(t, inc);
            double t2 = t - width;
            if (t2 < 0) t2 += 1;
            s -= PolyBlep(t2, inc);
            return s;
        }

        private static double PolyBlep(double t, double dt)
        {
            if (t < dt) { double x = t / dt; return x + x - x * x - 1.0; }
            if (t > 1.0 - dt) { double x = (t - 1.0) / dt; return x * x + x + x + 1.0; }
            return 0;
        }

        private static double NextEnvelope(Voice v, double dt)
        {
            switch (v.Stage)
            {
                case EnvStage.Attack:
                    v.Env += v.Attack > 0 ? dt / v.Attack : 1;
                    if (v.Env >= 1) { v.Env = 1; v.Stage = EnvStage.Decay; }
                    break;
                case EnvStage.Decay:
                    v.Env -= v.Decay > 0 ? dt / v.Decay * (1 - v.Sustain) : 1;
                    if (v.Env <= v.Sustain) { v.Env = v.Sustain; v.Stage = EnvStage.Sustain; }
                    break;
                case EnvStage.Sustain:
                    // Följer med om sustain ändras medan tonen hålls
                    v.Env += (v.Sustain - v.Env) * 0.002;
                    break;
                case EnvStage.Release:
                    v.Env -= v.Release > 0 ? dt / v.Release : 1;
                    if (v.Env <= 0) { v.Env = 0; v.Stage = EnvStage.Idle; }
                    break;
            }
            return v.Env;
        }

        // Filtrets egen envelope: stiger linjärt under attack, avtar sedan exponentiellt
        private static double NextFilterEnvelope(Voice v, double dt)
        {
            if (v.FilterEnvDepth == 0) return 0;
            if (v.FilterEnvRising)
            {
                v.FilterEnv += v.FilterEnvAttack > 0 ? dt / v.FilterEnvAttack : 1;
                if (v.FilterEnv >= 1) { v.FilterEnv = 1; v.FilterEnvRising = false; }
            }
            else
                v.FilterEnv *= Math.Exp(-3.0 * dt / v.FilterEnvDecay);
            return v.FilterEnv * v.FilterEnvDepth;
        }

        // Tillståndsvariabelfilter (TPT), stabilt även när brytfrekvensen ändras hela tiden.
        // envOffset (Hz) kommer från filtrets envelope och jämnas inte ut, så att attacken blir snabb.
        private static double ApplyFilter(Voice v, double x, double envOffset)
        {
            v.SmoothCutoff += (v.Cutoff - v.SmoothCutoff) * 0.005;
            double cutoff = Math.Clamp(v.SmoothCutoff + envOffset, 20, SampleRate * 0.45);
            double g = Math.Tan(Math.PI * cutoff / SampleRate);
            double k = 2.0 - 2.0 * v.Resonance;
            double a1 = 1.0 / (1.0 + g * (g + k));
            double a2 = g * a1;
            double a3 = g * a2;
            double v3 = x - v.Ic2;
            double v1 = a1 * v.Ic1 + a2 * v3;
            double v2 = v.Ic2 + a2 * v.Ic1 + a3 * v3;
            v.Ic1 = 2 * v1 - v.Ic1;
            v.Ic2 = 2 * v2 - v.Ic2;
            return v.Filter switch
            {
                FilterMode.Low => v2,
                FilterMode.Band => v1,
                FilterMode.High => x - k * v1 - v2,
                _ => x
            };
        }

        private static void StepSequencer(Voice v)
        {
            var pending = Volatile.Read(ref v.Pending);
            if (pending != null)
            {
                if (pending == StopSequence)
                {
                    Volatile.Write(ref v.Seq, null);
                    if (v.Stage != EnvStage.Idle && v.Drum == null) v.Stage = EnvStage.Release;
                }
                else
                {
                    Volatile.Write(ref v.Seq, pending);
                    v.SeqIndex = 0;
                    v.SamplesLeft = 0;
                }
                Interlocked.CompareExchange(ref v.Pending, null, pending);
            }

            var seq = v.Seq;
            if (seq == null) return;

            if (v.GateLeft > 0)
            {
                v.GateLeft--;
                if (v.GateLeft <= 0 && v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain)
                    v.Stage = EnvStage.Release;
            }

            v.SamplesLeft--;
            if (v.SamplesLeft > 0) return;

            if (v.SeqIndex >= seq.Events.Length)
            {
                if (seq.Loop && seq.Events.Length > 0) v.SeqIndex = 0;
                else
                {
                    Volatile.Write(ref v.Seq, null);
                    return;
                }
            }

            var e = seq.Events[v.SeqIndex++];
            double noteSamples = SampleRate * 60.0 / _bpm * 4.0 * e.Length;
            v.SamplesLeft += noteSamples;   // behåller bråkdelen, så takten inte glider

            if (e.Drum != null)
            {
                v.FilterEnvRising = true;
                v.Drum = e.Drum;
                v.DrumPos = 0;
                v.GateLeft = 0;
            }
            else if (e.Freq > 0)
            {
                if (v.Glide <= 0 || v.Stage == EnvStage.Idle) v.Freq = e.Freq;
                v.TargetFreq = e.Freq;
                v.Stage = EnvStage.Attack;   // startar från nuvarande nivå, utan klick
                v.FilterEnvRising = true;
                v.ArpIndex = 0;
                v.ArpCounter = 0;
                v.GateLeft = noteSamples * v.Gate;
            }
            else
            {
                v.GateLeft = 0;
                if (v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain)
                    v.Stage = EnvStage.Release;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Trummor
        // ════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, float[]> _drumCache = new(StringComparer.OrdinalIgnoreCase);

        private static float[]? DrumSound(string name)
        {
            lock (_drumCache)
            {
                if (_drumCache.TryGetValue(name, out var cached)) return cached;
                float[]? s = name.ToUpperInvariant() switch
                {
                    "BD" => Drum(0.25, (p, ph, r) => (Math.Sin(2 * Math.PI * ph) + r * 0.1) * (1 - p) * 0.9, p => 180 - 160 * p),
                    "SD" => Drum(0.15, (p, ph, r) => (Math.Sin(2 * Math.PI * ph) * 0.4 + r * 0.6) * Math.Exp(-p * 8) * 0.8, _ => 180),
                    "HH" => Drum(0.05, (p, _, r) => r * Math.Exp(-p * 20) * 0.6, _ => 0),
                    "OH" => Drum(0.15, (p, _, r) => r * Math.Exp(-p * 6) * 0.6, _ => 0),
                    "CY" => Drum(0.4, (p, _, r) => r * Math.Exp(-p * 4) * 0.5, _ => 0),
                    "TM" => Drum(0.2, (p, ph, r) => (Math.Sin(2 * Math.PI * ph) + r * 0.15) * Math.Exp(-p * 6) * 0.8, p => 120 - 80 * p),
                    _ => null
                };
                if (s != null) _drumCache[name] = s;
                return s;
            }
        }

        // Räknar ut ett trumljud: shape(andel av ljudet, fas, brus) med tonhöjd freq(andel)
        private static float[] Drum(double seconds, Func<double, double, double, double> shape, Func<double, double> freq)
        {
            var rnd = new Random(1);
            int n = (int)(SampleRate * seconds);
            var buf = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double p = (double)i / n;
                buf[i] = (float)shape(p, phase, rnd.NextDouble() * 2 - 1);
                phase += freq(p) / SampleRate;
                if (phase >= 1) phase -= 1;
            }
            return buf;
        }

        // ════════════════════════════════════════════════════════════════
        //  Ljudeffekter
        // ════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, float[]> _sfxCache = new();

        private static int PlaySfx(string name, Func<float[]> create)
        {
            float[] buf;
            lock (_sfxCache)
            {
                if (!_sfxCache.TryGetValue(name, out buf!))
                    _sfxCache[name] = buf = create();
            }
            int slot = Interlocked.Increment(ref _nextSfx) % SfxSlots;
            _sfx[slot].Pos = 0;
            _sfx[slot].Buffer = buf;
            EnsureStarted();
            return 1;
        }

        // Ljud där tonhöjden går från start till slut, med en enkel envelope
        private static float[] Effect(double seconds, double volume, Func<double, double> freqAt,
            Func<double, double, double> wave, double attack, double decay, double sustain, double release)
        {
            var rnd = new Random(2);
            int n = (int)(SampleRate * seconds);
            var buf = new float[n];
            double phase = 0;
            int a = (int)(SampleRate * attack), d = (int)(SampleRate * decay), r = (int)(SampleRate * release);
            if (a + d + r > n) a = d = r = n / 3;
            for (int i = 0; i < n; i++)
            {
                double p = (double)i / n;
                double env = i < a ? (double)i / a
                    : i < a + d ? 1.0 - (double)(i - a) / d * (1 - sustain)
                    : i < n - r ? sustain
                    : sustain * (1.0 - (double)(i - (n - r)) / r);
                buf[i] = (float)(wave(phase, rnd.NextDouble() * 2 - 1) * volume * env);
                phase += freqAt(p) / SampleRate;
                if (phase >= 1) phase -= 1;
            }
            return buf;
        }

        private static double Sq(double ph, double _) => ph < 0.5 ? 1 : -1;
        private static double Tri(double ph, double _) => ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph;
        private static double Wn(double _, double r) => r;

        private static Func<double, double> Arpeggio(double[] notes, double total, double step) =>
            p => notes[(int)(p * total / step) % notes.Length];

        public static int PlayLaser() =>
            PlaySfx("laser", () => Effect(0.15, 0.25, p => 1200 - 1000 * p, Sq, 0.01, 0.02, 0.3, 0.1));

        public static int PlayExplosion() =>
            PlaySfx("explosion", () => Effect(0.5, 0.4, _ => 0, Wn, 0.01, 0.1, 0.2, 0.35));

        public static int PlayJump() =>
            PlaySfx("jump", () => Effect(0.2, 0.3, p => 400 + 400 * p, Sq, 0.01, 0.05, 0.5, 0.1));

        public static int PlayCoin() =>
            PlaySfx("coin", () => Effect(0.15, 0.3, Arpeggio(new[] { 523.25, 659.25, 783.99 }, 0.15, 0.05), Sq, 0.01, 0.02, 0.6, 0.08));

        public static int PlayHit() =>
            PlaySfx("hit", () => Effect(0.1, 0.5, _ => 0, Wn, 0.001, 0.02, 0.1, 0.05));

        public static int PlayPowerUp() =>
            PlaySfx("powerup", () => Effect(0.4, 0.3, Arpeggio(new[] { 261.63, 329.63, 392.00, 523.25 }, 0.4, 0.1), Tri, 0.01, 0.1, 0.7, 0.2));

        public static int PlayBlip() =>
            PlaySfx("blip", () => Effect(0.08, 0.25, _ => 800, Sq, 0.01, 0.01, 0.5, 0.05));
    }
}
