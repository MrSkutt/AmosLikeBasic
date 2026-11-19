using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace AmosLikeBasic;

// A synth knob: drag up or down (Shift for small steps) or turn the mouse wheel; a double-click
// lets you type the value. It never takes the keyboard, so the piano keys go on playing while
// you turn it. Logarithmic knobs (cutoff, times) turn evenly through the octaves; bipolar ones
// (depths that can go both ways) have 0 at the top.
public sealed class Knob : Control
{
    public string Label { get; set; } = "";
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public bool Logarithmic { get; set; }
    public bool Bipolar { get; set; }
    public double Step { get; set; }            // values are rounded to this (0: not rounded)
    public string Format { get; set; } = "0";
    public string Unit { get; set; } = "";
    // How the value is written under the knob, when a number is not the best way (a note name)
    public Func<double, string>? ValueText { get; set; }

    private double _value;
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(value, Minimum, Maximum);
            if (Step > 0) v = Math.Round(v / Step) * Step;
            if (v == _value) return;
            _value = v;
            InvalidateVisual();
        }
    }

    // Raised when the user changes it (not when Value is set from code)
    public event Action<double>? ValueChanged;

    private Point? _dragFrom;
    private double _dragPos;

    public Knob()
    {
        Width = 64;
        Height = 78;
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    // 0 – 1 along the knob's travel
    private double Position
    {
        get
        {
            if (Maximum <= Minimum) return 0;
            if (Logarithmic && Minimum > 0) return Math.Log(_value / Minimum) / Math.Log(Maximum / Minimum);
            return (_value - Minimum) / (Maximum - Minimum);
        }
    }

    private double ValueAt(double pos)
    {
        pos = Math.Clamp(pos, 0, 1);
        if (Logarithmic && Minimum > 0) return Minimum * Math.Pow(Maximum / Minimum, pos);
        return Minimum + pos * (Maximum - Minimum);
    }

    private void SetByUser(double v)
    {
        double old = _value;
        Value = v;
        if (_value != old) ValueChanged?.Invoke(_value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;
        if (e.ClickCount == 2) { AskForValue(); e.Handled = true; return; }
        _dragFrom = e.GetPosition(this);
        _dragPos = Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from) return;
        var p = e.GetPosition(this);
        // 200 pixels for the whole travel, 2000 with Shift
        double range = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2000 : 200;
        SetByUser(ValueAt(_dragPos + (from.Y - p.Y) / range));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.002 : 0.02;
        double next = ValueAt(Position + Math.Sign(e.Delta.Y) * step);
        // A stepped knob always moves at least one step
        if (Step > 0 && Math.Abs(next - _value) < Step) next = _value + Math.Sign(e.Delta.Y) * Step;
        SetByUser(next);
        e.Handled = true;
    }

    // A small box over the knob to type the value in; Enter takes it, Esc or clicking away drops it
    private void AskForValue()
    {
        var box = new TextBox
        {
            Text = _value.ToString(Format, CultureInfo.InvariantCulture),
            Width = 80, FontSize = 12, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var popup = new Popup
        {
            Child = box, PlacementTarget = this, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (double.TryParse((box.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    SetByUser(v);
                popup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };
        popup.Closed += (_, _) => (Parent as Panel)?.Children.Remove(popup);
        (Parent as Panel)?.Children.Add(popup);
        popup.IsOpen = true;
        box.Focus();
        box.SelectAll();
    }

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        var accent = Res("AmosTitleFg", Brushes.Yellow);
        var label = Res("AmosLabelFg", Brushes.LightBlue);
        var track = Res("AmosInputBg", Brushes.Black);
        double size = Math.Min(Bounds.Width, Bounds.Height - 30);
        var c = new Point(Bounds.Width / 2, 16 + size / 2 - 2);
        double r = size / 2 - 4;

        // The travel goes from 7 to 5 o'clock (270°)
        const double start = 135, sweep = 270;
        double pos = Position;
        double zero = Bipolar ? 0.5 : 0;
        var trackPen = new Pen(track, 5, lineCap: PenLineCap.Round);
        var valuePen = new Pen(IsEnabled ? accent : label, 5, lineCap: PenLineCap.Round);
        ctx.DrawGeometry(null, trackPen, Arc(c, r, start, sweep));
        double a0 = start + sweep * Math.Min(zero, pos), a1 = start + sweep * Math.Max(zero, pos);
        if (a1 - a0 > 0.5) ctx.DrawGeometry(null, valuePen, Arc(c, r, a0, a1 - a0));

        // The knob itself, with a line pointing at the value
        ctx.DrawEllipse(track, new Pen(label, 1), c, r - 6, r - 6);
        double a = (start + sweep * pos) * Math.PI / 180;
        ctx.DrawLine(new Pen(IsEnabled ? accent : label, 2, lineCap: PenLineCap.Round),
            new Point(c.X + Math.Cos(a) * (r - 14), c.Y + Math.Sin(a) * (r - 14)),
            new Point(c.X + Math.Cos(a) * (r - 6), c.Y + Math.Sin(a) * (r - 6)));

        var font = new Typeface("Menlo, Consolas, Courier New, monospace");
        var title = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10, label);
        ctx.DrawText(title, new Point((Bounds.Width - title.Width) / 2, 0));
        string shown = ValueText?.Invoke(_value) ?? _value.ToString(Format, CultureInfo.InvariantCulture) + (Unit.Length > 0 ? " " + Unit : "");
        var text = new FormattedText(shown, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10, IsEnabled ? accent : label);
        ctx.DrawText(text, new Point((Bounds.Width - text.Width) / 2, Bounds.Height - 13));
    }

    private static StreamGeometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        g.BeginFigure(new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r), false);
        g.ArcTo(new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geo;
    }
}
