using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AmosLikeBasic;

// The strip above the tracker's pattern: an oscilloscope and a level meter per channel, in line
// with the pattern's columns. The picture is what the channel sounds like right now (the synth
// keeps the latest samples of every channel, ChiptuneSynth.ReadScope), so it also moves when
// you only try notes on the keyboard.
public sealed class ChannelScopes : Control
{
    private PatternView? _pattern;
    public PatternView? Pattern
    {
        get => _pattern;
        set
        {
            _pattern = value;
            if (value != null) value.ChannelsMoved += InvalidateVisual;
        }
    }

    private const int Shown = 512;          // samples across one scope, about 12 ms
    private const double Gain = 2.5;        // a channel at volume 0.4 fills the height
    private const double MeterFall = 0.82;  // how fast the meter falls back, per update
    private const double PeakHoldSeconds = 0.8;

    private readonly float[] _buf = new float[Shown * 2];
    private readonly double[] _level = new double[TrackerSong.MaxChannels];
    private readonly double[] _peak = new double[TrackerSong.MaxChannels];
    private readonly DateTime[] _peakTime = new DateTime[TrackerSong.MaxChannels];
    private readonly DispatcherTimer _timer;
    private bool _wasSilent;

    public ChannelScopes()
    {
        Height = 70;
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

    // Level of the latest 1/60 s of each channel; only redraw while something sounds or a meter falls
    private void Tick()
    {
        bool silent = true;
        Span<float> recent = stackalloc float[735];
        int channels = Pattern?.ChannelCount ?? TrackerSong.DefaultChannels;
        for (int c = 0; c < channels; c++)
        {
            ChiptuneSynth.ReadScope(c + 1, recent);
            double max = 0;
            foreach (var s in recent) max = Math.Max(max, Math.Abs(s));
            double level = Math.Min(1, max * Gain);

            _level[c] = Math.Max(level, _level[c] * MeterFall);
            if (_level[c] < 0.005) _level[c] = 0;
            if (level >= _peak[c]) { _peak[c] = level; _peakTime[c] = DateTime.UtcNow; }
            else if ((DateTime.UtcNow - _peakTime[c]).TotalSeconds > PeakHoldSeconds)
                _peak[c] = Math.Max(0, _peak[c] - 0.03);

            if (_level[c] > 0 || _peak[c] > 0) silent = false;
        }
        if (silent && _wasSilent) return;
        _wasSilent = silent;
        InvalidateVisual();
    }

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        var bg = Res("AmosPanelBg", Brushes.DarkBlue);
        var screen = Res("AmosInputBg", Brushes.Black);
        var border = Res("AmosPanelBorder", Brushes.LightBlue);
        var wave = Res("AmosTitleFg", Brushes.Yellow);
        ctx.FillRectangle(bg, new Rect(Bounds.Size));
        if (Pattern == null) return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 160, 255)), 1);
        var wavePen = new Pen(wave, 1.5);
        var dimPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)), 1.5);
        double h = Bounds.Height;

        for (int c = 0; c < Pattern.ChannelCount; c++)
        {
            double left = Pattern.ChannelLeft(c) + 3;
            double width = Pattern.ChannelSpan - 6;
            const double meterW = 8;
            var scopeRect = new Rect(left, 4, width - meterW - 4, h - 8);
            var meterRect = new Rect(left + width - meterW, 4, meterW, h - 8);
            if (scopeRect.Width < 20) continue;
            bool muted = Pattern.Muted[c];

            // The scope
            ctx.FillRectangle(screen, scopeRect);
            ctx.DrawRectangle(new Pen(border, 1), scopeRect);
            double mid = scopeRect.Center.Y;
            ctx.DrawLine(gridPen, new Point(scopeRect.Left, mid), new Point(scopeRect.Right, mid));

            ChiptuneSynth.ReadScope(c + 1, _buf);
            int start = Trigger(_buf);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                double half = scopeRect.Height / 2 - 2;
                for (int i = 0; i < Shown; i++)
                {
                    double y = mid - Math.Clamp(_buf[start + i] * Gain, -1, 1) * half;
                    var p = new Point(scopeRect.Left + i * scopeRect.Width / (Shown - 1), y);
                    if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
                }
                g.EndFigure(false);
            }
            using (ctx.PushClip(scopeRect))
                ctx.DrawGeometry(null, muted ? dimPen : wavePen, geo);

            // The meter: green, yellow at the top, red at the very top; a line where the peak was
            ctx.FillRectangle(screen, meterRect);
            double filled = meterRect.Height * _level[c];
            if (filled > 0)
            {
                var bar = new Rect(meterRect.X, meterRect.Bottom - filled, meterRect.Width, filled);
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#2ecc40"), 0),
                        new GradientStop(Color.Parse("#2ecc40"), 0.6),
                        new GradientStop(Color.Parse("#ffdc00"), 0.8),
                        new GradientStop(Color.Parse("#ff4136"), 1)
                    }
                };
                using (ctx.PushClip(bar))
                    ctx.FillRectangle(muted ? Brushes.Gray : brush, meterRect);
            }
            if (_peak[c] > 0)
            {
                double y = meterRect.Bottom - meterRect.Height * _peak[c];
                ctx.DrawLine(new Pen(Brushes.White, 2), new Point(meterRect.Left, y), new Point(meterRect.Right, y));
            }
            ctx.DrawRectangle(new Pen(border, 1), meterRect);
        }
    }

    // Start the picture where the wave rises through zero, so that a steady note stands still
    // instead of running sideways (as on a real oscilloscope)
    private static int Trigger(float[] buf)
    {
        int last = buf.Length - Shown;
        for (int i = last; i > 0; i--)
            if (buf[i - 1] < 0 && buf[i] >= 0) return i;
        return last;
    }
}
