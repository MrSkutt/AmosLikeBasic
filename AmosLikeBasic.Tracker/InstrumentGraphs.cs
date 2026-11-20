using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AmosLikeBasic;

// Pictures of a tracker instrument's settings on the instrument page: the waveform, the volume
// envelope and the filter curve. They are drawn from the settings (not from the sound), so they
// show at once what a change does.
public abstract class InstrumentGraph : Control
{
    // The instrument to draw; null draws an empty frame
    public Func<TrackerInstrument?> Instrument { get; set; } = () => null;

    protected InstrumentGraph()
    {
        Height = 120;
        ClipToBounds = true;
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    protected IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

    protected IBrush Screen => Res("AmosInputBg", Brushes.Black);
    protected IBrush Line => Res("AmosTitleFg", Brushes.Yellow);
    protected IBrush Label => Res("AmosLabelFg", Brushes.LightBlue);
    protected IBrush Border => Res("AmosPanelBorder", Brushes.LightBlue);
    protected static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(45, 128, 160, 255));
    protected static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(110, 255, 220, 120));

    protected FormattedText Text(string s, IBrush brush, double size = 11) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Menlo, Consolas, Courier New, monospace"), size, brush);

    protected Rect Area => new Rect(Bounds.Size).Deflate(new Thickness(6, 6, 6, 18));

    public sealed override void Render(DrawingContext ctx)
    {
        var all = new Rect(Bounds.Size);
        ctx.FillRectangle(Screen, all);
        ctx.DrawRectangle(new Pen(Border, 1), all.Deflate(0.5));
        var inst = Instrument();
        if (inst != null) Draw(ctx, inst, Area);
    }

    protected abstract void Draw(DrawingContext ctx, TrackerInstrument inst, Rect area);

    protected static StreamGeometry Polyline(IReadOnlyList<Point> points)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(points[0], false);
        for (int i = 1; i < points.Count; i++) g.LineTo(points[i]);
        g.EndFigure(false);
        return geo;
    }
}

// Two periods of the waveform. For a pulse wave with PWM, faint outlines show how far the
// width swings. Mixed: the sound of both oscillators together, with oscillator 2 faint behind.
public sealed class WaveGraph : InstrumentGraph
{
    public bool Mixed { get; init; }

    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        if (Mixed) { DrawMixed(ctx, inst, a); return; }
        double mid = a.Center.Y, half = a.Height / 2 - 2;
        ctx.DrawLine(new Pen(Grid, 1), new Point(a.Left, mid), new Point(a.Right, mid));

        string wave = (inst.Wave ?? "Pulse").ToUpperInvariant();
        double width = wave == "SQUARE" ? 0.5 : Math.Clamp(inst.PulseWidth / 100.0, 0.01, 0.99);
        if (wave == "PULSE" && inst.PwmDepth > 0 && inst.PwmRate > 0)
        {
            double d = Math.Clamp(inst.PwmDepth / 100.0, 0, 0.49);
            foreach (var w in new[] { Math.Clamp(width - d, 0.01, 0.99), Math.Clamp(width + d, 0.01, 0.99) })
                ctx.DrawGeometry(null, new Pen(Faint, 1, new DashStyle(new double[] { 3, 3 }, 0)), Polyline(Points(a, mid, half, "PULSE", w)));
        }
        ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(Points(a, mid, half, wave, width)));

        string caption = wave switch
        {
            "PULSE" => $"PULSE  width {inst.PulseWidth:0} %" + (inst.PwmDepth > 0 && inst.PwmRate > 0 ? $"  ±{inst.PwmDepth:0} % at {inst.PwmRate:0.#} Hz" : ""),
            "SAW" => "SAWTOOTH",
            _ => wave
        };
        ctx.DrawText(Text(caption, Label), new Point(a.Left, a.Bottom + 3));
    }

    private void DrawMixed(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        double mid = a.Center.Y, half = a.Height / 2 - 2;
        ctx.DrawLine(new Pen(Grid, 1), new Point(a.Left, mid), new Point(a.Right, mid));
        string wave2 = (inst.Osc2Wave ?? "Off").ToUpperInvariant();
        string wave1 = (inst.Wave ?? "Pulse").ToUpperInvariant();
        double width = Math.Clamp(inst.PulseWidth / 100.0, 0.01, 0.99);
        if (wave1 == "SAMPLE")
        {
            // Oscillator 1 is the sample, drawn on its own card; oscillator 2 alone here
            if (wave2 != "OFF") ctx.DrawGeometry(null, new Pen(Faint, 1.5), Polyline(Points(a, mid, half, wave2, width,
                Math.Pow(2, (inst.Osc2Semitones + inst.Osc2Detune / 100.0) / 12.0))));
            ctx.DrawText(Text("OSC 1: SAMPLE (see the SAMPLE card)" + (wave2 != "OFF" ? $"  + {wave2} {inst.Osc2Mix:0} %" : ""), Label),
                new Point(a.Left, a.Bottom + 3));
            return;
        }
        if (wave2 == "OFF")
        {
            // Only oscillator 1 sounds: its wave alone
            ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(Points(a, mid, half, wave1, width)));
            string name = wave1 == "SAW" ? "SAWTOOTH" : wave1;
            ctx.DrawText(Text(wave1 == "PULSE" ? $"PULSE  width {inst.PulseWidth:0} %" : name, Label), new Point(a.Left, a.Bottom + 3));
            return;
        }
        double ratio = Math.Pow(2, (inst.Osc2Semitones + inst.Osc2Detune / 100.0) / 12.0);
        double mix = Math.Clamp(inst.Osc2Mix / 100.0, 0, 1);
        ctx.DrawGeometry(null, new Pen(Faint, 1, new DashStyle(new double[] { 3, 3 }, 0)),
            Polyline(Points(a, mid, half, wave2, width, ratio)));
        var one = Points(a, mid, half, wave1, width);
        var two = Points(a, mid, half, wave2, width, ratio);
        // The sum, point by point (both lists have the same x places apart from the extra edge points)
        var sum = new List<Point>();
        const int n = 400;
        for (int i = 0; i <= n; i++)
        {
            double x = a.Left + i * a.Width / n;
            double y1 = YAt(one, x), y2 = YAt(two, x);
            sum.Add(new Point(x, mid + ((y1 - mid) * (1 - mix) + (y2 - mid) * mix)));
        }
        ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(sum));
        string tune = $"{inst.Osc2Semitones:+0;-0;0} st {inst.Osc2Detune:+0;-0;0} c";
        ctx.DrawText(Text($"{wave2}  {tune}  mix {inst.Osc2Mix:0} %", Label), new Point(a.Left, a.Bottom + 3));
    }

    private static double YAt(List<Point> pts, double x)
    {
        for (int i = pts.Count - 1; i >= 0; i--)
            if (pts[i].X <= x) return pts[i].Y;
        return pts[0].Y;
    }

    // ratio: the pitch compared with oscillator 1 (oscillator 2 runs that many periods as fast)
    private static List<Point> Points(Rect a, double mid, double half, string wave, double width, double ratio = 1)
    {
        var pts = new List<Point>();
        const int n = 400;
        var rng = new Random(7);
        double noise = 0;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n * 2 * ratio % 1.0;   // two periods of oscillator 1
            double s = wave switch
            {
                "TRIANGLE" => t < 0.5 ? 4 * t - 1 : 3 - 4 * t,
                "SAWTOOTH" or "SAW" => 2 * t - 1,
                "SINE" => Math.Sin(2 * Math.PI * t),
                "NOISE" => i % 6 == 0 ? noise = rng.Next(2) * 2 - 1 : noise,
                _ => t < (wave == "SQUARE" ? 0.5 : width) ? 1 : -1   // PULSE, SQUARE
            };
            var p = new Point(a.Left + i * a.Width / n, mid - s * half);
            // Vertical edges for the square shapes, so that they look like a scope picture
            if (pts.Count > 0 && Math.Abs(pts[^1].Y - p.Y) > half) pts.Add(new Point(p.X, pts[^1].Y));
            pts.Add(p);
        }
        return pts;
    }
}

// Attack, decay, sustain and release, with the note held for a while in between. Every part
// gets some room even when it is 0 ms, so that all four can be told apart.
public sealed class EnvelopeGraph : InstrumentGraph
{
    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        double atk = Math.Max(0, inst.Attack), dec = Math.Max(0, inst.Decay), rel = Math.Max(0, inst.Release);
        double sus = Math.Clamp(inst.Sustain / 100.0, 0, 1);
        double hold = Math.Max(150, 0.3 * (atk + dec + rel));
        double total = atk + dec + hold + rel;
        double minPart = total * 0.06;
        double[] parts = { Math.Max(atk, minPart), Math.Max(dec, minPart), hold, Math.Max(rel, minPart) };
        double sum = parts[0] + parts[1] + parts[2] + parts[3];
        double X(double ms) => a.Left + ms / sum * a.Width;
        double Y(double level) => a.Bottom - level * a.Height;

        double x0 = X(0), x1 = X(parts[0]), x2 = X(parts[0] + parts[1]), x3 = X(parts[0] + parts[1] + parts[2]), x4 = X(sum);
        var gridPen = new Pen(Grid, 1, new DashStyle(new double[] { 2, 3 }, 0));
        foreach (var x in new[] { x1, x2, x3 }) ctx.DrawLine(gridPen, new Point(x, a.Top), new Point(x, a.Bottom));
        ctx.DrawLine(gridPen, new Point(a.Left, Y(sus)), new Point(a.Right, Y(sus)));

        var pts = new List<Point> { new(x0, Y(0)), new(x1, Y(1)) };
        // The decay falls in a straight line, as in the synth
        pts.Add(new Point(x2, Y(sus)));
        pts.Add(new Point(x3, Y(sus)));
        pts.Add(new Point(x4, Y(0)));
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(x0, Y(0)), true);
            foreach (var p in pts) g.LineTo(p);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 255, 220, 0)), null, fill);
        ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(pts));

        // A letter over each part, and all the values in one line underneath
        void Letter(string s, double left, double right)
        {
            var t = Text(s, Faint, 10);
            if (right - left > t.Width + 2) ctx.DrawText(t, new Point((left + right - t.Width) / 2, a.Top));
        }
        Letter("A", x0, x1);
        Letter("D", x1, x2);
        Letter("S", x2, x3);
        Letter("R", x3, x4);
        ctx.DrawText(Text($"A {atk:0} ms  D {dec:0} ms  S {inst.Sustain:0} %  R {rel:0} ms", Label), new Point(a.Left, a.Bottom + 3));
    }
}

// How loud each frequency comes through the filter, from 20 Hz to 20 kHz. With a filter
// envelope a faint curve shows where the envelope takes the cutoff at its top.
public sealed class FilterGraph : InstrumentGraph
{
    private const double MinHz = 20, MaxHz = 20000, TopDb = 18, BottomDb = -36;

    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        double X(double hz) => a.Left + Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz) * a.Width;
        double Y(double db) => a.Top + (TopDb - Math.Clamp(db, BottomDb, TopDb)) / (TopDb - BottomDb) * a.Height;

        var gridPen = new Pen(Grid, 1);
        foreach (var hz in new[] { 100.0, 1000, 10000 })
        {
            ctx.DrawLine(gridPen, new Point(X(hz), a.Top), new Point(X(hz), a.Bottom));
            ctx.DrawText(Text(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", Grid, 10), new Point(X(hz) + 2, a.Top));
        }
        ctx.DrawLine(gridPen, new Point(a.Left, Y(0)), new Point(a.Right, Y(0)));

        string type = (inst.Filter ?? "Off").ToUpperInvariant();
        if (type == "OFF")
        {
            ctx.DrawLine(new Pen(Line, 2), new Point(a.Left, Y(0)), new Point(a.Right, Y(0)));
            ctx.DrawText(Text("FILTER OFF: every frequency passes", Label), new Point(a.Left, a.Bottom + 3));
            return;
        }

        double cutoff = Math.Clamp(inst.Cutoff, 20, 18000);
        double k = 2.0 - 2.0 * Math.Clamp(inst.Resonance / 100.0, 0, 0.98);
        if (inst.FilterEnvDepth != 0)
        {
            double swept = Math.Clamp(cutoff + inst.FilterEnvDepth, 20, 18000);
            ctx.DrawGeometry(null, new Pen(Faint, 1.5, new DashStyle(new double[] { 4, 3 }, 0)), Curve(type, swept, k, X, Y, a));
            ctx.DrawLine(new Pen(Faint, 1), new Point(X(cutoff), Y(TopDb - 4)), new Point(X(swept), Y(TopDb - 4)));
        }
        ctx.DrawGeometry(null, new Pen(Line, 2), Curve(type, cutoff, k, X, Y, a));
        ctx.DrawLine(new Pen(Line, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(X(cutoff), a.Top), new Point(X(cutoff), a.Bottom));

        string name = type switch { "LOW" => "LOW PASS", "HIGH" => "HIGH PASS", "BAND" => "BAND PASS", _ => type };
        string env = inst.FilterEnvDepth != 0 ? $"  env {inst.FilterEnvDepth:+0;-0} Hz" : "";
        ctx.DrawText(Text($"{name}  {cutoff:0} Hz  res {inst.Resonance:0}%{env}", Label), new Point(a.Left, a.Bottom + 3));
    }

    // The state variable filter's response (the analog prototype the synth's filter is made from)
    private static StreamGeometry Curve(string type, double cutoff, double k, Func<double, double> x, Func<double, double> y, Rect a)
    {
        var pts = new List<Point>();
        for (int i = 0; i <= 200; i++)
        {
            double hz = MinHz * Math.Pow(MaxHz / MinHz, i / 200.0);
            double w = hz / cutoff, w2 = w * w;
            double den = Math.Sqrt((1 - w2) * (1 - w2) + k * k * w2);
            double mag = type switch
            {
                "HIGH" => w2 / den,
                "BAND" => w / den,   // as in the synth: louder at the cutoff the more resonance
                _ => 1 / den
            };
            pts.Add(new Point(x(hz), y(20 * Math.Log10(Math.Max(mag, 1e-6)))));
        }
        return Polyline(pts);
    }
}

// One second of an LFO (or two periods when it follows the song's rows), with the delay as a
// growing outline, and what it moves and how much
public sealed class LfoGraph : InstrumentGraph
{
    public Func<TrackerInstrument, TrackerLfo> Lfo { get; init; } = i => i.Lfo1;

    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        var lfo = Lfo(inst);
        double mid = a.Center.Y, half = a.Height / 2 - 2;
        ctx.DrawLine(new Pen(Grid, 1), new Point(a.Left, mid), new Point(a.Right, mid));
        if (lfo.Target == "Off" || lfo.Depth == 0)
        {
            ctx.DrawText(Text("OFF  (choose what it moves and a depth)", Label), new Point(a.Left, a.Bottom + 3));
            return;
        }

        var shape = Enum.TryParse<ChiptuneSynth.LfoShape>((lfo.Shape ?? "Sine").Replace(" ", ""), true, out var sh)
            ? sh : ChiptuneSynth.LfoShape.Sine;
        // The time shown: a second, or two periods of the rows
        bool synced = lfo.SyncRows > 0;
        double seconds = synced ? 2 * lfo.SyncRows * 6 * 2.5 / 125 : 1;
        double rate = synced ? 1 / (lfo.SyncRows * 6 * 2.5 / 125) : lfo.Rate;
        double delay = Math.Max(0, lfo.Delay) / 1000.0;

        var rng = new Random(3);
        double random = rng.NextDouble() * 2 - 1;
        var pts = new List<Point>();
        const int n = 400;
        double lastPhase = 0;
        for (int i = 0; i <= n; i++)
        {
            double time = i * seconds / n;
            double phase = time * rate % 1.0;
            if (phase < lastPhase) random = rng.NextDouble() * 2 - 1;
            lastPhase = phase;
            double fade = delay > 0 ? Math.Min(1, time / delay) : 1;
            double y = ChiptuneSynth.LfoWave(shape, phase, random) * fade * Math.Sign(lfo.Depth);
            var p = new Point(a.Left + i * a.Width / n, mid - y * half);
            if (pts.Count > 0 && Math.Abs(pts[^1].Y - p.Y) > half * 0.5) pts.Add(new Point(p.X, pts[^1].Y));
            pts.Add(p);
        }
        if (delay > 0)
        {
            double dx = a.Left + Math.Min(1, delay / seconds) * a.Width;
            var pen = new Pen(Faint, 1, new DashStyle(new double[] { 3, 3 }, 0));
            ctx.DrawLine(pen, new Point(a.Left, mid), new Point(dx, mid - half));
            ctx.DrawLine(pen, new Point(a.Left, mid), new Point(dx, mid + half));
        }
        ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(pts));

        string speed = synced ? $"per {lfo.SyncRows} row{(lfo.SyncRows == 1 ? "" : "s")}" : $"{lfo.Rate:0.#} Hz";
        // The filter must be on for the cutoff and resonance to change anything
        if (lfo.Target is "Cutoff" or "Resonance" && (inst.Filter ?? "Off").Equals("Off", StringComparison.OrdinalIgnoreCase))
            speed += "  (turn the filter on)";
        else if (lfo.Target.StartsWith("Osc 2", StringComparison.Ordinal) && (inst.Osc2Wave ?? "Off").Equals("Off", StringComparison.OrdinalIgnoreCase))
            speed += "  (turn oscillator 2 on)";
        ctx.DrawText(Text($"{lfo.Target.ToUpperInvariant()}  ±{Math.Abs(lfo.Depth):0.#} {TrackerLfo.UnitOf(lfo.Target)}  {speed}", Label),
            new Point(a.Left, a.Bottom + 3));
    }
}

// The sample's wave from start to end, with the loop marked; the length and rate below
public sealed class SampleGraph : InstrumentGraph
{
    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        double mid = a.Center.Y, half = a.Height / 2 - 2;
        ctx.DrawLine(new Pen(Grid, 1), new Point(a.Left, mid), new Point(a.Right, mid));
        var sample = string.IsNullOrEmpty(inst.Sample) ? null : SampleBank.Get(inst.Sample);
        if (sample == null || sample.Data.Length == 0)
        {
            ctx.DrawText(Text(string.IsNullOrEmpty(inst.Sample) ? "NO SAMPLE  (load a WAV file)" : "THE SAMPLE COULD NOT BE READ", Label),
                new Point(a.Left, a.Bottom + 3));
            return;
        }

        // The loudest point in each column, so the whole sound fits
        var d = sample.Data;
        int columns = Math.Max(1, (int)a.Width);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int x = 0; x < columns; x++)
            {
                int from = (int)((long)x * d.Length / columns), to = Math.Max(from + 1, (int)((long)(x + 1) * d.Length / columns));
                float lo = 0, hi = 0;
                for (int i = from; i < to && i < d.Length; i++) { lo = Math.Min(lo, d[i]); hi = Math.Max(hi, d[i]); }
                g.BeginFigure(new Point(a.Left + x + 0.5, mid - hi * half), false);
                g.LineTo(new Point(a.Left + x + 0.5, mid - lo * half));
                g.EndFigure(false);
            }
        }
        ctx.DrawGeometry(null, new Pen(Line, 1), geo);

        if (inst.LoopEnd > inst.LoopStart)
        {
            double x0 = a.Left + a.Width * inst.LoopStart / d.Length, x1 = a.Left + a.Width * Math.Min(inst.LoopEnd, d.Length) / d.Length;
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 220, 120)), new Rect(x0, a.Top, Math.Max(1, x1 - x0), a.Height));
            var pen = new Pen(Faint, 1, new DashStyle(new double[] { 3, 3 }, 0));
            ctx.DrawLine(pen, new Point(x0, a.Top), new Point(x0, a.Bottom));
            ctx.DrawLine(pen, new Point(x1, a.Top), new Point(x1, a.Bottom));
        }
        string loop = inst.LoopEnd > inst.LoopStart ? "  loop" : "";
        ctx.DrawText(Text($"{System.IO.Path.GetFileName(inst.Sample)}  {sample.Seconds:0.00} s  {sample.Rate} Hz{loop}", Label),
            new Point(a.Left, a.Bottom + 3));
    }
}

// The volume envelope drawn with points: click to add a point, drag to move one, right-click to take
// one away (there are always at least two). The first point stays at tick 0. The sustain point has a
// line, the loop is shaded.
public sealed class PointEnvelopeEditor : InstrumentGraph
{
    public const int MaxPoints = 25;

    // Makes a change to the instrument through the tracker window (for undo and the sound)
    public Action<Action<TrackerInstrument>>? Edit { get; set; }

    private int _drag = -1;

    public PointEnvelopeEditor()
    {
        Height = 150;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
        ToolTip.SetTip(this, "Click to add a point, drag to move one, right-click to take one away. The ticks follow the song's speed.");
    }

    private static int Span(TrackerInstrument inst) =>
        Math.Max(32, (int)Math.Ceiling((inst.Envelope.Count > 0 ? inst.Envelope[^1].Tick : 0) * 1.15 + 4));

    private double X(TrackerInstrument inst, Rect a, double tick) => a.Left + tick / Span(inst) * a.Width;
    private static double Y(Rect a, double level) => a.Bottom - level / 64.0 * a.Height;

    protected override void Draw(DrawingContext ctx, TrackerInstrument inst, Rect a)
    {
        var env = inst.Envelope;
        var gridPen = new Pen(Grid, 1, new DashStyle(new double[] { 2, 3 }, 0));
        foreach (var level in new[] { 16, 32, 48 }) ctx.DrawLine(gridPen, new Point(a.Left, Y(a, level)), new Point(a.Right, Y(a, level)));
        if (env.Count < 2) return;

        if (inst.EnvelopeLoopStart >= 0 && inst.EnvelopeLoopEnd >= inst.EnvelopeLoopStart && inst.EnvelopeLoopEnd < env.Count)
        {
            double x0 = X(inst, a, env[inst.EnvelopeLoopStart].Tick), x1 = X(inst, a, env[inst.EnvelopeLoopEnd].Tick);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 220, 120)), new Rect(x0, a.Top, Math.Max(2, x1 - x0), a.Height));
        }
        if (inst.EnvelopeSustain >= 0 && inst.EnvelopeSustain < env.Count)
        {
            double x = X(inst, a, env[inst.EnvelopeSustain].Tick);
            ctx.DrawLine(new Pen(Faint, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(x, a.Top), new Point(x, a.Bottom));
            ctx.DrawText(Text("S", Faint, 10), new Point(x + 2, a.Top));
        }

        var pts = new List<Point>();
        foreach (var p in env) pts.Add(new Point(X(inst, a, p.Tick), Y(a, p.Level)));
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(pts[0].X, a.Bottom), true);
            foreach (var p in pts) g.LineTo(p);
            g.LineTo(new Point(pts[^1].X, a.Bottom));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 255, 220, 0)), null, fill);
        ctx.DrawGeometry(null, new Pen(Line, 2), Polyline(pts));
        for (int k = 0; k < pts.Count; k++)
        {
            double r = k == _drag ? 5 : 3.5;
            ctx.FillRectangle(k == _drag ? Line : Screen, new Rect(pts[k].X - r, pts[k].Y - r, 2 * r, 2 * r));
            ctx.DrawRectangle(new Pen(Line, 1.5), new Rect(pts[k].X - r, pts[k].Y - r, 2 * r, 2 * r));
        }
        string info = _drag >= 0 && _drag < env.Count
            ? $"point {_drag + 1}: tick {env[_drag].Tick}, level {env[_drag].Level}"
            : $"{env.Count} points, {env[^1].Tick} ticks";
        ctx.DrawText(Text(info, Label), new Point(a.Left, a.Bottom + 3));
    }

    private int HitPoint(TrackerInstrument inst, Point p)
    {
        var a = Area;
        for (int k = 0; k < inst.Envelope.Count; k++)
        {
            var e = inst.Envelope[k];
            if (Math.Abs(X(inst, a, e.Tick) - p.X) <= 6 && Math.Abs(Y(a, e.Level) - p.Y) <= 6) return k;
        }
        return -1;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var inst = Instrument();
        if (inst == null || Edit == null || inst.Envelope.Count < 2) return;
        var p = e.GetPosition(this);
        int hit = HitPoint(inst, p);
        e.Handled = true;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (hit > 0 && inst.Envelope.Count > 2) Edit(i => RemovePoint(i, hit));
            return;
        }
        if (hit < 0)
        {
            if (inst.Envelope.Count >= MaxPoints) return;
            var a = Area;
            int tick = (int)Math.Round(Math.Clamp((p.X - a.Left) / a.Width, 0, 1) * Span(inst));
            int level = (int)Math.Round(Math.Clamp((a.Bottom - p.Y) / a.Height, 0, 1) * 64);
            int at = inst.Envelope.FindIndex(q => q.Tick >= tick);
            if (at == 0 || (at > 0 && inst.Envelope[at].Tick == tick)) return;   // tick taken
            if (at < 0) at = inst.Envelope.Count;
            Edit(i => InsertPoint(i, at, new TrackerEnvelopePoint(tick, level)));
            hit = at;
        }
        _drag = hit;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var inst = Instrument();
        if (_drag < 0 || inst == null || Edit == null || _drag >= inst.Envelope.Count) return;
        var a = Area;
        var p = e.GetPosition(this);
        int k = _drag;
        // Between its neighbours; the first point stays at tick 0. Dragging past the right edge makes room.
        int lo = k == 0 ? 0 : inst.Envelope[k - 1].Tick + 1;
        int hi = k == 0 ? 0 : k < inst.Envelope.Count - 1 ? inst.Envelope[k + 1].Tick - 1 : 9999;
        int tick = Math.Clamp((int)Math.Round((p.X - a.Left) / a.Width * Span(inst)), lo, hi);
        int level = (int)Math.Round(Math.Clamp((a.Bottom - p.Y) / a.Height, 0, 1) * 64);
        if (tick == inst.Envelope[k].Tick && level == inst.Envelope[k].Level) return;
        Edit(i => { i.Envelope[k].Tick = tick; i.Envelope[k].Level = level; });
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    // The sustain and loop points follow the points they are on
    public static void InsertPoint(TrackerInstrument i, int at, TrackerEnvelopePoint point)
    {
        i.Envelope.Insert(at, point);
        int Moved(int index) => index >= at ? index + 1 : index;
        if (i.EnvelopeSustain >= 0) i.EnvelopeSustain = Moved(i.EnvelopeSustain);
        if (i.EnvelopeLoopStart >= 0) i.EnvelopeLoopStart = Moved(i.EnvelopeLoopStart);
        if (i.EnvelopeLoopEnd >= 0) i.EnvelopeLoopEnd = Moved(i.EnvelopeLoopEnd);
    }

    public static void RemovePoint(TrackerInstrument i, int at)
    {
        i.Envelope.RemoveAt(at);
        int Moved(int index) => index == at ? Math.Min(index, i.Envelope.Count - 1) : index > at ? index - 1 : index;
        if (i.EnvelopeSustain >= 0) i.EnvelopeSustain = Moved(i.EnvelopeSustain);
        if (i.EnvelopeLoopStart >= 0) i.EnvelopeLoopStart = Moved(i.EnvelopeLoopStart);
        if (i.EnvelopeLoopEnd >= 0) i.EnvelopeLoopEnd = Moved(i.EnvelopeLoopEnd);
        if (i.EnvelopeLoopStart > i.EnvelopeLoopEnd) i.EnvelopeLoopStart = i.EnvelopeLoopEnd;
    }
}
