using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace AmosLikeBasic
{
    // The tracker plays a TrackerSong on channels 1 – 4 (up to 8 if the song has more), as in ProTracker:
    // every row is split into a number of ticks (speed), and the effects work tick by tick. Everything runs
    // in the audio thread, so the song stays exactly in step. The channels above the song's are free for
    // PLAY MUSIC and sounds while the song plays.
    public partial class ChiptuneSynth
    {
        // ── The song, ready to play ──────────────────────────────────
        // Built in the BASIC thread from a TrackerSong and never changed afterwards, so that the audio thread
        // can read it without locks even if the song is edited while it plays.

        private readonly struct TableStep
        {
            public readonly WaveformType? Wave;
            public readonly double Offset;       // semitones from the note (fractions for vibrato)
            public readonly double? PulseWidth;  // 0 – 1
            public readonly double? Osc2;        // oscillator 2's pitch, semitones from the note

            public TableStep(WaveformType? wave, double offset, double? pulseWidth, double? osc2 = null)
            {
                Wave = wave;
                Offset = offset;
                PulseWidth = pulseWidth;
                Osc2 = osc2;
            }
        }

        private sealed class TrackInstrument
        {
            public WaveformType Wave = WaveformType.Pulse;
            public double Volume = 0.3, PulseWidth = 0.5, PwmDepth, PwmRate;
            public double Attack = 0.002, Decay = 0.1, Sustain = 0.7, Release = 0.1;
            public FilterMode Filter;
            public double Cutoff = 2000, Resonance;
            public double FilterEnvDepth, FilterEnvAttack = 0.005, FilterEnvDecay = 0.2;
            public double VibratoDepth, VibratoRate = 6;
            public int RingSource = -1, SyncSource = -1;
            public WaveformType? Osc2Wave;
            public double Osc2Ratio = 1, Osc2Mix = 0.5;
            public Osc2Mode Osc2Mode;
            public LfoSettings?[] Lfos = NoLfos;
            public double EchoTime = 0.3, EchoFeedback = 0.4, EchoMix;
            public int EchoSyncRows;
            public double Pan;
            public SampleData? Sample;
            public double SampleBaseFreq = 261.63;
            public int LoopStart, LoopEnd;
            public int DefaultVolume = 64;
            public TableStep[] Table = Array.Empty<TableStep>();
            public int TableLoop = -1;
            // A volume envelope with points (null: the ADSR); ticks and levels 0 – 1
            public int[]? EnvTicks;
            public double[] EnvLevels = Array.Empty<double>();
            public int EnvSustain = -1, EnvLoopStart = -1, EnvLoopEnd = -1;
            public double FadeOut;   // seconds, 0 = none
            // The pan envelope (null: none); levels -1 (left) – 1 (right)
            public int[]? PanTicks;
            public double[] PanLevels = Array.Empty<double>();
            public int PanSustain = -1, PanLoopStart = -1, PanLoopEnd = -1;
        }

        private sealed class TrackData
        {
            public TrackerCell[][][] Patterns = Array.Empty<TrackerCell[][]>();   // [pattern][row][channel]
            public int[] Order = Array.Empty<int>();
            public TrackInstrument[] Instruments = Array.Empty<TrackInstrument>();
            public int Speed = 6, Tempo = 125;
            public int Channels = TrackerSong.DefaultChannels;
        }

        private sealed class TrackRequest
        {
            public readonly TrackData? Data;   // null = stop
            public readonly int Order, Row;
            public readonly bool Loop;
            public TrackRequest(TrackData? data, int order, int row, bool loop)
            {
                Data = data;
                Order = order;
                Row = row;
                Loop = loop;
            }
        }

        // ── The state of a tracker channel (owned by the audio thread) ────────

        private sealed class TrackChannel
        {
            public TrackInstrument? Inst;
            public int InstNumber;         // to find the instrument again when the song is updated
            public bool Active;
            public double Note;            // semitones, MIDI scale, can be a fraction while gliding
            public double PortaTarget;
            public int PortaSpeed;
            public int VibSpeed, VibDepth, VibPhase;
            public int TremSpeed, TremDepth, TremPhase;   // effect 7
            public int VibWave, TremWave;                  // E4x / E7x: 0 sine, 1 ramp down, 2 square (+4: not from the start at a new note)
            // EFx: the channel's own copies of the samples it turns over, how fast, and where it is in the loop
            public Dictionary<SampleData, SampleData>? Funked;
            public int FunkSpeed, FunkCounter, FunkPos;
            public bool Glissando;                         // E3x: glides in whole semitones
            public int TremorPos;                          // Txy: where in its on-and-off it is
            public int NoteTicks;                          // ticks since the note started (Jxy goes round from there)
            public int LoopRow, LoopCount;                // effect E6: the row a pattern loop goes back to
            public int Vol = 64;           // 0 – 64 as in ProTracker
            public char Effect;
            public int Param;
            public int TableIndex;
            public double TableOffset;
            public int DelayNote = TrackerCell.NoNote;
            // The volume envelope: where it is, whether the note has been let go, and the fade (1 – 0)
            public int EnvTick, PanEnvTick;
            public bool KeyOff;
            public double Fade = 1;
            // Set with G, H and I. Apply until the channel changes instrument.
            public double? Cutoff, Resonance, PulseWidth, Pan;
            // Sound effect: samples left until the note is released, and the channel's PLAY settings from before
            public bool Sfx;
            public long SfxReleaseLeft;
            public Voice? SavedSettings;
            // A song channel an effect borrows: the song's instrument, back when the effect is over
            public TrackInstrument? SongInst;
            public int SongInstNumber;
        }

        private static readonly TrackInstrument DefaultInstrument = new();
        private static readonly TrackRequest TrackStopRequest = new(null, 0, 0, false);
        // Channels 1 – _trackUsed play the song (4 – 32), the ones above (up to 8) are used by the sound
        // effects (TRACK SFX). A song with 8 or more channels lends channel 8 to an effect while it sounds.
        private static readonly TrackChannel[] _trackChannels = CreateTrackChannels();
        private static int _trackUsed = TrackerSong.DefaultChannels;

        private static TrackChannel[] CreateTrackChannels()
        {
            var a = new TrackChannel[Voices];
            for (int i = 0; i < a.Length; i++) a[i] = new TrackChannel();
            return a;
        }

        private static TrackRequest? _trackPending;
        private static TrackData? _track;
        private static bool _trackLoop;
        private static int _trackOrder, _trackRow, _trackTick, _trackSpeed, _trackTempo;
        private static double _trackTickLeft;
        private static bool _trackJump;
        private static bool _trackPatternLoop;   // the jump is an E6 loop inside the pattern, not to another place
        // Effect EE: the row is played this many more times (its effects, not its notes)
        private static int _trackRowDelay;
        private static bool _trackRepeatingRow;
        private static int _trackJumpOrder, _trackBreakRow;
        private static int _trackPosOrder, _trackPosRow;
        private static double _trackVolume = 1.0;
        private static int _trackGlobal = 64;   // Vxx / Wxy: the song's own volume, 0 – 64
        private static bool _trackLed;          // E0x: the Amiga's filter (E00 on, E01 off)

        // EFx, ProTracker's "invert loop": how fast it goes for x = 0 – F
        private static readonly int[] FunkTable = { 0, 5, 6, 7, 8, 10, 11, 13, 16, 19, 22, 26, 32, 43, 64, 128 };
        private static bool _trackPreview;   // the editor previews notes without a song playing

        // Changes from the editor that the audio thread makes at the next sample
        private static readonly ConcurrentQueue<Action> _trackCommands = new();
        private static readonly bool[] _trackMute = new bool[Voices];

        // ════════════════════════════════════════════════════════════════
        //  From BASIC and the editor
        // ════════════════════════════════════════════════════════════════

        // Plays the song from a place in the order list. With loop it starts again when it ends.
        public static void TrackPlay(TrackerSong song, int order = 0, int row = 0, bool loop = true)
        {
            var data = CompileSong(song);
            order = Math.Clamp(order, 0, data.Order.Length - 1);
            Volatile.Write(ref _trackPending, new TrackRequest(data, order, Math.Max(0, row), loop));
            EnsureStarted();
        }

        // Plays a single pattern over and over, for the editor; with position, what that place in the
        // order list plays (each channel from its own pattern)
        public static void TrackPlayPattern(TrackerSong song, int pattern, int row = 0, int? position = null)
        {
            var data = CompileSong(song, pattern, position);
            Volatile.Write(ref _trackPending, new TrackRequest(data, 0, Math.Max(0, row), true));
            EnsureStarted();
        }

        // Switches to a changed version of the song without starting again. With pattern only that pattern plays.
        public static void TrackUpdate(TrackerSong song, int? pattern = null, int? position = null)
        {
            var data = CompileSong(song, pattern, position);
            _trackCommands.Enqueue(() => ApplyTrackUpdate(data));
        }

        // Previews a note with an instrument (from 1) on a channel of the song, until TrackPreviewRelease
        public static void TrackPreview(TrackerSong song, int instrument, int note, int channel)
        {
            if (channel < 1 || channel > song.Channels) return;
            int used = song.Channels;
            var inst = instrument >= 1 && instrument <= song.Instruments.Count
                ? CompileInstrument(song.Instruments[instrument - 1], instrument) : DefaultInstrument;
            int tempo = Math.Clamp(song.Tempo, 32, 255);
            _trackCommands.Enqueue(() =>
            {
                int c = channel - 1;
                var ch = _trackChannels[c];
                if (_track == null)
                {
                    _trackPreview = true;
                    _trackTempo = tempo;
                    _trackTick = 0;
                    _trackUsed = used;
                }
                ch.Inst = inst;
                ch.InstNumber = instrument;
                ch.Vol = 64;
                ch.Effect = '\0';
                ch.Cutoff = ch.Resonance = ch.PulseWidth = ch.Pan = null;
                TriggerTrackNote(c, note);
            });
            EnsureStarted();
        }

        public static void TrackPreviewRelease(int channel)
        {
            if (channel < 1 || channel > TrackerSong.MaxChannels) return;
            _trackCommands.Enqueue(() => ReleaseTrackVoice(channel - 1));
        }

        // Releases the channels after previewing, so that the PLAY commands get them back
        public static void TrackPreviewStop()
        {
            _trackCommands.Enqueue(() =>
            {
                if (!_trackPreview) return;
                _trackPreview = false;
                for (int c = 0; c < _trackUsed; c++)
                    if (!_trackChannels[c].Sfx) _trackChannels[c].Active = false;
                ReleaseTrackVoices();
            });
        }

        // ════════════════════════════════════════════════════════════════
        //  Sound effects: a tracker instrument played for a short while
        // ════════════════════════════════════════════════════════════════

        public const int FirstSfxChannel = 5;
        private const double SfxTickRate = 50;   // like a C64, independent of the song's tempo
        private static double _sfxVolume = 1.0;
        private static double _sfxTickLeft;
        private static int _sfxCount;            // channels playing an effect (audio thread)
        private static readonly long[] _sfxEnds = new long[Channels];   // when the effect is expected to be done (BASIC thread)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrackerInstrument, TrackInstrument> _sfxInstruments = new();

        // Plays the instrument as a sound effect. Without a channel, the one of the channels above the song's
        // (5 – 8 with a 4-channel song) that frees up first is chosen; with an 8-channel song, channel 8.
        // Without a length (ms < 0) the sound lasts as long as the instrument is made for. Returns the channel.
        public static int PlayTrackSfx(TrackerInstrument instrument, int number, int note, double ms = -1, int channel = 0)
        {
            var inst = _sfxInstruments.GetValue(instrument, i => CompileInstrument(i, number));
            if (ms < 0) ms = AutoSfxLength(inst) * 1000;
            long now = Stopwatch.GetTimestamp();
            if (channel < 1 || channel > Channels)
            {
                int first = Volatile.Read(ref _track) != null ? Math.Max(FirstSfxChannel, Volatile.Read(ref _trackUsed) + 1) : FirstSfxChannel;
                first = Math.Min(first, Channels);
                channel = first;
                for (int c = first; c <= Channels; c++)
                    if (_sfxEnds[c - 1] < _sfxEnds[channel - 1]) channel = c;
            }
            long releaseSamples = (long)(Math.Max(1, ms) / 1000.0 * SampleRate);
            _sfxEnds[channel - 1] = now + (long)((ms / 1000.0 + inst.Release) * Stopwatch.Frequency);

            int c0 = channel - 1;
            _trackCommands.Enqueue(() =>
            {
                var ch = _trackChannels[c0];
                var v = _voices[c0];
                if (!ch.Sfx)
                {
                    ch.SavedSettings = v.CopySettings();
                    ch.SongInst = ch.Inst;
                    ch.SongInstNumber = ch.InstNumber;
                    ch.Sfx = true;
                    // The first effect starts the clock, so that a sound always sounds the same
                    if (_sfxCount++ == 0) _sfxTickLeft = SampleRate / SfxTickRate;
                    Volatile.Write(ref v.Seq, null);   // the effect takes the channel over from PLAY MUSIC
                }
                ch.Inst = inst;
                ch.InstNumber = number;
                ch.Vol = 64;
                ch.Effect = '\0';
                ch.Cutoff = ch.Resonance = ch.PulseWidth = ch.Pan = null;
                ch.SfxReleaseLeft = releaseSamples;
                TriggerTrackNote(c0, note);
                UpdateTrackVoice(c0);
            });
            EnsureStarted();
            return channel;
        }

        // How long the note is held if not given: a sound that fades by itself (sustain 0)
        // may fade out fully, one with a table without a loop is held until the table ends, otherwise a quarter of a second
        private static double AutoSfxLength(TrackInstrument inst)
        {
            if (inst.EnvTicks is { } ticks)
                return inst.EnvSustain < 0 && inst.EnvLoopStart < 0 ? ticks[^1] / SfxTickRate : 0.25;
            if (inst.Sustain <= 0) return inst.Attack + inst.Decay;
            if (inst.Table.Length > 1 && inst.TableLoop < 0) return inst.Table.Length / SfxTickRate;
            return 0.25;
        }

        public static void SetSfxVolume(double volume) => _sfxVolume = Math.Clamp(volume, 0, 1);

        // With restore = false the channels' settings are left as they are (when a program restarts)
        public static void StopSfx(bool restore = true) => _trackCommands.Enqueue(() =>
        {
            for (int c = 0; c < Channels; c++)
                if (_trackChannels[c].Sfx) EndSfx(c, restore);
        });

        // Called every sample as long as any effect is playing
        private static void SfxSample()
        {
            for (int c = 0; c < Channels; c++)
            {
                var ch = _trackChannels[c];
                if (!ch.Sfx) continue;
                if (ch.SfxReleaseLeft > 0 && --ch.SfxReleaseLeft == 0) ReleaseTrackVoice(c);
                else if (ch.SfxReleaseLeft == 0 && _voices[c].Stage == EnvStage.Idle) EndSfx(c);
            }

            if (--_sfxTickLeft > 0) return;
            _sfxTickLeft += SampleRate / SfxTickRate;
            for (int c = 0; c < Channels; c++)
                if (_trackChannels[c].Sfx) UpdateTrackVoice(c);
        }

        // The effect is over: give the channel back to the PLAY commands
        private static void EndSfx(int c, bool restore = true)
        {
            var ch = _trackChannels[c];
            var v = _voices[c];
            ch.Sfx = false;
            ch.Active = false;
            _sfxCount--;
            if (v.Stage != EnvStage.Idle) v.Stage = EnvStage.Release;
            if (restore && ch.SavedSettings != null) v.RestoreSettings(ch.SavedSettings);
            ch.SavedSettings = null;
            // A borrowed song channel goes on with the song's instrument at its next note
            ch.Inst = ch.SongInst;
            ch.InstNumber = ch.SongInstNumber;
            ch.SongInst = null;
            ch.SongInstNumber = 0;
        }

        // Silence at once: the song, the notes being tried, the sound effects, PLAY MUSIC, and every
        // voice and its echo. For when something will not stop.
        public static void Panic()
        {
            TrackStop();
            PlayChipMusic(null);
            StopMusic();
            StopSfx(restore: false);
            TrackPreviewStop();
            _trackCommands.Enqueue(() =>
            {
                for (int c = 0; c < Voices; c++)
                {
                    var v = _voices[c];
                    v.Stage = EnvStage.Idle;
                    v.Drum = null;
                    Volatile.Write(ref v.Seq, null);
                    v.EchoMix = 0;
                    Array.Clear(v.EchoBuffer);
                    _trackChannels[c].Active = false;
                }
                foreach (var x in _sfx) x.Buffer = null;
            });
            EnsureStarted();
        }

        public static void TrackStop()
        {
            if (Volatile.Read(ref _track) != null || Volatile.Read(ref _trackPending) != null)
                Volatile.Write(ref _trackPending, TrackStopRequest);
        }

        public static bool IsTrackPlaying()
        {
            var pending = Volatile.Read(ref _trackPending);
            if (pending != null) return pending != TrackStopRequest;
            return Volatile.Read(ref _track) != null;
        }

        // The place in the order list and the row being played right now
        public static int TrackPosition => Volatile.Read(ref _trackPosOrder);
        public static int TrackRow => Volatile.Read(ref _trackPosRow);

        // A channel (1 – 8) that sounds with the tracker instrument (from 1) right now, preferring
        // the given one; 0 when none does. Read while the audio thread plays: fine for a picture.
        public static int ChannelWithInstrument(int instrument, int preferred = 0)
        {
            bool Sounds(int c) => _trackChannels[c].Active && _trackChannels[c].InstNumber == instrument
                                  && _voices[c].Stage != EnvStage.Idle;
            if (preferred >= 1 && preferred <= TrackerSong.MaxChannels && Sounds(preferred - 1)) return preferred;
            for (int c = 0; c < TrackerSong.MaxChannels; c++)
                if (Sounds(c)) return c + 1;
            return 0;
        }

        public static void SetTrackVolume(double volume) => _trackVolume = Math.Clamp(volume, 0, 1);

        public static void SetTrackMute(int channel, bool mute)
        {
            if (channel >= 1 && channel <= TrackerSong.MaxChannels) _trackMute[channel - 1] = mute;
        }

        // ════════════════════════════════════════════════════════════════
        //  Building the song
        // ════════════════════════════════════════════════════════════════

        // With onlyPattern the order list is just that pattern
        private static TrackData CompileSong(TrackerSong song, int? onlyPattern = null, int? onlyPosition = null)
        {
            // Channels with an order of their own: each place in the order list as the pattern it plays
            List<TrackerPattern> patterns = song.Patterns;
            int[] order = onlyPattern != null ? new[] { onlyPattern.Value }
                : song.Order.Count > 0 ? song.Order.ToArray() : new[] { 0 };
            if (onlyPosition is { } pos && song.Order.Count > 0)
            {
                patterns = new List<TrackerPattern> { song.Composed(Math.Clamp(pos, 0, song.Order.Count - 1)) };
                order = new[] { 0 };
            }
            else if (onlyPattern == null && song.HasChannelOrder)
            {
                patterns = new List<TrackerPattern>();
                var made = new Dictionary<string, int>();
                for (int i = 0; i < song.Order.Count; i++)
                {
                    string key = string.Join(",", Enumerable.Range(0, song.Channels).Select(c => song.PatternAt(i, c)));
                    if (!made.TryGetValue(key, out int p))
                    {
                        made[key] = p = patterns.Count;
                        patterns.Add(song.Composed(i));
                    }
                    order[i] = p;
                }
            }
            var data = new TrackData
            {
                Speed = Math.Clamp(song.Speed, 1, 31),
                Tempo = Math.Clamp(song.Tempo, 32, 255),
                Order = order,
                Patterns = new TrackerCell[patterns.Count][][],
                Instruments = new TrackInstrument[song.Instruments.Count],
                Channels = song.Channels
            };

            for (int p = 0; p < patterns.Count; p++)
            {
                var pat = patterns[p];
                var rows = new TrackerCell[pat.Rows][];
                for (int r = 0; r < pat.Rows; r++)
                {
                    rows[r] = new TrackerCell[song.Channels];
                    for (int c = 0; c < song.Channels; c++) rows[r][c] = pat[r, c];
                }
                data.Patterns[p] = rows;
            }
            if (data.Patterns.Length == 0)
                data.Patterns = new[] { new[] { new TrackerCell[song.Channels] } };
            for (int i = 0; i < data.Order.Length; i++)
                data.Order[i] = Math.Clamp(data.Order[i], 0, data.Patterns.Length - 1);

            for (int i = 0; i < song.Instruments.Count; i++)
                data.Instruments[i] = CompileInstrument(song.Instruments[i], i + 1);
            return data;
        }

        // Throws FormatException if the instrument has a bad table step
        public static void ValidateInstrument(TrackerInstrument instrument, int number) =>
            CompileInstrument(instrument, number);

        private const double MinAttack = 0.0015;

        private static TrackInstrument CompileInstrument(TrackerInstrument src, int number)
        {
            var steps = new List<TableStep>();
            foreach (var text in src.Table ?? new List<string>())
                steps.Add(ParseTableStep(text, number));

            var wave = ParseWave(src.Wave ?? "Pulse");
            // Points in tick order; the first is at tick 0
            var env = (src.Envelope ?? new()).Where(p => p != null).OrderBy(p => p.Tick).ToList();
            bool points = env.Count >= 2;
            var pan = (src.PanEnvelope ?? new()).Where(p => p != null).OrderBy(p => p.Tick).ToList();
            // A sampled instrument plays its file instead of oscillator 1 (a missing file is silent)
            var sample = (src.Wave ?? "").Equals("Sample", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(src.Sample)
                ? SampleBank.Get(src.Sample) ?? new SampleData(Array.Empty<float>(), 44100) : null;
            return new TrackInstrument
            {
                Wave = wave == WaveformType.Drums ? WaveformType.Noise : wave,
                Volume = Math.Clamp(src.Volume, 0, 1),
                PulseWidth = Math.Clamp(src.PulseWidth / 100.0, 0.01, 0.99),
                PwmDepth = Math.Clamp(src.PwmDepth / 100.0, 0, 0.49),
                PwmRate = Math.Max(0, src.PwmRate),
                // With an envelope of points the voice's own one is only there to start and end without a click
                // At least 1.5 ms: a sound that starts at full volume at once (a sample not starting at 0) clicks
                Attack = Math.Max(MinAttack, points ? 0 : src.Attack / 1000.0),
                Decay = points ? 0 : Math.Max(0, src.Decay) / 1000.0,
                Sustain = points ? 1 : Math.Clamp(src.Sustain / 100.0, 0, 1),
                Release = points ? 0.005 : Math.Max(0, src.Release) / 1000.0,
                EnvTicks = points ? env.Select((p, k) => k == 0 ? 0 : Math.Clamp(p.Tick, 0, 65535)).ToArray() : null,
                EnvLevels = points ? env.Select(p => Math.Clamp(p.Level, 0, 64) / 64.0).ToArray() : Array.Empty<double>(),
                EnvSustain = points && src.EnvelopeSustain < env.Count ? src.EnvelopeSustain : -1,
                EnvLoopStart = points && src.EnvelopeLoopStart >= 0 && src.EnvelopeLoopStart <= src.EnvelopeLoopEnd && src.EnvelopeLoopEnd < env.Count ? src.EnvelopeLoopStart : -1,
                EnvLoopEnd = points && src.EnvelopeLoopStart >= 0 && src.EnvelopeLoopStart <= src.EnvelopeLoopEnd && src.EnvelopeLoopEnd < env.Count ? src.EnvelopeLoopEnd : -1,
                FadeOut = Math.Clamp(src.FadeOut, 0, 60000) / 1000.0,
                PanTicks = pan.Count >= 2 ? pan.Select((p, k) => k == 0 ? 0 : Math.Clamp(p.Tick, 0, 65535)).ToArray() : null,
                PanLevels = pan.Count >= 2 ? pan.Select(p => (Math.Clamp(p.Level, 0, 64) - 32) / 32.0).ToArray() : Array.Empty<double>(),
                PanSustain = pan.Count >= 2 && src.PanEnvelopeSustain < pan.Count ? src.PanEnvelopeSustain : -1,
                PanLoopStart = pan.Count >= 2 && src.PanEnvelopeLoopStart >= 0 && src.PanEnvelopeLoopStart <= src.PanEnvelopeLoopEnd && src.PanEnvelopeLoopEnd < pan.Count ? src.PanEnvelopeLoopStart : -1,
                PanLoopEnd = pan.Count >= 2 && src.PanEnvelopeLoopStart >= 0 && src.PanEnvelopeLoopStart <= src.PanEnvelopeLoopEnd && src.PanEnvelopeLoopEnd < pan.Count ? src.PanEnvelopeLoopEnd : -1,
                Filter = ParseFilterMode(src.Filter ?? "Off"),
                Cutoff = Math.Clamp(src.Cutoff, 20, 18000),
                Resonance = Math.Clamp(src.Resonance / 100.0, 0, 0.98),
                FilterEnvDepth = Math.Clamp(src.FilterEnvDepth, -18000, 18000),
                FilterEnvAttack = Math.Max(0, src.FilterEnvAttack) / 1000.0,
                FilterEnvDecay = Math.Max(1, src.FilterEnvDecay) / 1000.0,
                VibratoDepth = Math.Max(0, src.Vibrato) / 100.0,
                VibratoRate = Math.Max(0, src.VibratoRate),
                RingSource = src.Ring <= 0 ? -1 : Math.Clamp(src.Ring - 1, 0, Voices - 1),
                SyncSource = src.Sync <= 0 ? -1 : Math.Clamp(src.Sync - 1, 0, Voices - 1),
                Osc2Wave = CompileOsc2Wave(src.Osc2Wave),
                Osc2Ratio = Math.Pow(2, (Math.Clamp(src.Osc2Semitones, -48, 48) + Math.Clamp(src.Osc2Detune, -1200, 1200) / 100.0) / 12.0),
                Osc2Mix = Math.Clamp(src.Osc2Mix / 100.0, 0, 1),
                Osc2Mode = (src.Osc2Mode ?? "").ToUpperInvariant() switch { "RING" => Osc2Mode.Ring, "SYNC" => Osc2Mode.Sync, _ => Osc2Mode.Mix },
                Lfos = new[] { CompileLfo(src.Lfo1), CompileLfo(src.Lfo2) },
                EchoTime = Math.Clamp(src.EchoTime, 1, MaxEchoSeconds * 1000) / 1000.0,
                EchoSyncRows = Math.Clamp(src.EchoSyncRows, 0, 64),
                EchoFeedback = Math.Clamp(src.EchoFeedback / 100.0, 0, 0.9),
                EchoMix = Math.Clamp(src.EchoMix / 100.0, 0, 1),
                Pan = Math.Clamp(src.Pan / 100.0, -1, 1),
                Sample = sample,
                DefaultVolume = Math.Clamp(src.DefaultVolume, 0, 64),
                SampleBaseFreq = 440.0 * Math.Pow(2, (Math.Clamp(src.SampleNote, 0, 127) - 69) / 12.0),
                // A missing sample is empty: silent, and no loop to keep inside it
                LoopStart = sample == null || sample.Data.Length == 0 ? 0 : Math.Clamp(src.LoopStart, 0, sample.Data.Length - 1),
                LoopEnd = sample == null || sample.Data.Length == 0 || src.LoopEnd <= src.LoopStart ? 0 : Math.Min(src.LoopEnd, sample.Data.Length),
                Table = steps.ToArray(),
                TableLoop = src.TableLoop < steps.Count ? src.TableLoop : -1
            };
        }

        private static WaveformType? CompileOsc2Wave(string? wave)
        {
            if (string.IsNullOrWhiteSpace(wave) || wave.Equals("Off", StringComparison.OrdinalIgnoreCase)) return null;
            var w = ParseWave(wave);
            return w == WaveformType.Drums ? WaveformType.Noise : w;
        }

        // The LFO in the synth's units (null when it does nothing)
        private static LfoSettings? CompileLfo(TrackerLfo? src) =>
            src == null ? null : MakeLfo(src.Target, src.Shape, src.Depth, src.Rate, src.SyncRows, src.Delay, src.Retrigger);

        // An LFO from its names and the depth in the target's unit (cents for pitch, semitones for the
        // cutoff, % otherwise), as the tracker and PLAY LFO give them; null when it does nothing
        public static LfoSettings? MakeLfo(string? targetName, string? shapeName, double depthIn, double rateHz,
                                           int syncRows, double delayMs, bool retrigger)
        {
            if (depthIn == 0) return null;
            var target = (targetName ?? "Off").Replace(" ", "").ToUpperInvariant() switch
            {
                "PITCH" => LfoTarget.Pitch,
                "VOLUME" => LfoTarget.Volume,
                "CUTOFF" => LfoTarget.Cutoff,
                "RESONANCE" => LfoTarget.Resonance,
                "PULSEWIDTH" => LfoTarget.PulseWidth,
                "OSC2PITCH" => LfoTarget.Osc2Pitch,
                "OSC2MIX" => LfoTarget.Osc2Mix,
                "PAN" => LfoTarget.Pan,
                _ => LfoTarget.Off
            };
            if (target == LfoTarget.Off) return null;
            var shape = (shapeName ?? "Sine").Replace(" ", "").ToUpperInvariant() switch
            {
                "TRIANGLE" => LfoShape.Triangle,
                "SQUARE" => LfoShape.Square,
                "SAWUP" or "SAW" => LfoShape.SawUp,
                "SAWDOWN" => LfoShape.SawDown,
                "RANDOM" => LfoShape.Random,
                _ => LfoShape.Sine
            };
            // Cents for pitch become semitones, procent becomes 0 – 1; the cutoff is in semitones already
            double depth = target switch
            {
                LfoTarget.Pitch or LfoTarget.Osc2Pitch => Math.Clamp(depthIn, -4800, 4800) / 100.0,
                LfoTarget.Cutoff => Math.Clamp(depthIn, -96, 96),
                LfoTarget.PulseWidth => Math.Clamp(depthIn, -49, 49) / 100.0,
                _ => Math.Clamp(depthIn, -100, 100) / 100.0
            };
            return new LfoSettings(shape, target, Math.Clamp(rateHz, 0, 100), Math.Clamp(syncRows, 0, 256),
                                   depth, Math.Max(0, delayMs) / 1000.0, retrigger);
        }

        // "NOISE +24", "PULSE", "-12", "+0.25" (a quarter of a semitone), "P30", "TRIANGLE 0 P50", "O-12" (osc 2)
        private static TableStep ParseTableStep(string text, int instrument)
        {
            WaveformType? wave = null;
            double offset = 0;
            double? pulse = null, osc2 = null;
            foreach (var word in text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n))
                    offset = Math.Clamp(n, -96, 96);
                else if ((word[0] == 'P' || word[0] == 'p') && word.Length > 1 &&
                         double.TryParse(word[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out double pw))
                    pulse = Math.Clamp(pw / 100.0, 0.01, 0.99);
                else if ((word[0] == 'O' || word[0] == 'o') && word.Length > 1 &&
                         double.TryParse(word[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out double o2))
                    osc2 = Math.Clamp(o2, -96, 96);
                else if (word.ToUpperInvariant() is "SQUARE" or "PULSE" or "TRIANGLE" or "SAWTOOTH" or "SAW" or "SINE" or "NOISE"
                         or "PULSESAW" or "PULSETRI" or "SAWTRI" or "PULSESAWTRI")
                    wave = ParseWave(word);
                else
                    throw new FormatException($"Instrument {instrument:X2}: '{text}' is not a table step");
            }
            return new TableStep(wave, offset, pulse, osc2);
        }

        // ════════════════════════════════════════════════════════════════
        //  The audio thread
        // ════════════════════════════════════════════════════════════════

        // Called once per sample, before the voices
        private static void TrackSample()
        {
            var req = Volatile.Read(ref _trackPending);
            if (req != null)
            {
                Interlocked.CompareExchange(ref _trackPending, null, req);
                StartTrack(req);
            }
            while (_trackCommands.TryDequeue(out var cmd)) cmd();
            if (_sfxCount > 0) SfxSample();
            if (_track == null && !_trackPreview) return;

            _trackTickLeft--;
            if (_trackTickLeft > 0) return;
            TrackTick();
            // ProTracker: tempo 125 gives 50 ticks per second
            _trackTickLeft += SampleRate / (_trackTempo * 0.4);
        }

        private static void StartTrack(TrackRequest req)
        {
            if (req.Data == null)
            {
                if (_track != null) ReleaseTrackVoices();
                Volatile.Write(ref _track, null);
                return;
            }

            // Channels a sound effect is using right now are left alone; the song takes them over afterwards
            if (_track != null) ReleaseTrackVoices();
            _trackUsed = req.Data.Channels;
            for (int c = 0; c < _trackUsed; c++)
            {
                if (_trackChannels[c].Sfx) { _trackChannels[c].SongInst = null; _trackChannels[c].SongInstNumber = 0; continue; }
                var v = _voices[c];
                Volatile.Write(ref v.Seq, null);
                v.Drum = null;
                if (v.Stage != EnvStage.Idle) v.Stage = EnvStage.Release;
                var ch = _trackChannels[c];
                ch.Inst = null;
                ch.InstNumber = 0;
                ch.Active = false;
                ch.Vol = 64;
                ch.Effect = '\0';
                ch.VibSpeed = ch.VibDepth = ch.VibPhase = ch.PortaSpeed = 0;
                ch.TremSpeed = ch.TremDepth = ch.TremPhase = ch.LoopRow = ch.LoopCount = 0;
                ch.VibWave = ch.TremWave = ch.TremorPos = 0;
                ch.Funked = null;
                ch.FunkSpeed = ch.FunkCounter = ch.FunkPos = 0;
                ch.Glissando = false;
                ch.DelayNote = TrackerCell.NoNote;
                ch.Cutoff = ch.Resonance = ch.PulseWidth = ch.Pan = null;
            }

            _trackPreview = false;
            _trackGlobal = 64;
            _trackLed = false;
            _trackLoop = req.Loop;
            _trackSpeed = req.Data.Speed;
            _trackTempo = req.Data.Tempo;
            _trackOrder = req.Order;
            _trackRow = Math.Min(req.Row, req.Data.Patterns[req.Data.Order[_trackOrder]].Length - 1);
            _trackTick = 0;
            _trackTickLeft = 0;
            _trackJump = false;
            _trackRowDelay = 0;
            _trackRepeatingRow = false;
            Volatile.Write(ref _track, req.Data);
        }

        private static void ApplyTrackUpdate(TrackData data)
        {
            var old = _track;
            if (old == null) return;
            if (data.Speed != old.Speed) _trackSpeed = data.Speed;
            if (data.Tempo != old.Tempo) _trackTempo = data.Tempo;
            if (data.Channels < _trackUsed)
            {
                // Fewer channels: the ones taken away fall silent
                for (int c = data.Channels; c < _trackUsed; c++)
                {
                    var gone = _trackChannels[c];
                    if (gone.Sfx) continue;
                    gone.Active = false;
                    var v = _voices[c];
                    if (v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain) v.Stage = EnvStage.Release;
                }
            }
            _trackUsed = data.Channels;
            for (int c = 0; c < _trackUsed; c++)
            {
                var ch = _trackChannels[c];
                ref var number = ref ch.Sfx ? ref ch.SongInstNumber : ref ch.InstNumber;
                if (number <= 0) continue;
                var inst = number <= data.Instruments.Length ? data.Instruments[number - 1] : DefaultInstrument;
                if (ch.Sfx) ch.SongInst = inst; else ch.Inst = inst;
            }
            if (_trackOrder >= data.Order.Length) _trackOrder = 0;
            if (_trackRow >= data.Patterns[data.Order[_trackOrder]].Length) _trackRow = 0;
            if (_trackJump && _trackJumpOrder >= data.Order.Length) _trackJumpOrder = 0;
            Volatile.Write(ref _track, data);
        }

        private static void ReleaseTrackVoices()
        {
            for (int c = 0; c < _trackUsed; c++)
            {
                if (_trackChannels[c].Sfx) continue;
                var v = _voices[c];
                if (v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain) v.Stage = EnvStage.Release;
            }
        }

        private static void TrackTick()
        {
            var d = _track;
            if (d == null)
            {
                // Only previewing: the instruments' tables should still run
                for (int c = 0; c < _trackUsed; c++)
                    if (!_trackChannels[c].Sfx) UpdateTrackVoice(c);
                return;
            }
            // A row repeated by EE plays its effects again, but not its notes
            if (_trackTick == 0 && !_trackRepeatingRow) ReadRow(d);
            else
                for (int c = 0; c < d.Channels; c++)
                    if (!_trackChannels[c].Sfx) TickEffects(c);

            for (int c = 0; c < d.Channels; c++)
                if (!_trackChannels[c].Sfx) UpdateTrackVoice(c);

            if (++_trackTick >= _trackSpeed)
            {
                _trackTick = 0;
                if (_trackRowDelay > 0)
                {
                    _trackRowDelay--;
                    _trackRepeatingRow = true;
                }
                else
                {
                    _trackRepeatingRow = false;
                    AdvanceRow(d);
                }
            }
        }

        private static void ReadRow(TrackData d)
        {
            var row = d.Patterns[d.Order[_trackOrder]][_trackRow];
            Volatile.Write(ref _trackPosOrder, _trackOrder);
            Volatile.Write(ref _trackPosRow, _trackRow);

            int? jumpOrder = null, breakRow = null;
            bool patternLoop = false;
            for (int c = 0; c < d.Channels; c++)
            {
                var cell = row[c];
                var ch = _trackChannels[c];
                char eff = cell.Effect;
                int p = cell.Param, x = p >> 4, y = p & 15;
                if (ch.Sfx)
                {
                    // A sound effect has the channel: skip its notes, but the song still jumps where it says
                    if (eff == 'B') jumpOrder = p;
                    else if (eff == 'D') breakRow = x * 10 + y;
                    continue;
                }
                ch.Effect = eff;
                ch.Param = p;
                ch.DelayNote = TrackerCell.NoNote;

                if (cell.Instrument > 0)
                {
                    var inst = cell.Instrument <= d.Instruments.Length ? d.Instruments[cell.Instrument - 1] : DefaultInstrument;
                    if (inst != ch.Inst) ch.Cutoff = ch.Resonance = ch.PulseWidth = ch.Pan = null;
                    ch.Inst = inst;
                    ch.InstNumber = cell.Instrument;
                    ch.Vol = inst.DefaultVolume;
                }
                if (cell.Volume >= 0) ch.Vol = cell.Volume;   // the volume column; a C effect on the row wins

                if (cell.Note == TrackerCell.NoteOff)
                    ReleaseTrackVoice(c);
                else if (cell.Note >= 0)
                {
                    if ((eff == '3' || eff == '5') && ch.Active)
                        ch.PortaTarget = cell.Note;
                    else if (eff == 'E' && x == 0xD && y > 0)
                        ch.DelayNote = cell.Note;   // played on tick y
                    else
                        TriggerTrackNote(c, cell.Note);
                }

                switch (eff)
                {
                    case '3':
                        if (p > 0) ch.PortaSpeed = p;
                        break;
                    case '4':
                        if (x > 0) ch.VibSpeed = x;
                        if (y > 0) ch.VibDepth = y;
                        break;
                    case 'B':
                        jumpOrder = p;
                        break;
                    case 'C':
                        ch.Vol = Math.Min(p, 64);
                        break;
                    case 'D':
                        breakRow = x * 10 + y;   // as in ProTracker: decimal
                        break;
                    case '7':
                        if (x > 0) ch.TremSpeed = x;
                        if (y > 0) ch.TremDepth = y;
                        break;
                    case 'E':
                        switch (x)
                        {
                            case 0x1: ch.Note += y / 16.0; ch.PortaTarget = ch.Note; break;   // fine slide up, once
                            case 0x2: ch.Note -= y / 16.0; ch.PortaTarget = ch.Note; break;   // fine slide down
                            case 0xA: ch.Vol = Math.Min(64, ch.Vol + y); break;               // fine volume up
                            case 0xB: ch.Vol = Math.Max(0, ch.Vol - y); break;                // fine volume down
                            case 0xC: if (y == 0) ch.Vol = 0; break;
                            case 0x3: ch.Glissando = y != 0; break;                            // glissando on/off
                            case 0x0: _trackLed = y == 0; break;                               // the Amiga's filter
                            case 0xF: ch.FunkSpeed = y; if (y > 0) Funk(c); break;              // invert loop
                            case 0x4: ch.VibWave = y; break;                                   // vibrato wave
                            case 0x7: ch.TremWave = y; break;                                  // tremolo wave
                            case 0x5:                                                          // finetune: eighths of a semitone, -8 – 7
                                if (cell.Note >= 0) ch.PortaTarget = ch.Note += (y < 8 ? y : y - 16) / 8.0;
                                break;
                            case 0x6:
                                // Pattern loop: E60 marks the start, E6x plays from there x times more
                                if (y == 0) ch.LoopRow = _trackRow;
                                else
                                {
                                    ch.LoopCount = ch.LoopCount == 0 ? y : ch.LoopCount - 1;
                                    if (ch.LoopCount > 0) { jumpOrder = _trackOrder; breakRow = ch.LoopRow; patternLoop = true; }
                                }
                                break;
                            case 0xE:
                                if (!_trackRepeatingRow) _trackRowDelay = y;   // the row lasts y rows longer
                                break;
                        }
                        break;
                    case 'F':
                        if (p > 0 && p < 0x20) _trackSpeed = p;
                        else if (p >= 0x20) _trackTempo = p;
                        break;
                    case 'V':
                        _trackGlobal = Math.Min(p, 64);                     // the song's volume, 00 – 40
                        break;
                    case 'K':
                        if (p == 0) ReleaseTrackVoice(c);                   // let go now, or on tick xx
                        break;
                    case 'L':
                        ch.EnvTick = p;                                     // the envelope of points from tick xx
                        break;
                    case 'G':
                    {
                        // Filterfrekvens: 00 = 20 Hz, 80 = ca 640 Hz, FF = 18 000 Hz
                        var v = _voices[c];
                        if (v.Filter == FilterMode.Off) v.Filter = FilterMode.Low;
                        v.Cutoff = (ch.Cutoff = Math.Min(18000, 20 * Math.Pow(1000, p / 255.0))).Value;
                        v.CutoffSlideLeft = 0;
                        break;
                    }
                    case 'H':
                        _voices[c].Resonance = (ch.Resonance = p / 255.0 * 0.98).Value;
                        break;
                    case 'I':
                        _voices[c].PulseWidth = (ch.PulseWidth = Math.Clamp(p / 255.0, 0.01, 0.99)).Value;
                        break;
                    case '9':
                        // Sample offset as in ProTracker: the note starts xx × 256 frames into the sample
                        if (cell.Note >= 0) _voices[c].SamplePos = p * 256;
                        break;
                    case '8':
                        // Pan as in ProTracker: 00 left, 80 the middle, FF right
                        _voices[c].Pan = (ch.Pan = Math.Clamp((p - 128) / 127.0, -1, 1)).Value;
                        break;
                }
            }

            if (jumpOrder != null || breakRow != null)
            {
                _trackJump = true;
                _trackJumpOrder = jumpOrder ?? _trackOrder + 1;
                _trackBreakRow = breakRow ?? 0;
                _trackPatternLoop = patternLoop;
            }
        }

        private static void TickEffects(int c)
        {
            var ch = _trackChannels[c];
            int p = ch.Param, x = p >> 4, y = p & 15;
            switch (ch.Effect)
            {
                case '1':
                    ch.Note += p / 16.0;
                    break;
                case '2':
                    ch.Note -= p / 16.0;
                    break;
                case '3':
                    TonePorta(ch);
                    break;
                case '4':
                    ch.VibPhase += ch.VibSpeed;
                    break;
                case '5':
                    TonePorta(ch);
                    VolumeSlide(ch, x, y);
                    break;
                case '6':
                    ch.VibPhase += ch.VibSpeed;
                    VolumeSlide(ch, x, y);
                    break;
                case 'A':
                    VolumeSlide(ch, x, y);
                    break;
                case '7':
                    ch.TremPhase += ch.TremSpeed;
                    break;
                case 'W':
                    _trackGlobal = Math.Clamp(x > 0 ? _trackGlobal + x : _trackGlobal - y, 0, 64);
                    break;
                case 'P':
                {
                    // Pan slide: x towards the right, y towards the left, in 1/128 of the way each tick
                    var v = _voices[c];
                    v.Pan = (ch.Pan = Math.Clamp((ch.Pan ?? v.Pan) + (x - y) / 127.5, -1, 1)).Value;
                    break;
                }
                case 'K':
                    if (_trackTick == p) ReleaseTrackVoice(c);
                    break;
                case 'R':
                    // Retrigger every y ticks, and the volume changed by x each time
                    if (y > 0 && _trackTick % y == 0 && ch.Active)
                    {
                        ch.Vol = Math.Clamp(x switch
                        {
                            1 => ch.Vol - 1, 2 => ch.Vol - 2, 3 => ch.Vol - 4, 4 => ch.Vol - 8, 5 => ch.Vol - 16,
                            6 => ch.Vol * 2 / 3, 7 => ch.Vol / 2,
                            9 => ch.Vol + 1, 10 => ch.Vol + 2, 11 => ch.Vol + 4, 12 => ch.Vol + 8, 13 => ch.Vol + 16,
                            14 => ch.Vol * 3 / 2, 15 => ch.Vol * 2,
                            _ => ch.Vol
                        }, 0, 64);
                        TriggerTrackNote(c, (int)Math.Round(ch.Note));
                    }
                    break;
                case 'E':
                    if (x == 0xC && _trackTick == y) ch.Vol = 0;
                    if (x == 0xF) Funk(c);
                    // Retrigger: the note is struck again every y ticks (drum rolls)
                    if (x == 0x9 && y > 0 && _trackTick % y == 0 && ch.Active) TriggerTrackNote(c, (int)Math.Round(ch.Note));
                    if (x == 0xD && _trackTick == y && ch.DelayNote >= 0)
                    {
                        TriggerTrackNote(c, ch.DelayNote);
                        ch.DelayNote = TrackerCell.NoNote;
                    }
                    break;
            }
        }

        // Speed in sixteenths of a semitone per tick: 310 glides a whole semitone in three ticks
        private static void TonePorta(TrackChannel ch)
        {
            double step = ch.PortaSpeed / 16.0;
            if (ch.Note < ch.PortaTarget) ch.Note = Math.Min(ch.PortaTarget, ch.Note + step);
            else ch.Note = Math.Max(ch.PortaTarget, ch.Note - step);
        }

        private static void VolumeSlide(TrackChannel ch, int up, int down) =>
            ch.Vol = Math.Clamp(up > 0 ? ch.Vol + up : ch.Vol - down, 0, 64);

        private static void TriggerTrackNote(int c, int note)
        {
            var ch = _trackChannels[c];
            var inst = ch.Inst ?? DefaultInstrument;
            ch.Inst = inst;
            ch.Active = true;
            ch.Note = ch.PortaTarget = note;
            ch.TableIndex = 0;
            ch.TableOffset = 0;
            ch.NoteTicks = 0;
            if ((ch.VibWave & 4) == 0) ch.VibPhase = 0;
            if ((ch.TremWave & 4) == 0) ch.TremPhase = 0;
            ch.EnvTick = ch.PanEnvTick = 0;
            ch.KeyOff = false;
            ch.Fade = 1;

            var v = _voices[c];
            SetVoiceFromInstrument(ch, v, inst);
            v.LfoTime = 0;   // the LFOs' delay starts again with every note
            for (int k = 0; k < inst.Lfos.Length; k++)
                if (inst.Lfos[k] is { Retrigger: true })
                {
                    // Random starts with a fresh value, the others from the start of the wave
                    v.LfoPhases[k] = 0;
                    v.LfoRandom[k] = NextLfoRandom();
                }
            v.Arp = Array.Empty<int>();
            v.Glide = 0;
            v.Bend = 0;
            v.CutoffSlideLeft = 0;
            v.VolumeSlideLeft = 0;
            v.Drum = null;
            v.Stage = EnvStage.Attack;   // from the current level, without a click
            v.FilterEnvRising = true;
            v.LfoPhase = 0;
            v.SamplePos = 0;
        }

        // The instrument's sound settings on the voice. Used when a note starts, and when the instrument
        // is changed while a note sounds: then the note goes on (envelope, pitch and LFOs where they are)
        // with the new sound, so a turn of a knob is heard straight away.
        private static void SetVoiceFromInstrument(TrackChannel ch, Voice v, TrackInstrument inst)
        {
            v.Wave = inst.Wave;
            v.PulseWidth = ch.PulseWidth ?? inst.PulseWidth;
            v.PwmDepth = inst.PwmDepth;
            v.PwmRate = inst.PwmRate;
            v.Attack = inst.Attack;
            v.Decay = inst.Decay;
            v.Sustain = inst.Sustain;
            v.Release = inst.Release;
            v.Filter = inst.Filter == FilterMode.Off && ch.Cutoff.HasValue ? FilterMode.Low : inst.Filter;
            v.Cutoff = ch.Cutoff ?? inst.Cutoff;
            v.Resonance = ch.Resonance ?? inst.Resonance;
            v.FilterEnvDepth = inst.FilterEnvDepth;
            v.FilterEnvAttack = inst.FilterEnvAttack;
            v.FilterEnvDecay = inst.FilterEnvDecay;
            v.VibratoDepth = inst.VibratoDepth;
            v.VibratoRate = inst.VibratoRate;
            v.RingSource = inst.RingSource;
            v.SyncSource = inst.SyncSource;
            v.Osc2Wave = inst.Osc2Wave;
            v.Osc2Ratio = inst.Osc2Ratio;
            v.Osc2Mix = inst.Osc2Mix;
            v.Osc2Mode = inst.Osc2Mode;
            v.Lfos = inst.Lfos;
            // An echo that starts is empty, so that an old one does not come back
            if (v.EchoMix <= 0 && inst.EchoMix > 0) Array.Clear(v.EchoBuffer);
            v.EchoTime = inst.EchoTime;
            v.EchoSyncRows = inst.EchoSyncRows;
            v.EchoFeedback = inst.EchoFeedback;
            v.EchoMix = inst.EchoMix;
            v.Pan = ch.Pan ?? inst.Pan;
            // (with EFx the channel plays its own copy, turned over as it goes)
            var sample = inst.Sample != null && ch.Funked != null && ch.Funked.TryGetValue(inst.Sample, out var own) ? own : inst.Sample;
            if (v.Sample != sample) v.SamplePos = 0;   // another sample: from its start
            v.Sample = sample;
            v.SampleBaseFreq = inst.SampleBaseFreq;
            v.LoopStart = inst.LoopStart;
            v.LoopEnd = inst.LoopEnd;
        }

        // EFx each tick: as its counter reaches 128, the next byte of the sample's loop is turned over
        // (in a copy of the sample that the channel keeps, as ProTracker changes the sample in memory)
        private static void Funk(int c)
        {
            var ch = _trackChannels[c];
            var v = _voices[c];
            if (ch.FunkSpeed == 0 || v.Sample == null || v.LoopEnd <= v.LoopStart) return;
            ch.FunkCounter += FunkTable[ch.FunkSpeed];
            if (ch.FunkCounter < 128) return;
            ch.FunkCounter = 0;
            var original = ch.Inst?.Sample;
            if (original == null) return;
            ch.Funked ??= new Dictionary<SampleData, SampleData>();
            if (!ch.Funked.TryGetValue(original, out var own))
            {
                own = new SampleData((float[])original.Data.Clone(), original.Rate);
                ch.Funked[original] = own;
                v.Sample = own;
            }
            int length = v.LoopEnd - v.LoopStart;
            ch.FunkPos = (ch.FunkPos + 1) % length;
            int at = v.LoopStart + ch.FunkPos;
            if (at < own.Data.Length) own.Data[at] = -own.Data[at] - 1f / 128;   // NOT of a signed byte
        }

        // An instrument (from 1) of the song was changed: the notes that sound with it take the new
        // settings at once, whether the song plays or the notes are tried on the keys
        public static void TrackInstrumentChanged(TrackerSong song, int instrument)
        {
            if (instrument < 1 || instrument > song.Instruments.Count) return;
            var inst = CompileInstrument(song.Instruments[instrument - 1], instrument);
            _trackCommands.Enqueue(() =>
            {
                for (int c = 0; c < TrackerSong.MaxChannels; c++)
                {
                    var ch = _trackChannels[c];
                    if (ch.Sfx || ch.InstNumber != instrument) continue;
                    ch.Inst = inst;
                    // A shorter table: carry on from its start rather than past its end
                    if (ch.TableIndex >= inst.Table.Length) ch.TableIndex = 0;
                    if (ch.Active && _voices[c].Stage != EnvStage.Idle) SetVoiceFromInstrument(ch, _voices[c], inst);
                }
            });
        }

        // Lets go of the note. With an envelope of points the envelope goes on and the fade starts; a sound
        // effect is let go at once.
        private static void ReleaseTrackVoice(int c)
        {
            var ch = _trackChannels[c];
            if (ch.Active && !ch.Sfx && ch.Inst?.EnvTicks != null)
            {
                ch.KeyOff = true;
                return;
            }
            var v = _voices[c];
            if (v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain) v.Stage = EnvStage.Release;
        }

        // Works out the pitch and volume of the channel and writes them to the voice
        private static void UpdateTrackVoice(int c)
        {
            var ch = _trackChannels[c];
            if (!ch.Active) return;
            var v = _voices[c];
            var inst = ch.Inst!;

            var table = inst.Table;
            if (table.Length > 0)
            {
                var step = table[ch.TableIndex];
                if (step.Wave.HasValue) v.Wave = step.Wave.Value;
                if (step.PulseWidth.HasValue) v.PulseWidth = step.PulseWidth.Value;
                if (step.Osc2.HasValue) v.Osc2Ratio = Math.Pow(2, step.Osc2.Value / 12);
                ch.TableOffset = step.Offset;
                if (++ch.TableIndex >= table.Length)
                    ch.TableIndex = inst.TableLoop >= 0 ? inst.TableLoop : table.Length - 1;
            }

            double note = ch.Note;
            // Glissando: a glide heard in whole semitones
            if (ch.Glissando && ch.Effect is '3' or '5') note = Math.Round(note);
            double semis = note + ch.TableOffset;
            int p = ch.Param;
            if (ch.Effect == '0' && p != 0)
                semis += (_trackTick % 3) switch { 1 => p >> 4, 2 => p & 15, _ => 0 };
            // Jx0: two notes, a tick each; Jxy: four, the last an octave up; counted from the note's start
            if (ch.Effect == 'J' && p != 0)
                semis += (p & 15) == 0 ? ch.NoteTicks % 2 * (p >> 4)
                    : (ch.NoteTicks % 4) switch { 1 => p >> 4, 2 => p & 15, 3 => 12, _ => 0 };
            ch.NoteTicks++;
            if (ch.Effect is '4' or '6')
                semis += Wave(ch.VibWave, ch.VibPhase) * ch.VibDepth / 8.0;

            double freq = 440.0 * Math.Pow(2, (semis - 69) / 12.0);
            v.Freq = v.TargetFreq = Math.Clamp(freq, 1, SampleRate * 0.45);
            double vol = ch.Vol;
            if (ch.Effect == '7')   // tremolo: the volume swings by up to depth × 4 of 64
                vol = Math.Clamp(vol + Wave(ch.TremWave, ch.TremPhase) * ch.TremDepth * 4, 0, 64);
            if (ch.Effect == 'T')
            {
                // Tremor: on for x + 1 ticks, off for y + 1
                int on = (p >> 4) + 1, off = (p & 15) + 1;
                if (ch.TremorPos++ % (on + off) >= on) vol = 0;
            }
            else ch.TremorPos = 0;
            vol *= _trackGlobal / 64.0;
            v.LedFilter = !ch.Sfx && _trackLed;
            if (inst.EnvTicks != null) vol *= NextEnvelope(ch, v, inst);
            if (inst.PanTicks != null)
            {
                // As far towards the side as there is room from where the pan is
                double basePan = ch.Pan ?? inst.Pan;
                double e = NextPanEnvelope(ch, inst);
                v.Pan = Math.Clamp(basePan + e * (1 - Math.Abs(basePan)), -1, 1);
            }
            v.Volume = ch.Sfx ? inst.Volume * _sfxVolume
                : _trackMute[c] ? 0 : inst.Volume * vol / 64.0 * _trackVolume;
        }

        // The pan envelope's level at the channel's tick (-1 – 1), and one tick on, as the volume envelope goes
        private static double NextPanEnvelope(TrackChannel ch, TrackInstrument inst)
        {
            var ticks = inst.PanTicks!;
            var levels = inst.PanLevels;
            int t = ch.PanEnvTick;
            double level = levels[^1];
            for (int k = 0; k < ticks.Length - 1; k++)
                if (t < ticks[k + 1])
                {
                    int span = Math.Max(1, ticks[k + 1] - ticks[k]);
                    level = levels[k] + (levels[k + 1] - levels[k]) * Math.Clamp((t - ticks[k]) / (double)span, 0, 1);
                    break;
                }
            if (!(inst.PanSustain >= 0 && !ch.KeyOff && t == ticks[inst.PanSustain]))
            {
                t++;
                if (inst.PanLoopEnd >= 0 && t > ticks[inst.PanLoopEnd]) t = ticks[inst.PanLoopStart];
                ch.PanEnvTick = Math.Min(t, ticks[^1] + 1);
            }
            return level;
        }

        // The vibrato's and tremolo's wave at a phase (64 a round), -1 – 1: sine, ramp down or square
        private static double Wave(int kind, int phase) => (kind & 3) switch
        {
            1 => 1 - (phase & 63) / 32.0,
            2 => (phase & 63) < 32 ? 1 : -1,
            _ => Math.Sin(phase * 2 * Math.PI / 64)
        };

        // The envelope's level at the channel's tick (0 – 1, with the fade), and one tick on
        private static double NextEnvelope(TrackChannel ch, Voice v, TrackInstrument inst)
        {
            var ticks = inst.EnvTicks!;
            var levels = inst.EnvLevels;
            int t = ch.EnvTick;
            double level = levels[^1];
            for (int k = 0; k < ticks.Length - 1; k++)
                if (t < ticks[k + 1])
                {
                    int span = Math.Max(1, ticks[k + 1] - ticks[k]);
                    level = levels[k] + (levels[k + 1] - levels[k]) * Math.Clamp((t - ticks[k]) / (double)span, 0, 1);
                    break;
                }

            // The sustain point holds the note until it is let go; the loop goes round either way
            if (!(inst.EnvSustain >= 0 && !ch.KeyOff && t == ticks[inst.EnvSustain]))
            {
                t++;
                if (inst.EnvLoopEnd >= 0 && t > ticks[inst.EnvLoopEnd]) t = ticks[inst.EnvLoopStart];
                ch.EnvTick = Math.Min(t, ticks[^1] + 1);
            }

            if (ch.KeyOff)
            {
                double tickSeconds = ch.Sfx ? 1 / SfxTickRate : 1 / (_trackTempo * 0.4);
                // Without a fade-out a sound that ends by itself (at 0, without a loop) goes on to its end;
                // one that would go on for ever fades out quickly instead, so letting go always stops it
                bool endsByItself = inst.EnvLoopEnd < 0 && levels[^1] <= 0;
                double fade = inst.FadeOut > 0 ? inst.FadeOut : endsByItself ? 0 : 0.05;
                if (fade > 0) ch.Fade = Math.Max(0, ch.Fade - tickSeconds / fade);
                // Faded out, or at the end of an envelope that ends silent: the note is over
                bool ended = ch.Fade <= 0 || (inst.EnvLoopEnd < 0 && ch.EnvTick > ticks[^1] && levels[^1] <= 0);
                if (ended && v.Stage is EnvStage.Attack or EnvStage.Decay or EnvStage.Sustain) v.Stage = EnvStage.Release;
            }
            return level * ch.Fade;
        }

        private static void AdvanceRow(TrackData d)
        {
            if (_trackJump)
            {
                _trackJump = false;
                // A jump back to a place already played is how a song loops; played once, it ends there
                bool back = _trackJumpOrder < _trackOrder || (_trackJumpOrder == _trackOrder && !_trackPatternLoop);
                if (back && !_trackLoop)
                {
                    ReleaseTrackVoices();
                    Volatile.Write(ref _track, null);
                    return;
                }
                _trackOrder = _trackJumpOrder;
                _trackRow = _trackBreakRow;
            }
            else if (++_trackRow >= d.Patterns[d.Order[_trackOrder]].Length)
            {
                _trackRow = 0;
                _trackOrder++;
            }

            if (_trackOrder < 0 || _trackOrder >= d.Order.Length)
            {
                if (!_trackLoop)
                {
                    ReleaseTrackVoices();
                    Volatile.Write(ref _track, null);
                    return;
                }
                _trackOrder = 0;
            }
            if (_trackRow >= d.Patterns[d.Order[_trackOrder]].Length) _trackRow = 0;
        }
    }
}
