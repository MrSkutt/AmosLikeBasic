using System;
using System.Linq;

namespace AmosLikeBasic
{
    // A song in YM mode: channels 1 – 3 are played by an Atari ST sound chip (Ym2149) instead of the
    // synth's voices. Each tick, a channel's instrument (its YM part, a maxYMiser instrument) steps its
    // sequences as maxYMiser's replay does, and what they say (the tone, noise and buzzer, the volume,
    // the pitch, and the timer effects: SID, PWM, sync buzzer, digidrum) is written to the chip. The
    // pitch and volume start from the tracker's own: the note with its slides, arpeggios and vibrato,
    // and the volume column. The chip then sounds as on the Atari, the tone and a SID on it drifting
    // apart and all.
    public partial class ChiptuneSynth
    {
        // An instrument's YM part, ready to play: the sequences as arrays, the digidrum as the chip's levels
        private sealed class YmInstrument
        {
            public TrackerYmInstrument Src = new();
            public int[][] Seq = new int[8][];      // volume, arpeggio, vibrato, mixer, noise, fixed, timer, PWM
            public int[] Repeat = new int[8];
            public float[]? Digi;
        }

        // What a channel's sequences have reached
        private sealed class YmChannelState
        {
            public readonly int[] Counter = new int[8], Value = new int[8];
            public readonly bool[] Done = new bool[8];
            public int SpeedCount, PwmSlide, Shape = -1;
            public bool DigiStarted, Fresh;
            public YmInstrument? Inst;   // the instrument it played last tick
            public double Due;
            // This tick's output
            public bool Sounds, Tone, Noise, Buzzer;
            public int Period, Volume, NoisePeriod, EnvPeriod, BuzzerShape, Timer;
            public double TimerHz;
            public int[]? SidSteps, SyncShapes;
            public double Duty;
        }

        private static Ym2149? _ym;
        private static TrackerYmSong? _ymSong;
        private static readonly YmChannelState[] _ymState = { new(), new(), new() };
        private const int ToneBit = 4, BuzzerBit = 2, TimerBit = 1;

        private static YmInstrument CompileYm(TrackerYmInstrument src)
        {
            var y = new YmInstrument { Src = src };
            var seqs = new[] { src.VolumeSeq, src.ArpeggioSeq, src.VibratoSeq, src.MixerSeq, src.NoiseSeq, src.FixedSeq, src.TimerSeq, src.PwmSeq };
            for (int i = 0; i < 8; i++)
            {
                y.Seq[i] = seqs[i]?.Steps?.ToArray() ?? Array.Empty<int>();
                y.Repeat[i] = Math.Clamp(seqs[i]?.Repeat ?? 0, 0, Math.Max(0, y.Seq[i].Length - 1));
            }
            // The digidrum as the chip plays it: each sample a volume, 4 bits
            if (src.DigiSample.Length > 0 && SampleBank.Get(src.DigiSample) is { } digi)
                y.Digi = digi.Data.Select(v => (float)Ym2149.VolumeLevel(Math.Clamp((int)((v + 1) * 8), 0, 15))).ToArray();
            return y;
        }

        private static void StartYm(TrackData data)
        {
            _ymSong = data.Ym;
            _ym = data.Ym != null ? new Ym2149(2000000, SampleRate) : null;
            foreach (var s in _ymState) { s.Sounds = false; s.Shape = -1; s.PwmSlide = 0; }
        }

        private static bool YmPlays(int c) => _ym != null && c < 3 && _trackChannels[c].Inst?.Ym != null && !_trackChannels[c].Sfx;

        // A note starts: its sequences from the beginning
        private static void YmNoteStarts(int c)
        {
            var s = _ymState[c];
            Array.Clear(s.Counter);
            Array.Clear(s.Value);
            Array.Clear(s.Done);
            s.SpeedCount = 0;
            if (_trackChannels[c].Inst?.Ym?.Src.PwmReset == true) s.PwmSlide = 0;
            s.Due = 0;
            s.DigiStarted = false;
            s.Fresh = true;
        }

        // One tick of a channel: semis is the tracker's pitch (MIDI, fractions too), vol its volume 0 – 64
        private static void YmChannel(int c, double semis, double vol)
        {
            var s = _ymState[c];
            var y = _trackChannels[c].Inst!.Ym!;
            var song = _ymSong!;
            // Another instrument under the note (an effect changed it in maxYMiser): the note goes on,
            // and the sequences it changed start from their beginning, as the effects that set them do
            if (s.Inst != null && s.Inst != y)
                for (int i = 0; i < 8; i++)
                    if (!s.Inst.Seq[i].AsSpan().SequenceEqual(y.Seq[i]) || s.Inst.Repeat[i] != y.Repeat[i])
                    {
                        s.Counter[i] = 0;
                        s.Done[i] = false;
                    }
            s.Inst = y;
            s.Due += Math.Max(0.01, song.CallsPerTick);
            while (s.Due >= 1)
            {
                StepYmSequences(s, y);
                s.Due -= 1;
                if (s.Due >= 1) s.PwmSlide = Math.Clamp(s.PwmSlide + y.Src.PwmSlide, -255, 255);
            }
            YmOutput(c, s, y, song, semis, vol);
            s.PwmSlide = Math.Clamp(s.PwmSlide + y.Src.PwmSlide, -255, 255);
        }

        // The sequences one step on, every Speed calls, as maxYMiser's dosequences
        private static void StepYmSequences(YmChannelState s, YmInstrument y)
        {
            bool step = s.SpeedCount == 0;
            int speed = Math.Max(1, y.Src.Speed);
            s.SpeedCount = s.SpeedCount + 1 >= speed ? 0 : s.SpeedCount + 1;
            if (!step) return;
            for (int i = 0; i < 8; i++)
            {
                if (i == 6 || s.Done[i]) continue;
                bool on = i switch { 1 => y.Src.Arpeggio != 0, 2 => y.Src.Vibrato != 0, 5 => y.Src.FixedFrequency != 0, _ => true };
                var q = y.Seq[i];
                if (!on || q.Length == 0) { s.Value[i] = 0; s.Done[i] = true; continue; }
                s.Value[i] = q[Math.Min(s.Counter[i], q.Length - 1)];
                if (++s.Counter[i] >= q.Length)
                {
                    s.Counter[i] = y.Repeat[i];
                    if (y.Repeat[i] == q.Length - 1) s.Done[i] = true;
                }
            }
        }

        private static double YmPeriod(double semis) => 125000.0 / (440.0 * Math.Pow(2, (semis - 69) / 12.0));

        // What the channel plays this tick, from its sequences' values
        private static void YmOutput(int c, YmChannelState s, YmInstrument y, TrackerYmSong song, double semis, double vol)
        {
            var src = y.Src;
            s.Sounds = false;
            int volume = s.Value[0], mix = s.Value[3];
            // The volume column (and the song's volume) take the chip's steps of 3 dB off
            int column = vol >= 64 ? 0 : vol <= 0.5 ? 16 : (int)Math.Round(20 / 3.0 * Math.Log10(64 / vol));
            int level = (volume & 0xFF) - src.Volume - column;
            if (volume == 0 || mix == 0 || level <= 0) return;
            level = Math.Min(level, 15);
            bool timerOn = (song.Timers & (4 >> c)) != 0;
            s.Tone = (mix & 0x100) != 0;
            s.Noise = (mix & 0x1000) != 0;
            s.Buzzer = (mix & 0xF0) != 0;
            s.Timer = timerOn ? mix & 15 : 0;

            // The period of each source: the note, its arpeggio, detune, vibrato, or a fixed one
            double Period(int bit)
            {
                int fix = s.Value[5];
                if ((src.FixedFrequency & bit) != 0 && y.Seq[5].Length > 0 && (fix & 0xFFFF) != 0xFFFF)
                    return (fix & 0xFFF) * (bit == BuzzerBit ? 16 : 1);
                double n = semis;
                if ((src.FixedDetune & bit) != 0) n += src.DetuneCoarse;
                if ((src.Arpeggio & bit) != 0) n += s.Value[1];
                double p = YmPeriod(n);
                if ((src.FixedDetune & bit) != 0) p -= src.DetuneFine;
                if ((src.Vibrato & bit) != 0) p -= s.Value[2];
                return p;
            }

            s.Period = (int)Math.Round(Period(ToneBit));
            if (s.Tone && (s.Period < 1 || s.Period > 4095)) s.Tone = false;
            s.Volume = level;
            s.NoisePeriod = Math.Clamp((s.Value[4] & 0xFF) + src.NoiseTranspose, 0, 31);
            if (s.Buzzer || s.Timer == 0xB)
            {
                int nibble = mix >> 4 & 15;
                s.BuzzerShape = nibble <= 1 || !song.ShapeFromMixer ? src.BuzzerWave & 15 : nibble;
                s.EnvPeriod = Math.Clamp((int)Math.Round(Period(BuzzerBit) / 16), 1, 65535);
                s.Buzzer = true;
            }
            s.SidSteps = null;
            s.TimerHz = 0;
            s.Duty = 0.5;   // a SID's steps take equal parts of its round
            switch (s.Timer)
            {
                case 5:
                    // SID: a round of the timer's steps at the note, each the volume less the step
                    if (y.Seq[6].Length > 0)
                    {
                        s.SidSteps = y.Seq[6].Take(16).Select(x => Math.Clamp(level - x, 0, 15)).ToArray();
                        s.TimerHz = 125000.0 / Math.Max(1, Period(TimerBit));
                    }
                    break;
                case 9:
                {
                    // PWM: the volume on and off, the halves the period ± w / 256 of it
                    int w = Math.Clamp(src.PulseWidth + (src.PwmFixed ? 0 : s.PwmSlide) + (s.Value[7] & 0xFF), 0, 255);
                    // (its two volumes from the timer's sequence, as a SID's)
                    s.SidSteps = y.Seq[6].Length >= 2 ? y.Seq[6].Take(2).Select(x => Math.Clamp(level - x, 0, 15)).ToArray() : new[] { level, 0 };
                    s.Duty = 0.5 * (1 + w / 256.0);
                    s.TimerHz = 125000.0 / Math.Max(1, Period(TimerBit));
                    break;
                }
                case 0xB:
                    // Sync buzzer: the timer at twice the note writes the shapes of its sequence in turn
                    s.TimerHz = 2 * 125000.0 / Math.Max(1, Period(TimerBit));
                    s.SyncShapes = y.Seq[6].Length > 0 ? y.Seq[6].Take(16).Select(x => x & 15).ToArray() : new[] { s.BuzzerShape };
                    break;
            }
            s.Sounds = true;
        }

        // The chip's registers from the three channels, once a tick. The noise and the buzzer are one for
        // the chip: the highest channel that uses them sets them, as maxYMiser's replay does.
        private static void YmFrame()
        {
            var ym = _ym;
            if (ym == null) return;
            int mixer = 0x3F;
            int noise = -1, buzzer = -1;
            for (int c = 0; c < 3; c++)
            {
                var s = _ymState[c];
                bool plays = s.Sounds && YmPlays(c) && _trackChannels[c].Active && !_trackMute[c];
                if (!plays)
                {
                    ym.WriteLive(8 + c, 0);
                    ym.StopSid(c);
                    continue;
                }
                if (s.Tone) { mixer &= ~(1 << c); ym.WriteLive(c * 2, (byte)s.Period); ym.WriteLive(c * 2 + 1, (byte)(s.Period >> 8)); }
                if (s.Noise) { mixer &= ~(8 << c); noise = c; }
                if (s.Buzzer) buzzer = c;
                ym.WriteLive(8 + c, (byte)(s.Buzzer ? 16 : s.Volume));
                if (s.SidSteps != null && s.TimerHz > 0) ym.StartSidSteps(c, s.TimerHz, s.SidSteps, s.Duty);
                else ym.StopSid(c);
                var digi = _trackChannels[c].Inst!.Ym!.Digi;
                if (s.Timer == 0xD && digi != null && !s.DigiStarted)
                {
                    ym.StartDrum(c, digi, 614400.0 / Math.Max(0x18, _trackChannels[c].Inst!.Ym!.Src.DigiRate));
                    s.DigiStarted = true;
                }
            }
            ym.WriteLive(7, (byte)mixer);
            if (noise >= 0) ym.WriteLive(6, (byte)_ymState[noise].NoisePeriod);
            if (buzzer >= 0)
            {
                var s = _ymState[buzzer];
                ym.WriteLive(11, (byte)s.EnvPeriod);
                ym.WriteLive(12, (byte)(s.EnvPeriod >> 8));
                // The shape written starts the envelope: at a new note or another shape
                if (s.Fresh || s.BuzzerShape != s.Shape) ym.WriteLive(13, (byte)s.BuzzerShape);
                s.Shape = s.BuzzerShape;
                if (s.Timer == 0xB && s.TimerHz > 0) ym.StartSyncBuzzer(s.TimerHz, s.SyncShapes ?? new[] { s.BuzzerShape });
                else ym.StopSyncBuzzer();
            }
            else ym.StopSyncBuzzer();
            foreach (var s in _ymState) s.Fresh = false;
        }
    }
}
