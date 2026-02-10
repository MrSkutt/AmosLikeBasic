using System;
using Un4seen.Bass;

namespace AmoslikeBasic
{
    /// <summary>
    /// Chiptune synthesizer for generating C64/Atari ST style sounds
    /// </summary>
    public class ChiptuneSynth
    {
        private const int SAMPLE_RATE = 22050; // Lower sample rate for authentic chiptune sound
        private const int BITS_PER_SAMPLE = 16;
        
        public enum WaveformType
        {
            Square,
            Triangle,
            Sawtooth,
            Noise,
            Pulse // Square wave with variable duty cycle
        }

        /// <summary>
        /// Generate a waveform buffer
        /// </summary>
        private static short[] GenerateWaveform(WaveformType type, double frequency, double duration, 
            double dutyCycle = 0.5, double volume = 0.3)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            short[] buffer = new short[samples];
            double phase = 0.0;
            double phaseIncrement = frequency / SAMPLE_RATE;
            Random random = new Random();

            for (int i = 0; i < samples; i++)
            {
                double sample = 0;

                switch (type)
                {
                    case WaveformType.Square:
                        sample = phase < 0.5 ? 1.0 : -1.0;
                        break;

                    case WaveformType.Pulse:
                        sample = phase < dutyCycle ? 1.0 : -1.0;
                        break;

                    case WaveformType.Triangle:
                        sample = phase < 0.5 
                            ? (4.0 * phase - 1.0) 
                            : (3.0 - 4.0 * phase);
                        break;

                    case WaveformType.Sawtooth:
                        sample = 2.0 * phase - 1.0;
                        break;

                    case WaveformType.Noise:
                        sample = (random.NextDouble() * 2.0) - 1.0;
                        break;
                }

                // Apply volume and convert to 16-bit
                buffer[i] = (short)(sample * volume * short.MaxValue);

                // Advance phase
                phase += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        /// <summary>
        /// Apply ADSR envelope to a buffer
        /// </summary>
        private static void ApplyEnvelope(short[] buffer, double attack, double decay, 
            double sustain, double release)
        {
            int attackSamples = (int)(SAMPLE_RATE * attack);
            int decaySamples = (int)(SAMPLE_RATE * decay);
            int releaseSamples = (int)(SAMPLE_RATE * release);
            int sustainSamples = buffer.Length - attackSamples - decaySamples - releaseSamples;

            if (sustainSamples < 0)
            {
                sustainSamples = 0;
                attackSamples = buffer.Length / 3;
                decaySamples = buffer.Length / 3;
                releaseSamples = buffer.Length / 3;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                double envelope = 1.0;

                if (i < attackSamples)
                {
                    // Attack: 0 to 1
                    envelope = (double)i / attackSamples;
                }
                else if (i < attackSamples + decaySamples)
                {
                    // Decay: 1 to sustain level
                    double decayProgress = (double)(i - attackSamples) / decaySamples;
                    envelope = 1.0 - (decayProgress * (1.0 - sustain));
                }
                else if (i < buffer.Length - releaseSamples)
                {
                    // Sustain: hold at sustain level
                    envelope = sustain;
                }
                else
                {
                    // Release: sustain to 0
                    double releaseProgress = (double)(i - (buffer.Length - releaseSamples)) / releaseSamples;
                    envelope = sustain * (1.0 - releaseProgress);
                }

                buffer[i] = (short)(buffer[i] * envelope);
            }
        }

        /// <summary>
        /// Apply vibrato (pitch modulation)
        /// </summary>
        private static short[] ApplyVibrato(short[] buffer, double vibratoRate, double vibratoDepth)
        {
            // This is a simplified vibrato - for true pitch vibrato you'd need to resample
            // Here we just apply amplitude modulation which gives a similar tremolo effect
            double phase = 0.0;
            double phaseIncrement = vibratoRate / SAMPLE_RATE;

            for (int i = 0; i < buffer.Length; i++)
            {
                double modulation = 1.0 + (vibratoDepth * Math.Sin(2 * Math.PI * phase));
                buffer[i] = (short)(buffer[i] * modulation);
                
                phase += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        /// <summary>
        /// Create frequency sweep (pitch slide)
        /// </summary>
        private static short[] GenerateSweep(WaveformType type, double startFreq, double endFreq, 
            double duration, double volume = 0.3)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            short[] buffer = new short[samples];
            double phase = 0.0;
            Random random = new Random();

            for (int i = 0; i < samples; i++)
            {
                // Calculate current frequency (linear interpolation)
                double progress = (double)i / samples;
                double frequency = startFreq + (endFreq - startFreq) * progress;
                double phaseIncrement = frequency / SAMPLE_RATE;

                double sample = 0;

                switch (type)
                {
                    case WaveformType.Square:
                        sample = phase < 0.5 ? 1.0 : -1.0;
                        break;

                    case WaveformType.Sawtooth:
                        sample = 2.0 * phase - 1.0;
                        break;

                    case WaveformType.Noise:
                        sample = (random.NextDouble() * 2.0) - 1.0;
                        break;
                }

                buffer[i] = (short)(sample * volume * short.MaxValue);

                phase += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }

        /// <summary>
        /// Generate arpeggio (rapid cycling through notes)
        /// </summary>
        private static short[] GenerateArpeggio(WaveformType type, double[] frequencies, 
            double duration, double noteLength = 0.05, double volume = 0.3)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            short[] buffer = new short[samples];
            int noteLengthSamples = (int)(SAMPLE_RATE * noteLength);
            
            int currentNote = 0;
            double phase = 0.0;

            for (int i = 0; i < samples; i++)
            {
                // Switch note every noteLength samples
                if (i % noteLengthSamples == 0)
                {
                    currentNote = (i / noteLengthSamples) % frequencies.Length;
                }

                double frequency = frequencies[currentNote];
                double phaseIncrement = frequency / SAMPLE_RATE;

                double sample = 0;

                switch (type)
                {
                    case WaveformType.Square:
                        sample = phase < 0.5 ? 1.0 : -1.0;
                        break;

                    case WaveformType.Triangle:
                        sample = phase < 0.5 ? (4.0 * phase - 1.0) : (3.0 - 4.0 * phase);
                        break;

                    case WaveformType.Sawtooth:
                        sample = 2.0 * phase - 1.0;
                        break;
                }

                buffer[i] = (short)(sample * volume * short.MaxValue);

                phase += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            return buffer;
        }


        /// <summary>
        /// Play a generated sound
        /// </summary>
        private static int PlaySound(short[] buffer)
        {
            // Convert short[] to byte[]
            byte[] byteBuffer = new byte[buffer.Length * 2];
            Buffer.BlockCopy(buffer, 0, byteBuffer, 0, byteBuffer.Length);

            // Create sample
            int sample = Bass.BASS_SampleCreate(
                byteBuffer.Length,
                SAMPLE_RATE,
                1, // mono
                1, // max simultaneous playbacks
                BASSFlag.BASS_SAMPLE_OVER_POS
            );

            if (sample != 0)
            {
                Bass.BASS_SampleSetData(sample, byteBuffer);
                int channel = Bass.BASS_SampleGetChannel(sample, BASSFlag.BASS_SAMPLE_OVER_POS);
                Bass.BASS_ChannelPlay(channel, false);
                return channel;
            }

            return 0;
        }

        // ============ PUBLIC SOUND EFFECT METHODS ============

        /// <summary>
        /// Classic laser/pew sound
        /// </summary>
        public static int PlayLaser()
        {
            var buffer = GenerateSweep(WaveformType.Square, 1200, 200, 0.15, 0.25);
            ApplyEnvelope(buffer, 0.01, 0.02, 0.3, 0.1);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Explosion sound
        /// </summary>
        public static int PlayExplosion()
        {
            var buffer = GenerateSweep(WaveformType.Noise, 0, 0, 0.5, 0.4);
            ApplyEnvelope(buffer, 0.01, 0.1, 0.2, 0.35);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Jump/bounce sound
        /// </summary>
        public static int PlayJump()
        {
            var buffer = GenerateSweep(WaveformType.Square, 400, 800, 0.2, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.05, 0.5, 0.1);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Coin/pickup sound
        /// </summary>
        public static int PlayCoin()
        {
            double[] notes = { 523.25, 659.25, 783.99 }; // C5, E5, G5
            var buffer = GenerateArpeggio(WaveformType.Square, notes, 0.15, 0.05, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.02, 0.6, 0.08);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Hit/damage sound
        /// </summary>
        public static int PlayHit()
        {
            var buffer = GenerateSweep(WaveformType.Noise, 0, 0, 0.1, 0.5);
            ApplyEnvelope(buffer, 0.001, 0.02, 0.1, 0.05);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Power up sound
        /// </summary>
        public static int PlayPowerUp()
        {
            double[] notes = { 261.63, 329.63, 392.00, 523.25 }; // C4, E4, G4, C5
            var buffer = GenerateArpeggio(WaveformType.Triangle, notes, 0.4, 0.1, 0.3);
            ApplyEnvelope(buffer, 0.01, 0.1, 0.7, 0.2);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Blip/menu selection sound
        /// </summary>
        public static int PlayBlip()
        {
            var buffer = GenerateWaveform(WaveformType.Square, 800, 0.08, 0.5, 0.25);
            ApplyEnvelope(buffer, 0.01, 0.01, 0.5, 0.05);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Play a musical note
        /// </summary>
        public static int PlayNote(WaveformType waveform, double frequency, double duration, 
            double volume = 0.3, bool useEnvelope = true)
        {
            var buffer = GenerateWaveform(waveform, frequency, duration, 0.5, volume);
            
            if (useEnvelope)
            {
                ApplyEnvelope(buffer, 0.02, 0.05, 0.7, 0.1);
            }
            
            return PlaySound(buffer);
        }

        /// <summary>
        /// Play a pulse width modulated note (varying duty cycle)
        /// </summary>
        public static int PlayPWMNote(double frequency, double duration, double startDuty = 0.1, 
            double endDuty = 0.9, double volume = 0.3)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            short[] buffer = new short[samples];
            double phase = 0.0;
            double phaseIncrement = frequency / SAMPLE_RATE;

            for (int i = 0; i < samples; i++)
            {
                // Interpolate duty cycle
                double progress = (double)i / samples;
                double dutyCycle = startDuty + (endDuty - startDuty) * progress;

                double sample = phase < dutyCycle ? 1.0 : -1.0;
                buffer[i] = (short)(sample * volume * short.MaxValue);

                phase += phaseIncrement;
                if (phase >= 1.0) phase -= 1.0;
            }

            ApplyEnvelope(buffer, 0.02, 0.05, 0.7, 0.1);
            return PlaySound(buffer);
        }

        /// <summary>
        /// Get frequency for a musical note (A4 = 440Hz)
        /// </summary>
        public static double GetNoteFrequency(int midiNote)
        {
            return 440.0 * Math.Pow(2.0, (midiNote - 69) / 12.0);
        }
    }
}
