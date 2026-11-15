using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AmosLikeBasic;

// The tracker: write music for the synth in patterns with notes and effects, as in ProTracker.
// The song is saved as .amostrk and played in BASIC with TRACK LOAD and TRACK PLAY.
public partial class TrackerWindow : Window
{
    private enum PlayMode { None, Song, Pattern }

    private TrackerSong _song;
    private string? _path;
    private bool _dirty;
    private bool _loading;          // true while the controls are being filled in, so their events do not count as changes
    private bool _forceClose;
    private int _orderPos;
    private PlayMode _playMode;

    // Undo: the whole song as JSON before each change
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private const int MaxUndo = 200;
    private string? _lastUndoKey;
    private DateTime _lastUndoTime;

    private readonly DispatcherTimer _timer;
    private readonly List<Action> _instrumentRefresh = new();
    private TextBox? _tableBox;

    private const string KeyHelp =
        "Z–M, Q–P: notes · 1: note off · Space: edit on/off · F5 song · F6 pattern · F7 from cursor · Esc stop · " +
        "F1/F2 octave · Shift+arrows select · Cmd+C/X/V · Cmd+↑/↓ transpose · Cmd+I / Cmd+Backspace rows · Cmd+Z undo";

    public TrackerWindow()
    {
        InitializeComponent();
        _song = CreateNewSong();

        Pattern.BeforeEdit += () => PushUndo();
        Pattern.Edited += SongChanged;
        Pattern.NotePreview += (ch, note) => TryEngine(() => ChiptuneSynth.TrackPreview(_song, Pattern.Instrument, note, ch));
        Pattern.NoteRelease += ChiptuneSynth.TrackPreviewRelease;
        Pattern.MuteChanged += ApplyMutes;
        Pattern.Command += OnPatternCommand;

        OrderList.SelectionChanged += (_, _) =>
        {
            if (_loading || OrderList.SelectedIndex < 0) return;
            SelectOrder(OrderList.SelectedIndex);
        };
        InstrumentList.SelectionChanged += (_, _) =>
        {
            if (_loading || InstrumentList.SelectedIndex < 0) return;
            Pattern.Instrument = InstrumentList.SelectedIndex + 1;
            RefreshInstrumentPanel();
        };

        TitleBox.TextChanged += (_, _) =>
        {
            // TextChanged can arrive later, so compare with the song
            if (_loading || (TitleBox.Text ?? "") == _song.Title) return;
            PushUndo("title");
            _song.Title = TitleBox.Text ?? "";
            SongChanged();
        };
        SpeedBox.ValueChanged += (_, _) =>
        {
            if (_loading || SpeedBox.Value == null) return;
            PushUndo("speed");
            _song.Speed = (int)SpeedBox.Value;
            SongChanged();
        };
        TempoBox.ValueChanged += (_, _) =>
        {
            if (_loading || TempoBox.Value == null) return;
            PushUndo("tempo");
            _song.Tempo = (int)TempoBox.Value;
            SongChanged();
        };
        OctaveBox.ValueChanged += (_, _) => Pattern.Octave = (int)(OctaveBox.Value ?? 4);
        StepBox.ValueChanged += (_, _) => Pattern.EditStep = (int)(StepBox.Value ?? 1);
        RowsBox.ValueChanged += (_, _) =>
        {
            if (_loading || RowsBox.Value == null) return;
            var pat = _song.Patterns[Pattern.PatternIndex];
            int rows = (int)RowsBox.Value;
            if (rows == pat.Rows) return;
            PushUndo("rows");
            pat.Resize(rows);
            Pattern.SetCursor(Math.Min(Pattern.CursorRow, rows - 1), Pattern.CursorChannel, Pattern.CursorColumn);
            SongChanged();
        };

        BuildInstrumentPanel();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer?.Stop();
            ChiptuneSynth.TrackStop();
            ChiptuneSynth.TrackPreviewStop();
            for (int c = 1; c <= TrackerSong.ChannelCount; c++) ChiptuneSynth.SetTrackMute(c, false);
        };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => UpdatePlayback());
        _timer.Start();

        OctaveBox.Value = 4;
        StepBox.Value = 1;
        LoadSongIntoUi();
        Opened += (_, _) => Pattern.Focus();
    }

    // ════════════════════════════════════════════════════════════════
    //  A new song with some ready-made instruments to start with
    // ════════════════════════════════════════════════════════════════

    private static TrackerSong CreateNewSong()
    {
        var song = new TrackerSong { Title = "New song", Speed = 6, Tempo = 125 };
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Lead", Wave = "Pulse", Volume = 0.22, PulseWidth = 30, PwmDepth = 12, PwmRate = 1.5,
            Attack = 5, Decay = 150, Sustain = 60, Release = 120, Vibrato = 12, VibratoRate = 5
        });
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Bass", Wave = "Sawtooth", Volume = 0.25, Attack = 2, Decay = 0, Sustain = 100, Release = 50,
            Filter = "Low", Cutoff = 300, Resonance = 60, FilterEnvDepth = 1800, FilterEnvAttack = 2, FilterEnvDecay = 150
        });
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Chord", Wave = "Pulse", Volume = 0.1, PulseWidth = 25, Attack = 10, Decay = 400, Sustain = 35, Release = 200,
            Table = new() { "0", "+4", "+7" }, TableLoop = 0
        });
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Kick", Wave = "Triangle", Volume = 0.45, Attack = 0, Decay = 180, Sustain = 0, Release = 30,
            Table = new() { "SQUARE +24", "TRIANGLE +12", "+6", "+2", "-2", "-5", "-8", "-10", "-12" }
        });
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Snare", Wave = "Noise", Volume = 0.3, Attack = 0, Decay = 160, Sustain = 0, Release = 40,
            Table = new() { "NOISE +12", "TRIANGLE -5", "NOISE +10", "+8" }
        });
        song.Instruments.Add(new TrackerInstrument
        {
            Name = "Hihat", Wave = "Noise", Volume = 0.16, Attack = 0, Decay = 40, Sustain = 0, Release = 20,
            Filter = "High", Cutoff = 7000
        });
        return song;
    }

    // ════════════════════════════════════════════════════════════════
    //  Filling in the controls from the song
    // ════════════════════════════════════════════════════════════════

    private void LoadSongIntoUi(bool keepPositions = false)
    {
        _loading = true;
        try
        {
            TitleBox.Text = _song.Title;
            SpeedBox.Value = _song.Speed;
            TempoBox.Value = _song.Tempo;
            Pattern.Song = _song;
            if (!keepPositions)
            {
                _orderPos = 0;
                Pattern.Instrument = _song.Instruments.Count > 0 ? 1 : 0;
            }
            _orderPos = Math.Clamp(_orderPos, 0, _song.Order.Count - 1);
            Pattern.Instrument = Math.Clamp(Pattern.Instrument, _song.Instruments.Count > 0 ? 1 : 0, _song.Instruments.Count);
            RefreshOrderList();
            RefreshInstrumentList();
        }
        finally { _loading = false; }

        SelectOrder(_orderPos, keepPositions);
        RefreshInstrumentPanel();
        UpdateTitle();
        UpdateStatus();
    }

    private void RefreshOrderList()
    {
        bool was = _loading;
        _loading = true;
        OrderList.ItemsSource = _song.Order.Select((p, i) => $"{i:D2}  pattern {p:D2}").ToList();
        OrderList.SelectedIndex = _orderPos;
        _loading = was;
    }

    private void RefreshInstrumentList()
    {
        bool was = _loading;
        _loading = true;
        InstrumentList.ItemsSource = _song.Instruments.Select((inst, i) => $"{i + 1:X2} {inst.Name}").ToList();
        InstrumentList.SelectedIndex = Pattern.Instrument - 1;
        _loading = was;
    }

    private void SelectOrder(int pos, bool keepRow = false)
    {
        _orderPos = Math.Clamp(pos, 0, _song.Order.Count - 1);
        int pattern = _song.Order[_orderPos];
        Pattern.PatternIndex = pattern;
        int rows = _song.Patterns[pattern].Rows;
        Pattern.SetCursor(keepRow ? Math.Min(Pattern.CursorRow, rows - 1) : 0, Pattern.CursorChannel, Pattern.CursorColumn);

        bool was = _loading;
        _loading = true;
        RowsBox.Value = rows;
        if (OrderList.SelectedIndex != _orderPos) OrderList.SelectedIndex = _orderPos;
        _loading = was;

        // If a pattern is playing while switching, play the new one instead
        if (_playMode == PlayMode.Pattern) TryEngine(() => ChiptuneSynth.TrackPlayPattern(_song, pattern, 0));
    }

    // ════════════════════════════════════════════════════════════════
    //  Instrumentpanelen
    // ════════════════════════════════════════════════════════════════

    private TrackerInstrument? CurrentInstrument =>
        Pattern.Instrument >= 1 && Pattern.Instrument <= _song.Instruments.Count ? _song.Instruments[Pattern.Instrument - 1] : null;

    private void BuildInstrumentPanel()
    {
        var panel = InstrumentPanel;

        var name = new TextBox();
        name.TextChanged += (_, _) =>
        {
            if ((name.Text ?? "") != (CurrentInstrument?.Name ?? ""))
                EditInstrument("name", i => i.Name = name.Text ?? "", refreshList: true);
        };
        AddRow(panel, "Name", name);
        _instrumentRefresh.Add(() => name.Text = CurrentInstrument?.Name ?? "");

        AddCombo(panel, "Wave", new[] { "Pulse", "Square", "Triangle", "Sawtooth", "Sine", "Noise" },
            i => i.Wave, (i, v) => i.Wave = v);
        AddNumber(panel, "Volume", 0, 1, 0.01, "0.00", i => i.Volume, (i, v) => i.Volume = v);

        AddHeading(panel, "PULSE");
        AddNumber(panel, "Width %", 1, 99, 1, "0", i => i.PulseWidth, (i, v) => i.PulseWidth = v);
        AddNumber(panel, "PWM depth %", 0, 49, 1, "0", i => i.PwmDepth, (i, v) => i.PwmDepth = v);
        AddNumber(panel, "PWM rate Hz", 0, 20, 0.1, "0.0", i => i.PwmRate, (i, v) => i.PwmRate = v);

        AddHeading(panel, "ENVELOPE");
        AddNumber(panel, "Attack ms", 0, 5000, 5, "0", i => i.Attack, (i, v) => i.Attack = v);
        AddNumber(panel, "Decay ms", 0, 5000, 10, "0", i => i.Decay, (i, v) => i.Decay = v);
        AddNumber(panel, "Sustain %", 0, 100, 5, "0", i => i.Sustain, (i, v) => i.Sustain = v);
        AddNumber(panel, "Release ms", 0, 5000, 10, "0", i => i.Release, (i, v) => i.Release = v);

        AddHeading(panel, "FILTER");
        AddCombo(panel, "Type", new[] { "Off", "Low", "High", "Band" }, i => i.Filter, (i, v) => i.Filter = v);
        AddNumber(panel, "Cutoff Hz", 20, 18000, 50, "0", i => i.Cutoff, (i, v) => i.Cutoff = v);
        AddNumber(panel, "Resonance %", 0, 100, 5, "0", i => i.Resonance, (i, v) => i.Resonance = v);
        AddNumber(panel, "Env depth Hz", -18000, 18000, 100, "0", i => i.FilterEnvDepth, (i, v) => i.FilterEnvDepth = v);
        AddNumber(panel, "Env attack ms", 0, 5000, 5, "0", i => i.FilterEnvAttack, (i, v) => i.FilterEnvAttack = v);
        AddNumber(panel, "Env decay ms", 1, 5000, 10, "0", i => i.FilterEnvDecay, (i, v) => i.FilterEnvDecay = v);

        AddHeading(panel, "PITCH");
        AddNumber(panel, "Vibrato cents", 0, 1200, 5, "0", i => i.Vibrato, (i, v) => i.Vibrato = v);
        AddNumber(panel, "Vibrato Hz", 0, 30, 0.5, "0.0", i => i.VibratoRate, (i, v) => i.VibratoRate = v);
        AddNumber(panel, "Ring mod ch", 0, 8, 1, "0", i => i.Ring, (i, v) => i.Ring = (int)v);
        AddNumber(panel, "Sync ch", 0, 8, 1, "0", i => i.Sync, (i, v) => i.Sync = (int)v);

        AddHeading(panel, "TABLE (one step per tick)");
        var table = new TextBox
        {
            AcceptsReturn = true,
            Height = 130,
            FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"),
            Watermark = "NOISE +24\nPULSE 0\n-12\nP30"
        };
        ToolTip.SetTip(table, "Each line is one tick: a wave (PULSE, SQUARE, TRIANGLE, SAWTOOTH, SINE, NOISE), " +
                              "semitones from the note (+12, -5) and/or pulse width (P30)");
        _tableBox = table;
        table.TextChanged += (_, _) =>
        {
            var steps = (table.Text ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (CurrentInstrument != null && !steps.SequenceEqual(CurrentInstrument.Table))
                EditInstrument("table", i => i.Table = steps);
        };
        panel.Children.Add(table);
        _instrumentRefresh.Add(() => table.Text = string.Join("\n", CurrentInstrument?.Table ?? new List<string>()));
        AddNumber(panel, "Loop from step", -1, 255, 1, "0", i => i.TableLoop, (i, v) => i.TableLoop = (int)v);
    }

    private void AddHeading(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, Classes = { "amos-title" }, Margin = new Thickness(0, 8, 0, 0) });

    private void AddRow(StackPanel panel, string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        grid.Children.Add(new TextBlock { Text = label, Classes = { "amos-label" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        panel.Children.Add(grid);
    }

    private void AddNumber(StackPanel panel, string label, double min, double max, double inc, string format,
        Func<TrackerInstrument, double> get, Action<TrackerInstrument, double> set)
    {
        var box = new NumericUpDown
        {
            Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)inc, FormatString = format
        };
        box.ValueChanged += (_, _) =>
        {
            if (box.Value is decimal d) EditInstrument(label, i => set(i, (double)d));
        };
        AddRow(panel, label, box);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            box.IsEnabled = inst != null;
            box.Value = inst == null ? null : (decimal)Math.Clamp(get(inst), min, max);
        });
    }

    private void AddCombo(StackPanel panel, string label, string[] items,
        Func<TrackerInstrument, string> get, Action<TrackerInstrument, string> set)
    {
        var combo = new ComboBox { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s) EditInstrument(label, i => set(i, s));
        };
        AddRow(panel, label, combo);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            combo.IsEnabled = inst != null;
            if (inst == null) { combo.SelectedIndex = -1; return; }
            string value = get(inst);
            int idx = Array.FindIndex(items, s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 && value.Equals("Saw", StringComparison.OrdinalIgnoreCase)) idx = Array.IndexOf(items, "Sawtooth");
            combo.SelectedIndex = idx < 0 ? 0 : idx;
        });
    }

    private void RefreshInstrumentPanel()
    {
        bool was = _loading;
        _loading = true;
        foreach (var r in _instrumentRefresh) r();
        _loading = was;
        ValidateTable();
    }

    private void EditInstrument(string key, Action<TrackerInstrument> change, bool refreshList = false)
    {
        if (_loading) return;
        var inst = CurrentInstrument;
        if (inst == null) return;
        PushUndo("inst:" + Pattern.Instrument + ":" + key);
        change(inst);
        if (refreshList) RefreshInstrumentList();
        if (key == "table") ValidateTable();
        SongChanged();
    }

    // Red frame around the table if a step is wrong
    private void ValidateTable()
    {
        if (_tableBox == null) return;
        var inst = CurrentInstrument;
        string? error = null;
        if (inst != null)
        {
            try { ChiptuneSynth.ValidateInstrument(inst, Pattern.Instrument); }
            catch (FormatException e) { error = e.Message; }
        }
        _tableBox.BorderBrush = error == null ? null : Brushes.Red;
        if (error != null) SetStatus(error);
    }

    // ════════════════════════════════════════════════════════════════
    //  Changes, undo
    // ════════════════════════════════════════════════════════════════

    // Saves the song before a change. Changes with the same key close after each other
    // (like holding the arrow of a number box) become a single undo step.
    private void PushUndo(string? key = null)
    {
        var now = DateTime.UtcNow;
        if (key != null && key == _lastUndoKey && (now - _lastUndoTime).TotalSeconds < 1.5)
        {
            _lastUndoTime = now;
            return;
        }
        _lastUndoKey = key;
        _lastUndoTime = now;
        _undo.Add(_song.ToJson());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Undo() => Restore(_undo, _redo);
    private void Redo() => Restore(_redo, _undo);

    private void Restore(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        to.Add(_song.ToJson());
        string json = from[^1];
        from.RemoveAt(from.Count - 1);
        _song = TrackerSong.FromJson(json);
        _lastUndoKey = null;
        int row = Pattern.CursorRow;
        LoadSongIntoUi(keepPositions: true);
        Pattern.SetCursor(Math.Min(row, _song.Patterns[Pattern.PatternIndex].Rows - 1), Pattern.CursorChannel, Pattern.CursorColumn);
        SongChanged();
    }

    private void SongChanged()
    {
        _dirty = true;
        UpdateTitle();
        Pattern.InvalidateVisual();
        if (_playMode == PlayMode.Song) TryEngine(() => ChiptuneSynth.TrackUpdate(_song));
        else if (_playMode == PlayMode.Pattern) TryEngine(() => ChiptuneSynth.TrackUpdate(_song, Pattern.PatternIndex));
    }

    // The song cannot be played if an instrument has a bad table step
    private bool TryEngine(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (FormatException e)
        {
            SetStatus(e.Message);
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Uppspelning
    // ════════════════════════════════════════════════════════════════

    private void ApplyMutes()
    {
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
            ChiptuneSynth.SetTrackMute(c + 1, Pattern.Muted[c]);
    }

    private void PlaySong()
    {
        ApplyMutes();
        if (TryEngine(() => ChiptuneSynth.TrackPlay(_song, _orderPos, 0, loop: true)))
            _playMode = PlayMode.Song;
        Pattern.Focus();
    }

    private void PlayPattern(int row)
    {
        ApplyMutes();
        if (TryEngine(() => ChiptuneSynth.TrackPlayPattern(_song, Pattern.PatternIndex, row)))
            _playMode = PlayMode.Pattern;
        Pattern.Focus();
    }

    private void Stop()
    {
        ChiptuneSynth.TrackStop();
        _playMode = PlayMode.None;
        Pattern.PlayRow = -1;
        Pattern.InvalidateVisual();
    }

    private void UpdatePlayback()
    {
        if (_playMode == PlayMode.None) return;
        if (!ChiptuneSynth.IsTrackPlaying())
        {
            _playMode = PlayMode.None;
            Pattern.PlayRow = -1;
            Pattern.InvalidateVisual();
            return;
        }

        int row = ChiptuneSynth.TrackRow;
        bool follow = FollowCheck.IsChecked == true;
        if (_playMode == PlayMode.Song)
        {
            int pos = ChiptuneSynth.TrackPosition;
            if (follow && pos != _orderPos && pos < _song.Order.Count) SelectOrder(pos);
            bool showing = pos < _song.Order.Count && _song.Order[pos] == Pattern.PatternIndex;
            Pattern.PlayRow = showing ? row : -1;
        }
        else
            Pattern.PlayRow = row;

        if (follow && Pattern.PlayRow >= 0) Pattern.FollowRow(row);
        Pattern.InvalidateVisual();
    }

    private void OnPatternCommand(string cmd)
    {
        switch (cmd)
        {
            case "play-song": PlaySong(); break;
            case "play-pattern": PlayPattern(0); break;
            case "play-cursor": PlayPattern(Pattern.CursorRow); break;
            case "stop": Stop(); break;
            case "toggle-edit":
                if (_playMode != PlayMode.None) Stop();
                else SetEditMode(!Pattern.EditMode);
                break;
            case "octave":
                OctaveBox.Value = Pattern.Octave;
                break;
            case "undo": Undo(); break;
            case "redo": Redo(); break;
            case "save": _ = SaveAsync(false); break;
        }
    }

    private void SetEditMode(bool on)
    {
        Pattern.EditMode = on;
        EditToggle.IsChecked = on;
        Pattern.InvalidateVisual();
    }

    // Keys that should work even when the focus is in the lists
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Source is TextBox && !(cmd && e.Key == Key.S)) return;
        switch (e.Key)
        {
            case Key.F5: PlaySong(); break;
            case Key.F6: PlayPattern(0); break;
            case Key.F7: PlayPattern(Pattern.CursorRow); break;
            case Key.Escape: Stop(); break;
            case Key.S when cmd: _ = SaveAsync(false); break;
            case Key.Z when cmd: if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void PlaySong_OnClick(object? sender, RoutedEventArgs e) => PlaySong();
    private void PlayPattern_OnClick(object? sender, RoutedEventArgs e) => PlayPattern(0);
    private void Stop_OnClick(object? sender, RoutedEventArgs e) { Stop(); Pattern.Focus(); }
    private void EditToggle_OnClick(object? sender, RoutedEventArgs e) { SetEditMode(EditToggle.IsChecked == true); Pattern.Focus(); }

    // ════════════════════════════════════════════════════════════════
    //  The order list and patterns
    // ════════════════════════════════════════════════════════════════

    private void OrderInsert_OnClick(object? sender, RoutedEventArgs e)
    {
        PushUndo();
        _song.Order.Insert(_orderPos + 1, _song.Order[_orderPos]);
        _orderPos++;
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
    }

    private void OrderDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_song.Order.Count <= 1) return;
        PushUndo();
        _song.Order.RemoveAt(_orderPos);
        _orderPos = Math.Min(_orderPos, _song.Order.Count - 1);
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
    }

    private void OrderPatternDown_OnClick(object? sender, RoutedEventArgs e) => ChangeOrderPattern(-1);
    private void OrderPatternUp_OnClick(object? sender, RoutedEventArgs e) => ChangeOrderPattern(+1);

    private void ChangeOrderPattern(int delta)
    {
        int p = _song.Order[_orderPos] + delta;
        if (p < 0) return;
        PushUndo();
        if (p >= _song.Patterns.Count)
        {
            // After the last pattern: make a new empty one with the same number of rows
            p = _song.Patterns.Count;
            _song.Patterns.Add(new TrackerPattern(_song.Patterns[Pattern.PatternIndex].Rows));
        }
        _song.Order[_orderPos] = p;
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
        Pattern.Focus();
    }

    private void ClonePattern_OnClick(object? sender, RoutedEventArgs e)
    {
        PushUndo();
        var src = _song.Patterns[Pattern.PatternIndex];
        var copy = new TrackerPattern(src.Rows);
        for (int r = 0; r < src.Rows; r++)
        for (int c = 0; c < TrackerSong.ChannelCount; c++)
            copy[r, c] = src[r, c];
        _song.Patterns.Add(copy);
        _song.Order[_orderPos] = _song.Patterns.Count - 1;
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
        SetStatus($"Copied to pattern {_song.Patterns.Count - 1:D2}");
        Pattern.Focus();
    }

    // ════════════════════════════════════════════════════════════════
    //  Instrument
    // ════════════════════════════════════════════════════════════════

    private void InstAdd_OnClick(object? sender, RoutedEventArgs e) =>
        AddInstrument(new TrackerInstrument { Name = "Instrument " + (_song.Instruments.Count + 1) });

    private void InstClone_OnClick(object? sender, RoutedEventArgs e)
    {
        var inst = CurrentInstrument;
        if (inst == null) return;
        // A copy via JSON, so that lists are not shared
        var json = System.Text.Json.JsonSerializer.Serialize(inst);
        var copy = System.Text.Json.JsonSerializer.Deserialize<TrackerInstrument>(json)!;
        copy.Name = inst.Name + " copy";
        AddInstrument(copy);
    }

    private void AddInstrument(TrackerInstrument inst)
    {
        if (_song.Instruments.Count >= 255) return;
        PushUndo();
        _song.Instruments.Add(inst);
        Pattern.Instrument = _song.Instruments.Count;
        RefreshInstrumentList();
        RefreshInstrumentPanel();
        SongChanged();
    }

    // Removes the instrument and renumbers the ones after it in all patterns
    private void InstDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        int n = Pattern.Instrument;
        if (n < 1 || n > _song.Instruments.Count) return;
        PushUndo();
        _song.Instruments.RemoveAt(n - 1);
        foreach (var pat in _song.Patterns)
            for (int r = 0; r < pat.Rows; r++)
            for (int c = 0; c < TrackerSong.ChannelCount; c++)
            {
                var cell = pat[r, c];
                if (cell.Instrument < n) continue;
                int inst = cell.Instrument == n ? 0 : cell.Instrument - 1;
                pat[r, c] = new TrackerCell(cell.Note, inst, cell.Effect, cell.Param);
            }
        Pattern.Instrument = Math.Min(n, _song.Instruments.Count);
        RefreshInstrumentList();
        RefreshInstrumentPanel();
        SongChanged();
    }

    // ════════════════════════════════════════════════════════════════
    //  Filer
    // ════════════════════════════════════════════════════════════════

    private static readonly FilePickerFileType SongFileType = new("AMOS tracker song") { Patterns = new[] { "*.amostrk" } };

    private async Task<IStorageFolder?> StartFolderAsync()
    {
        string dir = _path != null ? Path.GetDirectoryName(_path)! : Path.Combine(Environment.CurrentDirectory, "Sound", "Tracker");
        if (!Directory.Exists(dir)) dir = Environment.CurrentDirectory;
        return await StorageProvider.TryGetFolderFromPathAsync(dir);
    }

    private async void New_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        Stop();
        _song = CreateNewSong();
        _path = null;
        _undo.Clear();
        _redo.Clear();
        LoadSongIntoUi();
        _dirty = false;
        UpdateTitle();
        Pattern.Focus();
    }

    private async void Load_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load song",
            FileTypeFilter = new[] { SongFileType },
            SuggestedStartLocation = await StartFolderAsync()
        });
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (path != null) LoadFile(path);
    }

    public void LoadFile(string path)
    {
        try
        {
            var song = TrackerSong.Load(path);
            Stop();
            _song = song;
            _path = path;
            _undo.Clear();
            _redo.Clear();
            LoadSongIntoUi();
            _dirty = false;
            UpdateTitle();
            UpdateStatus();
            Pattern.Focus();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not load: " + ex.Message);
        }
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task<bool> SaveAsync(bool askForName)
    {
        string? path = _path;
        if (path == null || askForName)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save song",
                FileTypeChoices = new[] { SongFileType },
                DefaultExtension = "amostrk",
                SuggestedFileName = SafeFileName(_song.Title) + ".amostrk",
                SuggestedStartLocation = await StartFolderAsync()
            });
            path = file?.TryGetLocalPath();
            if (path == null) return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _song.Save(path);
            _path = path;
            _dirty = false;
            UpdateTitle();
            UpdateStatus();
            SetStatus("Saved. Play it in BASIC with " + LoadLine());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save: " + ex.Message);
            return false;
        }
    }

    private static string SafeFileName(string title)
    {
        var name = new string((title ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();
        return name.Length == 0 ? "song" : name.ToLowerInvariant().Replace(' ', '_');
    }

    // The line that loads the song in BASIC. The path counts from the project folder, as for images and sounds.
    private string LoadLine()
    {
        if (_path == null) return "";
        string rel = Path.GetRelativePath(Environment.CurrentDirectory, _path);
        if (rel.StartsWith("..")) rel = _path;
        return $"TRACK LOAD \"{rel.Replace('\\', '/')}\"";
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        return await AskAsync("Unsaved changes", "The song has unsaved changes. Throw them away?", "Yes", "No");
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || !_dirty) return;
        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _forceClose = true;
            Close();
        }
    }

    private async Task<bool> AskAsync(string title, string message, string yes, string no)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var tcs = new TaskCompletionSource<bool>();
        var yesButton = new Button { Content = yes, Width = 80, Margin = new Thickness(5) };
        var noButton = new Button { Content = no, Width = 80, Margin = new Thickness(5) };
        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { yesButton, noButton } }
            }
        };
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    // ════════════════════════════════════════════════════════════════
    //  Title and status bar
    // ════════════════════════════════════════════════════════════════

    private void UpdateTitle()
    {
        string name = _path != null ? Path.GetFileName(_path) : "untitled";
        Title = $"Tracker - {name}{(_dirty ? " *" : "")}";
    }

    private string? _message;
    private DateTime _messageTime;

    private void SetStatus(string message)
    {
        _message = message;
        _messageTime = DateTime.UtcNow;
        UpdateStatus();
        // The message stays for a while, then the keys are shown again
        DispatcherTimer.RunOnce(UpdateStatus, TimeSpan.FromSeconds(6.1));
    }

    private void UpdateStatus()
    {
        if (_message != null && (DateTime.UtcNow - _messageTime).TotalSeconds < 6)
        {
            StatusText.Text = _message;
            return;
        }
        _message = null;
        StatusText.Text = (_path != null ? LoadLine() + "    " : "") + KeyHelp;
    }
}
