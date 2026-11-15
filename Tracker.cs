using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmosLikeBasic
{
    // En låt för trackern: instrument, mönster (patterns) och i vilken ordning mönstren spelas.
    //
    // Sparas som JSON (.amostrk). Raderna i ett mönster sparas som text, en sträng per rad
    // och kanalerna åtskilda med |, så att filen går att läsa och diffa:
    //   "C-4 01 A04|--- .. ...|=== .. ...|E-3 02 C20"
    // Varje cell är not, instrument (hex) och effekt (bokstav/siffra + två hexsiffror).
    public sealed class TrackerSong
    {
        public const int ChannelCount = 4;
        public const int DefaultRows = 64;
        public const int MaxRows = 256;
        public const string FormatName = "AmosTracker";
        public const int FormatVersion = 1;

        public string Title { get; set; } = "";
        public int Speed { get; set; } = 6;      // tick per rad
        public int Tempo { get; set; } = 125;    // som i ProTracker: 125 ger 50 tick per sekund
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
                Instruments = Instruments,
                Order = Order
            };
            foreach (var p in Patterns)
            {
                var rows = new List<string>(p.Rows);
                for (int r = 0; r < p.Rows; r++) rows.Add(FormatRow(p, r));
                file.Patterns.Add(rows);
            }
            return JsonSerializer.Serialize(file, JsonOptions);
        }

        private static TrackerCell[] ParseRow(string text, int pattern, int row)
        {
            var cells = new TrackerCell[ChannelCount];
            var parts = (text ?? "").Split('|');
            for (int c = 0; c < ChannelCount; c++)
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

        private static string FormatRow(TrackerPattern p, int row)
        {
            var parts = new string[ChannelCount];
            for (int c = 0; c < ChannelCount; c++) parts[c] = p[row, c].ToString();
            return string.Join("|", parts);
        }
    }

    // Ett ljud i trackern. Enheterna är desamma som i PLAY-kommandona.
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
        public int Ring { get; set; }                      // kanal, 0 = av
        public int Sync { get; set; }                      // kanal, 0 = av

        // Ett steg per tick från noten startar: "NOISE +24", "PULSE", "-12", "P30" (pulsbredd).
        // TableLoop är steget tabellen hoppar tillbaka till, -1 stannar på sista steget.
        public List<string> Table { get; set; } = new();
        public int TableLoop { get; set; } = -1;
    }

    public sealed class TrackerPattern
    {
        private TrackerCell[,] _cells;

        public TrackerPattern(int rows = TrackerSong.DefaultRows)
        {
            _cells = new TrackerCell[Math.Clamp(rows, 1, TrackerSong.MaxRows), TrackerSong.ChannelCount];
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
            for (int c = 0; c < TrackerSong.ChannelCount; c++) _cells[row, c] = cells[c];
        }

        public void Clear()
        {
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TrackerSong.ChannelCount; c++)
                _cells[r, c] = TrackerCell.Empty;
        }

        // Ändrar antalet rader och behåller det som får plats
        public void Resize(int rows)
        {
            var old = _cells;
            _cells = new TrackerCell[Math.Clamp(rows, 1, TrackerSong.MaxRows), TrackerSong.ChannelCount];
            Clear();
            for (int r = 0; r < Math.Min(old.GetLength(0), Rows); r++)
            for (int c = 0; c < TrackerSong.ChannelCount; c++)
                _cells[r, c] = old[r, c];
        }
    }

    public readonly struct TrackerCell : IEquatable<TrackerCell>
    {
        public const int NoNote = -1;
        public const int NoteOff = -2;

        public readonly int Note;        // MIDI-nummer (A-4 = 69), NoNote eller NoteOff
        public readonly int Instrument;  // 1 – 255, 0 = inget
        public readonly char Effect;     // '0' – '9', 'A' – 'Z', '\0' = ingen
        public readonly int Param;       // 0 – 255

        public static readonly TrackerCell Empty = new(NoNote, 0, '\0', 0);

        public TrackerCell(int note, int instrument, char effect, int param)
        {
            Note = note;
            Instrument = instrument;
            Effect = effect;
            Param = param;
        }

        public bool IsEmpty => Note == NoNote && Instrument == 0 && Effect == '\0';

        private static readonly string[] NoteNames = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

        public static string NoteName(int note) => note switch
        {
            NoNote => "---",
            NoteOff => "===",
            _ => NoteNames[note % 12] + (note / 12 - 1).ToString(CultureInfo.InvariantCulture)
        };

        // "C-4", "C#4", "---" (ingen), "===" eller "OFF" (släpp tonen)
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

        public static TrackerCell Parse(string text)
        {
            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int note = parts.Length > 0 ? ParseNote(parts[0]) : NoNote;

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
            return new TrackerCell(note, Math.Clamp(inst, 0, 255), effect, param);
        }

        public override string ToString()
        {
            string inst = Instrument == 0 ? ".." : Instrument.ToString("X2", CultureInfo.InvariantCulture);
            string eff = Effect == '\0' ? "..." : Effect + Param.ToString("X2", CultureInfo.InvariantCulture);
            return $"{NoteName(Note)} {inst} {eff}";
        }

        public bool Equals(TrackerCell o) => Note == o.Note && Instrument == o.Instrument && Effect == o.Effect && Param == o.Param;
        public override bool Equals(object? obj) => obj is TrackerCell o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Note, Instrument, Effect, Param);
    }
}
