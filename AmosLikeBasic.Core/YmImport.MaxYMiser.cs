using System;
using System.Collections.Generic;
using System.Linq;

namespace AmosLikeBasic;

// A maxYMiser song read as it was written (MaxYMiserSong): its notes, rows and patterns as they are,
// rather than found by listening to the chip. The song is played through as the replay routine
// plays it, a line at a time, keeping each channel's state: the instrument it has (with what the
// effects changed in it), the note, the volume column, the transpose, the extra arpeggio and the
// slides. Wherever the instrument's sequences start, the tracker gets a note, and the sound from there
// (the sequences stepped tick by tick: volume, arpeggio, vibrato, mixer, noise, fixed frequency, and
// the timer's waveform) becomes an instrument's table and envelope. Each such sound is an instrument
// of the tracker, named after maxYMiser's.
//  - The tone, noise and buzzer as the mixer sequence switches them; a buzzer shape that goes once as
//    a click, and the buzzer with the tone on through it as a sample of the two.
//  - A SID voice (the timer switching the volume): with the tone a pulse (the two at one pitch), with
//    the tone off the timer's own wave as a sample. PWM a pulse of its width; the sync buzzer the
//    buzzer at the note's pitch, and a digidrum a sampled instrument.
//  - Effects: portamento (3xx), pitch slide (1xx / 2xx), the extra arpeggio (0xy, or Jx0 for two
//    steps), a note without an instrument (3FF: the pitch changes, the sound goes on), song speed
//    (Fxx), pattern break (D00) and the volume column. The effects that change the instrument (its
//    sequences, flags, detune) give a new instrument from the row they are on.
public static partial class YmImport
{
    // Chip music read into the tracker: a maxYMiser song in an SNDH file from the song itself (the
    // STe's DMA sound, where it has it, from the song as played), anything else from the chip
    public static ModImport.Result ImportChipMusic(byte[] bytes, string name, int tune = 0,
        string? sampleFolder = null, string? sampleFolderInSong = null, Action<double>? progress = null)
    {
        if (SndhFile.IsSndh(bytes))
        {
            var sndh = SndhFile.Read(bytes);
            int songs = MaxYMiserSong.Count(sndh.Data);
            // A song for each tune (some files play their tunes from one song: those are read from the chip)
            if (songs > 0 && songs == sndh.Tunes && MaxYMiserSong.Find(sndh.Data, tune) is { } m)
            {
                var recording = m.DmaUsed && sampleFolder != null ? SndhPlayer.Record(sndh, tune, progress: progress) : null;
                var r = ImportMaxYMiser(m, sndh.Title.Length > 0 ? sndh.Title : name, sampleFolder, sampleFolderInSong,
                    recording, sndh.Composer);
                if (r != null) return r;
            }
        }
        return Import(YmFile.ReadChipMusic(bytes, tune, progress), name, true, sampleFolder, sampleFolderInSong);
    }

    // The song as maxYMiser wrote it, or null when it cannot be (too many sounds for 255 instruments)
    // recording: the song as played, for the STe's DMA sound (the fourth track's samples) when it has it
    public static ModImport.Result? ImportMaxYMiser(MaxYMiserSong m, string name, string? sampleFolder = null,
        string? sampleFolderInSong = null, YmFile? recording = null, string composer = "")
    {
        var conv = new MymConverter(m, sampleFolder, sampleFolderInSong ?? sampleFolder);
        var dma = recording?.Dma != null && sampleFolder != null ? DmaInstruments(recording, sampleFolder, sampleFolderInSong ?? sampleFolder) : null;
        var song = conv.Convert(dma);
        if (song == null)
        {
            // Too many sounds: without an instrument for each note of the noises and fixed pitches (the
            // chip plays them exactly anyway; the synth's sound for them is then the first note's)
            conv = new MymConverter(m, sampleFolder, sampleFolderInSong ?? sampleFolder, byNote: false);
            dma = recording?.Dma != null && sampleFolder != null ? DmaInstruments(recording, sampleFolder, sampleFolderInSong ?? sampleFolder) : null;
            song = conv.Convert(dma);
        }
        if (song == null) return null;
        Compact(song);
        int shared = song.ShareChannels();
        song.Title = name;
        song.Tempo = conv.Tempo(m.Speed);
        var notes = new List<string>(conv.Notes);
        string report = conv.Report(song, dma, composer) + SharedLine(song, shared);
        return new ModImport.Result(song, conv.Samples, notes, report);
    }

    private sealed class MymConverter
    {
        private readonly MaxYMiserSong _m;
        private readonly string? _folder, _inSong;
        private readonly (string, string)? _buzzers;
        public readonly List<string> Notes = new();
        public int Samples;
        private readonly Dictionary<string, int> _kinds = new();
        private readonly List<TrackerInstrument> _instruments = new();
        private readonly Dictionary<string, int> _sounds = new();          // a sound's key → instrument (from 1)
        private readonly Dictionary<string, Rendered> _rendered = new();
        private readonly Dictionary<string, int> _contents = new();        // an instrument's settings → its number
        private readonly Dictionary<int, string> _digiFiles = new();
        private int _retriggers, _legato;

        private readonly bool _byNote;

        public MymConverter(MaxYMiserSong m, string? folder, string? inSong, bool byNote = true)
        {
            _byNote = byNote;
            _m = m;
            _folder = folder;
            _inSong = inSong;
            _k = Math.Max(1, (int)Math.Ceiling(m.Rate * 2.5 / 255));
            _rho = m.Speed / (double)TrackerSpeed(m.Speed);
            if (folder != null && inSong != null) _buzzers = BuzzerSamples(folder, inSong);
        }

        // What an instrument is set to: maxYMiser's, as the effects have changed it since it was given
        private sealed class Voice
        {
            public int Number;                       // the instrument, 1 – 32
            public int[] Flags = new int[6];         // portamento, arpeggio, vibrato, transpose, fixed frequency, fixed detune
            public int Speed, Width, BuzzerWave, Digi, DigiRate, Volume, Coarse, Fine;
            public int[] Seq = new int[8];           // volume, arpeggio, vibrato, mixer, noise, fixed, timer, PWM
            // The PWM slide (7xx): added to the pulse width every tick of the note, unless 9xx set the width
            public int PwmSlide;
            public bool PwmFixed, PwmReset;   // PwmReset: 780 on the note's line, the slide's sum back to 0

            public static Voice From(MaxYMiserSong.Instrument i, int number) => new()
            {
                Number = number,
                Flags = new[] { i.Portamento, i.Arpeggio, i.Vibrato, i.Transpose, i.FixedFrequency, i.FixedDetune },
                Speed = i.SequenceSpeed, Width = i.PulseWidth, BuzzerWave = i.BuzzerWave & 15, Digi = i.DigiSample,
                DigiRate = i.DigiRate, Volume = i.Volume, Coarse = i.DetuneCoarse, Fine = i.DetuneFine,
                Seq = new[] { i.VolumeSeq, i.ArpeggioSeq, i.VibratoSeq, i.MixerSeq, i.NoiseSeq, i.FixedSeq, i.TimerSeq, i.PwmSeq }
            };

            public string Key => $"{Number}:{string.Join(",", Flags)}:{Speed},{Width},{BuzzerWave},{Digi},{DigiRate},{Volume},{Coarse},{Fine}:{string.Join(",", Seq)}:{PwmSlide},{PwmFixed},{PwmReset}";
        }

        // A channel as the replay routine keeps it
        private sealed class Channel
        {
            public Voice? Voice;
            public bool Modified;                    // an effect changed the voice: giving the instrument again sets it anew
            public int Note;                         // 0: none
            public int Transpose, NoiseTranspose, VolumeColumn;
            public int ArpX, ArpY;                   // the extra arpeggio (X), 0 0: off
            public int Porta, Slide;                 // rates (period units a tick); a portamento goes on until it arrives
            public int PwmSlide;                     // 7xx: it stays with the channel, through new notes
            public int PortaTarget;
            public bool Sounding;                    // a note of the tracker plays
            public int WrittenNote;                  // the note the tracker plays
            public int WrittenVolume = 64;
        }

        // A sound rendered tick by tick, ready to be an instrument
        private sealed class Rendered
        {
            public Step[] Steps = Array.Empty<Step>();
            public bool Absolute;                    // its pitch does not follow the note (noise, fixed frequency, a digidrum)
            public bool Buzzer;                      // the buzzer: the volume column does not make it quieter
            public int Digi;                         // a digidrum (1 – 8) instead of the steps, 0 none
            public int DigiRate;
            public int SidWave;                      // the timer's sequence, where the SID with the tone off is the sound
            public int SidSteps, Sounding;           // ticks it plays, and how many of them are that SID
            public int BuzzerSteps, GatedSteps;      // ticks of the buzzer, and of them with the tone on
            public bool OneShotStarted;              // a buzzer shape that goes once has gone
        }

        private static double Period(double note) => 125000.0 / (440.0 * Math.Pow(2, (note - 69) / 12.0));

        // The source the pitch effects act on: bit 2 the tone, 1 the buzzer, 0 the timer
        private const int ToneBit = 4, BuzzerBit = 2, TimerBit = 1;

        // What is heard at the note's pitch, from a mixer word: the sync buzzer's pitch is the timer's
        // (it starts the buzzer again), else the buzzer's (a tone with it only lets it through), the
        // tone's or the timer's, in that order
        private static int Source(int mix, bool timerOn) =>
            timerOn && (mix & 15) == 0xB ? TimerBit : (mix & 0xF0) != 0 ? BuzzerBit : (mix & 0x100) != 0 ? ToneBit : TimerBit;

        private int SourceBit(Voice v, int track) => Source(Value(v.Seq[3], 0), TimerOn(track));

        private int Value(int seq, int step)
        {
            if (seq <= 0 || seq >= _m.Sequences.Count) return 0;
            var s = _m.Sequences[seq].Steps;
            return s.Length == 0 ? 0 : s[Math.Min(step, s.Length - 1)];
        }

        // ── Playing the song through ──────────────────────────────────────

        public TrackerSong? Convert(DmaPart? dma)
        {
            var song = new TrackerSong { Speed = TrackerSpeed(_m.Speed) };
            song.Patterns.Clear();
            song.Order.Clear();
            var ch = new Channel[3];
            for (int t = 0; t < 3; t++) ch[t] = new Channel();
            int speed = _m.Speed;
            _tempo = Tempo(speed);

            // The rows of all places, and the frame (the replay's call) each starts at
            var rows = new List<TrackerCell[]>();
            var placeStarts = new List<int>();
            var rowFrames = new List<int>();
            int frame = 0;
            for (int place = 0; place < _m.Length; place++)
            {
                placeStarts.Add(rows.Count);
                for (int line = 0; line < MaxYMiserSong.Lines; line++)
                {
                    var cells = Enumerable.Repeat(TrackerCell.Empty, Columns).ToArray();
                    bool brk = false;
                    var global = new List<(char fx, int param)>();
                    for (int t = 0; t < 3; t++)
                    {
                        int pattern = _m.Order[place, t];
                        bool muted = (_m.Muted & (1 << t)) != 0;
                        MaxYMiserSong.Line l = default;
                        if (pattern < _m.Patterns.Count) l = _m.Patterns[pattern][line];
                        else if (pattern == 0xFE && line == 0 && !muted) l = new MaxYMiserSong.Line(1, 0, 0, '\0', 0, '\0', 0);
                        cells[t] = Line(ch[t], t, l, muted, global, ref speed, ref brk);
                    }
                    // The song's effects: in a column of their own, or where a channel has room
                    foreach (var (fx, param) in global)
                    {
                        int free = cells[3].Effect == '\0' ? 3 : Array.FindIndex(cells, 0, 3, x => x.Effect == '\0');
                        if (free < 0 && cells[4].IsEmpty) free = 4;
                        if (free >= 0) cells[free] = new TrackerCell(cells[free].Note, cells[free].Instrument, fx, param, cells[free].Volume);
                    }
                    rows.Add(cells);
                    rowFrames.Add(frame);
                    frame += speed;
                    if (brk) break;
                }
                if (_instruments.Count > 255) { Notes.Add("The song has more sounds than 255 instruments."); return null; }
            }
            if (rows.Count == 0) return null;
            // The end: back to where the song repeats from
            if (_m.Repeat > 0 && _m.Repeat < _m.Length)
            {
                var last = rows[^1];
                int free = last[3].Effect == '\0' ? 3 : Enumerable.Range(0, 3).FirstOrDefault(c => last[c].Effect == '\0', -1);
                if (free >= 0) last[free] = new TrackerCell(last[free].Note, last[free].Instrument, 'B', _m.Repeat, last[free].Volume);
            }
            song.Instruments = _instruments;
            // Played by the sound chip as maxYMiser does (the tables and envelopes are for the synth)
            song.Ym = new TrackerYmSong { Timers = _m.TimerEnables, CallsPerTick = Math.Round(_rho, 4), ShapeFromMixer = _m.Version != 0 };

            // The STe's DMA sound, as the recording heard it
            var grid = new TrackerCell[rows.Count, Columns];
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < Columns; c++) grid[r, c] = rows[r][c];
            PlaceDma(song, dma, grid, rowFrames.ToArray());

            // A pattern for each place, the same ones written once
            var places = new Dictionary<string, int>();
            placeStarts.Add(rows.Count);
            for (int place = 0; place < _m.Length; place++)
            {
                int from = placeStarts[place], to = placeStarts[place + 1];
                var pattern = new TrackerPattern(to - from);
                for (int r = from; r < to; r++)
                    for (int c = 0; c < Columns; c++) pattern[r - from, c] = grid[r, c];
                string key = string.Join("\n", Enumerable.Range(0, pattern.Rows).Select(r =>
                    string.Join("|", Enumerable.Range(0, Columns).Select(c => pattern[r, c].ToString()))));
                if (!places.TryGetValue(key, out int index))
                {
                    places[key] = index = song.Patterns.Count;
                    song.Patterns.Add(pattern);
                }
                song.Order.Add(index);
            }
            return song;
        }

        // One line of a track, as parsetracker reads it; the tracker's cell for it
        private TrackerCell Line(Channel c, int track, MaxYMiserSong.Line l, bool muted, List<(char, int)> global,
            ref int speed, ref bool brk)
        {
            char[] fx = { l.Effect1, l.Effect2 };
            int[] fp = { l.Param1, l.Param2 };
            bool porta = fx.Contains('P');
            bool retrigger = false, legato = false, silence = false;
            bool midNote = false;   // the instrument changed under a note that goes on: no new note

            if (!muted)
            {
                if (l.Instrument is >= 1 and <= 32)
                {
                    if (c.Voice == null || c.Modified || c.Voice.Number != l.Instrument)
                    {
                        c.Voice = Voice.From(_m.Instruments[l.Instrument - 1], l.Instrument);
                        c.Modified = false;
                    }
                    // The sequences start again; what belongs to the note is cleared (the slides kept for a portamento)
                    // The PWM slide goes on from note to note (only 9xx on the line stops it for the note)
                    c.Voice.PwmSlide = c.PwmSlide;
                    c.Voice.PwmFixed = false;
                    c.Voice.PwmReset = false;
                    c.Transpose = c.NoiseTranspose = c.VolumeColumn = 0;
                    c.ArpX = c.ArpY = 0;
                    if (!porta) c.Porta = c.Slide = c.PortaTarget = 0;
                    retrigger = true;
                }
                if (l.Note == 1)
                {
                    // Note off: the channel is cleared
                    c.Voice = null;
                    c.Note = 0;
                    c.Transpose = c.NoiseTranspose = c.VolumeColumn = c.ArpX = c.ArpY = c.Porta = c.Slide = 0;
                    retrigger = false;
                    silence = true;
                }
                else
                {
                    if (l.Note > 1)
                    {
                        if (c.Note != 0 && porta && c.Voice != null && (c.Voice.Flags[0] & SourceBit(c.Voice, track)) != 0)
                            c.PortaTarget = l.Note;
                        else
                        {
                            if (!retrigger) legato = c.Note != 0;
                            c.PortaTarget = 0;
                        }
                        c.Note = l.Note;
                    }
                    if (l.Volume != 0) c.VolumeColumn = l.Volume & 15;
                }
            }

            // The effects
            for (int k = 0; k < 2; k++)
            {
                char e = fx[k];
                int p = fp[k];
                if (e == '\0' || e > 'Z') continue;
                if (muted && e is not ('B' or 'S')) continue;
                var v = c.Voice;
                bool changes = true;
                switch (e)
                {
                    case 'S':
                        if (p >= 2)
                        {
                            speed = p;
                            global.Add(('F', TrackerSpeed(p)));
                            if (Tempo(p) != _tempo) global.Add(('F', _tempo = Tempo(p)));
                        }
                        changes = false;
                        break;
                    case 'B': brk = true; global.Add(('D', 0)); changes = false; break;
                    case 'T': c.Transpose = (sbyte)p; changes = false; legato |= !retrigger && c.Note != 0; break;
                    case 'O': c.NoiseTranspose = (sbyte)p; break;
                    case 'P': c.Porta = p; c.Slide = 0; changes = false; break;
                    case 'H': c.Slide = (sbyte)p; c.Porta = 0; c.PortaTarget = 0; changes = false; break;
                    case 'X': c.ArpX = p >> 4; c.ArpY = p & 15; changes = false; break;
                    default:
                        if (v == null) { changes = false; break; }
                        switch (e)
                        {
                            case >= '1' and <= '6': v.Flags[e - '1'] = p; break;
                            case '9': v.Width = p; v.PwmFixed = true; break;
                            case '7':
                                c.PwmSlide = v.PwmSlide = p == 0x80 ? 0 : (sbyte)p;
                                v.PwmFixed = false;
                                if (p == 0x80) v.PwmReset = true;
                                break;
                            case 'A': v.Seq[1] = p; break;
                            case 'C': break;
                            case 'D': v.Coarse = (sbyte)p; break;
                            case 'E': v.Fine = (sbyte)p; break;
                            case 'F': v.Seq[5] = p; break;
                            case 'G': if (p <= 8) v.Digi = p; break;
                            case 'I': v.Seq[6] = p; break;
                            case 'L': v.Seq[0] = p; break;
                            case 'M': v.Seq[3] = p; break;
                            case 'N': v.Seq[4] = p; break;
                            case 'Q': v.Speed = p; break;
                            case 'R': if (p is >= 0x18 and <= 0xCD) v.DigiRate = p; break;
                            case 'V': v.Seq[2] = p; break;
                            case 'W': v.BuzzerWave = p & 15; break;
                            case '8': v.Seq[7] = p; break;
                            default: changes = false; break;   // U, Y, Z: not played
                        }
                        if (changes) c.Modified = true;
                        break;
                }
                // An effect that changes the sound mid-note starts it again (the tracker's instruments do
                // not change under a note)
                if (changes && !retrigger && c.Note != 0 && c.Voice != null)
                {
                    retrigger = true;
                    midNote = l.Note == 0 && c.Sounding;
                    _retriggers++;
                }
            }

            if (muted) return TrackerCell.Empty;

            if (silence)
            {
                bool was = c.Sounding;
                c.Sounding = false;
                return was ? new TrackerCell(TrackerCell.NoNote, 0, '\0', 0, 0) : TrackerCell.Empty;
            }

            int note = TrackerCell.NoNote, instrument = 0, volume = TrackerCell.NoVolume;
            char eff = '\0';
            int param = 0;
            var voice = c.Voice;
            if (voice != null && c.Note != 0)
            {
                int source = SourceBit(voice, track);
                int written = c.Note + ((voice.Flags[3] & source) != 0 ? c.Transpose : 0);
                // Into the tracker's notes, C-0 – B-8, by octaves
                while (written < 12) written += 12;
                while (written > 119) written -= 12;
                if (retrigger)
                {
                    var (number, rendered) = Sound(voice, written, c.NoiseTranspose, track);
                    note = midNote ? TrackerCell.NoNote : written;
                    instrument = number;
                    c.Sounding = true;
                    c.WrittenNote = written;
                    c.WrittenVolume = 64;
                    int vol = VolumeOf(c.VolumeColumn, rendered);
                    if (vol != 64) { volume = vol; c.WrittenVolume = vol; }
                    if (c.PortaTarget != 0) { eff = '3'; param = PortaParam(c); }
                }
                else if (c.Sounding)
                {
                    if (c.PortaTarget != 0 && (l.Note > 1 || fx.Contains('P')))
                    {
                        note = written;
                        c.WrittenNote = written;
                        eff = '3';
                        param = PortaParam(c);
                    }
                    else if (legato && written != c.WrittenNote)
                    {
                        note = written;
                        c.WrittenNote = written;
                        eff = '3';
                        param = 0xFF;
                        _legato++;
                    }
                    var r = _rendered.TryGetValue(RenderKey(voice, written, c.NoiseTranspose, track, true), out var rr) ? rr
                        : _rendered.GetValueOrDefault(RenderKey(voice, written, c.NoiseTranspose, track, false));
                    int vol = VolumeOf(c.VolumeColumn, r);
                    if (vol != c.WrittenVolume) { volume = vol; c.WrittenVolume = vol; }
                }

                if (c.Sounding && eff == '\0')
                {
                    if (c.PortaTarget != 0 && c.Porta > 0) { eff = '3'; param = 0; }
                    else if (c.Slide != 0 && (voice.Flags[0] & source) != 0)
                    {
                        double semis = 12 / Math.Log(2) * Math.Abs(c.Slide) / Period(c.WrittenNote);
                        eff = c.Slide > 0 ? '1' : '2';
                        param = Math.Clamp((int)Math.Round(semis * 16 * _rho), 1, 255);
                    }
                    else if ((c.ArpX != 0 || c.ArpY != 0) && (voice.Flags[1] & source) != 0)
                    {
                        if (c.ArpX == 15) { eff = 'J'; param = c.ArpY << 4; }
                        else { eff = '0'; param = c.ArpX << 4 | c.ArpY; }
                    }
                }
            }
            else if (c.Sounding && (voice == null || c.Note == 0))
            {
                c.Sounding = false;
                return new TrackerCell(TrackerCell.NoNote, 0, '\0', 0, 0);
            }
            return new TrackerCell(note, instrument, eff, param, volume);
        }

        // The portamento's rate (period units a tick) in sixteenths of a semitone, at the note it goes to
        private int PortaParam(Channel c)
        {
            double semis = 12 / Math.Log(2) * Math.Max(1, c.Porta) / Period(c.PortaTarget);
            return Math.Clamp((int)Math.Round(semis * 16 * _rho), 1, 255);
        }

        // The tracker's ticks: at most about 100 a second (tempo 255), so a faster replay has `_k` of its
        // calls in each. A row of `speed` calls is then speed / _k ticks, the tempo set so that the row
        // is exactly as long; the instruments' steps are taken every _rho calls (the song's own speed).
        private readonly int _k;
        private readonly double _rho;
        private int _tempo;

        public int TrackerSpeed(int speed) => Math.Clamp(speed / _k, 1, 31);

        public int Tempo(int speed) => Math.Clamp((int)Math.Round(TrackerSpeed(speed) * _m.Rate * 2.5 / speed), 32, 255);

        // The volume column takes so many of the chip's steps (3 dB each) off; the buzzer is not quieter
        private static int VolumeOf(int column, Rendered? r)
        {
            if (column == 0 || r == null) return 64;
            if (r.Buzzer) return 64;
            return (int)Math.Round(64 * Math.Pow(10, -3.0 * column / 20));
        }

        private string RenderKey(Voice v, int note, int noiseTranspose, int track, bool withNote) =>
            $"{v.Key}|{noiseTranspose}|{TimerOn(track)}" + (withNote ? "|" + note : "");

        private bool TimerOn(int track) => (_m.TimerEnables & (4 >> track)) != 0;

        // The instrument for a sound, made the first time it is heard
        private (int number, Rendered rendered) Sound(Voice v, int note, int noiseTranspose, int track)
        {
            string plain = RenderKey(v, note, noiseTranspose, track, false);
            string keyed = RenderKey(v, note, noiseTranspose, track, true);
            if (_sounds.TryGetValue(keyed, out int n)) return (n, _rendered[keyed]);
            if (_sounds.TryGetValue(plain, out n)) return (n, _rendered[plain]);
            var r = Render(v, note, noiseTranspose, TimerOn(track));
            string key = r.Absolute && _byNote ? keyed : plain;
            _rendered[key] = r;
            var inst = MakeMymInstrument(v, r, note);
            inst.Ym = YmPart(v, noiseTranspose);
            // Sounds that come out the same (the effects changed what is not heard) are one instrument
            string name = inst.Name;
            inst.Name = "";
            string content = System.Text.Json.JsonSerializer.Serialize(inst);
            inst.Name = name;
            if (!_contents.TryGetValue(content, out n))
            {
                _instruments.Add(inst);
                _contents[content] = n = _instruments.Count;
            }
            _sounds[key] = n;
            return (n, r);
        }

        // ── A sound, tick by tick ─────────────────────────────────────────

        // The sequences stepped as dosequences does: one step every `speed` ticks, each going round from
        // its repeat or, where the repeat is its last step, staying there
        private Rendered Render(Voice v, int note, int noiseTranspose, bool timerOn)
        {
            int speed = Math.Max(1, v.Speed);
            // How long: until the sequences have gone round twice together
            long lcm = 1;
            int start = 0;
            for (int i = 0; i < 8; i++)
            {
                if (i == 6) continue;
                int s = v.Seq[i];
                if (s <= 0 || s >= _m.Sequences.Count) continue;
                var q = _m.Sequences[s];
                int len = q.Steps.Length, rep = Math.Min(q.Repeat, Math.Max(0, len - 1));
                start = Math.Max(start, len);
                int period = rep >= len - 1 ? 1 : len - rep;
                lcm = Lcm(lcm, period);
            }
            int ticks = (int)Math.Clamp(speed * (start + 2 * Math.Min(lcm, 64)) + 1, 2, 1024);

            var counter = new int[8];
            var value = new int[8];
            var done = new bool[8];
            int speedCount = 0, pwmSlide = 0;
            var r = new Rendered();
            var steps = new List<Step>();
            int timerSeq = v.Seq[6];
            for (int t = 0; t < ticks; t++)
            {
                bool step = speedCount == 0;
                speedCount = speedCount + 1 >= speed ? 0 : speedCount + 1;
                if (step)
                    for (int i = 0; i < 8; i++)
                    {
                        if (i == 6 || done[i]) continue;
                        bool gated = i switch { 1 => v.Flags[1] != 0, 2 => v.Flags[2] != 0, 5 => v.Flags[4] != 0, _ => true };
                        int s = v.Seq[i];
                        if (!gated || s <= 0 || s >= _m.Sequences.Count || _m.Sequences[s].Steps.Length == 0)
                        {
                            value[i] = 0;
                            done[i] = true;
                            continue;
                        }
                        var q = _m.Sequences[s];
                        int len = q.Steps.Length;
                        value[i] = q.Steps[Math.Min(counter[i], len - 1)];
                        counter[i]++;
                        if (counter[i] >= len)
                        {
                            counter[i] = q.Repeat;
                            if (q.Repeat == len - 1) done[i] = true;
                        }
                    }
                steps.Add(Tick(v, note, noiseTranspose, timerOn, value, timerSeq, r, pwmSlide));
                // The PWM slide moves after the tick, up to ±255
                pwmSlide = Math.Clamp(pwmSlide + v.PwmSlide, -255, 255);
            }
            r.Steps = steps.ToArray();
            return r;
        }

        private static long Lcm(long a, long b)
        {
            long g = a, h = b;
            while (h != 0) (g, h) = (h, g % h);
            return Math.Min(a / g * b, 1 << 16);
        }

        // What the chip plays in a tick, from the sequences' values
        private Step Tick(Voice v, int note, int noiseTranspose, bool timerOn, int[] value, int timerSeq, Rendered r, int pwmSlide)
        {
            var silent = new Step(Mode.Silent, 0, 31);
            int vol = value[0], mix = value[3];
            if (vol == 0 || mix == 0) return silent;
            int level = (vol & 0xFF) - v.Volume;
            if (level <= 0) return silent;
            level = Math.Min(level, 15);
            bool tone = (mix & 0x100) != 0, noise = (mix & 0x1000) != 0, buzzer = (mix & 0xF0) != 0;
            int timer = timerOn ? mix & 15 : 0;
            int source = Source(mix, timerOn);

            // The pitch, from the note's own period. The buzzer's periods (the envelope's) are a
            // sixteenth of the tone's for the same pitch: its fixed frequency is one of them (its detune
            // and vibrato are divided by 16 to be the tone's).
            double offset;
            int fixedValue = value[5];
            double unit = source == BuzzerBit ? 16 : 1;
            if ((v.Flags[4] & source) != 0 && v.Seq[5] > 0 && (fixedValue & 0xFFFF) != 0xFFFF)
            {
                // A period of 0 is no sound (the tone is muted)
                if ((fixedValue & 0xFFF) == 0) return silent;
                offset = 12 * Math.Log2(Period(note) / ((fixedValue & 0xFFF) * unit));
                r.Absolute = true;
            }
            else
            {
                double semis = 0;
                if ((v.Flags[5] & source) != 0) semis += v.Coarse;
                if ((v.Flags[1] & source) != 0) semis += value[1];
                double period = Period(note + semis);
                if ((v.Flags[5] & source) != 0) period -= v.Fine;
                if ((v.Flags[2] & source) != 0) period -= value[2];
                offset = 12 * Math.Log2(Period(note) / Math.Max(1, period));
            }
            if (Math.Abs(offset) > 96) return silent;

            int down = 31 - (level * 2 + 1);
            if (timer == 0xD && v.Digi is >= 1 and <= 8 && v.Digi <= _m.Digidrums.Count && _m.Digidrums[v.Digi - 1].Length > 0)
            {
                r.Digi = v.Digi;
                r.DigiRate = v.DigiRate is >= 0x18 and <= 0xCD ? v.DigiRate : 0x20;
                r.Absolute = true;
            }
            if (buzzer || timer == 0xB)
            {
                // The buzzer's shape: the mixer's (1 there is the instrument's own); the older format's
                // mixer only switches it on
                int nibble = mix >> 4 & 15;
                int wave = nibble <= 1 || _m.Version == 0 ? v.BuzzerWave : nibble;
                bool sync = timer == 0xB;
                if (!sync && wave is not (8 or 10 or 12 or 14))
                {
                    // A shape that goes once: a click where it starts, then nothing, or the top level held
                    // (11 and 13), through which the tone is heard as it is
                    if (!r.OneShotStarted)
                    {
                        r.OneShotStarted = true;
                        return new Step(Mode.BuzzSaw, offset, 0);
                    }
                    return tone && wave is 11 or 13 ? new Step(noise ? Mode.ToneNoise : Mode.Tone, offset, 0) : silent;
                }
                r.Buzzer = true;
                r.BuzzerSteps++;
                if (tone && !sync) r.GatedSteps++;
                // The triangle is two rounds of the envelope down and up: an octave below the note
                var mode = wave is 10 or 14 ? Mode.BuzzTriangle : Mode.BuzzSaw;
                return new Step(mode, mode == Mode.BuzzTriangle ? offset - 12 : offset, 0);
            }
            r.Sounding++;
            if (timer == 5 && !noise)
            {
                // The timer writes the channel's volume less each step of its sequence, a round of them
                // at the note's pitch: with the tone off that is the wave; with it on, the two together
                // a pulse, narrower as they drift apart and meet again
                double duty = Duty(timerSeq);
                if (duty <= 0) return silent;
                if (!tone) { r.SidWave = timerSeq; r.SidSteps++; }
                int width = (int)Math.Round(tone ? 50 * duty : 100 * duty);
                return new Step(Mode.Pulse, offset, down, Math.Clamp(width, 3, 97));
            }
            if (timer == 9 && !noise)
            {
                // The timer's two halves are the period ± w/256 of it: from a square (0) to a thin pulse (255)
                int w = Math.Clamp(v.Width + (v.PwmFixed ? 0 : pwmSlide) + (value[7] & 0xFF), 0, 255);
                double duty = 0.5 * (1 + w / 256.0);
                int width = (int)Math.Round(tone ? 50 * duty : 100 * duty);
                return new Step(Mode.Pulse, offset, down, Math.Clamp(width, 3, 97));
            }
            if (noise && !tone)
            {
                int np = Math.Clamp((value[4] & 0xFF) + noiseTranspose, 0, 31);
                double freq = 125000.0 / Math.Max(1, np) / 16;
                r.Absolute = true;
                return new Step(Mode.Noise, 69 + 12 * Math.Log2(freq / 440.0) - note, down);
            }
            if (!tone && timer == 0) return silent;
            return new Step(noise ? Mode.ToneNoise : Mode.Tone, offset, down);
        }

        // How much of a SID's timer waveform is on
        private double Duty(int seq)
        {
            if (seq <= 0 || seq >= _m.Sequences.Count) return 0.5;
            var s = _m.Sequences[seq].Steps;
            int n = Math.Min(s.Length, 16);
            if (n == 0) return 0.5;
            return s.Take(n).Count(x => 15 - x > 0) / (double)n;
        }

        private TrackerInstrument MakeMymInstrument(Voice v, Rendered r, int note)
        {
            string baseName = _m.Instruments[v.Number - 1].Name;
            if (baseName.Length == 0) baseName = $"Instrument {v.Number}";
            int count = _instruments.Count(i => i.Name == baseName || i.Name.StartsWith(baseName + " ("));
            string name = count == 0 ? baseName : $"{baseName} ({count + 1})";
            if (r.Digi > 0 && _folder != null && _inSong != null)
            {
                string path = DigiFile(r.Digi);
                double rate = 614400.0 / r.DigiRate;
                // The sample's own rate at the note it is played on: SampleNote is that note
                int sampleRate = _digiRates[r.Digi];
                return new TrackerInstrument
                {
                    Name = name, Wave = "Sample", Sample = path,
                    SampleNote = (int)Math.Round(note - 12 * Math.Log2(rate / sampleRate)),
                    Volume = 0.23, Attack = 0, Decay = 0, Sustain = 100, Release = 5
                };
            }
            var steps = r.Steps;
            if (_rho > 1.01)
                steps = Enumerable.Range(0, (int)(steps.Length / _rho)).Select(j => steps[(int)Math.Round(j * _rho)]).ToArray();
            var inst = MakeInstrument(steps, _kinds, _buzzers);
            inst.Name = name;
            // The buzzer with the tone on: the tone lets it through half of each of its periods
            if (inst.Wave == "Sample" && r.BuzzerSteps > 0 && r.GatedSteps == r.BuzzerSteps && _folder != null && _inSong != null)
                inst.Sample = GatedBuzzer(inst.LoopEnd > 256);
            if (r.SidWave > 0 && r.SidSteps == r.Sounding && _folder != null && _inSong != null)
            {
                // The SID's own wave, a round of it looping; the table keeps only the pitch
                inst.Wave = "Sample";
                inst.Sample = SidFile(r.SidWave, out int frames);
                inst.SampleNote = 60;
                inst.LoopStart = 0;
                inst.LoopEnd = frames;
                inst.Volume = 0.23;
                inst.Table = inst.Table.Select(t =>
                {
                    var pitch = t.Split(' ').Where(w => w.Length > 0 && (char.IsDigit(w[0]) || w[0] is '+' or '-')).ToList();
                    return pitch.Count > 0 ? pitch[0] : "0";
                }).ToList();
            }
            return inst;
        }

        private readonly Dictionary<int, int> _digiRates = new();

        // The instrument as maxYMiser has it, for the sound chip
        private TrackerYmInstrument YmPart(Voice v, int noiseTranspose)
        {
            TrackerYmSequence? Seq(int i)
            {
                int n = v.Seq[i];
                if (n <= 0 || n >= _m.Sequences.Count) return null;
                var q = _m.Sequences[n];
                return new TrackerYmSequence { Steps = q.Steps.Select(x => i is 1 or 2 ? x : x & 0xFFFF).ToList(), Repeat = q.Repeat };
            }
            return new TrackerYmInstrument
            {
                Speed = Math.Max(1, v.Speed), Volume = v.Volume,
                Portamento = v.Flags[0], Arpeggio = v.Flags[1], Vibrato = v.Flags[2], Transpose = v.Flags[3],
                FixedFrequency = v.Flags[4], FixedDetune = v.Flags[5],
                DetuneCoarse = v.Coarse, DetuneFine = v.Fine, PulseWidth = v.Width, PwmSlide = v.PwmSlide, PwmFixed = v.PwmFixed,
                PwmReset = v.PwmReset,
                BuzzerWave = v.BuzzerWave & 15, NoiseTranspose = noiseTranspose,
                DigiSample = v.Digi is >= 1 and <= 8 && v.Digi <= _m.Digidrums.Count && _m.Digidrums[v.Digi - 1].Length > 0 && _folder != null ? DigiFile(v.Digi) : "",
                DigiRate = v.DigiRate is >= 0x18 and <= 0xCD ? v.DigiRate : 0x20,
                VolumeSeq = Seq(0), ArpeggioSeq = Seq(1), VibratoSeq = Seq(2), MixerSeq = Seq(3),
                NoiseSeq = Seq(4), FixedSeq = Seq(5), TimerSeq = Seq(6), PwmSeq = Seq(7)
            };
        }
        private readonly string?[] _gated = new string?[2];

        // The buzzer's sawtooth (256 frames, a round of the envelope) or triangle (512), as the plain
        // ones a round per cycle of the note, on only in the first half of each period of the tone (the
        // saw's own period, half the triangle's)
        private string GatedBuzzer(bool triangle)
        {
            int i = triangle ? 1 : 0;
            if (_gated[i] != null) return _gated[i]!;
            var down = Enumerable.Range(0, 256).Select(k => (float)Level[31 - k / 8]).ToArray();
            var wave = triangle ? down.Concat(down.Reverse()).ToArray() : down;
            var levels = wave.Select((l, k) => k % 256 < 128 ? l : 0f).ToArray();
            float mean = levels.Average();
            var data = levels.Select(l => Math.Clamp((l - mean) * 2, -1f, 1f)).ToArray();
            string file = triangle ? "ym buzzer triangle and tone.wav" : "ym buzzer saw and tone.wav";
            SampleBank.WriteWav(System.IO.Path.Combine(_folder!, file), new SampleData(data, (int)Math.Round(261.63 * data.Length)));
            string path = _inSong!.TrimEnd('/') + "/" + file;
            SampleBank.Forget(path);
            Samples++;
            return _gated[i] = path;
        }
        private readonly Dictionary<int, (string path, int frames)> _sidFiles = new();

        // A SID's wave: a round of its steps (at most 16), each the chip's level for full volume less
        // the step, 16 frames a step so that the steps stay sharp; C-4 plays a round per cycle
        private string SidFile(int seq, out int frames)
        {
            if (_sidFiles.TryGetValue(seq, out var known)) { frames = known.frames; return known.path; }
            var steps = _m.Sequences[seq].Steps.Take(16).ToArray();
            var levels = steps.SelectMany(x => Enumerable.Repeat((float)Level[Math.Max(0, 15 - x) switch { 0 => 0, var l => l * 2 + 1 }], 16)).ToArray();
            float mean = levels.Average();
            var data = levels.Select(l => Math.Clamp((l - mean) * 2, -1f, 1f)).ToArray();
            frames = data.Length;
            string file = $"maxymiser sid {seq}.wav";
            SampleBank.WriteWav(System.IO.Path.Combine(_folder!, file), new SampleData(data, (int)Math.Round(261.63 * frames)));
            string path = _inSong!.TrimEnd('/') + "/" + file;
            SampleBank.Forget(path);
            _sidFiles[seq] = (path, frames);
            Samples++;
            return path;
        }

        // A digidrum written as a WAV file at the default rate ($20)
        private string DigiFile(int digi)
        {
            if (_digiFiles.TryGetValue(digi, out var path)) return path;
            var data = _m.Digidrums[digi - 1].Select(b => b / 128f).ToArray();
            int rate = 614400 / 0x20;
            string file = $"maxymiser digi {digi}.wav";
            SampleBank.WriteWav(System.IO.Path.Combine(_folder!, file), new SampleData(data, rate));
            path = _inSong!.TrimEnd('/') + "/" + file;
            SampleBank.Forget(path);
            _digiFiles[digi] = path;
            _digiRates[digi] = rate;
            Samples++;
            return path;
        }

        public string Report(TrackerSong song, DmaPart? dma, string composer)
        {
            var lines = new List<string>
            {
                $"\"{song.Title}\"" + (composer.Length > 0 ? " by " + composer : ""),
                $"Made in maxYMiser (its format {_m.Version}): read from the song itself, its notes, instruments and patterns as they were written, not from the chip.",
                "",
                $"Speed {_m.Speed} at {_m.Rate} Hz, {_m.Length} places in the order list, {_m.Patterns.Count} maxYMiser patterns.",
                $"{song.Instruments.Count} instruments from maxYMiser's {_m.Instruments.Count(i => i.Name.Length > 0)}: one for each sound they make (with the effects that change them).",
                $"{song.Patterns.Count} patterns, played {song.Order.Count} times in the order list."
            };
            if (_m.Repeat > 0) lines.Add($"At the end it jumps back to place {_m.Repeat}.");
            if (_legato > 0) lines.Add($"{_legato} notes without an instrument change the pitch of the one playing (3FF).");
            if (_retriggers > 0) lines.Add($"{_retriggers} effects change an instrument under a note: the instrument is changed on the row, and the note goes on.");
            if (dma != null && dma.Notes.Count > 0) lines.Add($"The STe's DMA sound: {dma.Instruments.Count} sample{(dma.Instruments.Count == 1 ? "" : "s")} from the song as played, in channel {song.Channels}.");
            else if (_m.DmaUsed) lines.Add("The STe's DMA track was left out.");
            return string.Join("\n", lines);
        }
    }
}
