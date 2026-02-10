using System;
using System.Threading.Tasks;
//using AmosBasic.Audio;
using AmoslikeBasic;
using Un4seen.Bass;

namespace AmosLikeBasic
{
    /// <summary>
    /// Example of how to integrate ChiptuneSynth into Amos Basic commands
    /// </summary>
    public class AmosAudioCommands
    {
        public static void InitializeAudio()
        {
            // Initialize BASS
            Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
        }

        // Example Amos Basic command implementations:

        /// <summary>
        /// BOOM - Play explosion sound
        /// Usage: Boom
        /// </summary>
        public static void Boom()
        {
            ChiptuneSynth.PlayExplosion();
        }

        /// <summary>
        /// SHOOT - Play laser sound
        /// Usage: Shoot
        /// </summary>
        public static void Shoot()
        {
            ChiptuneSynth.PlayLaser();
        }

        /// <summary>
        /// BELL - Play a bell/coin sound
        /// Usage: Bell
        /// </summary>
        public static void Bell()
        {
            ChiptuneSynth.PlayCoin();
        }

        /// <summary>
        /// CLICK - Play a click/blip sound
        /// Usage: Click
        /// </summary>
        public static void Click()
        {
            ChiptuneSynth.PlayBlip();
        }

        /// <summary>
        /// ZAP - Play hit sound
        /// Usage: Zap
        /// </summary>
        public static void Zap()
        {
            ChiptuneSynth.PlayHit();
        }

        /// <summary>
        /// WAVE waveform, note, duration - Play a musical note
        /// Usage: Wave 0, 60, 0.5  (Square wave, Middle C, half second)
        /// Waveforms: 0=Square, 1=Triangle, 2=Sawtooth, 3=Noise
        /// </summary>
        public static void Wave(int waveform, int midiNote, double duration)
        {
            var waveType = (ChiptuneSynth.WaveformType)waveform;
            double frequency = ChiptuneSynth.GetNoteFrequency(midiNote);
            ChiptuneSynth.PlayNote(waveType, frequency, duration);
        }

        /// <summary>
        /// SYNTH frequency, duration - Play a tone at specific frequency
        /// Usage: Synth 440, 1.0  (A4 for 1 second)
        /// </summary>
        public static void Synth(double frequency, double duration)
        {
            ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Square, frequency, duration);
        }

        /// <summary>
        /// NOISE duration - Play white noise
        /// Usage: Noise 0.5
        /// </summary>
        public static void Noise(double duration)
        {
            ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Noise, 0, duration, 0.3, false);
        }

        // ===== Advanced Examples =====

        /// <summary>
        /// Example: Play a C major scale
        /// </summary>
        public static async Task PlayScale()
        {
            int[] scale = { 60, 62, 64, 65, 67, 69, 71, 72 }; // C major scale (MIDI notes)
            
            foreach (int note in scale)
            {
                double freq = ChiptuneSynth.GetNoteFrequency(note);
                ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Square, freq, 0.3);
                await Task.Delay(300);
            }
        }

        /// <summary>
        /// Example: Play a simple melody
        /// </summary>
        public static async Task PlayMelody()
        {
            // Simple melody: C-E-G-C
            int[] melody = { 60, 64, 67, 72 };
            double[] durations = { 0.25, 0.25, 0.25, 0.5 };

            for (int i = 0; i < melody.Length; i++)
            {
                double freq = ChiptuneSynth.GetNoteFrequency(melody[i]);
                ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Triangle, freq, durations[i]);
                await Task.Delay((int)(durations[i] * 1000));
            }
        }

        /// <summary>
        /// Example: PWM bass sound (C64 style)
        /// </summary>
        public static void PlayPWMBass()
        {
            // Low C with pulse width modulation
            ChiptuneSynth.PlayPWMNote(130.81, 1.0, 0.1, 0.9, 0.4);
        }

        /// <summary>
        /// Example: SID-style sound effect combo
        /// </summary>
        public static async Task PlaySIDEffect()
        {
            // Layer multiple sounds for complex effect
            ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Sawtooth, 200, 0.3, 0.2);
            await Task.Delay(50);
            ChiptuneSynth.PlayNote(ChiptuneSynth.WaveformType.Square, 400, 0.2, 0.15);
        }

        public static void Cleanup()
        {
            Bass.BASS_Free();
        }
    }

    // ===== DEMO PROGRAM =====
    public class ChiptuneDemo
    {
        public static async Task Main()
        {
            Console.WriteLine("Chiptune Synth Demo");
            Console.WriteLine("===================\n");

            AmosAudioCommands.InitializeAudio();

            Console.WriteLine("0. Playing laser sound...");
            await AmosAudioCommands.PlaySIDEffect();
            await Task.Delay(1500);

            Console.WriteLine("1. Playing laser sound...");
            AmosAudioCommands.Shoot();
            await Task.Delay(500);

            Console.WriteLine("2. Playing explosion...");
            AmosAudioCommands.Boom();
            await Task.Delay(1000);

            Console.WriteLine("3. Playing coin pickup...");
            AmosAudioCommands.Bell();
            await Task.Delay(500);

            Console.WriteLine("4. Playing jump sound...");
            ChiptuneSynth.PlayJump();
            await Task.Delay(500);

            Console.WriteLine("5. Playing power up...");
            ChiptuneSynth.PlayPowerUp();
            await Task.Delay(1000);

            Console.WriteLine("6. Playing C major scale...");
            await AmosAudioCommands.PlayScale();
            await Task.Delay(500);

            Console.WriteLine("7. Playing PWM bass (C64 style)...");
            AmosAudioCommands.PlayPWMBass();
            await Task.Delay(1500);

            Console.WriteLine("8. Playing simple melody...");
            await AmosAudioCommands.PlayMelody();

            Console.WriteLine("\nDemo complete!");
            AmosAudioCommands.Cleanup();
        }
    }
}
