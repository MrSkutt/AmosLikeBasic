using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmosLikeBasic
{
    // A song for the tracker: instruments, patterns and the order the patterns play in.
    //
    // Saved as JSON (.amostrk). The rows of a pattern are saved as text, one string per row
    // with the channels separated by |, so that the file can be read and diffed:
    //   "C-4 01 A04|--- .. ...|=== .. ...|E-3 02 C20"
    // Each cell is note, instrument (hex) and effect (letter/digit + two hex digits).
    //
    // A song has 4 – 32 channels (Channels); files from before that have 4. The patterns always
    // hold MaxChannels channels, so that fewer channels and back again loses nothing until saving.
    public sealed class TrackerSong
    {
        public const int MaxChannels = 32;
        public const int MinChannels = 3;
        public const int DefaultChannels = 4;
        public const int DefaultRows = 64;
        public const int MaxRows = 256;
        public const string FormatName = "AmosTracker";
        public const int FormatVersion = 1;

        public string Title { get; set; } = "";
        public int Speed { get; set; } = 6;      // ticks per row
        public int Tempo { get; set; } = 125;    // as in ProTracker: 125 gives 50 ticks per second

        private int _channels = DefaultChannels;
        public int Channels                      // how many channels the song uses: synth channels 1 – Channels
        {
            get => _channels;
            set => _channels = Math.Clamp(value, MinChannels, MaxChannels);
        }

        // What the song is made for, and so what it is kept within: null the tracker's own (FULL), "MOD"
        // (ProTracker's four channels and 31 instruments) or "XM" (FastTracker 2's 128 instruments).
        // A maxYMiser song has its Ym part instead, and "DHS" when it is kept within what DHS plays (DhsMode).
        public string? Mode { get; set; }

        public List<TrackerInstrument> Instruments { get; set; } = new();
        public List<int> Order { get; set; } = new() { 0 };
        public List<TrackerPattern> Patterns { get; set; } = new() { new TrackerPattern() };

        // A channel's own order: for a place in the order list, the pattern each channel takes its
        // column from (none, or -1 for a channel: the place's own pattern). A bass line can then repeat
        // while the melody above it changes, written once, as the C64's and the Atari's players did.
        public List<int[]?> ChannelOrder { get; set; } = new();

        public bool HasChannelOrder => ChannelOrder.Any(c => c != null && c.Any(p => p >= 0));

        // YM mode (null: the synth, as always): channels 1 – 3 are played by the Atari ST's sound chip,
        // each instrument by its YM part (a maxYMiser instrument), as a song read from maxYMiser sounds.
        public TrackerYmSong? Ym { get; set; }

        // The pattern a channel plays at a place in the order list
        public int PatternAt(int position, int channel)
        {
            if (position < 0 || position >= Order.Count) return 0;
            if (position < ChannelOrder.Count && ChannelOrder[position] is { } own && channel < own.Length
                && own[channel] >= 0 && own[channel] < Patterns.Count)
                return own[channel];
            return Order[position];
        }

        public void SetPatternAt(int position, int channel, int pattern)
        {
            if (position < 0 || position >= Order.Count || channel < 0 || channel >= MaxChannels) return;
            while (ChannelOrder.Count <= position) ChannelOrder.Add(null);
            var own = ChannelOrder[position] ?? Enumerable.Repeat(-1, MaxChannels).ToArray();
            if (own.Length < MaxChannels) own = own.Concat(Enumerable.Repeat(-1, MaxChannels - own.Length)).ToArray();
            own[channel] = pattern == Order[position] ? -1 : pattern;
            ChannelOrder[position] = own.Any(p => p >= 0) ? own : null;
            TrimChannelOrder();
        }

        // The order list changed: a place put in (or taken out) moves the channels' own orders with it
        public void InsertPosition(int position, int pattern)
        {
            Order.Insert(position, pattern);
            if (position <= ChannelOrder.Count) ChannelOrder.Insert(Math.Min(position, ChannelOrder.Count), null);
            TrimChannelOrder();
        }

        public void RemovePosition(int position)
        {
            Order.RemoveAt(position);
            if (position < ChannelOrder.Count) ChannelOrder.RemoveAt(position);
            TrimChannelOrder();
        }

        private void TrimChannelOrder()
        {
            while (ChannelOrder.Count > Order.Count) ChannelOrder.RemoveAt(ChannelOrder.Count - 1);
            while (ChannelOrder.Count > 0 && ChannelOrder[^1] == null) ChannelOrder.RemoveAt(ChannelOrder.Count - 1);
        }

        // What a place in the order list plays, each channel from its own pattern, as one pattern
        // (as long as the place's own pattern)
        public TrackerPattern Composed(int position)
        {
            var main = Patterns[Math.Clamp(Order[Math.Clamp(position, 0, Order.Count - 1)], 0, Patterns.Count - 1)];
            var result = new TrackerPattern(main.Rows);
            for (int c = 0; c < MaxChannels; c++)
            {
                var from = Patterns[PatternAt(position, c)];
                for (int r = 0; r < main.Rows; r++) result[r, c] = r < from.Rows ? from[r, c] : TrackerCell.Empty;
            }
            return result;
        }

        // Each channel's parts written once: the columns of the order list's patterns are gathered per
        // channel, a column that comes again is played from where it was first written, and the new ones
        // are packed into patterns (a place's columns kept together where they can be). For songs read
        // from chip music, whose voices repeat on their own. Only done when it is clearly worth it (at
        // least two patterns and a tenth fewer than the places themselves need); returns how many fewer.
        public int ShareChannels()
        {
            int before = Patterns.Count;
            // What the places need without it: each different place once
            int whole = Enumerable.Range(0, Order.Count).Select(pos =>
            {
                var p = Composed(pos);
                var key = new System.Text.StringBuilder().Append(p.Rows).Append(':');
                for (int r = 0; r < p.Rows; r++)
                    for (int c = 0; c < Channels; c++) key.Append(p[r, c]).Append('|');
                return key.ToString();
            }).Distinct().Count();
            int channels = Channels;
            var patterns = new List<TrackerPattern>();
            var used = new List<bool[]>();
            var seen = new Dictionary<string, int>[channels];
            for (int c = 0; c < channels; c++) seen[c] = new Dictionary<string, int>();
            var newOrder = new List<int>();
            var newChannels = new List<int[]?>();

            int Place(int rows, int c, int prefer)
            {
                if (prefer >= 0 && patterns[prefer].Rows == rows && !used[prefer][c]) return prefer;
                for (int p = 0; p < patterns.Count; p++)
                    if (patterns[p].Rows == rows && !used[p][c]) return p;
                patterns.Add(new TrackerPattern(rows));
                used.Add(new bool[MaxChannels]);
                return patterns.Count - 1;
            }

            for (int pos = 0; pos < Order.Count; pos++)
            {
                var src = Composed(pos);
                int rows = src.Rows, home = -1;
                var at = new int[channels];
                for (int c = 0; c < channels; c++)
                {
                    var key = new System.Text.StringBuilder().Append(rows).Append(':');
                    bool empty = true;
                    for (int r = 0; r < rows; r++)
                    {
                        var cell = src[r, c];
                        if (!cell.Equals(TrackerCell.Empty)) empty = false;
                        key.Append(cell).Append('|');
                    }
                    int p;
                    // An empty column is played from the place's pattern if it has room, so that
                    // silence does not send a channel to another pattern
                    if (empty && home >= 0 && patterns[home].Rows == rows && !used[home][c]) { p = home; used[p][c] = true; }
                    else if (!seen[c].TryGetValue(key.ToString(), out p))
                    {
                        p = Place(rows, c, home);
                        used[p][c] = true;
                        for (int r = 0; r < rows; r++) patterns[p][r, c] = src[r, c];
                        seen[c][key.ToString()] = p;
                    }
                    if (c == 0) home = p;
                    at[c] = p;
                }
                newOrder.Add(at[0]);
                var own = Enumerable.Range(0, MaxChannels).Select(c => c < channels && at[c] != at[0] ? at[c] : -1).ToArray();
                newChannels.Add(own.Any(p => p >= 0) ? own : null);
            }
            if (patterns.Count > whole - Math.Max(2, whole / 10)) return 0;
            Patterns = patterns;
            Order = newOrder;
            ChannelOrder = newChannels;
            TrimChannelOrder();
            return before - Patterns.Count;
        }

        // Patterns that are the same, cell for cell, become one: the order list plays it each time.
        // Returns how many patterns there were fewer.
        public int MergeSamePatterns()
        {
            string Key(TrackerPattern p)
            {
                var b = new System.Text.StringBuilder().Append(p.Rows).Append(':');
                for (int r = 0; r < p.Rows; r++)
                    for (int c = 0; c < MaxChannels; c++) b.Append(p[r, c]).Append('|');
                return b.ToString();
            }
            var first = new Dictionary<string, int>();
            var newIndex = new int[Patterns.Count];
            var kept = new List<TrackerPattern>();
            for (int i = 0; i < Patterns.Count; i++)
            {
                string key = Key(Patterns[i]);
                if (first.TryGetValue(key, out int k)) { newIndex[i] = k; continue; }
                first[key] = newIndex[i] = kept.Count;
                kept.Add(Patterns[i]);
            }
            int merged = Patterns.Count - kept.Count;
            if (merged == 0) return 0;
            for (int o = 0; o < Order.Count; o++)
                if (Order[o] >= 0 && Order[o] < newIndex.Length) Order[o] = newIndex[Order[o]];
            foreach (var own in ChannelOrder)
                if (own != null)
                    for (int c = 0; c < own.Length; c++)
                        if (own[c] >= 0 && own[c] < newIndex.Length) own[c] = newIndex[own[c]];
            for (int o = 0; o < ChannelOrder.Count; o++)
                if (ChannelOrder[o] is { } own2 && o < Order.Count)
                {
                    for (int c = 0; c < own2.Length; c++) if (own2[c] == Order[o]) own2[c] = -1;
                    if (!own2.Any(p => p >= 0)) ChannelOrder[o] = null;
                }
            Patterns = kept;
            TrimChannelOrder();
            return merged;
        }

        // ── Filformat ────────────────────────────────────────────────

        private sealed class SongFile
        {
            public string Format { get; set; } = FormatName;
            public int Version { get; set; } = FormatVersion;
            public string Title { get; set; } = "";
            public int Speed { get; set; } = 6;
            public int Tempo { get; set; } = 125;
            public int Channels { get; set; } = DefaultChannels;
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Mode { get; set; }
            public List<TrackerInstrument> Instruments { get; set; } = new();
            public List<int> Order { get; set; } = new();
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<int[]?>? ChannelOrder { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public TrackerYmSong? Ym { get; set; }
            public List<List<string>> Patterns { get; set; } = new();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static TrackerSong Load(string path) => FromJson(File.ReadAllText(path));

        public void Save(string path) => File.WriteAllText(path, ToJson());

        public static TrackerSong FromJson(string json)
        {
            SongFile? file;
            try { file = JsonSerializer.Deserialize<SongFile>(json, JsonOptions); }
            catch (JsonException e) { throw new FormatException($"The tracker file is not valid JSON: {e.Message}"); }
            if (file == null) throw new FormatException("The tracker file is empty");
            if (file.Version > FormatVersion)
                throw new FormatException($"The tracker file is version {file.Version}, this version can read {FormatVersion}");

            var song = new TrackerSong
            {
                Title = file.Title ?? "",
                Speed = Math.Clamp(file.Speed, 1, 31),
                Tempo = Math.Clamp(file.Tempo, 32, 255),
                Channels = file.Channels,
                Instruments = file.Instruments ?? new(),
                Order = file.Order ?? new(),
                Patterns = new()
            };
            for (int p = 0; p < (file.Patterns?.Count ?? 0); p++)
            {
                var rows = file.Patterns![p] ?? new();
                if (rows.Count > MaxRows)
                    throw new FormatException($"Pattern {p} has {rows.Count} rows, the most allowed is {MaxRows}");
                var pattern = new TrackerPattern(Math.Max(1, rows.Count));
                for (int r = 0; r < rows.Count; r++)
                    pattern.SetRow(r, ParseRow(rows[r], p, r));
                song.Patterns.Add(pattern);
            }
            if (song.Patterns.Count == 0) song.Patterns.Add(new TrackerPattern());
            if (song.Order.Count == 0) song.Order.Add(0);
            foreach (int o in song.Order)
                if (o < 0 || o >= song.Patterns.Count)
                    throw new FormatException($"The order list points to pattern {o}, which does not exist");
            song.ChannelOrder = file.ChannelOrder ?? new();
            foreach (var own in song.ChannelOrder)
                if (own != null && own.Any(p => p >= song.Patterns.Count))
                    throw new FormatException("A channel's order points to a pattern that does not exist");
            song.TrimChannelOrder();
            song.Ym = file.Ym;
            song.Mode = file.Mode is "MOD" or "XM" || (file.Mode == DhsMode.Name && file.Ym != null) ? file.Mode : null;
            foreach (var inst in song.Instruments) inst.Table ??= new();
            return song;
        }

        public string ToJson()
        {
            var file = new SongFile
            {
                Title = Title,
                Speed = Speed,
                Tempo = Tempo,
                Channels = Channels,
                Mode = Mode,
                Instruments = Instruments,
                Order = Order,
                ChannelOrder = HasChannelOrder ? ChannelOrder.Select(o => o?.Take(Channels).ToArray()).ToList() : null,
                Ym = Ym
            };
            foreach (var p in Patterns)
            {
                var rows = new List<string>(p.Rows);
                for (int r = 0; r < p.Rows; r++) rows.Add(FormatRow(p, r, Channels));
                file.Patterns.Add(rows);
            }
            return JsonSerializer.Serialize(file, JsonOptions);
        }

        private static TrackerCell[] ParseRow(string text, int pattern, int row)
        {
            var cells = new TrackerCell[MaxChannels];
            var parts = (text ?? "").Split('|');
            for (int c = 0; c < MaxChannels; c++)
            {
                if (c >= parts.Length) { cells[c] = TrackerCell.Empty; continue; }
                try { cells[c] = TrackerCell.Parse(parts[c]); }
                catch (FormatException e)
                {
                    throw new FormatException($"Pattern {pattern}, row {row}, channel {c + 1}: {e.Message}");
                }
            }
            return cells;
        }

        private static string FormatRow(TrackerPattern p, int row, int channels)
        {
            var parts = new string[channels];
            for (int c = 0; c < channels; c++) parts[c] = p[row, c].ToString();
            return string.Join("|", parts);
        }
    }

    // A sound in the tracker. The units are the same as in the PLAY commands.
    public sealed class TrackerInstrument
    {
        public string Name { get; set; } = "";
        public string Wave { get; set; } = "Pulse";
        public double Volume { get; set; } = 0.3;          // 0 – 1
        public double PulseWidth { get; set; } = 50;       // procent
        public double PwmDepth { get; set; }               // procent
        public double PwmRate { get; set; }                // Hz
        public double Attack { get; set; } = 2;            // ms
        public double Decay { get; set; } = 100;           // ms
        public double Sustain { get; set; } = 70;          // procent
        public double Release { get; set; } = 100;         // ms
        public string Filter { get; set; } = "Off";        // Off, Low, High, Band
        public double Cutoff { get; set; } = 2000;         // Hz
        public double Resonance { get; set; }              // procent
        public double FilterEnvDepth { get; set; }         // Hz
        public double FilterEnvAttack { get; set; } = 5;   // ms
        public double FilterEnvDecay { get; set; } = 200;  // ms
        public double Vibrato { get; set; }                // cent
        public double VibratoRate { get; set; } = 6;       // Hz
        public int Ring { get; set; }                      // channel, 0 = off
        public int Sync { get; set; }                      // channel, 0 = off

        // A second oscillator, mixed with the first: Off or a wave, tuned in semitones and cents
        // from the note (a few cents give a fat, chorus-like sound)
        public string Osc2Wave { get; set; } = "Off";
        public double Osc2Semitones { get; set; }          // -24 – 24
        public double Osc2Detune { get; set; }             // cent
        public double Osc2Mix { get; set; } = 50;          // procent of oscillator 2 in the sound
        // What oscillator 2 does: Mix (heard with the first), Ring (it turns the first upside down every
        // other half of its period, a ring modulation as the SID's) or Sync (the first starts again
        // each time it goes round, the SID's hard sync). With Ring or Sync it is not heard itself.
        public string Osc2Mode { get; set; } = "Mix";

        // A sampled instrument (Wave "Sample"): a WAV file played at the note's pitch. SampleNote is the
        // note the file sounds at (60 = C-4); a loop from LoopStart to LoopEnd (in sample frames) keeps
        // a held note sounding, LoopEnd 0 plays the sample once.
        public string Sample { get; set; } = "";
        // The volume a note starts at when it gets this instrument (0 – 64), as a MOD file's samples have
        public int DefaultVolume { get; set; } = 64;
        public int SampleNote { get; set; } = 60;
        public int LoopStart { get; set; }
        public int LoopEnd { get; set; }

        // Where in stereo: -100 left, 0 the middle, 100 right (effect 8xx changes it in a song)
        public double Pan { get; set; }

        // A volume envelope drawn with points, used instead of the ADSR when it has two or more: each
        // point is a tick from the note's start and a level 0 – 64. The note stays at the sustain point
        // until it is let go, and goes round from loop start to loop end (point numbers from 0, -1 = none).
        // After it is let go, the envelope goes on and the sound fades out over FadeOut ms (0: no fade).
        public List<TrackerEnvelopePoint> Envelope { get; set; } = new();
        public int EnvelopeSustain { get; set; } = -1;
        public int EnvelopeLoopStart { get; set; } = -1;
        public int EnvelopeLoopEnd { get; set; } = -1;
        public double FadeOut { get; set; }

        // A pan envelope drawn with points, as FastTracker 2 has: levels 0 (left) – 32 (the middle) – 64 (right),
        // with a sustain point and a loop as the volume envelope. It moves the sound as far as there is room
        // to that side of the instrument's (or 8xx's) pan.
        public List<TrackerEnvelopePoint> PanEnvelope { get; set; } = new();
        public int PanEnvelopeSustain { get; set; } = -1;
        public int PanEnvelopeLoopStart { get; set; } = -1;
        public int PanEnvelopeLoopEnd { get; set; } = -1;

        // Echo: the sound comes back after a while, a little quieter each time. Mix 0 is off.
        public double EchoTime { get; set; } = 300;        // ms, at most 2000
        public int EchoSyncRows { get; set; }              // > 0: the time is this many rows of the song
        public double EchoFeedback { get; set; } = 40;     // procent of each echo that comes back again
        public double EchoMix { get; set; }                // procent: how loud the echo is

        // Two LFOs that each move one setting up and down while the note sounds
        public TrackerLfo Lfo1 { get; set; } = new();
        public TrackerLfo Lfo2 { get; set; } = new();

        // One step per tick from when the note starts: "NOISE +24", "PULSE", "-12", "P30" (pulse width).
        // TableLoop is the step the table jumps back to, -1 stays on the last step.
        public List<string> Table { get; set; } = new();
        public int TableLoop { get; set; } = -1;

        // The instrument as the Atari ST's sound chip plays it, in a song in YM mode (null: none)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TrackerYmInstrument? Ym { get; set; }
    }

    // A song in YM mode: a maxYMiser song as maxYMiser has it. Channels 1 – 3 are its tracks A – C, played
    // by the Atari ST's sound chip as its replay plays them; each line has maxYMiser's two effects, and
    // instrument n is its instrument n. The sequences are the song's, numbered as in maxYMiser, and the
    // file it was read from is kept, so that it can be saved as it was with only what has been changed.
    public sealed class TrackerYmSong
    {
        public int Version { get; set; } = 1;            // maxYMiser's format: 0 or 1
        public int Rate { get; set; } = 50;               // how often the replay is called, Hz
        public int Speed { get; set; } = 6;               // its speed (the tracker shows up to 31; maxYMiser has more)
        public int Timers { get; set; } = 7;              // the timer effects each channel may use: bit 2 channel 1, 1 channel 2, 0 channel 3
        public int Repeat { get; set; }                   // the place in the order list the song goes back to at its end
        public int StartPlace { get; set; }               // the place it starts at (where maxYMiser was left; kept in the file)
        public List<TrackerYmSequence> Sequences { get; set; } = new();   // [n]: sequence n (0 is none)
        // The SNDH file the song was read from (base64): its replay, and what the song does not show
        public string Original { get; set; } = "";
        // The title it had when it was opened: a title changed since is written into the file's header
        public string OpenedTitle { get; set; } = "";
        // The patterns that stand for maxYMiser's "nothing here" ($FF) and "note off" ($FE) in its order
        public int EmptyPattern { get; set; } = -1;
        public int NoteOffPattern { get; set; } = -1;
        public int LoopMarkPattern { get; set; } = -1;    // and the editor's loop mark ($FD), played as nothing
        // An older replay (in maxYMiser's files from before about 2013): its buzzer is the mixer's bit 4
        // alone, sounds whatever the volume, and has the instrument's shape; a volume column of F is silence
        public bool OldReplay { get; set; }
        public bool ShapeFromMixer => Version != 0 && !OldReplay;   // the newer format's mixer says the buzzer's shape
    }

    // maxYMiser's instrument: sequences stepped every Speed ticks from the note's start, by their numbers
    // in the song (0: none):
    //   Volume   the chip's volume 0 – 15
    //   Arpeggio semitones; Vibrato and Fixed tone periods (Fixed: the period itself, $FFFF none)
    //   Mixer    a word: $0100 the tone, $1000 noise, $00x0 the buzzer (x its shape; 1 the instrument's),
    //            $000x the timer effect (5 SID, 9 PWM, B sync buzzer, D digidrum)
    //   Noise    the noise period 0 – 31;  Timer  the SID's steps (the volume less each);  Pwm  widths
    // The flags say what the pitch effects act on: bit 2 the tone, 1 the buzzer, 0 the timer.
    public sealed class TrackerYmInstrument
    {
        public int Speed { get; set; } = 1;
        public int Volume { get; set; }                  // taken off the volume sequence, 0 – 15
        public int Portamento { get; set; }
        public int Arpeggio { get; set; }
        public int Vibrato { get; set; }
        public int Transpose { get; set; }
        public int FixedFrequency { get; set; }
        public int FixedDetune { get; set; }
        public int DetuneCoarse { get; set; }            // semitones
        public int DetuneFine { get; set; }              // tone periods
        public int PulseWidth { get; set; }              // the PWM timer's width, 0 – 255
        public int BuzzerWave { get; set; } = 8;         // the envelope's shape, 0 – 15
        public int StartSync { get; set; }               // the buzzer started again at each note
        public int DigiSample { get; set; }              // the digidrum, 1 – 8 (0: none)
        public int DigiRate { get; set; } = 0x20;        // its MFP timer data: 614400 / rate Hz
        public int FrequencyResolution { get; set; }
        public int VolumeSeq { get; set; }
        public int ArpeggioSeq { get; set; }
        public int VibratoSeq { get; set; }
        public int MixerSeq { get; set; }
        public int NoiseSeq { get; set; }
        public int FixedSeq { get; set; }
        public int TimerSeq { get; set; }
        public int PwmSeq { get; set; }
    }

    public sealed class TrackerYmSequence
    {
        public List<int> Steps { get; set; } = new();
        public int Repeat { get; set; }
    }

    public sealed class TrackerEnvelopePoint
    {
        public int Tick { get; set; }
        public int Level { get; set; }      // 0 – 64

        public TrackerEnvelopePoint() { }
        public TrackerEnvelopePoint(int tick, int level) { Tick = tick; Level = level; }
    }

    // A low frequency oscillator: a slow wave that moves a setting of the instrument.
    // Depth is in the target's own unit: cents for pitch, semitones for the cutoff, procent otherwise.
    public sealed class TrackerLfo
    {
        public static readonly string[] Shapes = { "Sine", "Triangle", "Square", "Saw up", "Saw down", "Random" };
        public static readonly string[] Targets =
            { "Off", "Pitch", "Volume", "Cutoff", "Resonance", "Pulse width", "Osc 2 pitch", "Osc 2 mix", "Pan" };

        public string Shape { get; set; } = "Sine";
        public string Target { get; set; } = "Off";
        public double Rate { get; set; } = 4;              // Hz, when not in step with the song
        public int SyncRows { get; set; }                  // > 0: one period takes this many rows
        public double Depth { get; set; }
        public double Delay { get; set; }                  // ms until it has grown to full depth
        public bool Retrigger { get; set; } = true;        // starts from the beginning on every note

        // The unit Depth is given in, for the labels
        public static string UnitOf(string target) => target switch
        {
            "Pitch" or "Osc 2 pitch" => "cents",
            "Cutoff" => "semitones",
            "Off" => "",
            _ => "%"
        };
    }

    public sealed class TrackerPattern
    {
        private TrackerCell[,] _cells;

        public TrackerPattern(int rows = TrackerSong.DefaultRows)
        {
            _cells = new TrackerCell[Math.Clamp(rows, 1, TrackerSong.MaxRows), TrackerSong.MaxChannels];
            Clear();
        }

        public int Rows => _cells.GetLength(0);

        public TrackerCell this[int row, int channel]
        {
            get => _cells[row, channel];
            set => _cells[row, channel] = value;
        }

        public void SetRow(int row, TrackerCell[] cells)
        {
            for (int c = 0; c < TrackerSong.MaxChannels; c++) _cells[row, c] = cells[c];
        }

        public void Clear()
        {
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TrackerSong.MaxChannels; c++)
                _cells[r, c] = TrackerCell.Empty;
        }

        // Changes the number of rows and keeps what fits
        public void Resize(int rows)
        {
            var old = _cells;
            _cells = new TrackerCell[Math.Clamp(rows, 1, TrackerSong.MaxRows), TrackerSong.MaxChannels];
            Clear();
            for (int r = 0; r < Math.Min(old.GetLength(0), Rows); r++)
            for (int c = 0; c < TrackerSong.MaxChannels; c++)
                _cells[r, c] = old[r, c];
        }
    }

    public readonly struct TrackerCell : IEquatable<TrackerCell>
    {
        public const int NoNote = -1;
        public const int NoteOff = -2;
        public const int NoVolume = -1;

        public readonly int Note;        // MIDI number (A-4 = 69), NoNote or NoteOff
        public readonly int Instrument;  // 1 – 255, 0 = none
        public readonly char Effect;     // '0' – '9', 'A' – 'Z', '\0' = none
        public readonly int Param;       // 0 – 255
        // A second effect (a maxYMiser song has two on a line), '\0' = none
        public readonly char Effect2;
        public readonly int Param2;
        // The volume column: the volume the channel gets on the row, 0 – 64 (0x40), or NoVolume.
        // Kept as one more, so that a cell that is all zeros (default) has none.
        private readonly int _volume;
        public int Volume => _volume - 1;

        public static readonly TrackerCell Empty = new(NoNote, 0, '\0', 0);

        public TrackerCell(int note, int instrument, char effect, int param, int volume = NoVolume, char effect2 = '\0', int param2 = 0)
        {
            Note = note;
            Instrument = instrument;
            Effect = effect;
            Param = param;
            _volume = volume < 0 ? 0 : Math.Min(volume, 64) + 1;
            Effect2 = effect2;
            Param2 = effect2 == '\0' ? 0 : param2;
        }

        // The cell with one part changed, the rest as it is
        public TrackerCell WithVolume(int volume) => new(Note, Instrument, Effect, Param, volume, Effect2, Param2);
        public TrackerCell WithNote(int note, int instrument) => new(note, instrument, Effect, Param, Volume, Effect2, Param2);
        public TrackerCell WithEffect(char effect, int param) => new(Note, Instrument, effect, param, Volume, Effect2, Param2);
        public TrackerCell WithEffect2(char effect, int param) => new(Note, Instrument, Effect, Param, Volume, effect, param);

        public bool IsEmpty => Note == NoNote && Instrument == 0 && Effect == '\0' && Volume == NoVolume && Effect2 == '\0';

        private static readonly string[] NoteNames = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

        public static string NoteName(int note) => note switch
        {
            NoNote => "---",
            NoteOff => "===",
            _ => NoteNames[note % 12] + (note / 12 - 1).ToString(CultureInfo.InvariantCulture)
        };

        // "C-4", "C#4", "---" (none), "===" or "OFF" (let go of the note)
        public static int ParseNote(string text)
        {
            text = text.Trim().ToUpperInvariant();
            if (text is "---" or "..." or "") return NoNote;
            if (text is "===" or "OFF" or "^^^") return NoteOff;
            if (text.Length != 3 || !char.IsDigit(text[2]))
                throw new FormatException($"'{text}' is not a note (write for example C-4 or C#4)");
            int semi = Array.IndexOf(NoteNames, text[..2]);
            if (semi < 0) throw new FormatException($"'{text}' is not a note (write for example C-4 or C#4)");
            return (text[2] - '0' + 1) * 12 + semi;
        }

        // "C-4 01 40 A04": note, instrument, volume and effect. Songs from before the volume column
        // have three parts, "C-4 01 A04". A second effect is a fifth part, "C-4 01 40 A04 X37".
        public static TrackerCell Parse(string text)
        {
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int note = parts.Length > 0 ? ParseNote(parts[0]) : NoNote;
            var (effect2, param2) = parts.Length > 4 ? ParseEffect(parts[4]) : ('\0', 0);

            int volume = NoVolume;
            if (parts.Length > 3)
            {
                if (parts[2].Trim('.', '-').Length > 0)
                {
                    if (parts[2].Length != 2 || !int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out volume) || volume > 64)
                        throw new FormatException($"'{parts[2]}' is not a volume (two hex digits, 00 – 40)");
                }
                parts = new[] { parts[0], parts[1], parts[3] };
            }

            int inst = 0;
            if (parts.Length > 1 && parts[1].Trim('.', '-').Length > 0 &&
                !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out inst))
                throw new FormatException($"'{parts[1]}' is not an instrument number (two hex digits)");

            var (effect, param) = parts.Length > 2 ? ParseEffect(parts[2]) : ('\0', 0);
            if (effect == '0' && param == 0) effect = '\0';
            return new TrackerCell(note, Math.Clamp(inst, 0, 255), effect, param, volume, effect2, param2);
        }

        private static (char, int) ParseEffect(string text)
        {
            if (text.Trim('.', '-').Length == 0) return ('\0', 0);
            string e = text.ToUpperInvariant();
            if (e.Length != 3 || !char.IsLetterOrDigit(e[0]) ||
                !int.TryParse(e[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int param))
                throw new FormatException($"'{text}' is not an effect (a letter or digit and two hex digits)");
            return (e[0], param);
        }

        public string VolumeText => Volume == NoVolume ? ".." : Volume.ToString("X2", CultureInfo.InvariantCulture);

        public override string ToString()
        {
            string inst = Instrument == 0 ? ".." : Instrument.ToString("X2", CultureInfo.InvariantCulture);
            string eff = Effect == '\0' ? "..." : Effect + Param.ToString("X2", CultureInfo.InvariantCulture);
            return Effect2 == '\0' ? $"{NoteName(Note)} {inst} {VolumeText} {eff}"
                : $"{NoteName(Note)} {inst} {VolumeText} {eff} {Effect2}{Param2.ToString("X2", CultureInfo.InvariantCulture)}";
        }

        public bool Equals(TrackerCell o) => Note == o.Note && Instrument == o.Instrument && Effect == o.Effect && Param == o.Param && _volume == o._volume
            && Effect2 == o.Effect2 && Param2 == o.Param2;
        public override bool Equals(object? obj) => obj is TrackerCell o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Note, Instrument, Effect, Param, _volume, Effect2, Param2);
    }
}
