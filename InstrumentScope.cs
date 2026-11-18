using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AmosLikeBasic;

// The big oscilloscope on the instrument page: the sound of the instrument chosen, on a channel
// that plays it right now (the song's or the piano keys'), so a turn of a knob shows straight away.
// The height follows the sound, so quiet instruments can be seen too; the level is in the corner.
public sealed class InstrumentScope : Control
{
    public Func<int> Channel { get; set; } = () => 1;   // 0: the instrument is not sounding
    public Func<string> Instrument { get; set; } = () => "";

    private const int Shown = 1024;   // about 23 ms
    private readonly float[] _buf = new float[Shown * 2];
    private readonly DispatcherTimer _timer;
    private double _gain = 4;
    private bool _wasSilent;
    private string _shownInstrument = "";
    private int _lastChannel;
    private DateTime _lastHeard;

    public InstrumentScope()
    {
        Height = 150;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick()
    {
        // Between two notes of a song the channel is kept for a moment, so the picture does not blink
        int channel = Channel();
        if (channel > 0) { _lastChannel = channel; _lastHeard = DateTime.UtcNow; }
        else if (_lastChannel > 0 && (DateTime.UtcNow - _lastHeard).TotalSeconds < 0.5) channel = _lastChannel;
        else _lastChannel = 0;
        if (channel > 0) ChiptuneSynth.ReadScope(channel, _buf);
        else Array.Clear(_buf);
        double peak = 0;
        foreach (var s in _buf) peak = Math.Max(peak, Math.Abs(s));
        bool silent = peak < 0.0005;
        // Another instrument chosen: its name is shown even while it is silent
        string instrument = Instrument();
        bool renamed = instrument != _shownInstrument;
        _shownInstrument = instrument;
        if (silent && _wasSilent && !renamed) return;
        _wasSilent = silent;
        // Grows quickly, shrinks slowly, so the picture does not jump about
        double target = 0.9 / Math.Max(peak, 0.02);
        _gain = target < _gain ? target : _gain + (target - _gain) * 0.05;
        InvalidateVisual();
    }

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        var all = new Rect(Bounds.Size);
        ctx.FillRectangle(Res("AmosInputBg", Brushes.Black), all);
        ctx.DrawRectangle(new Pen(Res("AmosPanelBorder", Brushes.LightBlue), 1), all.Deflate(0.5));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 160, 255)), 1);
        double mid = all.Center.Y, half = all.Height / 2 - 6;
        for (int i = 1; i < 8; i++) ctx.DrawLine(grid, new Point(all.Width * i / 8, 0), new Point(all.Width * i / 8, all.Height));
        ctx.DrawLine(grid, new Point(0, mid), new Point(all.Width, mid));

        // Start where the wave goes up through zero, so a steady note stands still
        int start = 0;
        for (int i = 1; i < Shown; i++)
            if (_buf[i - 1] <= 0 && _buf[i] > 0) { start = i; break; }

        double peak = 0;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < Shown; i++)
            {
                float s = _buf[start + i];
                peak = Math.Max(peak, Math.Abs(s));
                var p = new Point(i * all.Width / (Shown - 1), mid - Math.Clamp(s * _gain, -1, 1) * half);
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        using (ctx.PushClip(all))
            ctx.DrawGeometry(null, new Pen(Res("AmosTitleFg", Brushes.Yellow), 2), geo);

        var label = Res("AmosLabelFg", Brushes.LightBlue);
        var font = new Typeface("Menlo, Consolas, Courier New, monospace");
        int ch = _lastChannel;
        string text = ch == 0 || peak < 0.0005 ? $"{Instrument()}  play a note (Z–M, Q–P) to see the sound"
                                               : $"{Instrument()}  on channel {ch}  level {peak * 100:0} %";
        ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, label), new Point(8, 6));
    }
}
