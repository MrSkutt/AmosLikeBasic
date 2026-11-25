using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AmosLikeBasic
{
    // A song in YM mode, a maxYMiser song, played as maxYMiser's replay plays it: channels 1 – 3 are the
    // Atari ST's sound chip (Ym2149), and their lines are maxYMiser's (instrument n its instrument n, the
    // volume column and the two effects its own). Each channel keeps what the replay keeps: the
    // instrument as its effects have changed it, the note, the volume column, the transpose, the extra
    // arpeggio, the portamento and pitch slide (in tone periods), the PWM slide, and where its sequences
    // are. Every tick the sequences step, and what they say (the tone, noise and buzzer, the volume and
    // pitch, and the timer effects: SID, PWM, sync buzzer, digidrum) is written to the chip. The fourth
    // channel (maxYMiser's DMA track) is kept, but not played.
    public partial class ChiptuneSynth
    {
        // The song's sequences ready to play, and its digidrums as the chip's volumes
        private sealed class YmSongData
        {
            public TrackerYmSong Song = new();
            public int[][] Seq = Array.Empty<int[]>();
            public int[] Repeat = Array.Empty<int>();
            public int[]?[] Digi = new int[]?[9];   // [1 – 8]
            public bool[] Skip = Array.Empty<bool>();   // places the replay passes over (a loop mark, $FD, in a track)
            public bool Dhs;                             // a DHS song: channel 4 is the DMA's samples, at their own pitch
        }

        // What a channel keeps, as the replay's instrument buffer
        private sealed class YmChannelState
        {
            // The instrument as the effects have changed it
            public readonly int[] Flags = new int[6];   // portamento, arpeggio, vibrato, transpose, fixed frequency, fixed detune
            public int Speed, Width, Buzzer, StartSync, Digi, DigiRate, Volume, Coarse, Fine;
            public readonly int[] SeqNo = new int[8];   // volume, arpeggio, vibrato, mixer, noise, fixed, timer, PWM
            public int InstNumber;                      // the instrument it has (0: changed since, so given again it is read anew)
            public int Note;                            // 0: none
            // What a note's instrument starts afresh
            public int Transpose, NoiseTranspose, VolumeColumn, ArpX, ArpY, ArpStep;
            public int PortaRate, PortaAcc, SlideRate, SlideAcc;
            public bool PwmFixed, DigiStarted, Synced;
            public readonly int[] Counter = new int[8], Value = new int[8];
            public readonly bool[] Done = new bool[8];
            public int SpeedCount;
            // Kept through notes: the PWM slide
            public int PwmRate, PwmAcc;
            // This tick's output
            public bool Sounds, Tone, Noise, Buzz, Fresh;
            public int Period, Level, NoisePeriod, EnvPeriod, BuzzerShape, Timer, Shape = -1;
            public double TimerHz, Duty;
            public int[]? SidSteps, SyncShapes;

            public void ClearNote()
            {
                Transpose = NoiseTranspose = VolumeColumn = ArpX = ArpY = ArpStep = 0;
                PortaRate = PortaAcc = SlideRate = SlideAcc = 0;
                PwmFixed = DigiStarted = Synced = false;
                Array.Clear(Counter); Array.Clear(Value); Array.Clear(Done);
                SpeedCount = 0;
            }

            public void ClearInstrument()
            {
                Array.Clear(Flags); Array.Clear(SeqNo);
                Speed = Width = Buzzer = StartSync = Digi = DigiRate = Volume = Coarse = Fine = 0;
            }
        }

        private static Ym2149? _ym;
        private static YmSongData? _ymData;
        private static readonly YmChannelState[] _ymState = { new(), new(), new() };
        private const int ToneBit = 4, BuzzerBit = 2, TimerBit = 1;

        // The digidrums of each original file, read once
        private static readonly ConditionalWeakTable<string, int[]?[]> _ymDigis = new();

        private static YmSongData CompileYm(TrackerYmSong song, bool dhs = false) => CompileYm(song, null, dhs);

        private static YmSongData CompileYm(TrackerYmSong song, TrackerSong? whole, bool dhs = false)
        {
            var data = new YmSongData
            {
                Dhs = dhs || (whole != null && DhsMode.Is(whole)),
                Song = song,
                Seq = song.Sequences.Select(q => q.Steps.ToArray()).ToArray(),
                Repeat = song.Sequences.Select(q => q.Repeat).ToArray()
            };
            // Places with a loop mark are passed over, but for the first and the last (as the replay does
            // outside its live mode)
            if (whole != null)
            {
                int n = whole.Order.Count;
                data.Skip = Enumerable.Range(0, n).Select(p => p > 0 && p < n - 1 &&
                    Enumerable.Range(0, Math.Min(4, whole.Channels)).Any(c => whole.PatternAt(p, c) == song.LoopMarkPattern)).ToArray();
            }
            data.Digi = _ymDigis.GetValue(song.Original, original =>
            {
                var digi = new int[]?[9];
                try
                {
                    var m = MaxYMiserSong.Find(Convert.FromBase64String(original));
                    if (m != null)
                        for (int i = 0; i < Math.Min(8, m.Digidrums.Count); i++)
                            if (m.Digidrums[i].Length > 0) digi[i + 1] = m.Digidrums[i].Select(v => Math.Clamp((v + 128) >> 4, 0, 15)).ToArray();
                }
                catch (FormatException) { }
                return digi;
            });
            return data;
        }

        private static void StartYm(YmSongData? data)
        {
            _ymData = data;
            _ym = data != null ? new Ym2149(2000000, SampleRate) : null;
            foreach (var s in _ymState)
            {
                s.ClearInstrument();
                s.ClearNote();
                s.InstNumber = s.Note = s.PwmRate = s.PwmAcc = 0;
                s.Sounds = false;
                s.Shape = -1;
            }
        }

        // Whether the chip plays a channel (in YM mode, channels 1 – 3; the fourth is not played)
        // (in a DHS song channel 4 is the DMA's: a sample channel of the tracker's own)
        private static bool YmChannel(int c) => _ymData != null && c < (_ymData.Dhs ? 3 : 4) && !_trackChannels[c].Sfx;

        private static TrackerYmInstrument? YmInstrumentOf(int n, TrackInstrument[] instruments) =>
            n >= 1 && n <= instruments.Length ? instruments[n - 1].Ym : null;

        // A line of a track, as the replay's parsetracker reads it. speed: a new song speed (S); brk: the
        // pattern ends after this line (B).
        private static void YmLine(int c, TrackerCell cell, TrackInstrument[] instruments, ref int? speed, ref bool brk)
        {
            if (c >= 3) return;
            var s = _ymState[c];
            int note = cell.Note == TrackerCell.NoteOff ? 1 : cell.Note < 0 ? 0 : cell.Note;
            char[] fx = { cell.Effect, cell.Effect2 };
            int[] fp = { cell.Param, cell.Param2 };
            bool porta = fx[0] == 'P' || fx[1] == 'P';
            if (cell.Instrument > 0)
            {
                if (cell.Instrument != s.InstNumber)
                {
                    LoadYmInstrument(s, YmInstrumentOf(cell.Instrument, instruments));
                    s.InstNumber = cell.Instrument;
                }
                int portaAcc = s.PortaAcc, slideAcc = s.SlideAcc;
                s.ClearNote();
                s.Fresh = true;
                if (porta) { s.PortaAcc = portaAcc; s.SlideAcc = slideAcc; }
            }
            if (note == 1)
            {
                // Note off: the channel's instrument and note gone
                s.ClearInstrument();
                s.ClearNote();
                s.InstNumber = 0;
            }
            else
            {
                if (note > 1)
                {
                    if (s.Note != 0 && porta)
                    {
                        s.PortaAcc += SquarePeriod(s.Note) - SquarePeriod(note) + s.SlideAcc;
                        s.SlideAcc = 0;
                    }
                    s.Note = note;
                }
                if (cell.Volume > 0) s.VolumeColumn = cell.Volume & 15;
            }
            for (int k = 0; k < 2; k++)
            {
                char e = fx[k];
                int p = fp[k];
                switch (e)
                {
                    case >= '1' and <= '6':
                        s.Flags[e - '1'] = p;
                        s.InstNumber = 0;
                        if (e == '2') s.Done[1] = false;
                        if (e == '3') s.Done[2] = false;
                        if (e == '5') s.Done[5] = false;
                        break;
                    case '7':
                        s.PwmFixed = false;
                        if (p == 0x80) s.PwmRate = s.PwmAcc = 0;
                        else s.PwmRate = (sbyte)p;
                        break;
                    case '8': SetSequence(s, 7, p); break;
                    case '9': s.Width = p; s.PwmFixed = true; s.InstNumber = 0; break;
                    case 'A': SetSequence(s, 1, p); break;
                    case 'B': brk = true; break;
                    case 'C': s.StartSync = p; s.Synced = false; s.InstNumber = 0; break;
                    case 'D': s.Coarse = (sbyte)p; s.InstNumber = 0; break;
                    case 'E': s.Fine = (sbyte)p; s.InstNumber = 0; break;
                    case 'F': SetSequence(s, 5, p); break;
                    case 'G': if (p <= 8) { s.Digi = p; s.InstNumber = 0; } break;
                    case 'H': s.SlideRate = -(sbyte)p; s.PortaRate = 0; break;
                    case 'I': s.SeqNo[6] = p; s.InstNumber = 0; break;
                    case 'L': SetSequence(s, 0, p); break;
                    case 'M': SetSequence(s, 3, p); break;
                    case 'N': SetSequence(s, 4, p); break;
                    case 'O': s.NoiseTranspose = (sbyte)p; break;
                    case 'P': s.PortaRate = s.PortaAcc < 0 ? -p : p; s.SlideRate = 0; break;
                    case 'Q': s.Speed = p; s.InstNumber = 0; break;
                    case 'R': if (p is >= 0x18 and <= 0xCD) { s.DigiRate = p; s.InstNumber = 0; } break;
                    case 'S': if (p >= 2) speed = p; break;
                    case 'T': s.Transpose = (sbyte)p; break;
                    case 'V': SetSequence(s, 2, p); break;
                    case 'W': s.Buzzer = p & 15; s.Synced = false; s.InstNumber = 0; break;
                    case 'X': s.ArpX = p >> 4; s.ArpY = p & 15; s.ArpStep = 0; s.Done[1] = false; break;
                }
            }
        }

        private static void SetSequence(YmChannelState s, int i, int number)
        {
            s.SeqNo[i] = number;
            s.Counter[i] = 0;
            s.Done[i] = false;
            s.InstNumber = 0;
        }

        private static void LoadYmInstrument(YmChannelState s, TrackerYmInstrument? y)
        {
            s.ClearInstrument();
            if (y == null) return;
            s.Flags[0] = y.Portamento; s.Flags[1] = y.Arpeggio; s.Flags[2] = y.Vibrato;
            s.Flags[3] = y.Transpose; s.Flags[4] = y.FixedFrequency; s.Flags[5] = y.FixedDetune;
            s.Speed = y.Speed; s.Width = y.PulseWidth; s.Buzzer = y.BuzzerWave; s.StartSync = y.StartSync;
            s.Digi = y.DigiSample; s.DigiRate = y.DigiRate; s.Volume = y.Volume; s.Coarse = y.DetuneCoarse; s.Fine = y.DetuneFine;
            s.SeqNo[0] = y.VolumeSeq; s.SeqNo[1] = y.ArpeggioSeq; s.SeqNo[2] = y.VibratoSeq; s.SeqNo[3] = y.MixerSeq;
            s.SeqNo[4] = y.NoiseSeq; s.SeqNo[5] = y.FixedSeq; s.SeqNo[6] = y.TimerSeq; s.SeqNo[7] = y.PwmSeq;
        }

        // A note tried on the keys: the instrument from its start
        private static void YmPreview(int c, TrackerYmInstrument? y, int note)
        {
            if (c >= 3) return;
            var s = _ymState[c];
            LoadYmInstrument(s, y);
            s.InstNumber = 0;
            s.ClearNote();
            s.Fresh = true;
            s.Note = note;
        }

        private static void YmRelease(int c)
        {
            if (c >= 3) return;
            var s = _ymState[c];
            s.ClearInstrument();
            s.ClearNote();
            s.Note = 0;
        }

        // The tone period of a note (maxYMiser's table: the note's number is MIDI's)
        private static int SquarePeriod(double note) => (int)Math.Round(125000.0 / (440.0 * Math.Pow(2, (note - 69) / 12.0)));

        // One call of the replay for a channel
        private static void YmTick(int c)
        {
            if (c >= 3) return;
            var s = _ymState[c];
            var data = _ymData!;
            if (s.Note != 0) StepYmSequences(s, data);
            YmOutput(c, s, data);
            // adjportslidacc: the PWM slide, the pitch slide and the portamento move on
            if (s.PwmRate != 0) s.PwmAcc = Math.Clamp(s.PwmAcc + s.PwmRate, -255, 255);
            if (s.Flags[0] != 0)
            {
                s.SlideAcc += s.SlideRate;
                if (s.PortaRate != 0)
                {
                    s.PortaAcc -= s.PortaRate;
                    if (s.PortaRate < 0 ? s.PortaAcc >= 0 : s.PortaAcc <= 0) s.PortaRate = s.PortaAcc = 0;
                }
            }
        }

        // The sequences one step on, every Speed calls, as dosequences (with the extra arpeggio in the
        // arpeggio's step)
        private static void StepYmSequences(YmChannelState s, YmSongData data)
        {
            bool step = s.SpeedCount == 0;
            int speed = Math.Max(1, s.Speed);
            s.SpeedCount = s.SpeedCount + 1 >= speed ? 0 : s.SpeedCount + 1;
            if (!step) return;
            for (int i = 0; i < 8; i++)
            {
                if (i == 6) continue;
                if (!s.Done[i])
                {
                    bool on = i switch { 1 => s.Flags[1] != 0, 2 => s.Flags[2] != 0, 5 => s.Flags[4] != 0, _ => true };
                    int n = s.SeqNo[i];
                    var q = on && n > 0 && n < data.Seq.Length ? data.Seq[n] : null;
                    if (q == null || q.Length == 0)
                    {
                        s.Value[i] = 0;
                        s.Done[i] = true;
                    }
                    else
                    {
                        s.Value[i] = q[Math.Min(s.Counter[i], q.Length - 1)];
                        if (++s.Counter[i] >= q.Length)
                        {
                            s.Counter[i] = data.Repeat[n];
                            if (data.Repeat[n] == q.Length - 1) s.Done[i] = true;
                        }
                    }
                }
                if (i == 1 && (s.ArpX != 0 || s.ArpY != 0))
                {
                    // The extra arpeggio (X): 0, x, y round and round (0, y with x = F); the arpeggio never ends
                    s.Done[1] = false;
                    int add = 0;
                    if (s.ArpStep == 0) s.ArpStep = 1;
                    else if (s.ArpStep == 1)
                    {
                        if (s.ArpX == 15) { add = s.ArpY; s.ArpStep = 0; }
                        else { add = s.ArpX; s.ArpStep = 2; }
                    }
                    else { add = s.ArpY; s.ArpStep = 0; }
                    s.Value[1] = (s.Flags[1] != 0 && s.SeqNo[1] > 0 ? s.Value[1] : 0) + add;
                }
            }
        }

        // What the channel plays this call, from its sequences' values
        private static void YmOutput(int c, YmChannelState s, YmSongData data)
        {
            s.Sounds = false;
            if (s.Note == 0) return;
            int volume = s.Value[0], mix = s.Value[3];
            int level = (volume & 0xFF) - s.VolumeColumn - s.Volume;
            bool old = data.Song.OldReplay;
            if (old)
            {
                // The older replay: the buzzer (bit 4) whatever the volume; F in the volume column silent
                if (mix == 0 || s.VolumeColumn == 15) return;
                if ((mix & 0x10) == 0 && level <= 0) return;
                level = Math.Clamp(level, 0, 15);
            }
            else
            {
                if (volume == 0 || mix == 0 || level <= 0) return;
                level = Math.Min(level, 15);
            }
            bool timerOn = (data.Song.Timers & (4 >> c)) != 0;
            s.Tone = (mix & 0x100) != 0;
            s.Noise = (mix & 0x1000) != 0;
            s.Buzz = old ? (mix & 0x10) != 0 : (mix & 0xF0) != 0;
            s.Timer = timerOn ? mix & 15 : 0;

            // The period of each source (getsqufreq, getbuzzfreq, gettimfreq): the note, its arpeggio,
            // transpose and detune, the portamento and slide (scaled to the note played), the fine
            // detune and vibrato; or a fixed one
            double Period(int bit)
            {
                int fix = s.Value[5];
                if ((s.Flags[4] & bit) != 0 && s.SeqNo[5] > 0 && (fix & 0xFFFF) != 0xFFFF)
                    return (fix & 0xFFF) * (bit == BuzzerBit ? 16 : 1);
                int adjust = 0;
                if ((s.Flags[5] & bit) != 0) adjust += s.Coarse;
                if ((s.Flags[1] & bit) != 0) adjust += s.Value[1];
                if ((s.Flags[3] & bit) != 0) adjust += s.Transpose;
                double p = SquarePeriod(s.Note + adjust);
                if ((s.Flags[0] & bit) != 0 && s.PortaAcc + s.SlideAcc != 0)
                    p += (s.PortaAcc + s.SlideAcc) * Math.Pow(2, -adjust / 12.0);
                if ((s.Flags[5] & bit) != 0) p -= s.Fine;
                if ((s.Flags[2] & bit) != 0) p -= s.Value[2];
                return p;
            }

            s.Period = (int)Math.Round(Period(ToneBit));
            if (s.Tone && (s.Period < 1 || s.Period > 4095)) s.Tone = false;
            s.Level = level;
            s.NoisePeriod = Math.Clamp((s.Value[4] & 0xFF) + s.NoiseTranspose, 0, 31);
            if (s.Buzz || s.Timer == 0xB)
            {
                int nibble = mix >> 4 & 15;
                s.BuzzerShape = nibble <= 1 || !data.Song.ShapeFromMixer ? s.Buzzer & 15 : nibble;
                s.EnvPeriod = Math.Clamp((int)Math.Round(Period(BuzzerBit) / 16), 1, 65535);
                s.Buzz = true;
            }
            s.SidSteps = s.SyncShapes = null;
            s.TimerHz = 0;
            s.Duty = 0.5;
            int timerSeq = s.SeqNo[6];
            var steps = timerSeq > 0 && timerSeq < data.Seq.Length ? data.Seq[timerSeq] : Array.Empty<int>();
            switch (s.Timer)
            {
                case 5:
                    // SID: a round of the timer's steps at the note, each the volume less the step
                    if (steps.Length > 0)
                    {
                        s.SidSteps = steps.Take(16).Select(x => Math.Clamp(level - x, 0, 15)).ToArray();
                        s.TimerHz = 125000.0 / Math.Max(1, Period(TimerBit));
                    }
                    break;
                case 9:
                {
                    // PWM: two volumes (from the timer's steps) for the period ± w / 256 of it
                    int w = Math.Clamp(s.Width + (s.PwmFixed ? 0 : s.PwmAcc) + (s.Value[7] & 0xFF), 0, 255);
                    s.SidSteps = steps.Length >= 2 ? steps.Take(2).Select(x => Math.Clamp(level - x, 0, 15)).ToArray() : new[] { level, 0 };
                    s.Duty = 0.5 * (1 + w / 256.0);
                    s.TimerHz = 125000.0 / Math.Max(1, Period(TimerBit));
                    break;
                }
                case 0xB:
                    // Sync buzzer: the timer at twice the note writes the shapes of its steps in turn
                    s.TimerHz = 2 * 125000.0 / Math.Max(1, Period(TimerBit));
                    s.SyncShapes = steps.Length > 0 ? steps.Take(16).Select(x => x & 15).ToArray() : new[] { s.BuzzerShape };
                    break;
            }
            s.Sounds = true;
        }

        // The chip's registers from the three channels, once a tick. The noise and the buzzer are one for
        // the chip: the highest channel that uses them sets them, as maxYMiser's replay does.
        // While set, each frame's registers are handed to it after they are written (for DHS export):
        // registers 0 – 13 as written this frame, and whether 13 was written (it starts the envelope again)
        internal static Action<byte[], bool, bool>? YmRecorder;
        private static readonly byte[] _ymOut = new byte[14];
        private static bool _ymEnvWritten, _ymTimerUsed;

        private static void YmFrame()
        {
            var ym = _ym;
            if (ym == null) return;
            _ymEnvWritten = _ymTimerUsed = false;
            void Put(int r, byte v)
            {
                ym.WriteLive(r, v);
                _ymOut[r] = v;
                if (r == 13) _ymEnvWritten = true;
            }
            int mixer = 0x3F;
            int noise = -1, buzzer = -1;
            for (int c = 0; c < 3; c++)
            {
                var s = _ymState[c];
                if (!s.Sounds || _trackMute[c])
                {
                    Put(8 + c, 0);
                    ym.StopSid(c);
                    continue;
                }
                if (s.Tone) { mixer &= ~(1 << c); Put(c * 2, (byte)s.Period); Put(c * 2 + 1, (byte)(s.Period >> 8)); }
                if (s.Noise) { mixer &= ~(8 << c); noise = c; }
                if (s.Buzz) buzzer = c;
                Put(8 + c, (byte)(s.Buzz ? 16 : s.Level));
                if (s.SidSteps != null && s.TimerHz > 0) ym.StartSidSteps(c, s.TimerHz, s.SidSteps, s.Duty);
                else ym.StopSid(c);
                if (s.Timer == 0xD && !s.DigiStarted && s.Digi is >= 1 and <= 8 && _ymData!.Digi[s.Digi] is { } digi)
                {
                    ym.StartDrum(c, digi.Select(v => (float)Ym2149.VolumeLevel(v)).ToArray(), 614400.0 / Math.Max(0x18, s.DigiRate));
                    s.DigiStarted = true;
                }
            }
            Put(7, (byte)mixer);
            if (noise >= 0) Put(6, (byte)_ymState[noise].NoisePeriod);
            if (buzzer >= 0)
            {
                var s = _ymState[buzzer];
                Put(11, (byte)s.EnvPeriod);
                Put(12, (byte)(s.EnvPeriod >> 8));
                // The shape written starts the envelope: at a new note (with start sync) or another shape
                if ((s.Fresh && (s.StartSync != 0 || s.Shape < 0)) || s.BuzzerShape != s.Shape) Put(13, (byte)s.BuzzerShape);
                s.Shape = s.BuzzerShape;
                if (s.Timer == 0xB && s.TimerHz > 0) ym.StartSyncBuzzer(s.TimerHz, s.SyncShapes ?? new[] { s.BuzzerShape });
                else ym.StopSyncBuzzer();
            }
            else ym.StopSyncBuzzer();
            for (int c = 0; c < 3; c++)
                if (_ymState[c].Sounds && !_trackMute[c] && _ymState[c].Timer != 0) _ymTimerUsed = true;
            foreach (var s in _ymState) s.Fresh = false;
            YmRecorder?.Invoke(_ymOut, _ymEnvWritten, _ymTimerUsed);
        }
    }
}
