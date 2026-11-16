using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            public readonly int Offset;          // semitones from the note
            public readonly double? PulseWidth;  // 0 – 1

            public TableStep(WaveformType? wave, int offset, double? pulseWidth)
            {
                Wave = wave;
                Offset = offset;
                PulseWidth = pulseWidth;
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
            public TableStep[] Table = Array.Empty<TableStep>();
            public int TableLoop = -1;
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
            public int Vol = 64;           // 0 – 64 as in ProTracker
            public char Effect;
            public int Param;
            public int TableIndex, TableOffset;
            public int DelayNote = TrackerCell.NoNote;
            // Set with G, H and I. Apply until the channel changes instrument.
            public double? Cutoff, Resonance, PulseWidth;
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
        // Channels 1 – _trackUsed play the song (4 – 8), the ones above are used by the sound effects
        // (TRACK SFX). A song with 8 channels lends channel 8 to an effect while it sounds.
        private static readonly TrackChannel[] _trackChannels = CreateTrackChannels();
        private static int _trackUsed = TrackerSong.DefaultChannels;

        private static TrackChannel[] CreateTrackChannels()
        {
            var a = new TrackChannel[Channels];
            for (int i = 0; i < a.Length; i++) a[i] = new TrackChannel();
            return a;
        }

        private static TrackRequest? _trackPending;
        private static TrackData? _track;
        private static bool _trackLoop;
        private static int _trackOrder, _trackRow, _trackTick, _trackSpeed, _trackTempo;
        private static double _trackTickLeft;
        private static bool _trackJump;
        private static int _trackJumpOrder, _trackBreakRow;
        private static int _trackPosOrder, _trackPosRow;
        private static double _trackVolume = 1.0;
        private static bool _trackPreview;   // the editor previews notes without a song playing

        // Changes from the editor that the audio thread makes at the next sample
        private static readonly ConcurrentQueue<Action> _trackCommands = new();
        private static readonly bool[] _trackMute = new bool[Channels];

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

        // Plays a single pattern over and over, for the editor
        public static void TrackPlayPattern(TrackerSong song, int pattern, int row = 0)
        {
            var data = CompileSong(song, pattern);
            Volatile.Write(ref _trackPending, new TrackRequest(data, 0, Math.Max(0, row), true));
            EnsureStarted();
        }

        // Switches to a changed version of the song without starting again. With pattern only that pattern plays.
        public static void TrackUpdate(TrackerSong song, int? pattern = null)
        {
            var data = CompileSong(song, pattern);
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
                ch.Cutoff = ch.Resonance = ch.PulseWidth = null;
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
                ch.Cutoff = ch.Resonance = ch.PulseWidth = null;
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

        public static void SetTrackVolume(double volume) => _trackVolume = Math.Clamp(volume, 0, 1);

        public static void SetTrackMute(int channel, bool mute)
        {
            if (channel >= 1 && channel <= TrackerSong.MaxChannels) _trackMute[channel - 1] = mute;
        }

        // ════════════════════════════════════════════════════════════════
        //  Building the song
        // ════════════════════════════════════════════════════════════════

        // With onlyPattern the order list is just that pattern
        private static TrackData CompileSong(TrackerSong song, int? onlyPattern = null)
        {
            var data = new TrackData
            {
                Speed = Math.Clamp(song.Speed, 1, 31),
                Tempo = Math.Clamp(song.Tempo, 32, 255),
                Order = onlyPattern != null ? new[] { onlyPattern.Value }
                    : song.Order.Count > 0 ? song.Order.ToArray() : new[] { 0 },
                Patterns = new TrackerCell[song.Patterns.Count][][],
                Instruments = new TrackInstrument[song.Instruments.Count],
                Channels = song.Channels
            };

            for (int p = 0; p < song.Patterns.Count; p++)
            {
                var pat = song.Patterns[p];
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

        private static TrackInstrument CompileInstrument(TrackerInstrument src, int number)
        {
            var steps = new List<TableStep>();
            foreach (var text in src.Table ?? new List<string>())
                steps.Add(ParseTableStep(text, number));

            var wave = ParseWave(src.Wave ?? "Pulse");
            return new TrackInstrument
            {
                Wave = wave == WaveformType.Drums ? WaveformType.Noise : wave,
                Volume = Math.Clamp(src.Volume, 0, 1),
                PulseWidth = Math.Clamp(src.PulseWidth / 100.0, 0.01, 0.99),
                PwmDepth = Math.Clamp(src.PwmDepth / 100.0, 0, 0.49),
                PwmRate = Math.Max(0, src.PwmRate),
                Attack = Math.Max(0, src.Attack) / 1000.0,
                Decay = Math.Max(0, src.Decay) / 1000.0,
                Sustain = Math.Clamp(src.Sustain / 100.0, 0, 1),
                Release = Math.Max(0, src.Release) / 1000.0,
                Filter = ParseFilterMode(src.Filter ?? "Off"),
                Cutoff = Math.Clamp(src.Cutoff, 20, 18000),
                Resonance = Math.Clamp(src.Resonance / 100.0, 0, 0.98),
                FilterEnvDepth = Math.Clamp(src.FilterEnvDepth, -18000, 18000),
                FilterEnvAttack = Math.Max(0, src.FilterEnvAttack) / 1000.0,
                FilterEnvDecay = Math.Max(1, src.FilterEnvDecay) / 1000.0,
                VibratoDepth = Math.Max(0, src.Vibrato) / 100.0,
                VibratoRate = Math.Max(0, src.VibratoRate),
                RingSource = src.Ring <= 0 ? -1 : ChIdx(src.Ring),
                SyncSource = src.Sync <= 0 ? -1 : ChIdx(src.Sync),
                Table = steps.ToArray(),
                TableLoop = src.TableLoop < steps.Count ? src.TableLoop : -1
            };
        }

        // "NOISE +24", "PULSE", "-12", "P30", "TRIANGLE 0 P50"
        private static TableStep ParseTableStep(string text, int instrument)
        {
            WaveformType? wave = null;
            int offset = 0;
            double? pulse = null;
            foreach (var word in text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
                    offset = n;
                else if ((word[0] == 'P' || word[0] == 'p') && word.Length > 1 &&
                         double.TryParse(word[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out double pw))
                    pulse = Math.Clamp(pw / 100.0, 0.01, 0.99);
                else if (word.ToUpperInvariant() is "SQUARE" or "PULSE" or "TRIANGLE" or "SAWTOOTH" or "SAW" or "SINE" or "NOISE")
                    wave = ParseWave(word);
                else
                    throw new FormatException($"Instrument {instrument:X2}: '{text}' is not a table step");
            }
            return new TableStep(wave, offset, pulse);
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
                ch.DelayNote = TrackerCell.NoNote;
                ch.Cutoff = ch.Resonance = ch.PulseWidth = null;
            }

            _trackPreview = false;
            _trackLoop = req.Loop;
            _trackSpeed = req.Data.Speed;
            _trackTempo = req.Data.Tempo;
            _trackOrder = req.Order;
            _trackRow = Math.Min(req.Row, req.Data.Patterns[req.Data.Order[_trackOrder]].Length - 1);
            _trackTick = 0;
            _trackTickLeft = 0;
            _trackJump = false;
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
            if (_trackTick == 0) ReadRow(d);
            else
                for (int c = 0; c < d.Channels; c++)
                    if (!_trackChannels[c].Sfx) TickEffects(c);

            for (int c = 0; c < d.Channels; c++)
                if (!_trackChannels[c].Sfx) UpdateTrackVoice(c);

            if (++_trackTick >= _trackSpeed)
            {
                _trackTick = 0;
                AdvanceRow(d);
            }
        }

        private static void ReadRow(TrackData d)
        {
            var row = d.Patterns[d.Order[_trackOrder]][_trackRow];
            Volatile.Write(ref _trackPosOrder, _trackOrder);
            Volatile.Write(ref _trackPosRow, _trackRow);

            int? jumpOrder = null, breakRow = null;
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
                    if (inst != ch.Inst) ch.Cutoff = ch.Resonance = ch.PulseWidth = null;
                    ch.Inst = inst;
                    ch.InstNumber = cell.Instrument;
                    ch.Vol = 64;
                }

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
                    case 'E':
                        if (x == 0xC && y == 0) ch.Vol = 0;
                        break;
                    case 'F':
                        if (p > 0 && p < 0x20) _trackSpeed = p;
                        else if (p >= 0x20) _trackTempo = p;
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
                }
            }

            if (jumpOrder != null || breakRow != null)
            {
                _trackJump = true;
                _trackJumpOrder = jumpOrder ?? _trackOrder + 1;
                _trackBreakRow = breakRow ?? 0;
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
                case 'E':
                    if (x == 0xC && _trackTick == y) ch.Vol = 0;
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
            ch.VibPhase = 0;

            var v = _voices[c];
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
            v.Arp = Array.Empty<int>();
            v.Glide = 0;
            v.Bend = 0;
            v.CutoffSlideLeft = 0;
            v.VolumeSlideLeft = 0;
            v.Drum = null;
            v.Stage = EnvStage.Attack;   // from the current level, without a click
            v.FilterEnvRising = true;
            v.LfoPhase = 0;
        }

        private static void ReleaseTrackVoice(int c)
        {
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
                ch.TableOffset = step.Offset;
                if (++ch.TableIndex >= table.Length)
                    ch.TableIndex = inst.TableLoop >= 0 ? inst.TableLoop : table.Length - 1;
            }

            double semis = ch.Note + ch.TableOffset;
            int p = ch.Param;
            if (ch.Effect == '0' && p != 0)
                semis += (_trackTick % 3) switch { 1 => p >> 4, 2 => p & 15, _ => 0 };
            if (ch.Effect is '4' or '6')
                semis += Math.Sin(ch.VibPhase * 2 * Math.PI / 64) * ch.VibDepth / 8.0;

            double freq = 440.0 * Math.Pow(2, (semis - 69) / 12.0);
            v.Freq = v.TargetFreq = Math.Clamp(freq, 1, SampleRate * 0.45);
            v.Volume = ch.Sfx ? inst.Volume * _sfxVolume
                : _trackMute[c] ? 0 : inst.Volume * ch.Vol / 64.0 * _trackVolume;
        }

        private static void AdvanceRow(TrackData d)
        {
            if (_trackJump)
            {
                _trackJump = false;
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
