using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AmosLikeBasic;

// Writes a tracker song as a FastTracker 2 module (.xm), for other trackers and players. The rows,
// the order list, the volume column and nearly all effects are XM's own (the tracker's slides are in
// sixteenths of a semitone, as XM's linear frequencies). What XM does not have is the synth:
//  - a sampled instrument goes as its sample, with its loop, envelopes, fadeout and pan;
//  - any other instrument (a wave, a table, a filter, oscillator 2, LFOs, echo) is played by the
//    synth into samples: a note held as long as the song holds it, for each note it plays (at most
//    16, XM's samples an instrument; keys between take the nearest), so that its table steps keep
//    their ticks. Its attack and decay are in the sample, its release in an XM envelope.
//  - each channel's own order is written out: every place becomes a pattern as it is heard;
//  - Gxx, Hxx and Ixx (filter and pulse width), E0x and EFx have nothing in XM and are left out;
//    Jxy becomes the arpeggio 0xy.
public static class XmExport
{
    public sealed record Result(byte[] Bytes, List<string> Notes);

    private const int MaxInstruments = 128, MaxSamples = 16, MaxEnvelopePoints = 12;
    private const double C4Rate = 8363;
    private const double MaxHoldSeconds = 6;

    private sealed class XmSample
    {
        public short[] Data = Array.Empty<short>();
        public float[] Raw = Array.Empty<float>();
        public int LoopStart, LoopLength;
        public int Volume = 64, Panning = 128, RelativeNote, Finetune;
        public string Name = "";
    }

    private sealed class XmInstrument
    {
        public string Name = "";
        public List<XmSample> Samples = new();
        public byte[] KeyMap = new byte[96];
        public List<(int Tick, int Level)> Volume = new(), Pan = new();
        public int VolumeSustain = -1, VolumeLoopStart = -1, VolumeLoopEnd = -1;
        public int PanSustain = -1, PanLoopStart = -1, PanLoopEnd = -1;
        public int Fadeout;
    }

    // render: plays notes of instruments into sound (ChiptuneSynth.RenderNotes), for the instruments
    // that are not samples; without it they are left out
    public static Result Write(TrackerSong song,
        Func<IReadOnlyList<ChiptuneSynth.NoteJob>, int, List<float[]>>? render = null)
    {
        var notes = new List<string>();
        int channels = Math.Min(32, song.Channels + song.Channels % 2);

        // The places as heard, each channel's sound settings (Gxx, Hxx, Ixx) made instruments of their own
        var places = Enumerable.Range(0, Math.Min(song.Order.Count, 256)).Select(pos => Copy(song.Composed(pos), song.Channels)).ToList();
        if (song.Order.Count > 256) notes.Add("The order list is longer than XM's 256 places; the rest is left out.");
        var lost = new SortedDictionary<string, int>();
        var (variants, asPlayed) = Variants(song, places, lost);

        // The same places once
        var patterns = new List<TrackerPattern>();
        var order = new List<int>();
        var keys = new Dictionary<string, int>();
        foreach (var p in places)
        {
            var key = new StringBuilder().Append(p.Rows).Append(':');
            for (int r = 0; r < p.Rows; r++)
                for (int c = 0; c < song.Channels; c++) key.Append(p[r, c]).Append('|');
            if (!keys.TryGetValue(key.ToString(), out int index))
            {
                if (patterns.Count == 256) { notes.Add("The song has more than 256 different places; the rest are left out."); break; }
                keys[key.ToString()] = index = patterns.Count;
                patterns.Add(p);
            }
            order.Add(index);
        }
        if (variants.Count > MaxInstruments)
            notes.Add($"XM has at most {MaxInstruments} instruments: {variants.Count - MaxInstruments} above that are left out.");

        // How the song plays each instrument: the notes, and the longest it holds one (in ticks)
        var uses = Uses(song, patterns, order);
        double ticksPerSecond = song.Tempo * 0.4;
        int maxTicks = (int)(MaxHoldSeconds * ticksPerSecond);

        // The instruments: samples as they are, the rest played by the synth
        int count = Math.Min(variants.Count, MaxInstruments);
        var xm = new XmInstrument[count];
        var jobs = new List<ChiptuneSynth.NoteJob>();
        var jobOf = new List<(int instrument, int note, bool loop)>();
        int rendered = 0;
        for (int i = 0; i < count; i++)
        {
            var inst = variants[i];
            var x = xm[i] = new XmInstrument { Name = inst.Name };
            var sample = IsPlainSample(inst) ? SampleBank.Get(inst.Sample) : null;
            if (sample != null)
            {
                x.Samples.Add(FromSample(inst, sample));
                SetEnvelopes(x, inst, ticksPerSecond, baked: false);
                continue;
            }
            if (!uses.TryGetValue(i + 1, out var use) || render == null) continue;
            rendered++;
            // The notes it plays, at most 16 of them: the rest take the nearest
            var played = use.Notes.OrderBy(n => n).ToList();
            var chosen = played.Count <= MaxSamples ? played
                : Enumerable.Range(0, MaxSamples).Select(k => played[k * (played.Count - 1) / (MaxSamples - 1)]).Distinct().ToList();
            var copy = TrackerSong.FromJson(new TrackerSong { Instruments = { inst } }.ToJson()).Instruments[0];
            copy.Pan = 0;
            copy.DefaultVolume = 64;
            int Nearest(int midi) => chosen.Select((n, j) => (d: Math.Abs(n - midi), j)).Min().j;
            for (int j = 0; j < chosen.Count; j++)
            {
                // As long as the longest of the notes it plays for
                int longest = played.Where(p => Nearest(p) == j).Max(p => use.Holds[p] > 0 ? use.Holds[p] : use.Hold);
                jobs.Add(new ChiptuneSynth.NoteJob(copy, chosen[j], Math.Clamp(longest, 1, maxTicks), asPlayed.GetValueOrDefault(i + 1)));
                jobOf.Add((i, chosen[j], longest > maxTicks));
            }
            SetEnvelopes(x, inst, ticksPerSecond, baked: true);
            for (int k = 0; k < 96; k++)
            {
                int midi = k + 12;
                x.KeyMap[k] = (byte)Nearest(midi);
            }
        }

        if (jobs.Count > 0)
        {
            // How loud the synth plays a plain square at full volume, so that its notes and the
            // samples keep their sizes
            var reference = new TrackerInstrument { Wave = "Square", Volume = 0.5, Attack = 0, Decay = 0, Sustain = 100, Release = 0 };
            var all = render!(jobs.Prepend(new ChiptuneSynth.NoteJob(reference, 60, (int)ticksPerSecond)).ToList(), song.Tempo);
            float gain = all[0].Skip(all[0].Length / 2).Select(Math.Abs).DefaultIfEmpty(0.5f).Max() / 0.5f;
            if (gain <= 0) gain = 1;
            for (int j = 0; j < jobOf.Count; j++)
            {
                var (i, n, loop) = jobOf[j];
                var inst = variants[i];
                var data = all[j + 1].Select(v => v / gain).ToArray();
                var s = new XmSample { Raw = data, Name = $"{TrackerCell.NoteName(n)}", Panning = Panning(inst.Pan) };
                Tune(s, ChiptuneSynth.SampleRate, n);
                Shorten(s, n, inst, ticksPerSecond, loop);
                xm[i].Samples.Add(s);
            }
            notes.Add($"{rendered} instrument{(rendered == 1 ? " is" : "s are")} the synth's, played into {jobs.Count} sample{(jobs.Count == 1 ? "" : "s")}.");
        }
        else if (render == null && variants.Take(count).Any(i => !IsPlainSample(i)))
            notes.Add("The synth's instruments were left out.");

        // One size for all samples: the loudest at full scale
        float peak = xm.SelectMany(x => x.Samples).SelectMany(s => s.Raw).Select(Math.Abs).DefaultIfEmpty(0).Max();
        float scale = peak > 0 ? 0.98f / peak : 1;
        foreach (var s in xm.SelectMany(x => x.Samples))
            s.Data = s.Raw.Select(v => (short)Math.Round(Math.Clamp(v * scale, -1f, 1f) * 32767)).ToArray();

        // The cells
        var bytes = new MemoryStream();
        var w = new BinaryWriter(bytes);
        WriteHeader(w, song, channels, order, patterns.Count, count);
        foreach (var p in patterns) WritePattern(w, p, song.Channels, channels, lost);
        foreach (var x in xm) WriteInstrument(w, x);
        if (lost.Count > 0)
            notes.Add("Left out, as XM has no such effect: " + string.Join(", ", lost.Select(kv => $"{kv.Key} ({kv.Value})")) + ".");
        return new Result(bytes.ToArray(), notes);
    }

    private static TrackerPattern Copy(TrackerPattern p, int channels)
    {
        var q = new TrackerPattern(p.Rows);
        for (int r = 0; r < p.Rows; r++)
            for (int c = 0; c < channels; c++) q[r, c] = p[r, c];
        return q;
    }

    // The instruments as XM gets them: the song's own (in their numbers), and after them one for each
    // instrument played with a filter frequency (Gxx), resonance (Hxx) or pulse width (Ixx) a channel
    // has set, as the synth keeps them for its notes until it gets another instrument. The cells get
    // these numbers, and the G, H and I effects go. A note that they change while it plays is played
    // into an instrument of its own as it sounds, the changes on their ticks ("as played"; the same
    // note with the same changes once); with XM's 128 instruments taken, they are left out.
    private static (List<TrackerInstrument>, Dictionary<int, List<(int, char, int)>>) Variants(TrackerSong song,
        List<TrackerPattern> places, SortedDictionary<string, int> lost)
    {
        var list = song.Instruments.Select(i => i).ToList();
        var played = new Dictionary<int, List<(int, char, int)>>();
        var playedIndex = new Dictionary<string, int>();
        var index = new Dictionary<(int, int, int, int), int>();
        int n = song.Channels;
        var last = new int[n];
        var written = new int[n];
        var set = Enumerable.Repeat((g: -1, h: -1, i: -1), n).ToArray();
        int Variant(int inst, (int g, int h, int i) o)
        {
            if (o == (-1, -1, -1) || inst > song.Instruments.Count) return inst;
            if (index.TryGetValue((inst, o.g, o.h, o.i), out int v)) return v;
            var copy = TrackerSong.FromJson(new TrackerSong { Instruments = { song.Instruments[inst - 1] } }.ToJson()).Instruments[0];
            var name = new StringBuilder(copy.Name);
            if (o.g >= 0)
            {
                copy.Cutoff = Math.Min(18000, 20 * Math.Pow(1000, o.g / 255.0));
                if (copy.Filter == "Off") copy.Filter = "Low";
                name.Append($" G{o.g:X2}");
            }
            if (o.h >= 0) { copy.Resonance = o.h / 255.0 * 98; name.Append($" H{o.h:X2}"); }
            if (o.i >= 0) { copy.PulseWidth = Math.Clamp(o.i / 255.0 * 100, 1, 99); name.Append($" I{o.i:X2}"); }
            copy.Name = name.ToString();
            list.Add(copy);
            return index[(inst, o.g, o.h, o.i)] = list.Count;
        }
        // The note each channel plays: where it was struck, its instrument there, and the changes since
        var events = new (TrackerPattern p, int row, int inst, int note, long start, List<(int, char, int)> changes)?[n];
        int speed = song.Speed;
        long tick = 0;
        void Finish(int c)
        {
            if (events[c] is not { } ev) return;
            events[c] = null;
            if (ev.changes.Count == 0) return;
            string key = $"{ev.inst}:{ev.note}:" + string.Join(",", ev.changes);
            if (!playedIndex.TryGetValue(key, out int v))
            {
                if (list.Count >= MaxInstruments)
                {
                    foreach (var (_, e, _) in ev.changes) lost[$"{e}xx under a note that plays on"] = lost.GetValueOrDefault($"{e}xx under a note that plays on") + 1;
                    return;
                }
                var copy = TrackerSong.FromJson(new TrackerSong { Instruments = { list[ev.inst - 1] } }.ToJson()).Instruments[0];
                copy.Name += " (as played)";
                list.Add(copy);
                playedIndex[key] = v = list.Count;
                played[v] = ev.changes;
            }
            var cell = ev.p[ev.row, c];
            ev.p[ev.row, c] = new TrackerCell(cell.Note, v, cell.Effect, cell.Param, cell.Volume);
            if (written[c] == ev.inst) written[c] = v;
        }
        foreach (var p in places)
            for (int r = 0; r < p.Rows; r++, tick += speed)
                for (int c = 0; c < n; c++)
                {
                    if (c == 0)
                        for (int k = 0; k < n; k++)
                            if (p[r, k].Effect == 'F' && p[r, k].Param is > 0 and < 0x20) speed = p[r, k].Param;
                    var cell = p[r, c];
                    char e = cell.Effect;
                    int param = cell.Param;
                    if (cell.Instrument > 0)
                    {
                        if (cell.Instrument != last[c]) set[c] = (-1, -1, -1);
                        last[c] = cell.Instrument;
                    }
                    bool glide = e is '3' or '5';
                    bool struck = cell.Note >= 0 && !glide && last[c] > 0;
                    if (struck || cell.Note == TrackerCell.NoteOff || cell.Volume == 0 || (e == 'C' && param == 0)) Finish(c);
                    if (e is 'G' or 'H' or 'I')
                    {
                        set[c] = e == 'G' ? (param, set[c].h, set[c].i) : e == 'H' ? (set[c].g, param, set[c].i) : (set[c].g, set[c].h, param);
                        if (!struck && events[c] is { } ev) ev.changes.Add(((int)(tick - ev.start), e, param));
                        (e, param) = ('\0', 0);
                    }
                    int inst = cell.Instrument;
                    int sounds = 0;
                    if (last[c] > 0 && ((cell.Note >= 0 && !glide) || cell.Instrument > 0))
                    {
                        int v = sounds = Variant(last[c], set[c]);
                        if (cell.Instrument > 0 || (cell.Note >= 0 && v != written[c])) { inst = v; written[c] = v; }
                    }
                    p[r, c] = new TrackerCell(cell.Note, inst, e, param, cell.Volume);
                    if (struck) events[c] = (p, r, sounds, cell.Note, tick, new List<(int, char, int)>());
                }
        for (int c = 0; c < n; c++) Finish(c);
        return (list, played);
    }

    // ── The song's use of its instruments ─────────────────────────────

    private sealed class Use
    {
        public Dictionary<int, int> Holds = new();   // each note it plays, and the longest it holds it (0: glided to)
        public int Hold;                             // the longest of all
        public IEnumerable<int> Notes => Holds.Keys;
    }

    // Each note sounds until the channel's next note, a note off or silence; counted in ticks with the
    // speed as the song sets it
    private static Dictionary<int, Use> Uses(TrackerSong song, List<TrackerPattern> patterns, List<int> order)
    {
        var uses = new Dictionary<int, Use>();
        int speed = song.Speed;
        var current = new int[song.Channels];      // the channel's instrument
        var sounding = new int[song.Channels];     // the one its note plays
        var start = new long[song.Channels];
        var startNote = new int[song.Channels];
        var playing = new bool[song.Channels];
        long tick = 0;
        void End(int c)
        {
            if (!playing[c]) return;
            playing[c] = false;
            var u = uses[sounding[c]];
            int held = (int)Math.Min(int.MaxValue, tick - start[c]);
            u.Hold = Math.Max(u.Hold, held);
            u.Holds[startNote[c]] = Math.Max(u.Holds.GetValueOrDefault(startNote[c]), held);
        }
        foreach (int index in order)
        {
            var p = patterns[index];
            for (int r = 0; r < p.Rows; r++)
            {
                for (int c = 0; c < song.Channels; c++)
                    if (p[r, c].Effect == 'F' && p[r, c].Param is > 0 and < 0x20) speed = p[r, c].Param;
                for (int c = 0; c < song.Channels; c++)
                {
                    var cell = p[r, c];
                    if (cell.Instrument > 0) current[c] = cell.Instrument;
                    if (cell.Note == TrackerCell.NoteOff || cell.Volume == 0 || (cell.Effect == 'C' && cell.Param == 0)) End(c);
                    if (cell.Note >= 0 && current[c] > 0 && !(cell.Effect is '3' or '5' && playing[c]))
                    {
                        End(c);
                        if (!uses.TryGetValue(current[c], out var u)) uses[current[c]] = u = new Use();
                        u.Holds.TryAdd(cell.Note, 0);
                        startNote[c] = cell.Note;
                        playing[c] = true;
                        sounding[c] = current[c];
                        start[c] = tick;
                    }
                    else if (cell.Note >= 0 && playing[c]) uses[sounding[c]].Holds.TryAdd(cell.Note, 0);   // a glide
                }
                tick += speed;
            }
        }
        for (int c = 0; c < song.Channels; c++) End(c);
        return uses;
    }

    // ── Instruments ──────────────────────────────────────────────────

    // A sampled instrument that XM plays as it is: nothing of the synth's in it
    private static bool IsPlainSample(TrackerInstrument i) =>
        i.Wave == "Sample" && i.Sample.Length > 0 && i.Filter == "Off" && i.Osc2Wave == "Off" && i.EchoMix == 0
        && i.Ring == 0 && i.Sync == 0 && i.Lfo1.Target == "Off" && i.Lfo2.Target == "Off" && i.Vibrato == 0
        && i.Table.All(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(w => !(char.IsDigit(w[0]) || w[0] is '+' or '-' or 'O' or 'o') || w.TrimStart('+', '-').All(ch => ch is '0' or '.')));

    private static XmSample FromSample(TrackerInstrument inst, SampleData data)
    {
        var s = new XmSample
        {
            Raw = data.Data.Select(v => (float)(v * inst.Volume)).ToArray(),
            Name = Path.GetFileNameWithoutExtension(inst.Sample),
            Volume = Math.Clamp(inst.DefaultVolume, 0, 64),
            Panning = Panning(inst.Pan)
        };
        if (inst.LoopEnd > inst.LoopStart && inst.LoopStart < data.Data.Length)
        {
            s.LoopStart = inst.LoopStart;
            s.LoopLength = Math.Min(inst.LoopEnd, data.Data.Length) - inst.LoopStart;
        }
        Tune(s, data.Rate, inst.SampleNote);
        return s;
    }

    private static int Panning(double pan) => Math.Clamp((int)Math.Round(128 + pan * 1.27), 0, 255);

    // The relative note and finetune that play the sample at its own rate on `note`; XM plays a sample
    // with neither at 8363 Hz on C-4
    private static void Tune(XmSample s, double rate, int note)
    {
        double semis = 12 * Math.Log2(rate / C4Rate) + 60 - note;
        int rel = (int)Math.Floor(semis);
        int fine = (int)Math.Round((semis - rel) * 128);
        if (fine >= 128) { rel++; fine -= 128; }
        if (fine > 63) { rel++; fine -= 128; }
        s.RelativeNote = Math.Clamp(rel, -96, 95);
        s.Finetune = Math.Clamp(fine, -128, 127);
    }

    // A played note made as short as it can be: the silence at its end cut off, and where it has
    // become the same over and over (the envelope held, the table going round), cut after the first
    // round of that and looped; a note held longer than the sample goes round its end in any case.
    // A round: the table's loop in ticks, or about a tenth of a second of the note's periods.
    private static void Shorten(XmSample s, int note, TrackerInstrument inst, double ticksPerSecond, bool heldLonger)
    {
        var d = s.Raw;
        int end = d.Length;
        while (end > 0 && Math.Abs(d[end - 1]) < 1e-4f) end--;
        if (end < d.Length)
        {
            s.Raw = d[..Math.Max(1, end)];
            return;
        }
        double perTick = ChiptuneSynth.SampleRate / ticksPerSecond;
        double period = ChiptuneSynth.SampleRate / (440 * Math.Pow(2, (note - 69) / 12.0));
        int round = inst.Table.Count > 1 && inst.TableLoop >= 0 && inst.TableLoop < inst.Table.Count
            ? (int)Math.Round((inst.Table.Count - inst.TableLoop) * perTick)
            : (int)Math.Round(Math.Max(1, Math.Round(0.1 * ChiptuneSynth.SampleRate / period)) * period);
        if (round < 2 || round * 2 > d.Length) return;
        double Same(int a, int b)
        {
            double ab = 0, aa = 0, bb = 0;
            for (int k = 0; k < round; k++) { ab += d[a + k] * d[b + k]; aa += d[a + k] * d[a + k]; bb += d[b + k] * d[b + k]; }
            return aa + bb < 1e-9 ? 1 : 2 * ab / (aa + bb);   // 1 only when alike in shape and size
        }
        int start = d.Length - round;
        while (start - round >= 0 && Same(start - round, start) > 0.98) start -= round;
        if (start < d.Length - round)
        {
            // From `start` it repeats: keep one round after it, looped
            s.Raw = d[..(start + round)];
            s.LoopStart = start;
            s.LoopLength = round;
        }
        else if (heldLonger)
        {
            s.LoopStart = d.Length - round;
            s.LoopLength = round;
        }
    }

    // The envelopes. baked: the synth's sound already has its attack, decay and envelope up to the
    // sustain, so XM's envelope holds it and only lets go: over the release (an ADSR), what the
    // envelope does after its sustain point, or the fadeout.
    private static void SetEnvelopes(XmInstrument x, TrackerInstrument inst, double ticksPerSecond, bool baked)
    {
        int Ticks(double ms) => Math.Max(1, (int)Math.Round(ms / 1000 * ticksPerSecond));
        x.Fadeout = inst.FadeOut > 0 ? Math.Clamp((int)Math.Round(65536 / (inst.FadeOut / 1000 * ticksPerSecond)), 1, 0xFFF) : 0;
        var env = inst.Envelope.Select(p => (p.Tick, p.Level)).ToList();
        if (env.Count >= 2)
        {
            if (!baked)
            {
                x.Volume = env;
                x.VolumeSustain = inst.EnvelopeSustain;
                x.VolumeLoopStart = inst.EnvelopeLoopStart;
                x.VolumeLoopEnd = inst.EnvelopeLoopEnd;
            }
            else if (inst.EnvelopeSustain >= 0 && inst.EnvelopeSustain < env.Count)
            {
                // Held at the sustain, then on from there as it goes
                var after = env.Skip(inst.EnvelopeSustain).ToList();
                int level0 = Math.Max(1, after[0].Level);
                x.Volume = after.Select(p => (p.Tick - after[0].Tick, Math.Clamp(p.Level * 64 / level0, 0, 64))).ToList();
                if (x.Volume.Count < 2) x.Volume.Add((1, x.Volume[0].Level));
                x.VolumeSustain = 0;
            }
            else
                x.Volume = new() { (0, 64), (1, 64) };   // it goes on as baked; a note off only fades it
        }
        else
        {
            // The ADSR: in the sample (baked) or as points; its release after the note off
            int sustain = (int)Math.Round(Math.Clamp(inst.Sustain, 0, 100) * 0.64);
            if (baked)
            {
                x.Volume = new() { (0, 64), (Ticks(inst.Release), 0) };
                x.VolumeSustain = 0;
            }
            else
            {
                int a = inst.Attack > 0 ? Ticks(inst.Attack) : 0, d = a + Ticks(inst.Decay);
                x.Volume = a > 0 ? new() { (0, 0), (a, 64), (d, sustain), (d + Ticks(inst.Release), 0) }
                                 : new() { (0, 64), (d, sustain), (d + Ticks(inst.Release), 0) };
                x.VolumeSustain = x.Volume.Count - 2;
            }
        }
        if (inst.PanEnvelope.Count >= 2 && !baked)
        {
            x.Pan = inst.PanEnvelope.Select(p => (p.Tick, p.Level)).ToList();
            x.PanSustain = inst.PanEnvelopeSustain;
            x.PanLoopStart = inst.PanEnvelopeLoopStart;
            x.PanLoopEnd = inst.PanEnvelopeLoopEnd;
        }
        Fit(x.Volume, ref x.VolumeSustain, ref x.VolumeLoopStart, ref x.VolumeLoopEnd);
        Fit(x.Pan, ref x.PanSustain, ref x.PanLoopStart, ref x.PanLoopEnd);
    }

    // At most 12 points, rising ticks below 65536: the points that matter least (nearest the line
    // between their neighbours) go first; the sustain and loop points stay
    private static void Fit(List<(int Tick, int Level)> env, ref int sustain, ref int loopStart, ref int loopEnd)
    {
        for (int i = 1; i < env.Count; i++)
            if (env[i].Tick <= env[i - 1].Tick) env[i] = (env[i - 1].Tick + 1, env[i].Level);
        for (int i = 0; i < env.Count; i++) env[i] = (Math.Min(env[i].Tick, 65535), Math.Clamp(env[i].Level, 0, 64));
        while (env.Count > MaxEnvelopePoints)
        {
            int best = -1;
            double least = double.MaxValue;
            for (int i = 1; i < env.Count - 1; i++)
            {
                if (i == sustain || i == loopStart || i == loopEnd) continue;
                var (a, b, c) = (env[i - 1], env[i], env[i + 1]);
                double line = a.Level + (c.Level - a.Level) * (b.Tick - a.Tick) / (double)(c.Tick - a.Tick);
                double err = Math.Abs(line - b.Level);
                if (err < least) { least = err; best = i; }
            }
            if (best < 0) { env.RemoveRange(MaxEnvelopePoints, env.Count - MaxEnvelopePoints); break; }
            env.RemoveAt(best);
            if (sustain > best) sustain--;
            if (loopStart > best) loopStart--;
            if (loopEnd > best) loopEnd--;
        }
        if (sustain >= env.Count) sustain = -1;
        if (loopStart >= env.Count || loopEnd >= env.Count || loopEnd < loopStart) loopStart = loopEnd = -1;
    }

    // ── Writing ──────────────────────────────────────────────────────

    private static void Text(BinaryWriter w, string s, int length)
    {
        var b = new byte[length];
        Encoding.Latin1.GetBytes(s.Length > length ? s[..length] : s).CopyTo(b, 0);
        w.Write(b);
    }

    private static void WriteHeader(BinaryWriter w, TrackerSong song, int channels, List<int> order, int patterns, int instruments)
    {
        Text(w, "Extended Module: ", 17);
        Text(w, song.Title, 20);
        w.Write((byte)0x1A);
        Text(w, "AMOS Like Basic", 20);
        w.Write((ushort)0x0104);
        w.Write(276);                                   // the header from here
        w.Write((ushort)order.Count);
        w.Write((ushort)0);                             // restart position
        w.Write((ushort)channels);
        w.Write((ushort)patterns);
        w.Write((ushort)instruments);
        w.Write((ushort)1);                             // linear frequencies
        w.Write((ushort)Math.Clamp(song.Speed, 1, 31));
        w.Write((ushort)Math.Clamp(song.Tempo, 32, 255));
        var table = new byte[256];
        for (int i = 0; i < order.Count; i++) table[i] = (byte)order[i];
        w.Write(table);
    }

    private static void WritePattern(BinaryWriter w, TrackerPattern p, int songChannels, int channels, SortedDictionary<string, int> lost)
    {
        var data = new MemoryStream();
        for (int r = 0; r < p.Rows; r++)
            for (int c = 0; c < channels; c++)
            {
                var cell = c < songChannels ? p[r, c] : TrackerCell.Empty;
                int note = cell.Note == TrackerCell.NoteOff ? 97 : cell.Note >= 0 ? XmNote(cell.Note) : 0;
                int inst = cell.Instrument <= MaxInstruments ? cell.Instrument : 0;
                int vol = cell.Volume >= 0 ? 0x10 + Math.Min(cell.Volume, 64) : 0;
                var (eff, prm) = Effect(cell.Effect, cell.Param, lost);
                int mask = (note > 0 ? 1 : 0) | (inst > 0 ? 2 : 0) | (vol > 0 ? 4 : 0) | (eff > 0 ? 8 : 0) | (prm > 0 ? 16 : 0);
                data.WriteByte((byte)(0x80 | mask));
                if (note > 0) data.WriteByte((byte)note);
                if (inst > 0) data.WriteByte((byte)inst);
                if (vol > 0) data.WriteByte((byte)vol);
                if (eff > 0) data.WriteByte((byte)eff);
                if (prm > 0) data.WriteByte((byte)prm);
            }
        w.Write(9);                                     // the pattern's header
        w.Write((byte)0);
        w.Write((ushort)p.Rows);
        w.Write((ushort)data.Length);
        w.Write(data.ToArray());
    }

    // XM's notes: 1 is C-0, 96 B-7 (the tracker's MIDI numbers from 12); others moved by octaves
    private static int XmNote(int midi)
    {
        while (midi < 12) midi += 12;
        while (midi > 107) midi -= 12;
        return midi - 11;
    }

    // The tracker's effect as XM's number and parameter (0 0: none)
    private static (int, int) Effect(char e, int p, SortedDictionary<string, int> lost)
    {
        int x = p >> 4, y = p & 15;
        void Lost(string what) => lost[what] = lost.GetValueOrDefault(what) + 1;
        switch (e)
        {
            case '\0': return (0, 0);
            case '0': return (0, p);
            case 'J': return (0, p);                    // the nearest XM has: an arpeggio
            // XM's 1, 2 and A with 00 take their last value again, the tracker's do nothing
            case '1' or '2' or 'A': return p == 0 ? (0, 0) : (e == 'A' ? 0xA : e - '0', p);
            case '5': return p == 0 ? (3, 0) : (5, p);
            case '6': return p == 0 ? (4, 0) : (6, p);
            case >= '3' and <= '9': return (e - '0', p);
            case 'B': return (0xB, p);
            case 'C': return (0xC, Math.Min(p, 64));
            case 'D': return (0xD, p);
            case 'F': return (0xF, p);
            case 'E':
                if (x is 0x0 or 0xF) { Lost(x == 0 ? "E0x (the Amiga's filter)" : "EFx (invert loop)"); return (0, 0); }
                if (x == 0x5)
                {
                    int eighths = y < 8 ? y : y - 16;
                    return (0xE, 0x50 | Math.Clamp(eighths + 8, 0, 15));
                }
                return (0xE, p);
            case 'V': return (0x10, Math.Min(p, 64));   // Gxx: the song's volume
            case 'W': return (0x11, p);                 // Hxy: its slide
            case 'K': return (0x14, p);
            case 'L': return (0x15, p);
            case 'P': return (0x19, p);
            case 'R': return (0x1B, p);
            case 'T': return (0x1D, p);
            case 'G': Lost("Gxx (filter frequency)"); return (0, 0);
            case 'H': Lost("Hxx (filter resonance)"); return (0, 0);
            case 'I': Lost("Ixx (pulse width)"); return (0, 0);
            default: Lost(e + "xx"); return (0, 0);
        }
    }

    private static void WriteInstrument(BinaryWriter w, XmInstrument x)
    {
        if (x.Samples.Count == 0)
        {
            w.Write(29);
            Text(w, x.Name, 22);
            w.Write((byte)0);
            w.Write((ushort)0);
            return;
        }
        w.Write(263);
        Text(w, x.Name, 22);
        w.Write((byte)0);
        w.Write((ushort)x.Samples.Count);
        w.Write(40);                                    // each sample's header
        w.Write(x.KeyMap);
        void Points(List<(int Tick, int Level)> env)
        {
            for (int i = 0; i < MaxEnvelopePoints; i++)
            {
                w.Write((ushort)(i < env.Count ? env[i].Tick : 0));
                w.Write((ushort)(i < env.Count ? env[i].Level : 0));
            }
        }
        Points(x.Volume);
        Points(x.Pan);
        w.Write((byte)x.Volume.Count);
        w.Write((byte)x.Pan.Count);
        w.Write((byte)Math.Max(0, x.VolumeSustain));
        w.Write((byte)Math.Max(0, x.VolumeLoopStart));
        w.Write((byte)Math.Max(0, x.VolumeLoopEnd));
        w.Write((byte)Math.Max(0, x.PanSustain));
        w.Write((byte)Math.Max(0, x.PanLoopStart));
        w.Write((byte)Math.Max(0, x.PanLoopEnd));
        byte Type(List<(int, int)> env, int sustain, int loop) =>
            (byte)(env.Count >= 2 ? 1 | (sustain >= 0 ? 2 : 0) | (loop >= 0 ? 4 : 0) : 0);
        w.Write(Type(x.Volume, x.VolumeSustain, x.VolumeLoopStart));
        w.Write(Type(x.Pan, x.PanSustain, x.PanLoopStart));
        w.Write(new byte[4]);                           // no auto-vibrato
        w.Write((ushort)x.Fadeout);
        w.Write(new byte[22]);                          // reserved, to the 263 bytes

        foreach (var s in x.Samples)
        {
            w.Write(s.Data.Length * 2);
            w.Write(s.LoopStart * 2);
            w.Write(s.LoopLength * 2);
            w.Write((byte)s.Volume);
            w.Write((sbyte)s.Finetune);
            w.Write((byte)(0x10 | (s.LoopLength > 0 ? 1 : 0)));   // 16-bit, a forward loop
            w.Write((byte)s.Panning);
            w.Write((sbyte)s.RelativeNote);
            w.Write((byte)0);
            Text(w, s.Name, 22);
        }
        // The samples, each value as the change from the one before
        foreach (var s in x.Samples)
        {
            short last = 0;
            foreach (var v in s.Data)
            {
                w.Write((short)(v - last));
                last = v;
            }
        }
    }
}
