using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Un4seen.Bass;

namespace AmosLikeBasic
{
    public class ChiptuneSynth
    {
        private const int SAMPLE_RATE    = 22050;
        private const int BITS_PER_SAMPLE = 16;

        public enum WaveformType
        {
            Square, Triangle, Sawtooth, Noise, Pulse, Drums
        }

        // ── Globalt tillstånd ────────────────────────────────────────
        private static int _bpm = 120;

        // ── Per-kanal tillstånd (index 0-3 = kanal 1-4) ─────────────
        private static readonly WaveformType[]            _wave    = new WaveformType[4];
        private static readonly double[]                  _volume  = Enumerable.Repeat(0.3, 4).ToArray();
        private static readonly CancellationTokenSource?[] _musicCts = new CancellationTokenSource[4];
        private static readonly Random _random = new Random();
        private static readonly double[] _phase = new double[4];

        // ── Kanal-validering ─────────────────────────────────────────
        private static int ChIdx(int channel) => Math.Clamp(channel - 1, 0, 3);

        // ── Globala inställningar ────────────────────────────────────
        public static void SetBpm(int bpm)
        {
            _bpm = Math.Max(1, bpm);
        }

        public static void StopMusic()
        {
            for (int i = 0; i < 4; i++)
            {
                _musicCts[i]?.Cancel();
                _musicCts[i]?.Dispose();
                _musicCts[i] = null;
            }
        }
        private static bool IsDrumChannel(int idx) => _wave[idx] == WaveformType.Drums;
        
        public static bool IsPlaying(int channel)
        {
            int idx = ChIdx(channel);
            return _musicCts[idx] != null;
        }

        public static bool IsAnyPlaying()
        {
            for (int i = 0; i < 4; i++)
                if (_musicCts[i] != null) return true;
            return false;
        }
        
        // ── Per-kanal inställningar ──────────────────────────────────
        public static void SetWave(int channel, string wave)
        {
            _wave[ChIdx(channel)] = wave.ToUpperInvariant() switch
            {
                "SQUARE"   => WaveformType.Square,
                "TRIANGLE" => WaveformType.Triangle,
                "SAWTOOTH" => WaveformType.Sawtooth,
                "NOISE"    => WaveformType.Noise,
                "PULSE"    => WaveformType.Pulse,
                "DRUMS" => WaveformType.Drums,
                _          => WaveformType.Square
            };
        }

        public static void SetVolume(int channel, double volume)
        {
            _volume[ChIdx(channel)] = Math.Clamp(volume, 0.0, 1.0);
        }

        // ── Spela musik på kanal ─────────────────────────────────────
        public static void PlayMusic(int channel, string sequence)
        {
            int idx = ChIdx(channel);

            // Stoppa eventuell pågående melodi på denna kanal
            _musicCts[idx]?.Cancel();
            _musicCts[idx]?.Dispose();

            _musicCts[idx] = new CancellationTokenSource();
            var token = _musicCts[idx].Token;

            Task.Run(() => PlayMusicAsync(sequence, idx, token));
        }

        private static void PlayMusicAsync(string sequence, int idx, CancellationToken token)
        {
            sequence = sequence.Trim().Trim('"');
            var tokens = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool isDrum = IsDrumChannel(idx);
            int lastLength = 4; 
            
            foreach (var t in tokens)
            {
                if (token.IsCancellationRequested) return;

                var parts = t.Split(':');
                //if (parts.Length != 2) continue;

                string noteName = parts[0].Trim().Trim('"');
                
                // Använd angiven längd eller föregående
                int length;
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim().Trim('"'), out int parsedLength))
                {
                    length     = parsedLength;
                    lastLength = parsedLength; // spara för nästa not
                }
                else
                {
                    length = lastLength; // återanvänd föregående
                }
                
                //if (!int.TryParse(parts[1].Trim().Trim('"'), out int length)) continue;

                double duration = NoteLengthToSeconds(length);

                if (noteName.Equals("R", StringComparison.OrdinalIgnoreCase) ||
                    noteName.Equals("P", StringComparison.OrdinalIgnoreCase))
                {
                    SleepCancellable((int)(duration * 1000), token);
                }
                else if (isDrum)
                {
                    // Trumkanal — generera trumljud
                    var drumBuffer = GenerateDrumSound(noteName);
                    if (drumBuffer != null)
                    {
                        // Applicera volym
                        for (int i = 0; i < drumBuffer.Length; i++)
                            drumBuffer[i] = (short)(drumBuffer[i] * _volume[idx]);

                        PlaySound(drumBuffer);
                    }
                    SleepCancellable((int)(duration * 1000), token);
                }
                else
                {
                    // Tonkanal — som tidigare
                    double freq         = NoteNameToFreq(noteName);
                    double playDuration = duration * 0.9;

                    var buffer = GenerateWaveform(_wave[idx], freq, playDuration, 0.5, _volume[idx], idx);
                    ApplyEnvelope(buffer, 0.02, 0.05, 0.7, 0.1);
                    PlaySound(buffer);

                    SleepCancellable((int)(duration * 1000), token);
                }
            }

            // Extra väntetid för sista noten
            if (!token.IsCancellationRequested)
            {
                var lastParts = tokens[^1].Split(':');
                if (lastParts.Length == 2 &&
                    int.TryParse(lastParts[1].Trim().Trim('"'), out int lastLen))
                {
                    //SleepCancellable((int)(NoteLengthToSeconds(lastLen) * 1000) + 50, token);
                }
            }
            // ← DETTA är nyckeln — markera kanalen som klar
            if (!token.IsCancellationRequested)
            {
                _musicCts[idx]?.Dispose();
                _musicCts[idx] = null;
            }
        }

        // ── Hjälp: avbrottsbar sleep ─────────────────────────────────
        private static void SleepCancellable(int ms, CancellationToken token)
        {
            for (int i = 0; i < ms; i += 50)
            {
                if (token.IsCancellationRequested) return;
                Thread.Sleep(Math.Min(50, ms - i));
            }
        }

        // ── Notlängd → sekunder ──────────────────────────────────────
        private static double NoteLengthToSeconds(int length)
        {
            double quarterSeconds = 60.0 / _bpm;
            return quarterSeconds * (4.0 / length);
        }

        // ── Notnamn → frekvens ───────────────────────────────────────
        private static readonly Dictionary<string, int> _noteMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["C"]  = 0,  ["C#"] = 1, ["Db"] = 1,
                ["D"]  = 2,  ["D#"] = 3, ["Eb"] = 3,
                ["E"]  = 4,
                ["F"]  = 5,  ["F#"] = 6, ["Gb"] = 6,
                ["G"]  = 7,  ["G#"] = 8, ["Ab"] = 8,
                ["A"]  = 9,  ["A#"] = 10,["Bb"] = 10,
                ["B"]  = 11,
            };

        private static double NoteNameToFreq(string note)
        {
            note = note.Trim().Trim('"');

            int octaveStart = -1;
            for (int i = note.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(note[i]))
                    octaveStart = i;
                else
                    break;
            }

            if (octaveStart <= 0) return 440.0;

            string notePart = note.Substring(0, octaveStart);
            int    octave   = int.Parse(note.Substring(octaveStart));

            if (!_noteMap.TryGetValue(notePart, out int semitone))
                return 440.0;

            int midi = (octave + 1) * 12 + semitone;
            return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
        }

        // ── Vågformsgenerering ───────────────────────────────────────
        private static short[] GenerateWaveform(WaveformType type, double frequency,
            double duration, double dutyCycle = 0.5, double volume = 0.3, int idx = 0)
        {
            int    samples        = (int)(SAMPLE_RATE * duration);
            short[] buffer        = new short[samples];
            double phase = _phase[idx];
            double phaseIncrement = frequency / SAMPLE_RATE;
            Random random = _random;

            for (int i = 0; i < samples; i++)
            {
                double sample = type switch
                {
                    WaveformType.Square   => phase < 0.5 ? 1.0 : -1.0,
                    WaveformType.Pulse    => phase < dutyCycle ? 1.0 : -1.0,
                    WaveformType.Triangle => phase < 0.5 ? (4.0 * phase - 1.0) : (3.0 - 4.0 * phase),
                    WaveformType.Sawtooth => 2.0 * phase - 1.0,
                    WaveformType.Noise    => (random.NextDouble() * 2.0) - 1.0,
                    _                    => 0.0
                };

                buffer[i]  = (short)(sample * volume * short.MaxValue);
                phase      += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        private static void ApplyEnvelope(short[] buffer, double attack, double decay,
            double sustain, double release)
        {
            int attackSamples  = (int)(SAMPLE_RATE * attack);
            int decaySamples   = (int)(SAMPLE_RATE * decay);
            int releaseSamples = (int)(SAMPLE_RATE * release);
            int sustainSamples = buffer.Length - attackSamples - decaySamples - releaseSamples;

            if (sustainSamples < 0)
            {
                sustainSamples = 0;
                attackSamples  = buffer.Length / 3;
                decaySamples   = buffer.Length / 3;
                releaseSamples = buffer.Length / 3;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                double envelope;

                if (i < attackSamples)
                    envelope = (double)i / attackSamples;
                else if (i < attackSamples + decaySamples)
                {
                    double p = (double)(i - attackSamples) / decaySamples;
                    envelope = 1.0 - p * (1.0 - sustain);
                }
                else if (i < buffer.Length - releaseSamples)
                    envelope = sustain;
                else
                {
                    double p = (double)(i - (buffer.Length - releaseSamples)) / releaseSamples;
                    envelope = sustain * (1.0 - p);
                }

                buffer[i] = (short)(buffer[i] * envelope);
            }
        }

        private static short[] GenerateSweep(WaveformType type, double startFreq,
            double endFreq, double duration, double volume = 0.3)
        {
            int    samples = (int)(SAMPLE_RATE * duration);
            short[] buffer = new short[samples];
            double phase   = 0.0;
            Random random  = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress       = (double)i / samples;
                double frequency      = startFreq + (endFreq - startFreq) * progress;
                double phaseIncrement = frequency / SAMPLE_RATE;

                double sample = type switch
                {
                    WaveformType.Square   => phase < 0.5 ? 1.0 : -1.0,
                    WaveformType.Sawtooth => 2.0 * phase - 1.0,
                    WaveformType.Noise    => (random.NextDouble() * 2.0) - 1.0,
                    _                    => 0.0
                };

                buffer[i] = (short)(sample * volume * short.MaxValue);
                phase     += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        private static short[] GenerateArpeggio(WaveformType type, double[] frequencies,
            double duration, double noteLength = 0.05, double volume = 0.3)
        {
            int    samples          = (int)(SAMPLE_RATE * duration);
            short[] buffer          = new short[samples];
            int    noteLengthSamples = (int)(SAMPLE_RATE * noteLength);
            double phase            = 0.0;

            for (int i = 0; i < samples; i++)
            {
                int    currentNote    = (i / noteLengthSamples) % frequencies.Length;
                double frequency      = frequencies[currentNote];
                double phaseIncrement = frequency / SAMPLE_RATE;

                double sample = type switch
                {
                    WaveformType.Square   => phase < 0.5 ? 1.0 : -1.0,
                    WaveformType.Triangle => phase < 0.5 ? (4.0 * phase - 1.0) : (3.0 - 4.0 * phase),
                    WaveformType.Sawtooth => 2.0 * phase - 1.0,
                    _                    => 0.0
                };

                buffer[i] = (short)(sample * volume * short.MaxValue);
                phase     += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        // ── BASS ljuduppspelning ─────────────────────────────────────
        private static int PlaySound(short[] buffer)
        {
            byte[] byteBuffer = new byte[buffer.Length * 2];
            Buffer.BlockCopy(buffer, 0, byteBuffer, 0, byteBuffer.Length);

            int sample = Bass.BASS_SampleCreate(
                byteBuffer.Length, SAMPLE_RATE, 1, 1,
                BASSFlag.BASS_SAMPLE_OVER_POS);

            if (sample != 0)
            {
                Bass.BASS_SampleSetData(sample, byteBuffer);
                int channel = Bass.BASS_SampleGetChannel(sample, BASSFlag.BASS_SAMPLE_OVER_POS);
                Bass.BASS_ChannelPlay(channel, false);
                return channel;
            }

            return 0;
        }

        // ── Trumljud ─────────────────────────────────────────────────

        private static short[] GenerateBassDrum()
        {
            // Snabb pitch-sweep nedåt + noise i attack
            int    samples = (int)(SAMPLE_RATE * 0.25);
            short[] buffer = new short[samples];
            double phase   = 0.0;
            Random random  = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress  = (double)i / samples;
                double frequency = 180.0 - (160.0 * progress); // 180Hz → 20Hz
                double sine      = Math.Sin(2 * Math.PI * phase);
                double noise     = (random.NextDouble() * 2.0 - 1.0) * 0.1;
                double sample    = (sine + noise) * (1.0 - progress); // snabb decay

                buffer[i] = (short)(sample * 0.9 * short.MaxValue);
                phase     += frequency / SAMPLE_RATE;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        private static short[] GenerateSnareDrum()
        {
            // Kort ton + noise
            int    samples = (int)(SAMPLE_RATE * 0.15);
            short[] buffer = new short[samples];
            double phase   = 0.0;
            Random random  = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress = (double)i / samples;
                double envelope = Math.Exp(-progress * 8.0); // exponentiell decay
                double tone     = Math.Sin(2 * Math.PI * phase) * 0.4;
                double noise    = (random.NextDouble() * 2.0 - 1.0) * 0.6;
                double sample   = (tone + noise) * envelope;

                buffer[i] = (short)(sample * 0.8 * short.MaxValue);
                phase     += 180.0 / SAMPLE_RATE;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        private static short[] GenerateHiHat(bool open)
        {
            double duration = open ? 0.15 : 0.05;
            int    samples  = (int)(SAMPLE_RATE * duration);
            short[] buffer  = new short[samples];
            Random random   = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress = (double)i / samples;
                double envelope = open
                    ? Math.Exp(-progress * 6.0)
                    : Math.Exp(-progress * 20.0); // stängd = mycket kort
                double sample   = (random.NextDouble() * 2.0 - 1.0) * envelope;

                // Filtrera mot höga frekvenser — multiplicera varannan sample
                if (i % 2 == 0) sample *= 0.3;

                buffer[i] = (short)(sample * 0.6 * short.MaxValue);
            }

            return buffer;
        }

        private static short[] GenerateCymbal()
        {
            int    samples = (int)(SAMPLE_RATE * 0.4);
            short[] buffer = new short[samples];
            Random random  = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress = (double)i / samples;
                double envelope = Math.Exp(-progress * 4.0);
                double sample   = (random.NextDouble() * 2.0 - 1.0) * envelope;

                buffer[i] = (short)(sample * 0.5 * short.MaxValue);
            }

            return buffer;
        }

        private static short[] GenerateTom()
        {
            int    samples = (int)(SAMPLE_RATE * 0.2);
            short[] buffer = new short[samples];
            double phase   = 0.0;
            Random random  = new Random();

            for (int i = 0; i < samples; i++)
            {
                double progress  = (double)i / samples;
                double frequency = 120.0 - (80.0 * progress); // 120Hz → 40Hz
                double envelope  = Math.Exp(-progress * 6.0);
                double sine      = Math.Sin(2 * Math.PI * phase);
                double noise     = (random.NextDouble() * 2.0 - 1.0) * 0.15;
                double sample    = (sine + noise) * envelope;

                buffer[i] = (short)(sample * 0.8 * short.MaxValue);
                phase     += frequency / SAMPLE_RATE;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        private static short[]? GenerateDrumSound(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "BD" => GenerateBassDrum(),
                "SD" => GenerateSnareDrum(),
                "HH" => GenerateHiHat(false),
                "OH" => GenerateHiHat(true),
                "CY" => GenerateCymbal(),
                "TM" => GenerateTom(),
                _    => null
            };
        }
        // ── Inbyggda ljudeffekter ────────────────────────────────────
        public static int PlayLaser()
        {
            var buffer = GenerateSweep(WaveformType.Square, 1200, 200, 0.15, 0.25);
            ApplyEnvelope(buffer, 0.01, 0.02, 0.3, 0.1);
            return PlaySound(buffer);
        }

        public static int PlayExplosion()
        {
            var buffer = GenerateSweep(WaveformType.Noise, 0, 0, 0.5, 0.4);
            ApplyEnvelope(buffer, 0.01, 0.1, 0.2, 0.35);
            return PlaySound(buffer);
        }

        public static int PlayJump()
        {
            var buffer = GenerateSweep(WaveformType.Square, 400, 800, 0.2, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.05, 0.5, 0.1);
            return PlaySound(buffer);
        }

        public static int PlayCoin()
        {
            double[] notes = { 523.25, 659.25, 783.99 };
            var buffer = GenerateArpeggio(WaveformType.Square, notes, 0.15, 0.05, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.02, 0.6, 0.08);
            return PlaySound(buffer);
        }

        public static int PlayHit()
        {
            var buffer = GenerateSweep(WaveformType.Noise, 0, 0, 0.1, 0.5);
            ApplyEnvelope(buffer, 0.001, 0.02, 0.1, 0.05);
            return PlaySound(buffer);
        }

        public static int PlayPowerUp()
        {
            double[] notes = { 261.63, 329.63, 392.00, 523.25 };
            var buffer = GenerateArpeggio(WaveformType.Triangle, notes, 0.4, 0.1, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.1, 0.7, 0.2);
            return PlaySound(buffer);
        }

        public static int PlayBlip()
        {
            var buffer = GenerateWaveform(WaveformType.Square, 800, 0.08, 0.5, 0.25);
            ApplyEnvelope(buffer, 0.01, 0.01, 0.5, 0.05);
            return PlaySound(buffer);
        }
    }
}