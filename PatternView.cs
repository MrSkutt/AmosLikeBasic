using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AmosLikeBasic;

// Mönstret i trackern: fyra kanaler med not, instrument och effekt per rad.
// Raden med markören står still mitt i vyn och mönstret rullar förbi, som i ProTracker.
public class PatternView : Control
{
    // Kolumner i en kanal: not, instrument (två siffror), effekt, parameter (två siffror)
    private const int ColNote = 0, ColInstHi = 1, ColInstLo = 2, ColEffect = 3, ColParamHi = 4, ColParamLo = 5;
    private const int ColumnsPerChannel = 6;

    // Teckenposition för varje kolumn inom cellen "C-4 01 A04"
    private static readonly int[] ColumnChar = { 0, 4, 5, 7, 8, 9 };
    private static readonly int[] ColumnWidth = { 3, 1, 1, 1, 1, 1 };
    private const int CellChars = 10;
    private const int ChannelGapChars = 2;
    private const int RowNumberChars = 4;

    public TrackerSong? Song { get; set; }
    public int PatternIndex { get; set; }
    public int CursorRow { get; private set; }
    public int CursorChannel { get; private set; }
    public int CursorColumn { get; private set; }
    public int Octave { get; set; } = 4;
    public int EditStep { get; set; } = 1;
    public int Instrument { get; set; } = 1;   // instrumentet som nya noter får
    public bool EditMode { get; set; } = true;
    public int PlayRow { get; set; } = -1;     // raden som spelas, -1 = ingen
    public bool[] Muted { get; } = new bool[TrackerSong.ChannelCount];

    // Händelser till fönstret
    public event Action? BeforeEdit;                 // innan mönstret ändras, för ångra
    public event Action? Edited;                     // efter en ändring
    public event Action<int, int>? NotePreview;      // kanal (1 – 4), not
    public event Action<int>? NoteRelease;           // kanal (1 – 4)
    public event Action<string>? Command;            // "play-song", "play-pattern", "play-cursor", "stop", "toggle-edit", "octave", "undo", "redo", "save"
    public event Action? MuteChanged;
    public event Action? CursorMoved;

    // Markering: ett block av hela celler, från ankaret till markören
    private int _anchorRow = -1, _anchorChannel;
    private bool _dragging;

    // Urklipp delas mellan alla mönster
    private static TrackerCell[,]? _clipboard;

    private readonly Dictionary<Key, int> _heldNotes = new();   // tangent → kanal som provspelar

    private Typeface _typeface = new("Menlo, Consolas, Courier New, monospace");
    private const double FontSize = 15;
    private double _charW, _rowH;
    private readonly Dictionary<(string, IBrush), FormattedText> _textCache = new();

    public PatternView()
    {
        Focusable = true;
        ClipToBounds = true;
        MeasureFont();
        // Rita om med nya färger när temat byts
        ResourcesChanged += (_, _) =>
        {
            _textCache.Clear();
            InvalidateVisual();
        };
    }

    private void MeasureFont()
    {
        var ft = new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, Brushes.White);
        _charW = ft.Width / 10;
        _rowH = Math.Ceiling(ft.Height + 3);
    }

    private TrackerPattern? Pattern =>
        Song != null && PatternIndex >= 0 && PatternIndex < Song.Patterns.Count ? Song.Patterns[PatternIndex] : null;

    private int Rows => Pattern?.Rows ?? TrackerSong.DefaultRows;

    public double PreferredWidth => (RowNumberChars + TrackerSong.ChannelCount * (CellChars + ChannelGapChars)) * _charW + 16;

    protected override Size MeasureOverride(Size availableSize) =>
        new(PreferredWidth, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    // ════════════════════════════════════════════════════════════════
    //  Markören
    // ════════════════════════════════════════════════════════════════

    public void SetCursor(int row, int channel, int column, bool extendSelection = false)
    {
        if (extendSelection && _anchorRow < 0)
        {
            _anchorRow = CursorRow;
            _anchorChannel = CursorChannel;
        }
        else if (!extendSelection)
            _anchorRow = -1;

        int rows = Rows;
        CursorRow = ((row % rows) + rows) % rows;
        CursorChannel = Math.Clamp(channel, 0, TrackerSong.ChannelCount - 1);
        CursorColumn = Math.Clamp(column, 0, ColumnsPerChannel - 1);
        InvalidateVisual();
        CursorMoved?.Invoke();
    }

    // Följer uppspelningen utan att röra markeringen
    public void FollowRow(int row)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        _anchorRow = -1;
        InvalidateVisual();
    }

    private bool HasSelection => _anchorRow >= 0 && (_anchorRow != CursorRow || _anchorChannel != CursorChannel);

    private (int r0, int r1, int c0, int c1) SelectionOrCursor()
    {
        if (!HasSelection) return (CursorRow, CursorRow, CursorChannel, CursorChannel);
        return (Math.Min(_anchorRow, CursorRow), Math.Max(_anchorRow, CursorRow),
                Math.Min(_anchorChannel, CursorChannel), Math.Max(_anchorChannel, CursorChannel));
    }

    // ════════════════════════════════════════════════════════════════
    //  Ritning
    // ════════════════════════════════════════════════════════════════

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var o) && o is IBrush b ? b : fallback;

    private FormattedText Text(string s, IBrush brush)
    {
        if (!_textCache.TryGetValue((s, brush), out var ft))
        {
            if (_textCache.Count > 4000) _textCache.Clear();
            ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, brush);
            _textCache[(s, brush)] = ft;
        }
        return ft;
    }

    private double ChannelX(int c) => 8 + (RowNumberChars + c * (CellChars + ChannelGapChars)) * _charW;

    private double HeaderH => _rowH + 6;

    public override void Render(DrawingContext ctx)
    {
        var bg = Res("AmosCanvasBg", Brushes.Black);
        var fg = Res("AmosInputFg", Brushes.White);
        var label = Res("AmosLabelFg", Brushes.LightBlue);
        var title = Res("AmosTitleFg", Brushes.Yellow);
        var panel = Res("AmosPanelBg", Brushes.DarkBlue);
        var border = Res("AmosPanelBorder", Brushes.LightBlue);

        ctx.FillRectangle(bg, new Rect(Bounds.Size));
        var pattern = Pattern;
        if (pattern == null) return;

        double w = Bounds.Width;
        double headerH = HeaderH;
        int visible = Math.Max(1, (int)((Bounds.Height - headerH) / _rowH));
        int center = visible / 2;
        int top = CursorRow - center;

        // Taktmarkeringar i textfärgen, så att de syns i både ljusa och mörka teman
        var fgColor = fg is ISolidColorBrush sb ? sb.Color : Colors.White;
        var beatBrush = new SolidColorBrush(Color.FromArgb(24, fgColor.R, fgColor.G, fgColor.B));
        var barBrush = new SolidColorBrush(Color.FromArgb(44, fgColor.R, fgColor.G, fgColor.B));
        var cursorRowBrush = new SolidColorBrush(EditMode ? Color.FromArgb(90, 200, 40, 40) : Color.FromArgb(80, 60, 120, 255));
        var playBrush = new SolidColorBrush(Color.FromArgb(90, 40, 200, 80));
        var selBrush = new SolidColorBrush(Color.FromArgb(80, 255, 220, 0));
        var dim = new SolidColorBrush(Color.FromArgb(110, 160, 160, 160));
        var instBrush = title;
        var effBrush = label;

        var (s0, s1, sc0, sc1) = HasSelection ? SelectionOrCursor() : (-1, -1, -1, -1);

        for (int i = 0; i < visible; i++)
        {
            int r = top + i;
            if (r < 0 || r >= pattern.Rows) continue;
            double y = headerH + i * _rowH;
            var rowRect = new Rect(0, y, w, _rowH);

            if (r % 16 == 0) ctx.FillRectangle(barBrush, rowRect);
            else if (r % 4 == 0) ctx.FillRectangle(beatBrush, rowRect);
            if (r == PlayRow) ctx.FillRectangle(playBrush, rowRect);
            if (r == CursorRow) ctx.FillRectangle(cursorRowBrush, rowRect);

            ctx.DrawText(Text(r.ToString("D2", CultureInfo.InvariantCulture), r % 4 == 0 ? title : label), new Point(8, y + 1));

            for (int c = 0; c < TrackerSong.ChannelCount; c++)
            {
                double x = ChannelX(c);
                if (r >= s0 && r <= s1 && c >= sc0 && c <= sc1)
                    ctx.FillRectangle(selBrush, new Rect(x - _charW * 0.5, y, (CellChars + 1) * _charW, _rowH));

                var cell = pattern[r, c];
                bool muted = Muted[c];
                string note = TrackerCell.NoteName(cell.Note);
                string inst = cell.Instrument == 0 ? ".." : cell.Instrument.ToString("X2", CultureInfo.InvariantCulture);
                string eff = cell.Effect == '\0' ? "..." : cell.Effect + cell.Param.ToString("X2", CultureInfo.InvariantCulture);
                ctx.DrawText(Text(note, muted || cell.Note == TrackerCell.NoNote ? dim : fg), new Point(x, y + 1));
                ctx.DrawText(Text(inst, muted || cell.Instrument == 0 ? dim : instBrush), new Point(x + 4 * _charW, y + 1));
                ctx.DrawText(Text(eff, muted || cell.Effect == '\0' ? dim : effBrush), new Point(x + 7 * _charW, y + 1));
            }
        }

        // Markören
        {
            double y = headerH + center * _rowH;
            double x = ChannelX(CursorChannel) + ColumnChar[CursorColumn] * _charW;
            var pen = new Pen(IsFocused ? title : label, 2);
            ctx.DrawRectangle(pen, new Rect(x - 1, y, ColumnWidth[CursorColumn] * _charW + 2, _rowH));
        }

        // Kanalrubriker
        ctx.FillRectangle(panel, new Rect(0, 0, w, headerH));
        ctx.DrawLine(new Pen(border, 1), new Point(0, headerH - 1), new Point(w, headerH - 1));
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
        {
            string h = Muted[c] ? $"CH{c + 1} MUTE" : $"CH{c + 1}";
            ctx.DrawText(Text(h, Muted[c] ? dim : title), new Point(ChannelX(c), 3));
        }
        ctx.DrawText(Text(PatternIndex.ToString("D2", CultureInfo.InvariantCulture), label), new Point(8, 3));

        // Linjer mellan kanalerna
        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(70, 128, 160, 255)), 1);
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
        {
            double x = ChannelX(c) - _charW;
            ctx.DrawLine(linePen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Mus
    // ════════════════════════════════════════════════════════════════

    private (int row, int channel, int column) HitTest(Point p)
    {
        int visible = Math.Max(1, (int)((Bounds.Height - HeaderH) / _rowH));
        int row = CursorRow - visible / 2 + (int)Math.Floor((p.Y - HeaderH) / _rowH);
        int channel = 0;
        for (int c = TrackerSong.ChannelCount - 1; c >= 0; c--)
            if (p.X >= ChannelX(c) - _charW) { channel = c; break; }
        double local = (p.X - ChannelX(channel)) / _charW;
        int column = 0;
        for (int col = ColumnsPerChannel - 1; col >= 0; col--)
            if (local >= ColumnChar[col] - 0.5) { column = col; break; }
        return (Math.Clamp(row, 0, Rows - 1), channel, column);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        var (row, channel, column) = HitTest(p);

        if (p.Y < HeaderH)
        {
            // Vänsterklick: tysta kanalen. Högerklick: spela bara den kanalen (solo).
            if (props.IsRightButtonPressed)
            {
                // Är kanalen redan ensam hörs alla igen
                bool alreadySolo = !Muted[channel] && CountUnmuted() == 1;
                for (int c = 0; c < Muted.Length; c++) Muted[c] = !alreadySolo && c != channel;
            }
            else
                Muted[channel] = !Muted[channel];
            MuteChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        SetCursor(row, channel, column, shift);
        if (!shift)
        {
            _anchorRow = CursorRow;
            _anchorChannel = CursorChannel;
        }
        _dragging = true;
        e.Pointer.Capture(this);
    }

    private int CountUnmuted()
    {
        int n = 0;
        foreach (var m in Muted) if (!m) n++;
        return n;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var (row, channel, _) = HitTest(e.GetPosition(this));
        if (row != CursorRow || channel != CursorChannel)
        {
            CursorRow = row;
            CursorChannel = channel;
            InvalidateVisual();
            CursorMoved?.Invoke();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        if (!HasSelection) _anchorRow = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int steps = (int)Math.Round(-e.Delta.Y * 3);
        if (steps == 0) steps = e.Delta.Y > 0 ? -1 : 1;
        SetCursor(Math.Clamp(CursorRow + steps, 0, Rows - 1), CursorChannel, CursorColumn, HasSelection);
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        ReleaseHeldNotes();
        InvalidateVisual();
    }

    // ════════════════════════════════════════════════════════════════
    //  Tangentbord
    // ════════════════════════════════════════════════════════════════

    // Pianotangenter som i ProTracker: nedre raden är en oktav, övre raden nästa
    private static readonly Dictionary<Key, int> PianoKeys = new()
    {
        [Key.Z] = 0, [Key.S] = 1, [Key.X] = 2, [Key.D] = 3, [Key.C] = 4, [Key.V] = 5, [Key.G] = 6,
        [Key.B] = 7, [Key.H] = 8, [Key.N] = 9, [Key.J] = 10, [Key.M] = 11, [Key.OemComma] = 12,
        [Key.L] = 13, [Key.OemPeriod] = 14,
        [Key.Q] = 12, [Key.D2] = 13, [Key.W] = 14, [Key.D3] = 15, [Key.E] = 16, [Key.R] = 17,
        [Key.D5] = 18, [Key.T] = 19, [Key.D6] = 20, [Key.Y] = 21, [Key.D7] = 22, [Key.U] = 23,
        [Key.I] = 24, [Key.D9] = 25, [Key.O] = 26, [Key.D0] = 27, [Key.P] = 28,
    };

    private static int HexDigit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        >= Key.A and <= Key.F => 10 + (key - Key.A),
        _ => -1
    };

    // Effektkolumnen tar också bokstäver efter F (G, H, I ...)
    private static char EffectChar(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        _ => '\0'
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Pattern == null) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        e.Handled = true;

        if (cmd)
        {
            switch (e.Key)
            {
                case Key.C: Copy(); return;
                case Key.X: Copy(); ClearBlock(); return;
                case Key.V: Paste(); return;
                case Key.A: SelectAll(); return;
                case Key.Z: Command?.Invoke(shift ? "redo" : "undo"); return;
                case Key.Y: Command?.Invoke("redo"); return;
                case Key.S: Command?.Invoke("save"); return;
                case Key.Up: Transpose(shift ? 12 : 1); return;
                case Key.Down: Transpose(shift ? -12 : -1); return;
                // Mac-tangentbord saknar Insert, och deras delete-tangent är Backspace
                case Key.I: if (EditMode) InsertRow(shift); return;
                case Key.Back: if (EditMode) DeleteRow(shift); return;
            }
            e.Handled = false;
            return;
        }

        switch (e.Key)
        {
            case Key.Up: SetCursor(CursorRow - 1, CursorChannel, CursorColumn, shift); return;
            case Key.Down: SetCursor(CursorRow + 1, CursorChannel, CursorColumn, shift); return;
            case Key.PageUp: SetCursor(Math.Max(0, CursorRow - 16), CursorChannel, CursorColumn, shift); return;
            case Key.PageDown: SetCursor(Math.Min(Rows - 1, CursorRow + 16), CursorChannel, CursorColumn, shift); return;
            case Key.Home: SetCursor(0, CursorChannel, CursorColumn, shift); return;
            case Key.End: SetCursor(Rows - 1, CursorChannel, CursorColumn, shift); return;
            case Key.Left:
                if (shift) SetCursor(CursorRow, CursorChannel - 1, CursorColumn, true);
                else if (CursorColumn > 0) SetCursor(CursorRow, CursorChannel, CursorColumn - 1);
                else SetCursor(CursorRow, (CursorChannel + 3) % 4, ColumnsPerChannel - 1);
                return;
            case Key.Right:
                if (shift) SetCursor(CursorRow, CursorChannel + 1, CursorColumn, true);
                else if (CursorColumn < ColumnsPerChannel - 1) SetCursor(CursorRow, CursorChannel, CursorColumn + 1);
                else SetCursor(CursorRow, (CursorChannel + 1) % 4, 0);
                return;
            case Key.Tab:
                SetCursor(CursorRow, (CursorChannel + (shift ? 3 : 1)) % 4, 0);
                return;
            case Key.F1: Octave = Math.Max(0, Octave - 1); Command?.Invoke("octave"); return;
            case Key.F2: Octave = Math.Min(8, Octave + 1); Command?.Invoke("octave"); return;
            case Key.F5: Command?.Invoke("play-song"); return;
            case Key.F6: Command?.Invoke("play-pattern"); return;
            case Key.F7: Command?.Invoke("play-cursor"); return;
            case Key.Escape: Command?.Invoke("stop"); return;
            case Key.Space: Command?.Invoke("toggle-edit"); return;
            case Key.Delete:
            case Key.Back:
                if (HasSelection) ClearBlock();
                else if (EditMode) { ClearField(); Advance(); }
                return;
            case Key.Insert:
                if (EditMode) InsertRow(shift);
                return;
        }

        if (CursorColumn == ColNote)
        {
            if (e.Key == Key.D1 || e.Key == Key.OemPlus)
            {
                if (EditMode) { SetNote(TrackerCell.NoteOff); Advance(); }
                return;
            }
            if (PianoKeys.TryGetValue(e.Key, out int semi))
            {
                int note = (Octave + 1) * 12 + semi;
                if (note > 119) return;
                if (!_heldNotes.ContainsKey(e.Key))
                {
                    _heldNotes[e.Key] = CursorChannel + 1;
                    NotePreview?.Invoke(CursorChannel + 1, note);
                }
                if (EditMode) { SetNote(note); Advance(); }
                return;
            }
        }
        else if (EditMode)
        {
            if (CursorColumn == ColEffect)
            {
                char ch = EffectChar(e.Key);
                if (ch != '\0') { SetEffectChar(ch); Advance(); return; }
            }
            else
            {
                int digit = HexDigit(e.Key);
                if (digit >= 0) { SetHexDigit(digit); Advance(); return; }
            }
        }
        e.Handled = false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_heldNotes.Remove(e.Key, out int channel))
            NoteRelease?.Invoke(channel);
    }

    private void ReleaseHeldNotes()
    {
        foreach (var ch in _heldNotes.Values) NoteRelease?.Invoke(ch);
        _heldNotes.Clear();
    }

    private void Advance() => SetCursor(Math.Min(Rows - 1, CursorRow + EditStep), CursorChannel, CursorColumn);

    // ════════════════════════════════════════════════════════════════
    //  Ändringar
    // ════════════════════════════════════════════════════════════════

    private void Change(Action action)
    {
        BeforeEdit?.Invoke();
        action();
        InvalidateVisual();
        Edited?.Invoke();
    }

    private TrackerCell Cell
    {
        get => Pattern![CursorRow, CursorChannel];
        set => Pattern![CursorRow, CursorChannel] = value;
    }

    private void SetNote(int note) => Change(() =>
    {
        var c = Cell;
        int inst = note >= 0 ? Instrument : c.Instrument;
        Cell = new TrackerCell(note, inst, c.Effect, c.Param);
    });

    private void SetHexDigit(int digit) => Change(() =>
    {
        var c = Cell;
        switch (CursorColumn)
        {
            case ColInstHi: Cell = new TrackerCell(c.Note, digit * 16 + c.Instrument % 16, c.Effect, c.Param); break;
            case ColInstLo: Cell = new TrackerCell(c.Note, c.Instrument / 16 * 16 + digit, c.Effect, c.Param); break;
            case ColParamHi: Cell = new TrackerCell(c.Note, c.Instrument, c.Effect == '\0' ? '0' : c.Effect, digit * 16 + c.Param % 16); break;
            case ColParamLo: Cell = new TrackerCell(c.Note, c.Instrument, c.Effect == '\0' ? '0' : c.Effect, c.Param / 16 * 16 + digit); break;
        }
    });

    private void SetEffectChar(char ch) => Change(() =>
    {
        var c = Cell;
        Cell = new TrackerCell(c.Note, c.Instrument, ch, c.Param);
    });

    private void ClearField() => Change(() =>
    {
        var c = Cell;
        Cell = CursorColumn switch
        {
            ColNote => new TrackerCell(TrackerCell.NoNote, 0, c.Effect, c.Param),
            ColInstHi or ColInstLo => new TrackerCell(c.Note, 0, c.Effect, c.Param),
            _ => new TrackerCell(c.Note, c.Instrument, '\0', 0)
        };
    });

    private void ClearBlock()
    {
        var (r0, r1, c0, c1) = SelectionOrCursor();
        Change(() =>
        {
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                Pattern![r, c] = TrackerCell.Empty;
        });
    }

    private void Copy()
    {
        var (r0, r1, c0, c1) = SelectionOrCursor();
        _clipboard = new TrackerCell[r1 - r0 + 1, c1 - c0 + 1];
        for (int r = r0; r <= r1; r++)
        for (int c = c0; c <= c1; c++)
            _clipboard[r - r0, c - c0] = Pattern![r, c];
    }

    private void Paste()
    {
        var clip = _clipboard;
        if (clip == null) return;
        Change(() =>
        {
            for (int r = 0; r < clip.GetLength(0); r++)
            for (int c = 0; c < clip.GetLength(1); c++)
            {
                int row = CursorRow + r, ch = CursorChannel + c;
                if (row < Rows && ch < TrackerSong.ChannelCount) Pattern![row, ch] = clip[r, c];
            }
        });
        ClearSelection();
    }

    private void SelectAll()
    {
        _anchorRow = 0;
        _anchorChannel = 0;
        CursorRow = Rows - 1;
        CursorChannel = TrackerSong.ChannelCount - 1;
        InvalidateVisual();
    }

    private void Transpose(int semis)
    {
        var (r0, r1, c0, c1) = SelectionOrCursor();
        Change(() =>
        {
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                var cell = Pattern![r, c];
                if (cell.Note < 0) continue;
                int n = Math.Clamp(cell.Note + semis, 0, 119);
                Pattern![r, c] = new TrackerCell(n, cell.Instrument, cell.Effect, cell.Param);
            }
        });
    }

    // Skjuter ned raderna från markören och lämnar en tom rad. Med allChannels i alla kanaler.
    private void InsertRow(bool allChannels) => Change(() =>
    {
        var p = Pattern!;
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
        {
            if (!allChannels && c != CursorChannel) continue;
            for (int r = p.Rows - 1; r > CursorRow; r--) p[r, c] = p[r - 1, c];
            p[CursorRow, c] = TrackerCell.Empty;
        }
    });

    // Tar bort raden vid markören och drar upp raderna under
    private void DeleteRow(bool allChannels) => Change(() =>
    {
        var p = Pattern!;
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
        {
            if (!allChannels && c != CursorChannel) continue;
            for (int r = CursorRow; r < p.Rows - 1; r++) p[r, c] = p[r + 1, c];
            p[p.Rows - 1, c] = TrackerCell.Empty;
        }
    });
}
