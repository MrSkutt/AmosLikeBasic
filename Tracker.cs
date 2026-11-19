using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    // A song has 4 – 8 channels (Channels); files from before that have 4. The patterns always
    // hold MaxChannels channels, so that fewer channels and back again loses nothing until saving.
    public sealed class TrackerSong
    {
        public const int MaxChannels = 8;
        public const int MinChannels = 4;
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

        public List<TrackerInstrument> Instruments { get; set; } = new();
        public List<int> Order { get; set; } = new() { 0 };
        public List<TrackerPattern> Patterns { get; set; } = new() { new TrackerPattern() };

        // ── Filformat ────────────────────────────────────────────────

        private sealed class SongFile
        {
            public string Format { get; set; } = FormatName;
            public int Version { get; set; } = FormatVersion;
            public string Title { get; set; } = "";
            public int Speed { get; set; } = 6;
            public int Tempo { get; set; } = 125;
            public int Channels { get; set; } = DefaultChannels;
            public List<TrackerInstrument> Instruments { get; set; } = new();
            public List<int> Order { get; set; } = new();
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
                Instruments = Instruments,
                Order = Order
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
        // The volume column: the volume the channel gets on the row, 0 – 64 (0x40), or NoVolume.
        // Kept as one more, so that a cell that is all zeros (default) has none.
        private readonly int _volume;
        public int Volume => _volume - 1;

        public static readonly TrackerCell Empty = new(NoNote, 0, '\0', 0);

        public TrackerCell(int note, int instrument, char effect, int param, int volume = NoVolume)
        {
            Note = note;
            Instrument = instrument;
            Effect = effect;
            Param = param;
            _volume = volume < 0 ? 0 : Math.Min(volume, 64) + 1;
        }

        public TrackerCell WithVolume(int volume) => new(Note, Instrument, Effect, Param, volume);

        public bool IsEmpty => Note == NoNote && Instrument == 0 && Effect == '\0' && Volume == NoVolume;

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
        // have three parts, "C-4 01 A04".
        public static TrackerCell Parse(string text)
        {
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int note = parts.Length > 0 ? ParseNote(parts[0]) : NoNote;

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

            char effect = '\0';
            int param = 0;
            if (parts.Length > 2 && parts[2].Trim('.', '-').Length > 0)
            {
                string e = parts[2].ToUpperInvariant();
                if (e.Length != 3 || !char.IsLetterOrDigit(e[0]) ||
                    !int.TryParse(e[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out param))
                    throw new FormatException($"'{parts[2]}' is not an effect (a letter or digit and two hex digits)");
                effect = e[0];
                if (effect == '0' && param == 0) effect = '\0';
            }
            return new TrackerCell(note, Math.Clamp(inst, 0, 255), effect, param, volume);
        }

        public string VolumeText => Volume == NoVolume ? ".." : Volume.ToString("X2", CultureInfo.InvariantCulture);

        public override string ToString()
        {
            string inst = Instrument == 0 ? ".." : Instrument.ToString("X2", CultureInfo.InvariantCulture);
            string eff = Effect == '\0' ? "..." : Effect + Param.ToString("X2", CultureInfo.InvariantCulture);
            return $"{NoteName(Note)} {inst} {VolumeText} {eff}";
        }

        public bool Equals(TrackerCell o) => Note == o.Note && Instrument == o.Instrument && Effect == o.Effect && Param == o.Param && _volume == o._volume;
        public override bool Equals(object? obj) => obj is TrackerCell o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Note, Instrument, Effect, Param, _volume);
    }
}
