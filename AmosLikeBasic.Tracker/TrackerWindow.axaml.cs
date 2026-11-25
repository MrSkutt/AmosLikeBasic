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
using Avalonia.VisualTree;

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
    private readonly List<InstrumentGraph> _graphs = new();
    private TextBox? _tableBox;
    private TextBlock? _instrumentHeading;
    private readonly Dictionary<Key, int> _pageNotes = new();   // keys held on the instrument page → channel

    private const string KeyHelp =
        "Z–M, Q–P: notes · 1: note off · Space: edit on/off · F5 song · F6 pattern · F7 from cursor · Esc stop · " +
        "F1/F2 octave · F3 pattern · F4 instrument · Shift+arrows select · Cmd+C/X/V · Cmd+↑/↓ transpose · Cmd+I / Cmd+Backspace rows · Cmd+Z undo";

    public TrackerWindow()
    {
        InitializeComponent();
        _song = CreateNewSong();

        Scopes.Pattern = Pattern;
        Pattern.BeforeEdit += () => PushUndo();
        Pattern.Edited += SongChanged;
        Pattern.NotePreview += (ch, note) => TryEngine(() => ChiptuneSynth.TrackPreview(_song, Pattern.Instrument, note, ch));
        Pattern.NoteRelease += ChiptuneSynth.TrackPreviewRelease;
        Pattern.MuteChanged += ApplyMutes;
        Pattern.CursorMoved += FollowDhsChannel;
        Pattern.Command += OnPatternCommand;

        OrderList.SelectionChanged += (_, _) =>
        {
            if (_loading || OrderList.SelectedIndex < 0) return;
            SelectOrder(OrderList.SelectedIndex);
            // While the song plays, a click on a position goes on playing from there
            if (_playMode == PlayMode.Song) TryEngine(() => ChiptuneSynth.TrackPlay(_song, _orderPos, 0, loop: true));
            KeepTypingFocus();
        };
        InstrumentList.SelectionChanged += (_, _) =>
        {
            if (_loading || InstrumentList.SelectedIndex < 0) return;
            if (!DhsAllows(Pattern.CursorChannel, InstrumentList.SelectedIndex + 1))
            {
                SetStatus(Pattern.CursorChannel == 3
                    ? "Channel 4 is the DMA: it plays samples (instruments from 21, Instrument → Add)."
                    : "Channels 1 – 3 are the YM: instruments 01 – 20. The samples (from 21) are for channel 4.");
                RefreshInstrumentList();
                KeepTypingFocus();
                return;
            }
            Pattern.Instrument = InstrumentList.SelectedIndex + 1;
            RefreshInstrumentPanel();
            KeepTypingFocus();
        };

        InstrumentList.DoubleTapped += (_, _) => ShowInstrumentPage(true);
        InstrumentKeys.PointerPressed += (_, _) => InstrumentKeys.Focus();
        InstrumentKeys.KeyDown += InstrumentKeys_OnKeyDown;
        InstrumentKeys.KeyUp += InstrumentKeys_OnKeyUp;
        InstrumentKeys.LostFocus += (_, _) => ReleasePageNotes();

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
        ChannelsBox.ValueChanged += async (_, e) =>
        {
            if (_loading || ChannelsBox.Value == null) return;
            int channels = (int)ChannelsBox.Value;
            if (channels == _song.Channels) return;
            // maxYMiser has its three tracks and the DMA track: more is the tracker's own song
            if ((_song.Ym != null && channels > 4 && !await LeaveYmModeAsync("more than four tracks"))
                || !await KeepModeAsync(channels, _song.Instruments.Count))
            {
                _loading = true;
                ChannelsBox.Value = _song.Channels;
                _loading = false;
                return;
            }
            if (channels < _song.Channels && !await ConfirmFewerChannelsAsync(channels))
            {
                _loading = true;
                ChannelsBox.Value = _song.Channels;
                _loading = false;
                return;
            }
            PushUndo("channels");
            _song.Channels = channels;
            Pattern.ChannelsChanged();
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

        NumberBox(SpeedBox, () => _song.Speed);
        NumberBox(TempoBox, () => _song.Tempo);
        NumberBox(OctaveBox, () => Pattern.Octave);
        NumberBox(StepBox, () => Pattern.EditStep);
        NumberBox(ChannelsBox, () => _song.Channels);
        NumberBox(RowsBox, () => _song.Patterns[Pattern.PatternIndex].Rows);

        BuildMenu();
        BuildInstrumentPanel();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer?.Stop();
            ChiptuneSynth.TrackStop();
            ChiptuneSynth.TrackPreviewStop();
            for (int c = 1; c <= TrackerSong.MaxChannels; c++) ChiptuneSynth.SetTrackMute(c, false);
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
            UpdateYmMode();
            TitleBox.Text = _song.Title;
            SpeedBox.Value = _song.Speed;
            TempoBox.Value = _song.Tempo;
            ChannelsBox.Value = _song.Channels;
            Pattern.Song = _song;
            Pattern.ChannelsChanged();
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
        CheckDhs();   // a DHS song as it comes is what later changes are checked against
    }

    private void RefreshOrderList()
    {
        bool was = _loading;
        _loading = true;
        // "+": a channel plays a pattern of its own there
        OrderList.ItemsSource = _song.Order.Select((p, i) =>
            $"{i:D2}  pattern {p:D2}{(Enumerable.Range(0, _song.Channels).Any(c => _song.PatternAt(i, c) != p) ? " +" : "")}").ToList();
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
        Pattern.Position = _orderPos;
        int rows = _song.Patterns[pattern].Rows;
        Pattern.SetCursor(keepRow ? Math.Min(Pattern.CursorRow, rows - 1) : 0, Pattern.CursorChannel, Pattern.CursorColumn);

        bool was = _loading;
        _loading = true;
        RowsBox.Value = rows;
        if (OrderList.SelectedIndex != _orderPos) OrderList.SelectedIndex = _orderPos;
        _loading = was;

        // If a pattern is playing while switching, play the new one instead
        if (_playMode == PlayMode.Pattern) TryEngine(() => ChiptuneSynth.TrackPlayPattern(_song, pattern, 0, PositionToPlay));
    }

    // When channels have orders of their own, what the shown place plays (else the pattern itself)
    private int? PositionToPlay => _song.HasChannelOrder ? _orderPos : null;

    // Alt+arrows: the channel under the cursor plays the next or the previous pattern at this place;
    // Alt+N: a new pattern of its own, a copy of the column it plays
    private void ChangeChannelPattern(int delta)
    {
        int c = Pattern.CursorChannel;
        int now = _song.PatternAt(_orderPos, c);
        int next = now + delta;
        if (next < 0 || next >= _song.Patterns.Count) return;
        PushUndo();
        _song.SetPatternAt(_orderPos, c, next);
        ChannelPatternChanged(c);
    }

    private void NewChannelPattern()
    {
        int c = Pattern.CursorChannel;
        var from = _song.Patterns[_song.PatternAt(_orderPos, c)];
        int rows = _song.Patterns[_song.Order[_orderPos]].Rows;
        PushUndo();
        var copy = new TrackerPattern(rows);
        for (int r = 0; r < rows && r < from.Rows; r++) copy[r, c] = from[r, c];
        _song.Patterns.Add(copy);
        _song.SetPatternAt(_orderPos, c, _song.Patterns.Count - 1);
        ChannelPatternChanged(c);
    }

    private void ChannelPatternChanged(int c)
    {
        RefreshOrderList();
        SelectOrder(_orderPos, keepRow: true);
        SongChanged();
        int p = _song.PatternAt(_orderPos, c);
        SetStatus(p == _song.Order[_orderPos]
            ? $"Channel {c + 1} plays this place's pattern {p:D2}."
            : $"Channel {c + 1} plays pattern {p:D2} here (the place's own is {_song.Order[_orderPos]:D2}).");
        KeepTypingFocus();
    }

    // ════════════════════════════════════════════════════════════════
    //  Instrumentpanelen
    // ════════════════════════════════════════════════════════════════

    private TrackerInstrument? CurrentInstrument =>
        Pattern.Instrument >= 1 && Pattern.Instrument <= _song.Instruments.Count ? _song.Instruments[Pattern.Instrument - 1] : null;

    // The instrument page: cards for the sound, the envelope, the filter, the pitch and the table,
    // with pictures of what the settings do. The cards wrap onto more rows in a narrow window.
    private void BuildInstrumentPanel()
    {
        var panel = InstrumentPanel;
        _instrumentHeading = new TextBlock { Classes = { "amos-title" }, TextWrapping = TextWrapping.Wrap };
        _instrumentRefresh.Add(() => _instrumentHeading.Text = CurrentInstrument == null
            ? "No instrument. Make one with ADD."
            : $"INSTRUMENT {Pattern.Instrument:X2}: {CurrentInstrument.Name}");
        panel.Children.Add(_instrumentHeading);
        panel.Children.Add(new TextBlock
        {
            Text = "Play the instrument with the note keys (Z–M, Q–P) while no box is selected. Enter (Esc in the table) " +
                   "leaves a box and goes back to playing. F3 goes back to the pattern.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap
        });

        // The sound as it is right now, on the channel the piano keys play
        panel.Children.Add(new InstrumentScope
        {
            Channel = () => ChiptuneSynth.ChannelWithInstrument(Pattern.Instrument, Pattern.CursorChannel + 1),
            Instrument = () => CurrentInstrument == null ? "" : $"{Pattern.Instrument:X2} {CurrentInstrument.Name}".ToUpperInvariant(),
            Margin = new Thickness(0, 0, 10, 0)
        });

        var cards = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        // In a song in YM mode, an instrument with a YM part is edited as the chip plays it
        var ymPanel = BuildYmPanel();
        panel.Children.Add(ymPanel);
        panel.Children.Add(cards);
        _instrumentRefresh.Add(() =>
        {
            bool ym = _song.Ym != null && CurrentInstrument?.Ym != null;
            ymPanel.IsVisible = ym;
            cards.IsVisible = !ym;
        });

        // Enter in a box (Esc in the table, where Enter makes a new line) goes back to playing notes.
        // handledEventsToo: the number box has used Enter to take the typed value by then.
        panel.AddHandler(KeyDownEvent, (_, e) =>
        {
            bool inTable = e.Source is Visual v && _tableBox != null && (v == _tableBox || _tableBox.IsVisualAncestorOf(v));
            if (e.Key == Key.Escape || (e.Key == Key.Enter && !inTable))
            {
                e.Handled = true;
                Dispatcher.UIThread.Post(() => InstrumentKeys.Focus(), DispatcherPriority.Input);
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        // The sections follow the sound: the oscillators make it, the filter shapes it, the amp
        // envelope gives it its volume over time, the effects come last, and the modulation moves things
        // ── OSCILLATORS ──
        var osc = Card(cards, "OSCILLATORS", 400);
        var name = new TextBox();
        name.TextChanged += (_, _) =>
        {
            if ((name.Text ?? "") != (CurrentInstrument?.Name ?? ""))
                EditInstrument("name", i => i.Name = name.Text ?? "", refreshList: true);
        };
        AddRow(osc, "Name", name);
        _instrumentRefresh.Add(() => name.Text = CurrentInstrument?.Name ?? "");
        AddGraph(osc, new WaveGraph { Mixed = true });
        AddSubHeading(osc, "OSC 1");
        AddChoice(osc, new[] { "Pulse", "Square", "Triangle", "Sawtooth", "Sine", "Noise", "Sample" },
            new[] { "PULSE", "SQUARE", "TRI", "SAW", "SINE", "NOISE", "SAMPLE" }, i => i.Wave, (i, v) => i.Wave = v, "wave");
        // The SID's combined waves (the waves ANDed: thin, nasal, buzzing)
        AddChoice(osc, new[] { "PulseSaw", "PulseTri", "SawTri", "PulseSawTri" },
            new[] { "P+SAW", "P+TRI", "SAW+TRI", "P+S+T" }, i => i.Wave, (i, v) => i.Wave = v, "wave");
        var o1 = KnobRow(osc);
        AddKnob(o1, "VOLUME", 0, 1, i => i.Volume, (i, v) => i.Volume = v, "volume", "0.00", step: 0.01);
        AddKnob(o1, "WIDTH", 1, 99, i => i.PulseWidth, (i, v) => i.PulseWidth = v, "width", unit: "%", step: 1);
        AddKnob(o1, "PWM", 0, 49, i => i.PwmDepth, (i, v) => i.PwmDepth = v, "pwm", unit: "%", step: 1);
        AddKnob(o1, "PWM RATE", 0, 20, i => i.PwmRate, (i, v) => i.PwmRate = v, "pwm rate", "0.0", "Hz", step: 0.1);
        AddSubHeading(osc, "OSC 2");
        AddChoice(osc, new[] { "Off", "Pulse", "Square", "Triangle", "Sawtooth", "Sine", "Noise" },
            new[] { "OFF", "PULSE", "SQUARE", "TRI", "SAW", "SINE", "NOISE" }, i => i.Osc2Wave ?? "Off", (i, v) => i.Osc2Wave = v, "osc2 wave");
        // MIX: heard with oscillator 1; RING and SYNC: it changes oscillator 1 (as the SID's ring
        // modulation and hard sync) and is not heard itself, so its wave does not matter
        AddChoice(osc, new[] { "Mix", "Ring", "Sync" }, new[] { "MIX", "RING", "SYNC" },
            i => i.Osc2Mode ?? "Mix", (i, v) => i.Osc2Mode = v, "osc2 mode");
        var o2 = KnobRow(osc);
        AddKnob(o2, "SEMI", -24, 24, i => i.Osc2Semitones, (i, v) => i.Osc2Semitones = v, "osc2 semi", "+0;-0;0", bipolar: true, step: 1);
        AddKnob(o2, "DETUNE", -100, 100, i => i.Osc2Detune, (i, v) => i.Osc2Detune = v, "osc2 detune", "+0;-0;0", "c", bipolar: true, step: 1);
        AddKnob(o2, "MIX", 0, 100, i => i.Osc2Mix, (i, v) => i.Osc2Mix = v, "osc2 mix", unit: "%", step: 1);

        // ── SAMPLE: a WAV file instead of oscillator 1 ──
        var sampleCard = Card(cards, "SAMPLE", 400);
        AddGraph(sampleCard, new SampleGraph());
        var sampleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var sampleName = new TextBlock { Classes = { "amos-label" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var loadSample = new Button { Content = "Load sample...", Focusable = false };
        ToolTip.SetTip(loadSample, "A WAV file (8, 16, 24 or 32-bit, mono or stereo); OSC 1 becomes SAMPLE");
        loadSample.Click += async (_, _) => await LoadSampleAsync();
        sampleRow.Children.Add(sampleName);
        Grid.SetColumn(loadSample, 1);
        sampleRow.Children.Add(loadSample);
        sampleCard.Children.Add(sampleRow);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            loadSample.IsEnabled = inst != null;
            sampleName.Text = inst == null || string.IsNullOrEmpty(inst.Sample) ? "No sample" : Path.GetFileName(inst.Sample);
        });
        var s1 = KnobRow(sampleCard);
        AddKnob(s1, "NOTE", 0, 119, i => i.SampleNote, (i, v) => i.SampleNote = (int)v, "sample note", step: 1);
        ((Knob)((WrapPanel)s1.Children[0]).Children[^1]).ValueText = NoteName;
        ToolTip.SetTip(((WrapPanel)s1.Children[0]).Children[^1], "The note the sample sounds at as it is recorded");
        AddKnob(s1, "LOOP START", 0, 100, i => LoopPercent(i, i.LoopStart), (i, v) => i.LoopStart = LoopFrame(i, v), "loop start", "0.0", "%", step: 0.1);
        AddKnob(s1, "LOOP END", 0, 100, i => LoopPercent(i, i.LoopEnd), (i, v) => i.LoopEnd = LoopFrame(i, v), "loop end", "0.0", "%", step: 0.1);
        var sampleHelp = new TextBlock
        {
            Text = "A loop keeps a held note sounding; LOOP END at 0 plays the sample once. Effect 9xx starts it xx × 256 frames in.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap, FontSize = 11
        };
        sampleCard.Children.Add(sampleHelp);

        // A DHS song's sample (channel 4, the STe's DMA): only this card, with the instrument's name
        var dhsName = new TextBox();
        dhsName.TextChanged += (_, _) =>
        {
            if ((dhsName.Text ?? "") != (CurrentInstrument?.Name ?? ""))
                EditInstrument("name", i => i.Name = dhsName.Text ?? "", refreshList: true);
        };
        var dhsNameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 4) };
        dhsNameRow.Children.Add(new TextBlock { Text = "Name", Classes = { "amos-label" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(dhsName, 1);
        dhsNameRow.Children.Add(dhsName);
        sampleCard.Children.Insert(1, dhsNameRow);
        var dhsHelp = new TextBlock
        {
            Text = "DHS: the STe's DMA plays it on channel 4, 8-bit mono at the nearest of 6258, 12517, 25033 or 50066 Hz, " +
                   "at its own pitch whatever the note (NOTE is the pitch it is heard at here). A loop goes from LOOP START to " +
                   "LOOP END over and over; LOOP END at 0 plays it once. Note off stops it.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap, FontSize = 11
        };
        sampleCard.Children.Add(dhsHelp);
        var sampleBorder = (Border)sampleCard.Parent!;
        _instrumentRefresh.Add(() =>
        {
            bool dhsSample = DhsMode.Is(_song) && Pattern.Instrument > DhsMode.YmInstruments && CurrentInstrument?.Ym == null;
            foreach (var child in cards.Children) child.IsVisible = !dhsSample || child == sampleBorder;
            dhsNameRow.IsVisible = dhsHelp.IsVisible = dhsSample;
            sampleHelp.IsVisible = !dhsSample;
            if (dhsSample) dhsName.Text = CurrentInstrument?.Name ?? "";
        });

        // ── FILTER ──
        var filter = Card(cards, "FILTER", 400);
        AddGraph(filter, new FilterGraph());
        AddChoice(filter, new[] { "Off", "Low", "High", "Band" }, new[] { "OFF", "LOW PASS", "HIGH PASS", "BAND" },
            i => i.Filter, (i, v) => i.Filter = v, "filter");
        var f1 = KnobRow(filter);
        AddKnob(f1, "CUTOFF", 20, 18000, i => i.Cutoff, (i, v) => i.Cutoff = v, "cutoff", "0", "Hz", log: true);
        AddKnob(f1, "RESO", 0, 100, i => i.Resonance, (i, v) => i.Resonance = v, "resonance", unit: "%", step: 1);
        AddSubHeading(filter, "FILTER ENVELOPE");
        var f2 = KnobRow(filter);
        AddKnob(f2, "DEPTH", -18000, 18000, i => i.FilterEnvDepth, (i, v) => i.FilterEnvDepth = v, "fenv depth", "0", "Hz", bipolar: true, step: 10);
        AddKnob(f2, "ATTACK", 1, 5000, i => i.FilterEnvAttack, (i, v) => i.FilterEnvAttack = v, "fenv attack", "0", "ms", log: true);
        AddKnob(f2, "DECAY", 1, 5000, i => i.FilterEnvDecay, (i, v) => i.FilterEnvDecay = v, "fenv decay", "0", "ms", log: true);

        // ── AMP ──
        var amp = Card(cards, "AMP ENVELOPE", 300);
        // ADSR, or an envelope drawn with points (as in FastTracker 2)
        AddChoice(amp, new[] { "ADSR", "Points" }, new[] { "ADSR", "POINTS" },
            i => i.Envelope.Count >= 2 ? "Points" : "ADSR", SetEnvelopeMode, "envelope mode", refreshPage: true);
        var adsrGraph = new EnvelopeGraph();
        AddGraph(amp, adsrGraph);
        var pointEditor = new PointEnvelopeEditor();
        pointEditor.Edit = change =>
        {
            int before = CurrentInstrument?.Envelope.Count ?? 0;
            EditInstrument("envelope", change);
            // A point added or taken away changes what the sustain and loop knobs can point at
            if ((CurrentInstrument?.Envelope.Count ?? 0) != before) RefreshInstrumentPanel();
        };
        AddGraph(amp, pointEditor);
        var pointKnobs = KnobRow(amp);
        Knob? sustainKnob = null, loopStartKnob = null, loopEndKnob = null;
        _instrumentRefresh.Add(() =>
        {
            int last = Math.Max(0, (CurrentInstrument?.Envelope.Count ?? 1) - 1);
            foreach (var k in new[] { sustainKnob, loopStartKnob, loopEndKnob })
                if (k != null) k.Maximum = last;
        });
        string PointName(double v) => v < 0 ? "OFF" : "P" + (v + 1).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        Knob LastKnob() => (Knob)((WrapPanel)pointKnobs.Children[0]).Children[^1];
        AddKnob(pointKnobs, "SUSTAIN", -1, 24, i => i.EnvelopeSustain,
            (i, v) => i.EnvelopeSustain = Math.Min((int)v, i.Envelope.Count - 1), "env sustain", step: 1);
        sustainKnob = LastKnob();
        sustainKnob.ValueText = PointName;
        ToolTip.SetTip(sustainKnob, "The point a held note stays at until it is let go");
        AddKnob(pointKnobs, "LOOP START", -1, 24, i => i.EnvelopeLoopStart, (i, v) =>
        {
            i.EnvelopeLoopStart = Math.Min((int)v, i.Envelope.Count - 1);
            if (i.EnvelopeLoopStart < 0) i.EnvelopeLoopEnd = -1;
            else if (i.EnvelopeLoopEnd < i.EnvelopeLoopStart) i.EnvelopeLoopEnd = i.EnvelopeLoopStart;
        }, "env loop start", step: 1);
        loopStartKnob = LastKnob();
        loopStartKnob.ValueText = PointName;
        AddKnob(pointKnobs, "LOOP END", -1, 24, i => i.EnvelopeLoopEnd, (i, v) =>
        {
            i.EnvelopeLoopEnd = Math.Min((int)v, i.Envelope.Count - 1);
            if (i.EnvelopeLoopEnd < 0) i.EnvelopeLoopStart = -1;
            else if (i.EnvelopeLoopStart < 0 || i.EnvelopeLoopStart > i.EnvelopeLoopEnd) i.EnvelopeLoopStart = i.EnvelopeLoopEnd;
        }, "env loop end", step: 1);
        loopEndKnob = LastKnob();
        loopEndKnob.ValueText = PointName;
        AddKnob(pointKnobs, "FADE OUT", 0, 20000, i => i.FadeOut, (i, v) => i.FadeOut = v, "fade out", "0", "ms", step: 10);
        LastKnob().ValueText = v => v <= 0 ? "NONE" : v.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
        ToolTip.SetTip(LastKnob(), "After the note is let go, the sound fades out over this time (NONE: it follows the envelope)");
        var adsr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        amp.Children.Add(adsr);
        _instrumentRefresh.Add(() =>
        {
            bool points = CurrentInstrument?.Envelope.Count >= 2;
            adsrGraph.IsVisible = adsr.IsVisible = !points;
            pointEditor.IsVisible = pointKnobs.IsVisible = points;
        });
        AddVSlider(adsr, "A", 1, 5000, true, i => i.Attack, (i, v) => i.Attack = v, "attack", "0", "ms");
        AddVSlider(adsr, "D", 1, 5000, true, i => i.Decay, (i, v) => i.Decay = v, "decay", "0", "ms");
        AddVSlider(adsr, "S", 0, 100, false, i => i.Sustain, (i, v) => i.Sustain = v, "sustain", "0", "%");
        AddVSlider(adsr, "R", 1, 5000, true, i => i.Release, (i, v) => i.Release = v, "release", "0", "ms");
        var panRow = KnobRow(amp);
        AddKnob(panRow, "PAN", -100, 100, i => i.Pan, (i, v) => i.Pan = v, "pan", "+0;-0;0", bipolar: true, step: 1);

        // The pan envelope: off, or points as the volume envelope (up is right, down is left)
        AddSubHeading(amp, "PAN ENVELOPE");
        AddChoice(amp, new[] { "Off", "Points" }, new[] { "OFF", "POINTS" },
            i => i.PanEnvelope.Count >= 2 ? "Points" : "Off", SetPanEnvelopeMode, "pan envelope mode", refreshPage: true);
        var panEditor = new PointEnvelopeEditor { Parts = EnvelopeParts.Pan };
        panEditor.Edit = change =>
        {
            int before = CurrentInstrument?.PanEnvelope.Count ?? 0;
            EditInstrument("pan envelope", change);
            if ((CurrentInstrument?.PanEnvelope.Count ?? 0) != before) RefreshInstrumentPanel();
        };
        AddGraph(amp, panEditor);
        var panKnobs = KnobRow(amp);
        Knob? panSustainKnob = null, panLoopStartKnob = null, panLoopEndKnob = null;
        _instrumentRefresh.Add(() =>
        {
            int last = Math.Max(0, (CurrentInstrument?.PanEnvelope.Count ?? 1) - 1);
            foreach (var k in new[] { panSustainKnob, panLoopStartKnob, panLoopEndKnob })
                if (k != null) k.Maximum = last;
            panEditor.IsVisible = panKnobs.IsVisible = CurrentInstrument?.PanEnvelope.Count >= 2;
        });
        Knob PanLastKnob() => (Knob)((WrapPanel)panKnobs.Children[0]).Children[^1];
        AddKnob(panKnobs, "SUSTAIN", -1, 24, i => i.PanEnvelopeSustain,
            (i, v) => i.PanEnvelopeSustain = Math.Min((int)v, i.PanEnvelope.Count - 1), "pan env sustain", step: 1);
        panSustainKnob = PanLastKnob();
        panSustainKnob.ValueText = PointName;
        AddKnob(panKnobs, "LOOP START", -1, 24, i => i.PanEnvelopeLoopStart, (i, v) =>
        {
            i.PanEnvelopeLoopStart = Math.Min((int)v, i.PanEnvelope.Count - 1);
            if (i.PanEnvelopeLoopStart < 0) i.PanEnvelopeLoopEnd = -1;
            else if (i.PanEnvelopeLoopEnd < i.PanEnvelopeLoopStart) i.PanEnvelopeLoopEnd = i.PanEnvelopeLoopStart;
        }, "pan env loop start", step: 1);
        panLoopStartKnob = PanLastKnob();
        panLoopStartKnob.ValueText = PointName;
        AddKnob(panKnobs, "LOOP END", -1, 24, i => i.PanEnvelopeLoopEnd, (i, v) =>
        {
            i.PanEnvelopeLoopEnd = Math.Min((int)v, i.PanEnvelope.Count - 1);
            if (i.PanEnvelopeLoopEnd < 0) i.PanEnvelopeLoopStart = -1;
            else if (i.PanEnvelopeLoopStart < 0 || i.PanEnvelopeLoopStart > i.PanEnvelopeLoopEnd) i.PanEnvelopeLoopStart = i.PanEnvelopeLoopEnd;
        }, "pan env loop end", step: 1);
        panLoopEndKnob = PanLastKnob();
        panLoopEndKnob.ValueText = PointName;

        // ── EFFECTS ──
        var fx = Card(cards, "EFFECTS", 300);
        AddSubHeading(fx, "ECHO");
        var e1 = KnobRow(fx);
        AddKnob(e1, "MIX", 0, 100, i => i.EchoMix, (i, v) => i.EchoMix = v, "echo mix", unit: "%", step: 1);
        AddKnob(e1, "TIME", 10, 2000, i => i.EchoTime, (i, v) => i.EchoTime = v, "echo time", "0", "ms", log: true);
        AddKnob(e1, "FEEDBACK", 0, 90, i => i.EchoFeedback, (i, v) => i.EchoFeedback = v, "echo feedback", unit: "%", step: 1);
        AddKnob(e1, "ROWS", 0, 16, i => i.EchoSyncRows, (i, v) => i.EchoSyncRows = (int)v, "echo rows", step: 1);
        AddSubHeading(fx, "PITCH");
        var p1 = KnobRow(fx);
        AddKnob(p1, "VIBRATO", 0, 200, i => i.Vibrato, (i, v) => i.Vibrato = v, "vibrato", "0", "c", step: 1);
        AddKnob(p1, "RATE", 0, 30, i => i.VibratoRate, (i, v) => i.VibratoRate = v, "vibrato rate", "0.0", "Hz", step: 0.1);
        AddNumber(fx, "Ring mod ch", 0, 8, 1, "0", i => i.Ring, (i, v) => i.Ring = (int)v);
        AddNumber(fx, "Sync ch", 0, 8, 1, "0", i => i.Sync, (i, v) => i.Sync = (int)v);

        // ── MODULATION ──
        AddLfoCard(cards, "LFO 1", i => i.Lfo1 ??= new TrackerLfo());
        AddLfoCard(cards, "LFO 2", i => i.Lfo2 ??= new TrackerLfo());

        // ── The table ──
        var tableCard = Card(cards, "TABLE (one step per tick)", 300);
        var table = new TextBox
        {
            AcceptsReturn = true,
            Height = 150,
            FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"),
            PlaceholderText = "NOISE +24\nPULSE 0\n-12\nP30"
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
        tableCard.Children.Add(table);
        _instrumentRefresh.Add(() => table.Text = string.Join("\n", CurrentInstrument?.Table ?? new List<string>()));
        AddNumber(tableCard, "Loop from step", -1, 255, 1, "0", i => i.TableLoop, (i, v) => i.TableLoop = (int)v);
    }

    private static string NoteName(double note)
    {
        string[] names = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };
        int n = (int)Math.Round(note);
        return $"{names[((n % 12) + 12) % 12]}{n / 12 - 1}";
    }

    // The loop points as percent of the sample, and back to frames
    private static double LoopPercent(TrackerInstrument i, int frame)
    {
        int length = SampleBank.Get(i.Sample)?.Data.Length ?? 0;
        return length == 0 ? 0 : 100.0 * frame / length;
    }

    private static int LoopFrame(TrackerInstrument i, double percent)
    {
        int length = SampleBank.Get(i.Sample)?.Data.Length ?? 0;
        return (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * length);
    }

    // A WAV file for the instrument; kept relative to the working folder when it is inside it
    private async Task LoadSampleAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Sample",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound, "Samples"),
            FileTypeFilter = new[] { new FilePickerFileType("WAV sound") { Patterns = new[] { "*.wav" } } }
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        string root = Path.GetFullPath(Workspace.Root) + Path.DirectorySeparatorChar;
        string stored = Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(Workspace.Root, path).Replace('\\', '/') : path;
        SampleBank.Forget(stored);
        var sample = SampleBank.Get(stored);
        if (sample == null) { SetStatus($"Could not read {Path.GetFileName(path)}: is it a WAV file?"); return; }
        EditInstrument("sample", i =>
        {
            i.Wave = "Sample";
            i.Sample = stored;
            i.LoopStart = 0;
            i.LoopEnd = 0;
        });
        RefreshInstrumentPanel();
        SetStatus($"Sample {Path.GetFileName(path)}: {sample.Seconds:0.00} s at {sample.Rate} Hz");
    }

    // An LFO: what it moves, its wave, speed (in Hz or in rows of the song) and depth
    private void AddLfoCard(WrapPanel cards, string title, Func<TrackerInstrument, TrackerLfo> lfo)
    {
        var card = Card(cards, title, 340);
        AddGraph(card, new LfoGraph { Lfo = lfo });
        AddCombo(card, "Moves", TrackerLfo.Targets, i => lfo(i).Target, (i, v) => lfo(i).Target = v, title + " Moves");
        AddCombo(card, "Wave", TrackerLfo.Shapes, i => lfo(i).Shape, (i, v) => lfo(i).Shape = v, title + " Wave");
        var row = KnobRow(card);
        AddKnob(row, "DEPTH", -1200, 1200, i => lfo(i).Depth, (i, v) => lfo(i).Depth = v, title + " Depth", "+0;-0;0", bipolar: true, step: 1);
        AddKnob(row, "RATE", 0.05, 50, i => lfo(i).Rate, (i, v) => lfo(i).Rate = v, title + " Rate", "0.0#", "Hz", log: true);
        AddKnob(row, "ROWS", 0, 64, i => lfo(i).SyncRows, (i, v) => lfo(i).SyncRows = (int)v, title + " Rows", step: 1);
        AddKnob(row, "DELAY", 0, 5000, i => lfo(i).Delay, (i, v) => lfo(i).Delay = v, title + " Delay", "0", "ms", step: 10);
        AddCheck(card, "Restart on note", i => lfo(i).Retrigger, (i, v) => lfo(i).Retrigger = v, title + " retrigger");
        card.Children.Add(new TextBlock
        {
            Text = "Depth: cents for pitch, semitones for cutoff, % otherwise. ROWS above 0 ties the speed to the song.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap, FontSize = 11
        });
    }

    private static StackPanel KnobRow(StackPanel panel)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var holder = new StackPanel();
        holder.Children.Add(row);
        panel.Children.Add(holder);
        return holder;
    }

    private static void AddSubHeading(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, Classes = { "amos-label" }, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });

    // A knob for one setting; key is what undo groups its changes by
    private void AddKnob(StackPanel row, string label, double min, double max, Func<TrackerInstrument, double> get,
        Action<TrackerInstrument, double> set, string key, string format = "0", string unit = "",
        bool log = false, bool bipolar = false, double step = 0)
    {
        var knob = new Knob
        {
            Label = label, Minimum = min, Maximum = max, Logarithmic = log, Bipolar = bipolar, Step = step,
            Format = format, Unit = unit, Margin = new Thickness(0, 2, 4, 2)
        };
        knob.ValueChanged += v => EditInstrument(key, i => set(i, v));
        ((WrapPanel)row.Children[0]).Children.Add(knob);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            knob.IsEnabled = inst != null;
            if (inst != null) knob.Value = get(inst);
        });
    }

    // A vertical slider (the amp envelope); logarithmic ones move evenly through short and long times
    private void AddVSlider(StackPanel row, string label, double min, double max, bool log,
        Func<TrackerInstrument, double> get, Action<TrackerInstrument, double> set, string key, string format, string unit)
    {
        double ToPos(double v) => log ? Math.Log(Math.Clamp(v, min, max) / min) / Math.Log(max / min) : (Math.Clamp(v, min, max) - min) / (max - min);
        double ToValue(double p) => Math.Round(log ? min * Math.Pow(max / min, p) : min + p * (max - min));
        var slider = new Slider
        {
            Orientation = Orientation.Vertical, Minimum = 0, Maximum = 1, Height = 120, Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var value = new TextBlock { Classes = { "amos-label" }, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
        slider.ValueChanged += (_, _) =>
        {
            double v = ToValue(slider.Value);
            value.Text = v.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " " + unit;
            EditInstrument(key, i => set(i, v));
        };
        var column = new StackPanel { Width = 62, Spacing = 2 };
        column.Children.Add(new TextBlock { Text = label, Classes = { "amos-title" }, HorizontalAlignment = HorizontalAlignment.Center });
        column.Children.Add(slider);
        column.Children.Add(value);
        row.Children.Add(column);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            slider.IsEnabled = inst != null;
            if (inst == null) return;
            slider.Value = ToPos(get(inst));
            value.Text = get(inst).ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " " + unit;
        });
    }

    // A row of buttons for a choice (waves, filter type); they do not take the keyboard from the piano
    private void AddChoice(StackPanel panel, string[] values, string[] labels, Func<TrackerInstrument, string> get,
        Action<TrackerInstrument, string> set, string key, bool refreshPage = false)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        var buttons = new Avalonia.Controls.Primitives.ToggleButton[values.Length];
        for (int k = 0; k < values.Length; k++)
        {
            string v = values[k];
            var b = new Avalonia.Controls.Primitives.ToggleButton { Content = labels[k], Focusable = false, FontSize = 11, Padding = new Thickness(7, 3), Margin = new Thickness(0, 0, 3, 3) };
            b[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");
            b.Click += (_, _) =>
            {
                EditInstrument(key, i => set(i, v));
                for (int j = 0; j < buttons.Length; j++) buttons[j].IsChecked = buttons[j] == b;
                if (refreshPage) RefreshInstrumentPanel();   // the choice shows other settings
            };
            buttons[k] = b;
            row.Children.Add(b);
        }
        panel.Children.Add(row);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            string current = inst == null ? "" : get(inst) ?? "";
            if (current.Equals("Saw", StringComparison.OrdinalIgnoreCase)) current = "Sawtooth";
            for (int j = 0; j < buttons.Length; j++)
            {
                buttons[j].IsEnabled = inst != null;
                buttons[j].IsChecked = values[j].Equals(current, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private void AddCheck(StackPanel panel, string label, Func<TrackerInstrument, bool> get, Action<TrackerInstrument, bool> set,
        string key)
    {
        var check = new CheckBox();
        check.IsCheckedChanged += (_, _) => EditInstrument(key, i => set(i, check.IsChecked == true));
        AddRow(panel, label, check);
        _instrumentRefresh.Add(() =>
        {
            var inst = CurrentInstrument;
            check.IsEnabled = inst != null;
            check.IsChecked = inst != null && get(inst);
        });
    }

    // Points: an envelope like the ADSR to start from (ticks at the song's tempo); ADSR: the points go
    private void SetEnvelopeMode(TrackerInstrument i, string mode)
    {
        if (mode != "Points")
        {
            i.Envelope.Clear();
            i.EnvelopeSustain = i.EnvelopeLoopStart = i.EnvelopeLoopEnd = -1;
            return;
        }
        if (i.Envelope.Count >= 2) return;
        double tickMs = 2500.0 / Math.Clamp(_song.Tempo, 32, 255);
        int Ticks(double ms) => Math.Max(1, (int)Math.Round(ms / tickMs));
        int a = Ticks(i.Attack), d = a + Ticks(i.Decay), r = d + Ticks(i.Release);
        int s = (int)Math.Round(Math.Clamp(i.Sustain, 0, 100) / 100 * 64);
        i.Envelope = new List<TrackerEnvelopePoint> { new(0, 0), new(a, 64), new(d, s), new(r, 0) };
        i.EnvelopeSustain = 2;
        i.EnvelopeLoopStart = i.EnvelopeLoopEnd = -1;
    }

    // A new pan envelope swings from the middle to the right, the left and back, over a second or so
    private static void SetPanEnvelopeMode(TrackerInstrument i, string mode)
    {
        if (mode != "Points")
        {
            i.PanEnvelope.Clear();
            i.PanEnvelopeSustain = i.PanEnvelopeLoopStart = i.PanEnvelopeLoopEnd = -1;
            return;
        }
        if (i.PanEnvelope.Count >= 2) return;
        i.PanEnvelope = new List<TrackerEnvelopePoint> { new(0, 32), new(12, 64), new(36, 0), new(48, 32) };
        i.PanEnvelopeSustain = -1;
        i.PanEnvelopeLoopStart = 0;
        i.PanEnvelopeLoopEnd = 3;
    }

    // A card on the instrument page; returns the panel its rows go in
    private static StackPanel Card(WrapPanel cards, string title, double width = 340)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "amos-title" }, Margin = new Thickness(0, 0, 0, 2) });
        var border = new Border
        {
            Width = width,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(2),
            Child = stack
        };
        border[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        border[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBorder");
        cards.Children.Add(border);
        return stack;
    }

    private void AddGraph(StackPanel card, InstrumentGraph graph)
    {
        graph.Instrument = () => CurrentInstrument;
        graph.Margin = new Thickness(0, 0, 0, 4);
        _graphs.Add(graph);
        card.Children.Add(graph);
    }

    // ── The YM part of an instrument (a song in YM mode) ─────────────────

    private static readonly string[] YmFlagNames = { "Portamento", "Arpeggio", "Vibrato", "Transpose", "Fixed frequency", "Fixed detune" };

    private static readonly (string Label, string Help)[] YmSequenceNames =
    {
        ("Volume", "the chip's volume, 0 – F"),
        ("Arpeggio", "semitones from the note (with the arpeggio flags)"),
        ("Vibrato", "tone periods off the note (with the vibrato flags)"),
        ("Mixer", "0100 tone, 1000 noise, 00x0 buzzer (x its shape, 1 the instrument's), 000x timer: 5 SID, 9 PWM, B sync buzzer, D digidrum"),
        ("Noise", "the noise period, 0 – 1F"),
        ("Fixed", "a tone period of its own, FFFF none (with the fixed frequency flags)"),
        ("Timer", "the SID's steps (the volume less each), or the sync buzzer's shapes"),
        ("PWM", "the PWM timer's width added, 0 – FF")
    };

    private static int GetYmSeq(TrackerYmInstrument y, int i) => i switch
    {
        0 => y.VolumeSeq, 1 => y.ArpeggioSeq, 2 => y.VibratoSeq, 3 => y.MixerSeq,
        4 => y.NoiseSeq, 5 => y.FixedSeq, 6 => y.TimerSeq, _ => y.PwmSeq
    };

    private static void SetYmSeq(TrackerYmInstrument y, int i, int n)
    {
        switch (i)
        {
            case 0: y.VolumeSeq = n; break;
            case 1: y.ArpeggioSeq = n; break;
            case 2: y.VibratoSeq = n; break;
            case 3: y.MixerSeq = n; break;
            case 4: y.NoiseSeq = n; break;
            case 5: y.FixedSeq = n; break;
            case 6: y.TimerSeq = n; break;
            default: y.PwmSeq = n; break;
        }
    }

    // A sequence as text: its steps in hex, "|" before the step it goes round from (none: it stays on
    // its last step). The mixer's words with four digits.
    public static string YmSequenceText(TrackerYmSequence? q, bool words)
    {
        if (q == null || q.Steps.Count == 0) return "";
        var parts = q.Steps.Select(v => v < 0 ? "-" + (-v).ToString("X") : v.ToString(words ? "X4" : "X")).ToList();
        if (q.Repeat < q.Steps.Count - 1) parts.Insert(Math.Max(0, q.Repeat), "|");
        return string.Join(" ", parts);
    }

    public static TrackerYmSequence? ParseYmSequence(string text)
    {
        var steps = new List<int>();
        int repeat = -1;
        foreach (var raw in text.Replace("|", " | ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw == "|") { repeat = steps.Count; continue; }
            bool minus = raw.StartsWith('-');
            int v = int.Parse(minus ? raw[1..] : raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            steps.Add(minus ? -v : v);
        }
        if (steps.Count == 0) return null;
        if (steps.Count > 63) throw new FormatException("A sequence has at most 63 steps");
        return new TrackerYmSequence { Steps = steps, Repeat = repeat < 0 || repeat >= steps.Count ? steps.Count - 1 : repeat };
    }

    private StackPanel BuildYmPanel()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        AddHeading(panel, "MAXYMISER INSTRUMENT");
        panel.Children.Add(new TextBlock
        {
            Text = "The song is maxYMiser's (YM mode): channels 1 – 3 are played by the Atari ST's sound chip as maxYMiser plays " +
                   "them. An instrument's sequences are the song's, by their numbers, shared by the instruments that use them. " +
                   "They are stepped every Speed ticks from the note's start; each is written in hex, with | before the step " +
                   "it goes round from (without it, it stays on its last step).",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap
        });
        var dhsNote = new TextBlock
        {
            Text = "A DHS song: no timer effects. The mixer's last digit stays 0 (no 5 SID, 9 PWM, B sync buzzer, D digidrum); " +
                   "the timer's and PWM's settings are not used. Song → What DHS can play tells the rest.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange
        };
        panel.Children.Add(dhsNote);
        _instrumentRefresh.Add(() => dhsNote.IsVisible = DhsMode.Is(_song));

        // (timer: only for a timer effect, which a DHS song does not have)
        void Number(string label, int min, int max, Func<TrackerYmInstrument, int> get, Action<TrackerYmInstrument, int> set, bool timer = false)
        {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
            box.ValueChanged += (_, _) =>
            {
                if (box.Value is decimal d) EditInstrument("ym:" + label, i => { if (i.Ym != null) set(i.Ym, (int)d); });
            };
            AddRow(panel, label, box);
            _instrumentRefresh.Add(() =>
            {
                var y = CurrentInstrument?.Ym;
                box.IsEnabled = y != null && !(timer && DhsMode.Is(_song));
                box.Value = y == null ? null : Math.Clamp(get(y), min, max);
            });
        }
        Number("Speed", 1, 63, y => y.Speed, (y, v) => y.Speed = v);
        Number("Volume off", 0, 15, y => y.Volume, (y, v) => y.Volume = v);
        Number("Detune coarse", -96, 96, y => y.DetuneCoarse, (y, v) => y.DetuneCoarse = v);
        Number("Detune fine", -128, 127, y => y.DetuneFine, (y, v) => y.DetuneFine = v);
        Number("Pulse width", 0, 255, y => y.PulseWidth, (y, v) => y.PulseWidth = v, timer: true);
        Number("Buzzer shape", 0, 15, y => y.BuzzerWave, (y, v) => y.BuzzerWave = v);
        Number("Start sync", 0, 255, y => y.StartSync, (y, v) => y.StartSync = v);
        Number("Digidrum", 0, 8, y => y.DigiSample, (y, v) => y.DigiSample = v, timer: true);
        Number("Digi rate", 0x18, 0xCD, y => y.DigiRate, (y, v) => y.DigiRate = v, timer: true);

        // What the pitch effects act on: the tone, the buzzer, the timer
        AddHeading(panel, "WHAT THE PITCH ACTS ON");
        var flags = new Grid { ColumnDefinitions = new ColumnDefinitions("140,80,80,80") };
        for (int r = 0; r <= YmFlagNames.Length; r++) flags.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] sources = { "Tone", "Buzzer", "Timer" };
        for (int k = 0; k < 3; k++)
        {
            var head = new TextBlock { Text = sources[k], Classes = { "amos-label" } };
            Grid.SetColumn(head, k + 1);
            flags.Children.Add(head);
        }
        for (int f = 0; f < YmFlagNames.Length; f++)
        {
            var name = new TextBlock { Text = YmFlagNames[f], Classes = { "amos-label" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, f + 1);
            flags.Children.Add(name);
            for (int k = 0; k < 3; k++)
            {
                int flag = f, bit = 4 >> k;
                var check = new CheckBox { Focusable = false };
                Grid.SetRow(check, f + 1);
                Grid.SetColumn(check, k + 1);
                check.IsCheckedChanged += (_, _) => EditInstrument("ym:flags", i =>
                {
                    if (i.Ym == null) return;
                    int v = GetYmFlag(i.Ym, flag);
                    SetYmFlag(i.Ym, flag, check.IsChecked == true ? v | bit : v & ~bit);
                });
                flags.Children.Add(check);
                _instrumentRefresh.Add(() =>
                {
                    var y = CurrentInstrument?.Ym;
                    check.IsEnabled = y != null;
                    check.IsChecked = y != null && (GetYmFlag(y, flag) & bit) != 0;
                });
            }
        }
        panel.Children.Add(flags);

        // The sequences: each slot's number, and the song's sequence of that number
        AddHeading(panel, "SEQUENCES");
        for (int i = 0; i < YmSequenceNames.Length; i++)
        {
            int index = i;
            var number = new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 120 };
            var box = new TextBox { FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"), Watermark = YmSequenceNames[i].Help, Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(box, YmSequenceNames[i].Help);
            NumberBox(number, () => CurrentInstrument?.Ym is { } y ? GetYmSeq(y, index) : 0);
            number.ValueChanged += (_, _) =>
            {
                if (number.Value is decimal d) EditInstrument("ym:seqno" + index, inst => { if (inst.Ym != null) SetYmSeq(inst.Ym, index, (int)d); });
                if (!_loading) RefreshInstrumentPanel();
            };
            void Commit()
            {
                if (_loading || _song.Ym is not { } song) return;
                try
                {
                    var q = ParseYmSequence(box.Text ?? "");
                    box.BorderBrush = null;
                    EditInstrument("ym:seq" + index, inst =>
                    {
                        if (inst.Ym == null) return;
                        int n = GetYmSeq(inst.Ym, index);
                        if (q == null && n == 0) return;
                        if (n == 0)
                        {
                            // A sequence of its own, the next number
                            n = Math.Max(1, song.Sequences.Count);
                            SetYmSeq(inst.Ym, index, n);
                        }
                        while (song.Sequences.Count <= n) song.Sequences.Add(new TrackerYmSequence());
                        song.Sequences[n] = q ?? new TrackerYmSequence();
                    });
                    RefreshInstrumentPanel();
                }
                catch (FormatException e) { box.BorderBrush = Brushes.Red; SetStatus($"{YmSequenceNames[index].Label}: {e.Message}"); }
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(number);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            AddRow(panel, YmSequenceNames[i].Label, row);
            _instrumentRefresh.Add(() =>
            {
                var y = CurrentInstrument?.Ym;
                // (the timer's and the PWM's sequences: not in a DHS song)
                number.IsEnabled = box.IsEnabled = y != null && !(index >= 6 && DhsMode.Is(_song));
                box.BorderBrush = null;
                int n = y == null ? 0 : GetYmSeq(y, index);
                number.Value = n;
                var seqs = _song.Ym?.Sequences;
                box.Text = y == null || seqs == null || n <= 0 || n >= seqs.Count ? "" : YmSequenceText(seqs[n], index is 3 or 5);
            });
        }
        return panel;
    }

    private static int GetYmFlag(TrackerYmInstrument y, int f) => f switch
    {
        0 => y.Portamento, 1 => y.Arpeggio, 2 => y.Vibrato, 3 => y.Transpose, 4 => y.FixedFrequency, _ => y.FixedDetune
    };

    private static void SetYmFlag(TrackerYmInstrument y, int f, int v)
    {
        switch (f)
        {
            case 0: y.Portamento = v; break;
            case 1: y.Arpeggio = v; break;
            case 2: y.Vibrato = v; break;
            case 3: y.Transpose = v; break;
            case 4: y.FixedFrequency = v; break;
            default: y.FixedDetune = v; break;
        }
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

    // key: what undo groups the changes by (the label when not given)
    private void AddNumber(StackPanel panel, string label, double min, double max, double inc, string format,
        Func<TrackerInstrument, double> get, Action<TrackerInstrument, double> set, string? key = null)
    {
        var box = new NumericUpDown
        {
            Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)inc, FormatString = format
        };
        box.ValueChanged += (_, _) =>
        {
            if (box.Value is decimal d) EditInstrument(key ?? label, i => set(i, (double)d));
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
        Func<TrackerInstrument, string> get, Action<TrackerInstrument, string> set, string? key = null)
    {
        var combo = new ComboBox { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s) EditInstrument(key ?? label, i => set(i, s));
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
        foreach (var g in _graphs) g.InvalidateVisual();
    }

    private void EditInstrument(string key, Action<TrackerInstrument> change, bool refreshList = false)
    {
        if (_loading) return;
        var inst = CurrentInstrument;
        if (inst == null) return;
        PushUndo("inst:" + Pattern.Instrument + ":" + key);
        change(inst);
        // The notes that sound right now with the instrument change with it
        TryEngine(() => ChiptuneSynth.TrackInstrumentChanged(_song, Pattern.Instrument));
        if (refreshList) RefreshInstrumentList();
        if (key == "table") ValidateTable();
        if (refreshList) _instrumentRefresh[0]();   // the heading shows the name
        foreach (var g in _graphs) g.InvalidateVisual();
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
        CheckDhs();
        _dirty = true;
        UpdateTitle();
        Pattern.InvalidateVisual();
        if (_playMode == PlayMode.Song) TryEngine(() => ChiptuneSynth.TrackUpdate(_song));
        else if (_playMode == PlayMode.Pattern) TryEngine(() => ChiptuneSynth.TrackUpdate(_song, Pattern.PatternIndex, PositionToPlay));
    }

    // ── DHS: kept within what the VBL player plays ─────────────────────────

    // The song as it was last within DHS (with what it had then that DHS cannot play, if anything), to go
    // back to when a change goes outside and the song should stay a DHS song
    private TrackerSong? _dhsSong;
    private SortedSet<string> _dhsKnown = new();
    private string? _dhsGood;
    private bool _dhsAsking;

    private void CheckDhs()
    {
        if (!DhsMode.Is(_song)) { _dhsSong = null; _dhsGood = null; return; }
        var now = DhsMode.Problems(_song);
        if (!ReferenceEquals(_dhsSong, _song) || _dhsGood == null || now.IsSubsetOf(_dhsKnown))
        {
            _dhsSong = _song;
            _dhsKnown = now;
            _dhsGood = _song.ToJson();
            return;
        }
        if (_dhsAsking) return;
        var added = now.Except(_dhsKnown).ToList();
        _ = AskLeaveDhsAsync(added);
    }

    private async Task AskLeaveDhsAsync(List<string> added)
    {
        _dhsAsking = true;
        try
        {
            string text = "DHS cannot play " + string.Join("; ", added) + ".\n\n" +
                          "Its player is called from the VBL alone, so there is nothing that needs a timer, and channel 4 is " +
                          "the STe's DMA samples. Make the song an ordinary maxYMiser song (no longer saved as DHS), or take " +
                          "the change back?";
            if (await AskAsync("Leave DHS?", text, "Make it maxYMiser", "Take it back"))
            {
                DhsMode.Leave(_song);
                _dhsSong = null;
                UpdateYmMode();
                SongChanged();
                SetStatus("The song is an ordinary maxYMiser song now (Undo takes it back).");
            }
            else if (_dhsGood != null)
            {
                Stop();
                _song = TrackerSong.FromJson(_dhsGood);
                int row = Pattern.CursorRow;
                LoadSongIntoUi(keepPositions: true);
                Pattern.SetCursor(Math.Min(row, _song.Patterns[Pattern.PatternIndex].Rows - 1), Pattern.CursorChannel, Pattern.CursorColumn);
                SongChanged();
            }
        }
        finally { _dhsAsking = false; }
    }

    private async void MenuToDhs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_song.Ym == null || DhsMode.Is(_song)) return;
        var copy = TrackerSong.FromJson(_song.ToJson());
        var told = DhsMode.Convert(copy);
        string text = "The song is kept within what DHS plays from then on: the YM without timer effects on channels 1 – 3, " +
                      "and the STe's DMA samples on channel 4.\n\n" +
                      (told.Count > 0 ? "Taken out:\n" + string.Join("\n", told.Select(t => "  • " + t)) : "Nothing needs to be taken out.") +
                      "\n\n(Undo takes it back.)";
        if (!await AskAsync("Make it a DHS song?", text, "Make it DHS", "Cancel")) return;
        Stop();
        PushUndo("dhs");
        _song = copy;
        LoadSongIntoUi(keepPositions: true);
        UpdateYmMode();
        SongChanged();
    }

    // In a DHS song channel 4 takes the samples (instruments from 33) and channels 1 – 3 the YM's (1 – 32)
    private bool DhsAllows(int channel, int instrument) =>
        !DhsMode.Is(_song) || instrument < 1 || (channel == 3) == (instrument > DhsMode.YmInstruments);

    // The instrument each side had last, taken up again when the cursor goes over
    private int _dhsYmInstrument = 1, _dhsSampleInstrument;

    private void FollowDhsChannel()
    {
        if (!DhsMode.Is(_song)) return;
        int c = Pattern.CursorChannel, now = Pattern.Instrument;
        if (DhsAllows(c, now))
        {
            if (now > DhsMode.YmInstruments) _dhsSampleInstrument = now; else if (now >= 1) _dhsYmInstrument = now;
            return;
        }
        int next;
        if (c == 3)
        {
            if (now >= 1) _dhsYmInstrument = now;
            next = _dhsSampleInstrument > DhsMode.YmInstruments && _dhsSampleInstrument <= _song.Instruments.Count
                ? _dhsSampleInstrument
                : _song.Instruments.Count > DhsMode.YmInstruments ? DhsMode.YmInstruments + 1 : 0;
            if (next == 0)
            {
                SetStatus("Channel 4 is the DMA: it plays samples. Make one with Instrument → Add.");
                return;
            }
        }
        else
        {
            _dhsSampleInstrument = now;
            next = Math.Clamp(_dhsYmInstrument, 1, Math.Min(DhsMode.YmInstruments, _song.Instruments.Count));
        }
        Pattern.Instrument = next;
        RefreshInstrumentList();
        RefreshInstrumentPanel();
    }

    private void MenuDhsHelp_OnClick(object? sender, RoutedEventArgs e) => _ = ShowReportAsync("What DHS can play", DhsMode.Help);

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
        for (int c = 0; c < TrackerSong.MaxChannels; c++)
            ChiptuneSynth.SetTrackMute(c + 1, Pattern.Muted[c]);
    }

    private void PlaySong()
    {
        ApplyMutes();
        if (TryEngine(() => ChiptuneSynth.TrackPlay(_song, _orderPos, 0, loop: true)))
            _playMode = PlayMode.Song;
        KeepTypingFocus();
    }

    private void PlayPattern(int row)
    {
        ApplyMutes();
        if (TryEngine(() => ChiptuneSynth.TrackPlayPattern(_song, Pattern.PatternIndex, row, PositionToPlay)))
            _playMode = PlayMode.Pattern;
        KeepTypingFocus();
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
            case "channel-pattern-up": ChangeChannelPattern(+1); break;
            case "channel-pattern-down": ChangeChannelPattern(-1); break;
            case "channel-pattern-new": NewChannelPattern(); break;
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
            case "save-as": _ = SaveAsync(true); break;
        }
    }

    // A number box in the toolbar: only digits can be typed in it. Its arrows (and the mouse wheel)
    // change the value without taking the keys from the pattern; after typing a number, Enter keeps
    // it and Esc goes back to the one before, and either gives the keys back. Left empty, it shows
    // the value it had.
    private void NumberBox(NumericUpDown box, Func<int> current)
    {
        box.ParsingNumberStyle = NumberStyles.Integer;
        box.AddHandler(TextInputEvent, (_, e) =>
        {
            if (e.Text != null && !e.Text.All(char.IsAsciiDigit)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        box.Spinned += (_, _) => KeepTypingFocus();
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            if (e.Key == Key.Escape) box.Text = current().ToString(CultureInfo.InvariantCulture);
            e.Handled = true;
            KeepTypingFocus();
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (box.Value == null) box.Value = current();
        };
    }

    // Picking an instrument or a position in a list should not take the keys away from the
    // pattern: you go on typing notes where you were. Posted, so that it runs after the list
    // has taken the focus for the click.
    private void KeepTypingFocus() => Dispatcher.UIThread.Post(() =>
    {
        if (InstrumentPage.IsVisible) InstrumentKeys.Focus();
        else Pattern.Focus();
    }, DispatcherPriority.Input);

    // ════════════════════════════════════════════════════════════════
    //  The two pages: the pattern and the instrument
    // ════════════════════════════════════════════════════════════════

    private void ShowInstrumentPage(bool show)
    {
        ReleasePageNotes();
        PatternPage.IsVisible = !show;
        InstrumentPage.IsVisible = show;
        PatternTab.IsChecked = !show;
        InstrumentTab.IsChecked = show;
        if (show) RefreshInstrumentPanel();
        KeepTypingFocus();
    }

    private void PatternTab_OnClick(object? sender, RoutedEventArgs e) => ShowInstrumentPage(false);
    private void InstrumentTab_OnClick(object? sender, RoutedEventArgs e) => ShowInstrumentPage(true);

    // On the instrument page the note keys play the instrument, on the channel the pattern cursor is in
    private void InstrumentKeys_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source != InstrumentKeys || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.F1 || e.Key == Key.F2)
        {
            OctaveBox.Value = Math.Clamp(Pattern.Octave + (e.Key == Key.F1 ? -1 : 1), 0, 8);
            e.Handled = true;
            return;
        }
        int note = PatternView.NoteForKey(e.Key, Pattern.Octave);
        if (note < 0) return;
        e.Handled = true;
        if (_pageNotes.ContainsKey(e.Key)) return;   // key repeat
        int channel = Pattern.CursorChannel + 1;
        _pageNotes[e.Key] = channel;
        TryEngine(() => ChiptuneSynth.TrackPreview(_song, Pattern.Instrument, note, channel));
    }

    private void InstrumentKeys_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_pageNotes.Remove(e.Key, out int channel)) ChiptuneSynth.TrackPreviewRelease(channel);
    }

    private void ReleasePageNotes()
    {
        foreach (var ch in _pageNotes.Values) ChiptuneSynth.TrackPreviewRelease(ch);
        _pageNotes.Clear();
    }

    // Fewer channels: the notes in the channels taken away stay until the song is saved, then they
    // are gone. Ask first if there are any.
    private async Task<bool> ConfirmFewerChannelsAsync(int channels)
    {
        bool hasNotes = _song.Patterns.Any(p =>
            Enumerable.Range(0, p.Rows).Any(r => Enumerable.Range(channels, _song.Channels - channels).Any(c => !p[r, c].IsEmpty)));
        if (!hasNotes) return true;
        string which = channels + 1 == _song.Channels ? $"Channel {_song.Channels} has" : $"Channels {channels + 1} – {_song.Channels} have";
        return await AskAsync("Fewer channels", $"{which} notes. They are not saved with {channels} channels. Go on?", "Yes", "No");
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
            case Key.F3: ShowInstrumentPage(false); break;
            case Key.F4: ShowInstrumentPage(true); break;
            case Key.Escape: Stop(); break;
            case Key.S when cmd: _ = SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); break;
            case Key.N when cmd: New_OnClick(this, e); break;
            case Key.O when cmd: Load_OnClick(this, e); break;
            case Key.W when cmd: Close(); break;
            case Key.Z when cmd: if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void PlaySong_OnClick(object? sender, RoutedEventArgs e) => PlaySong();
    private void PlayPattern_OnClick(object? sender, RoutedEventArgs e) => PlayPattern(0);
    private void Stop_OnClick(object? sender, RoutedEventArgs e) { Stop(); KeepTypingFocus(); }

    private void Panic_OnClick(object? sender, RoutedEventArgs e)
    {
        Stop();
        TryEngine(ChiptuneSynth.Panic);
        SetStatus("All sound stopped.");
        KeepTypingFocus();
    }
    private void EditToggle_OnClick(object? sender, RoutedEventArgs e) { SetEditMode(EditToggle.IsChecked == true); KeepTypingFocus(); }

    // ════════════════════════════════════════════════════════════════
    //  The order list and patterns
    // ════════════════════════════════════════════════════════════════

    private void OrderInsert_OnClick(object? sender, RoutedEventArgs e)
    {
        PushUndo();
        _song.InsertPosition(_orderPos + 1, _song.Order[_orderPos]);
        // The new place plays what the one before it does, channels and all
        for (int c = 0; c < _song.Channels; c++) _song.SetPatternAt(_orderPos + 1, c, _song.PatternAt(_orderPos, c));
        _orderPos++;
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
    }

    private void OrderDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_song.Order.Count <= 1) return;
        PushUndo();
        _song.RemovePosition(_orderPos);
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
        KeepTypingFocus();
    }

    private void ClonePattern_OnClick(object? sender, RoutedEventArgs e)
    {
        PushUndo();
        var src = _song.Patterns[Pattern.PatternIndex];
        var copy = new TrackerPattern(src.Rows);
        for (int r = 0; r < src.Rows; r++)
        for (int c = 0; c < TrackerSong.MaxChannels; c++)
            copy[r, c] = src[r, c];
        _song.Patterns.Add(copy);
        _song.Order[_orderPos] = _song.Patterns.Count - 1;
        RefreshOrderList();
        SelectOrder(_orderPos);
        SongChanged();
        SetStatus($"Copied to pattern {_song.Patterns.Count - 1:D2}");
        KeepTypingFocus();
    }

    // ════════════════════════════════════════════════════════════════
    //  Instrument
    // ════════════════════════════════════════════════════════════════

    private async void InstAdd_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DhsMode.Is(_song))
        {
            // A DHS song's instruments from 33 are the DMA's samples (channel 4)
            while (_song.Instruments.Count < DhsMode.YmInstruments) _song.Instruments.Add(new TrackerInstrument { Ym = new TrackerYmInstrument() });
            AddInstrument(new TrackerInstrument { Name = "Sample " + (_song.Instruments.Count + 1), Wave = "Sample" });
            SetStatus("A sample for channel 4 (the DMA): choose its WAV file on the instrument page.");
            return;
        }
        if (!await LeaveYmModeAsync("more than its 32 instruments, nor the synth's")) return;
        AddInstrument(new TrackerInstrument { Name = "Instrument " + (_song.Instruments.Count + 1) });
    }

    // A maxYMiser song as the tracker's own: its instruments become the synth's (each sound they make
    // one), its effects the tracker's, as when a maxYMiser song was read in before. After it, the song
    // cannot be saved for maxYMiser any more.
    private async void MenuConvert_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_song.Ym != null) await ConvertFromYmAsync(null);
        else await ConvertToFullAsync(null);
    }

    // A MOD or XM song as the tracker's own: nothing changes but what it is kept within
    private async Task<bool> ConvertToFullAsync(string? why)
    {
        if (_song.Mode == null) return true;
        string text = (why != null ? why + "\n\n" : "") +
                      $"As the tracker's own song (FULL) it can use all the tracker can do, but is no longer kept within what a {_song.Mode} " +
                      "can hold. It can still be saved as XM, the instruments as samples. (Undo takes it back.)";
        if (!await AskAsync("Change to FULL?", text, "Change", "Cancel")) return false;
        PushUndo("full");
        _song.Mode = null;
        UpdateYmMode();
        SongChanged();
        return true;
    }

    // In a MOD or XM song, more than it can hold: the song becomes the tracker's own first, or nothing is done
    private async Task<bool> KeepModeAsync(int channels, int instruments)
    {
        string? what = _song.Mode switch
        {
            "MOD" when channels > 4 => "more than four channels",
            "MOD" when instruments > 31 => "more than 31 instruments",
            "XM" when instruments > 128 => "more than 128 instruments",
            _ => null
        };
        return what == null || await ConvertToFullAsync($"A {_song.Mode} has no {what}.");
    }

    private async Task<bool> ConvertFromYmAsync(string? why)
    {
        if (_song.Ym == null) return true;
        string text = (why != null ? why + "\n\n" : "") +
                      "Turned into the tracker's own song, it can use all the tracker can do, but can no longer be saved " +
                      "as an SNDH file for maxYMiser. (Undo takes it back.)";
        if (!await AskAsync("Convert to the tracker's format?", text, "Convert", "Cancel")) return false;
        Stop();
        try
        {
            var m = YmImport.ToMaxYMiser(_song);
            string safe = new string((_song.Title.Length > 0 ? _song.Title : "song").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string folder = Path.Combine(Workspace.Sound, "Samples", safe);
            string inSong = Path.GetRelativePath(Workspace.Root, folder).Replace('\\', '/');
            if (inSong.StartsWith("..")) inSong = folder;
            var bytes = m.Write();
            var recording = m.DmaUsed ? await Task.Run(() => SndhPlayer.Record(SndhFile.Read(bytes), 0)) : null;
            var result = await Task.Run(() => YmImport.ImportMaxYMiser(MaxYMiserSong.Find(bytes)!, _song.Title, folder, inSong, recording));
            if (result == null) { SetStatus("The song has more sounds than the tracker's 255 instruments."); return false; }
            PushUndo("convert");
            _song = result.Song;
            LoadSongIntoUi(keepPositions: true);
            SongChanged();
            _ = ShowReportAsync("Converted", result.Report ?? "");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            SetStatus("Could not convert: " + ex.Message);
            return false;
        }
    }

    // In a maxYMiser song, something it does not have: the song is converted first, or nothing is done
    private async Task<bool> LeaveYmModeAsync(string what) =>
        _song.Ym == null || await ConvertFromYmAsync($"maxYMiser has no {what}.");

    // The song as maxYMiser's SNDH file again: its own file, with the song as it is now
    private async void ExportSndh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_song.Ym == null) { SetStatus("Only a song read from maxYMiser can be saved for it."); return; }
        string name = string.IsNullOrWhiteSpace(_song.Title) ? "song" : _song.Title;
        if (_path != null) name = Path.GetFileNameWithoutExtension(_path);
        var path = await SaveDialog.AskPathAsync(StorageProvider, new FilePickerSaveOptions
        {
            Title = "Save the song for maxYMiser (SNDH)",
            SuggestedFileName = name + ".sndh",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound),
            FileTypeChoices = new[] { new FilePickerFileType("SNDH (maxYMiser)") { Patterns = new[] { "*.sndh", "*.snd" } } }
        });
        if (path == null) return;
        try
        {
            var bytes = YmImport.SaveMaxYMiser(_song);
            await File.WriteAllBytesAsync(path, bytes);
            SetStatus($"Saved {Path.GetFileName(path)} for maxYMiser ({bytes.Length / 1024} KB)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            SetStatus("Could not save the SNDH file: " + ex.Message);
        }
    }

    // The song as DHS (Dead Hackers Society): YM registers at 50 Hz, DMA samples and LMC1992 commands
    // for the 68000 player in DHSPLAY.S, which is written next to it with a test program, if not there
    private async void ExportDhs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!DhsMode.Is(_song)) { SetStatus("Only a DHS song can be saved as DHS: Song → Make it a DHS song."); return; }
        string name = string.IsNullOrWhiteSpace(_song.Title) ? "song" : _song.Title;
        if (_path != null) name = Path.GetFileNameWithoutExtension(_path);
        // An Atari file name: 8 letters or digits, in capitals
        name = new string(name.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_').Take(8).ToArray());
        if (name.Length == 0) name = "SONG";
        var path = await SaveDialog.AskPathAsync(StorageProvider, new FilePickerSaveOptions
        {
            Title = "Save the song as DHS for the Atari ST",
            SuggestedFileName = name + ".DHS",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound),
            FileTypeChoices = new[] { new FilePickerFileType("DHS (YM, STe DMA, LMC1992)") { Patterns = new[] { "*.DHS", "*.dhs" } } }
        });
        if (path == null) return;

        Stop();
        ReleasePageNotes();
        var song = TrackerSong.FromJson(_song.ToJson());
        try
        {
            SetStatus("Making the DHS file...");
            var result = await Task.Run(() => ChiptuneSynth.ExportDhs(song));
            await File.WriteAllBytesAsync(path, result.Data);
            string folder = Path.GetDirectoryName(path)!;
            var written = new List<string>();
            foreach (var source in new[] { "DHSPLAY.S", "DHSTEST.S" })
            {
                string to = Path.Combine(folder, source);
                if (File.Exists(to)) continue;
                await File.WriteAllTextAsync(to, ChiptuneSynth.DhsSource(source, Path.GetFileName(path).ToUpperInvariant()));
                written.Add(source);
            }
            SetStatus($"Saved {Path.GetFileName(path)} ({result.Data.Length / 1024} KB, {result.Frames / 50.0:0.0} s)"
                      + (written.Count > 0 ? " and " + string.Join(", ", written) : ""));
            if (result.Warnings.Count > 0)
                _ = ShowReportAsync("Saved " + Path.GetFileName(path), string.Join("\n\n", result.Warnings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus("Could not save the DHS file: " + ex.Message);
        }
        finally
        {
            ApplyMutes();
        }
    }

    private void UpdateYmMode()
    {
        MenuSaveSndh.IsEnabled = _song.Ym != null;
        MenuSaveDhs.IsEnabled = DhsMode.Is(_song);
        MenuToDhs.IsEnabled = _song.Ym != null && !DhsMode.Is(_song);
        MenuConvert.IsEnabled = _song.Ym != null || _song.Mode != null;
        UpdateTitle();
    }

    private async void InstClone_OnClick(object? sender, RoutedEventArgs e)
    {
        bool dhsSample = DhsMode.Is(_song) && Pattern.Instrument > DhsMode.YmInstruments;
        if (!dhsSample && !await LeaveYmModeAsync("more than its 32 instruments")) return;
        var inst = CurrentInstrument;
        if (inst == null) return;
        // A copy via JSON, so that lists are not shared
        var json = System.Text.Json.JsonSerializer.Serialize(inst);
        var copy = System.Text.Json.JsonSerializer.Deserialize<TrackerInstrument>(json)!;
        copy.Name = inst.Name + " copy";
        AddInstrument(copy);
    }

    private async void AddInstrument(TrackerInstrument inst)
    {
        if (_song.Instruments.Count >= 255) return;
        if (!await KeepModeAsync(_song.Channels, _song.Instruments.Count + 1)) return;
        PushUndo();
        _song.Instruments.Add(inst);
        Pattern.Instrument = _song.Instruments.Count;
        RefreshInstrumentList();
        RefreshInstrumentPanel();
        SongChanged();
    }

    // Removes the instrument and renumbers the ones after it in all patterns
    private InstrumentLibraryWindow? _library;

    // LIBRARY: one window at a time, which adds to or replaces in the song open here
    // CHIP...: a window of Atari chip music (YM and SNDH) to hear and open
    private YmBrowserWindow? _ymBrowser;

    private void YmBrowser_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_ymBrowser != null) { _ymBrowser.Activate(); return; }
        _ymBrowser = new YmBrowserWindow(async (path, tune) =>
        {
            if (!await ConfirmDiscardAsync()) return;
            await LoadFile(path, tune);
        });
        _ymBrowser.Closed += (_, _) => _ymBrowser = null;
        _ymBrowser.Show(this);
    }

    private async void InstLibrary_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await LeaveYmModeAsync("instruments of the synth")) return;
        if (_library != null) { _library.Activate(); return; }
        _library = new InstrumentLibraryWindow(() => CurrentInstrument, AddInstrument, ReplaceInstrument, PreviewInstrument);
        _library.Closed += (_, _) => { _library = null; ChiptuneSynth.TrackPreviewStop(); };
        _library.Show(this);
    }

    private void ReplaceInstrument(TrackerInstrument inst)
    {
        int n = Pattern.Instrument;
        if (n < 1 || n > _song.Instruments.Count) { AddInstrument(inst); return; }
        PushUndo();
        _song.Instruments[n - 1] = inst;
        RefreshInstrumentList();
        RefreshInstrumentPanel();
        SongChanged();
    }

    private DispatcherTimer? _previewRelease;

    // Plays a note (C in the octave chosen) with an instrument that need not be in the song, on the
    // cursor's channel, and lets go of it after a while
    private void PreviewInstrument(TrackerInstrument inst)
    {
        var song = new TrackerSong { Channels = _song.Channels, Speed = _song.Speed, Tempo = _song.Tempo };
        song.Instruments.Add(inst);
        int channel = Pattern.CursorChannel + 1;
        int note = (Pattern.Octave + 1) * 12;
        TryEngine(() => ChiptuneSynth.TrackPreview(song, 1, note, channel));
        _previewRelease?.Stop();
        _previewRelease = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(inst.Attack + 600, 600, 2500)) };
        _previewRelease.Tick += (_, _) =>
        {
            _previewRelease?.Stop();
            ChiptuneSynth.TrackPreviewRelease(channel);
        };
        _previewRelease.Start();
    }

    private async void InstDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!(DhsMode.Is(_song) && Pattern.Instrument > DhsMode.YmInstruments) && !await LeaveYmModeAsync("fewer than its 32 instruments")) return;
        int n = Pattern.Instrument;
        if (n < 1 || n > _song.Instruments.Count) return;
        PushUndo();
        _song.Instruments.RemoveAt(n - 1);
        foreach (var pat in _song.Patterns)
            for (int r = 0; r < pat.Rows; r++)
            for (int c = 0; c < TrackerSong.MaxChannels; c++)
            {
                var cell = pat[r, c];
                if (cell.Instrument < n) continue;
                int inst = cell.Instrument == n ? 0 : cell.Instrument - 1;
                pat[r, c] = new TrackerCell(cell.Note, inst, cell.Effect, cell.Param, cell.Volume);
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
        // The open song's folder, otherwise the Sound folder's Tracker folder
        if (_path != null && Directory.Exists(Path.GetDirectoryName(_path)))
            return await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_path)!);
        return await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound, "Tracker");
    }

    private async void New_OnClick(object? sender, RoutedEventArgs e)
    {
        string? kind = await AskNewSongAsync();
        if (kind == null) { KeepTypingFocus(); return; }
        if (!await ConfirmDiscardAsync()) return;
        TrackerSong song;
        if (kind is "maxYMiser" or "DHS")
        {
            var template = await MaxYMiserTemplateAsync();
            if (template == null) { KeepTypingFocus(); return; }
            try { song = YmImport.NewMaxYMiser(template, "New song"); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or IndexOutOfRangeException)
            {
                SetStatus("Could not start a maxYMiser song from the template: " + ex.Message);
                return;
            }
            if (kind == "DHS") DhsMode.Convert(song);
        }
        else
        {
            song = CreateNewSong();
            if (kind == "MOD") { song.Mode = "MOD"; song.Channels = 4; }
            if (kind == "XM") { song.Mode = "XM"; song.Channels = 8; }
        }
        Stop();
        _song = song;
        _path = null;
        _undo.Clear();
        _redo.Clear();
        LoadSongIntoUi();
        _dirty = false;
        UpdateTitle();
        KeepTypingFocus();
    }

    // What kinds of song NEW makes, and what each is
    private static readonly (string kind, string title, string text, bool ready)[] NewSongKinds =
    {
        ("FULL", "FULL – the tracker's own",
         "All the tracker can do: the synth's instruments, samples, 3 – 32 channels and every effect. " +
         "Saved as a tracker song, and as WAV or XM.", true),
        ("MOD", "MOD – ProTracker",
         "Four channels and up to 31 instruments, as on the Amiga. The synth's instruments become samples when it is saved " +
         "as a module. (Saving as .mod comes next; until then it is saved as a tracker song or XM.)", true),
        ("XM", "XM – FastTracker 2",
         "Up to 32 channels and 128 instruments. Saved as an XM module with the instruments as samples, for other trackers and players.", true),
        ("maxYMiser", "SNDH – maxYMiser on the Atari ST",
         "The Atari ST's sound chip, as in gwEm's maxYMiser: three tracks, 32 instruments made of sequences, two effects on a line. " +
         "Saved as an SNDH file that maxYMiser opens and SNDH players play.", true),
        ("DHS", "DHS – the Atari ST from the VBL (Dead Hackers Society)",
         "A maxYMiser song kept within what a small 68000 player called once a frame can play: the YM without timer effects " +
         "on channels 1 – 3, and the STe's DMA samples and volume chip (LMC1992) on channel 4. Saved as DHS for DHSPLAY.S, " +
         "and for maxYMiser too.", true),
        ("SID", "SID – the Commodore 64",
         "The C64's SID chip, saved to play on a C64. Not yet.", false),
    };

    // The window NEW opens: which kind of song. Null: none (cancelled)
    private async Task<string?> AskNewSongAsync()
    {
        var dialog = new Window
        {
            Title = "New song",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        string? chosen = null;
        var list = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        list.Children.Add(new TextBlock { Text = "What kind of song?", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var k in NewSongKinds)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = k.ready,
                Padding = new Thickness(10, 6),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = k.title, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = k.text, TextWrapping = TextWrapping.Wrap, FontSize = 12 }
                    }
                }
            };
            string kind = k.kind;
            button.Click += (_, _) => { chosen = kind; dialog.Close(); };
            list.Children.Add(button);
        }
        list.Children.Add(new TextBlock
        {
            Text = "A MOD, XM or maxYMiser song can be made the tracker's own later: Song → Convert to FULL.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0)
        });
        var cancel = new Button { Content = "Cancel", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => dialog.Close();
        list.Children.Add(cancel);
        dialog.Content = list;
        await dialog.ShowDialog(this);
        return chosen;
    }

    // A new maxYMiser song starts from a file maxYMiser saved, for its replay (gwEm's, which is not ours
    // to hand out): the template in Sound/Tracker/Templates, or one chosen and kept there
    private static string MaxYMiserTemplatePath => Path.Combine(Workspace.Sound, "Tracker", "Templates", "maxYMiser.sndh");

    private async Task<byte[]?> MaxYMiserTemplateAsync()
    {
        string path = MaxYMiserTemplatePath;
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path);
        if (!await AskAsync("A song from maxYMiser",
                "A new maxYMiser song starts from a file maxYMiser has saved, and plays with its replay. Choose one: an empty song " +
                "saved in maxYMiser, or one of its songs (its instruments and sequences are kept, its notes are not). " +
                "It is kept in Sound/Tracker/Templates for the next time.", "Choose...", "Cancel"))
            return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "A song saved by maxYMiser",
            FileTypeFilter = new[] { new FilePickerFileType("SNDH (maxYMiser)") { Patterns = new[] { "*.sndh", "*.snd" } } },
            SuggestedStartLocation = await StartFolderAsync()
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } chosen) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(chosen);
            var data = SndhFile.IsSndh(bytes) ? SndhFile.Read(bytes).Data : bytes;
            if (MaxYMiserSong.Count(data) != 1 || MaxYMiserSong.Find(data) == null)
            {
                SetStatus($"{Path.GetFileName(chosen)} is not one song saved by maxYMiser.");
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            SetStatus("Could not read the file: " + ex.Message);
            return null;
        }
    }

    private async void Load_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load song",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Songs and modules") { Patterns = new[] { "*.amostrk", "*.mod", "*.xm", "*.ym", "*.sndh", "*.snd", "*.sid" } },
                SongFileType,
                new FilePickerFileType("Modules (MOD, XM)") { Patterns = new[] { "*.mod", "*.xm" } },
                new FilePickerFileType("Atari ST chip music (YM, SNDH)") { Patterns = new[] { "*.ym", "*.sndh", "*.snd" } },
                new FilePickerFileType("Commodore 64 music (SID)") { Patterns = new[] { "*.sid" } }
            },
            SuggestedStartLocation = await StartFolderAsync()
        });
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (path != null) await LoadFile(path);
    }

    // tune: which tune of an SNDH or SID file (0: the one it starts with). Done when the song is in the
    // tracker (a module or chip music is read in the background)
    public Task LoadFile(string path, int tune = 0)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".mod" or ".xm" or ".ym" or ".sndh" or ".snd" or ".sid" || IsModFile(path))
            return ImportModAsync(path, tune);
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
            AddRecent(path);
            KeepTypingFocus();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not load: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    // A ProTracker module ("M.K." at byte 1080) or an XM file, whatever its file name
    private static bool IsModFile(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[17];
            if (f.Read(head, 0, 17) == 17 && (XmImport.IsXm(head) || YmFile.IsChipMusic(head))) return true;
            if (f.Length < 1084) return false;
            var tag = new byte[4];
            f.Seek(1080, SeekOrigin.Begin);
            f.ReadExactly(tag);
            return System.Text.Encoding.ASCII.GetString(tag) is "M.K." or "M!K!" or "FLT4" or "4CHN" or "6CHN" or "8CHN";
        }
        catch (Exception) { return false; }
    }

    // A MOD or XM file becomes a new, unsaved song; its samples are written as WAV files in
    // Sound/Samples/<module name>/, which the song refers to. It is read in the background: chip music
    // is run through its player first, which can take some seconds, shown in a window with how far it is
    private bool _importing;

    private async Task ImportModAsync(string path, int tune = 0)
    {
        if (_importing) return;
        _importing = true;
        Window? waiting = null;
        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string folder = Path.Combine(Workspace.Sound, "Samples", safe);
            string inSong = Path.GetRelativePath(Workspace.Root, folder).Replace('\\', '/');
            if (inSong.StartsWith("..")) inSong = folder;   // the Sound folder is moved outside the working folder

            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(0, 8, 0, 0) };
            var percent = new TextBlock { Text = "0 %", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            void Progress(double done) => Dispatcher.UIThread.Post(() =>
            {
                bar.Value = done * 100;
                percent.Text = $"{(int)(done * 100)} %";
            });
            var work = Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(path);
                return XmImport.IsXm(bytes) ? XmImport.Import(bytes, name, folder, inSong)
                    : SidFile.IsSid(bytes) ? YmImport.ImportSid(SidFile.Load(path), name, tune, progress: Progress)
                    : YmFile.IsChipMusic(bytes) ? YmImport.OpenChipMusic(bytes, name, tune, folder, inSong, Progress)
                    : ModImport.Import(bytes, name, folder, inSong);
            });
            // Only a read that takes a while shows the window
            if (await Task.WhenAny(work, Task.Delay(300)) != work)
            {
                waiting = new Window
                {
                    Title = "Reading",
                    Width = 360,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(14),
                        Children = { new TextBlock { Text = $"Reading {Path.GetFileName(path)}...", TextTrimming = TextTrimming.CharacterEllipsis }, bar, percent }
                    }
                };
                _ = waiting.ShowDialog(this);
            }
            var result = await work;
            waiting?.Close();
            waiting = null;
            Stop();
            _song = result.Song;
            _path = null;   // saved as a tracker song the first time SAVE is pressed
            AddRecent(path);
            _undo.Clear();
            _redo.Clear();
            LoadSongIntoUi();
            _dirty = true;
            UpdateTitle();
            UpdateStatus();
            KeepTypingFocus();
            string samples = result.Samples > 0 ? $", {result.Samples} samples (in Sound/Samples/{safe})" : "";
            SetStatus($"Read {Path.GetFileName(path)}: {result.Song.Patterns.Count} patterns{samples}. " +
                      string.Join(" ", result.Notes) + " Save it to keep it as a tracker song.");
            if (result.Report != null)
                _ = ShowReportAsync("Read " + Path.GetFileName(path), result.Report,
                    result.Song.Instruments.Count > 1 ? ("Simplify instruments", Simplify) : null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not read the module: " + ex.Message);
        }
        finally
        {
            waiting?.Close();
            _importing = false;
        }
    }

    // The song as a WAV file, rendered faster than it plays; muted channels are left out
    private async void ExportWav_OnClick(object? sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(_song.Title) ? "song" : _song.Title;
        if (_path != null) name = Path.GetFileNameWithoutExtension(_path);
        var path = await SaveDialog.AskPathAsync(StorageProvider, new FilePickerSaveOptions
        {
            Title = "Save the song as WAV",
            SuggestedFileName = name + ".wav",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound),
            FileTypeChoices = new[] { new FilePickerFileType("WAV sound") { Patterns = new[] { "*.wav" } } }
        });
        if (path == null) return;

        Stop();
        ReleasePageNotes();
        ApplyMutes();
        ExportWavButton.IsEnabled = false;
        var song = TrackerSong.FromJson(_song.ToJson());   // a copy, so editing on does not change it half way
        try
        {
            var samples = await Task.Run(() => ChiptuneSynth.RenderSong(song, progress: s =>
                Dispatcher.UIThread.Post(() => SetStatus($"Making the WAV file... {s:0} s"))));
            await Task.Run(() => ChiptuneSynth.WriteWav(path, samples));
            SetStatus($"Saved {Path.GetFileName(path)} ({samples.Length / 2.0 / ChiptuneSynth.SampleRate:0.0} s, stereo)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save the WAV file: " + ex.Message);
        }
        finally
        {
            ExportWavButton.IsEnabled = true;
            ApplyMutes();
        }
    }

    // The song as a FastTracker 2 module; the synth's instruments are played into samples, which takes
    // the synth a moment, as the WAV file does
    private async void ExportXm_OnClick(object? sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(_song.Title) ? "song" : _song.Title;
        if (_path != null) name = Path.GetFileNameWithoutExtension(_path);
        var path = await SaveDialog.AskPathAsync(StorageProvider, new FilePickerSaveOptions
        {
            Title = "Save the song as XM",
            SuggestedFileName = name + ".xm",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound),
            FileTypeChoices = new[] { new FilePickerFileType("FastTracker 2 module") { Patterns = new[] { "*.xm" } } }
        });
        if (path == null) return;

        Stop();
        ReleasePageNotes();
        ExportXmButton.IsEnabled = false;
        var song = TrackerSong.FromJson(_song.ToJson());   // a copy, so editing on does not change it half way
        try
        {
            var result = await Task.Run(() => XmExport.Write(song, (notes, tempo) => ChiptuneSynth.RenderNotes(notes, tempo,
                done => Dispatcher.UIThread.Post(() => SetStatus($"Making the XM file... {(int)(done * 100)} %")))));
            await File.WriteAllBytesAsync(path, result.Bytes);
            SetStatus($"Saved {Path.GetFileName(path)} ({result.Bytes.Length / 1024} KB)");
            if (result.Notes.Count > 0)
                _ = ShowReportAsync("Saved " + Path.GetFileName(path), string.Join("\n\n", result.Notes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save the XM file: " + ex.Message);
        }
        finally
        {
            ExportXmButton.IsEnabled = true;
            ApplyMutes();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  The menu
    // ════════════════════════════════════════════════════════════════

    // Set by AMOS Like Basic, so that a theme chosen here is its theme too; on its own the tracker
    // keeps the theme in AMOS Like Basic's settings, which both read when they start
    public static Action<string>? ThemeChanger { get; set; }

    private static readonly (string Name, string Key)[] Themes =
    {
        ("Light Clean", "LightClean"), ("Dark Pro", "DarkPro"), ("Classic AMOS", "ClassicBlue"), ("Workbench", "Workbench"),
        ("C64", "C64"), ("Stos Classic", "StosClassic"), ("Stos Editor", "StosEditor"), ("Emerald", "Emerald"),
        ("Neon Night", "NeonNight"), ("Catppuccin Mocha", "CatppuccinMocha")
    };

    private void BuildMenu()
    {
        // Cmd on a Mac, Ctrl elsewhere
        var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        MenuNew.InputGesture = new KeyGesture(Key.N, cmd);
        MenuOpen.InputGesture = new KeyGesture(Key.O, cmd);
        MenuSave.InputGesture = new KeyGesture(Key.S, cmd);
        MenuSaveAs.InputGesture = new KeyGesture(Key.S, cmd | KeyModifiers.Shift);
        MenuClose.InputGesture = new KeyGesture(Key.W, cmd);
        MenuUndo.InputGesture = new KeyGesture(Key.Z, cmd);
        MenuRedo.InputGesture = new KeyGesture(Key.Z, cmd | KeyModifiers.Shift);

        // The themes, the one in use marked (looked up again when the menu opens, as AMOS Like Basic
        // may have changed it)
        var themeItems = Themes.Select(t =>
        {
            var item = new MenuItem { Header = t.Name, ToggleType = MenuItemToggleType.Radio, Tag = t.Key };
            item.Click += (_, _) => ChangeTheme(t.Key);
            return item;
        }).ToList();
        ThemeMenu.ItemsSource = themeItems;
        void MarkTheme()
        {
            var config = new ConfigService("AmosProject");
            config.Load();
            string current = AmosThemes.ByName(config.Config.DefaultTheme).Name;
            foreach (var item in themeItems) item.IsChecked = AmosThemes.ByName((string)item.Tag!).Name == current;
        }
        ThemeMenu.SubmenuOpened += (_, _) => MarkTheme();
        MarkTheme();

        FillRecentMenu();
    }

    private static void ChangeTheme(string key)
    {
        if (ThemeChanger != null) { ThemeChanger(key); return; }
        ThemeResources.Apply(AmosThemes.ByName(key));
        var config = new ConfigService("AmosProject");
        config.Load();
        config.Config.DefaultTheme = key;
        config.Save();
    }

    // The songs and music opened or saved last, newest first, in the settings of this name (the
    // programs that open the tracker set it; without it nothing is remembered, as in the tests)
    public static string? RecentSettings { get; set; }
    private const int RecentCount = 10;

    private static List<string> RecentFiles()
    {
        if (RecentSettings == null) return new List<string>();
        var config = new ConfigService(RecentSettings);
        config.Load();
        return config.Config.RecentFiles;
    }

    private void AddRecent(string path)
    {
        if (RecentSettings == null) return;
        try
        {
            var config = new ConfigService(RecentSettings);
            config.Load();
            var list = config.Config.RecentFiles;
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            if (list.Count > RecentCount) list.RemoveRange(RecentCount, list.Count - RecentCount);
            config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        FillRecentMenu();
    }

    private void FillRecentMenu()
    {
        var files = RecentFiles().Where(File.Exists).ToList();
        RecentMenu.IsEnabled = files.Count > 0;
        RecentMenu.ItemsSource = files.Select(f =>
        {
            var item = new MenuItem { Header = Path.GetFileName(f) };
            ToolTip.SetTip(item, f);
            item.Click += async (_, _) =>
            {
                if (!await ConfirmDiscardAsync()) return;
                await LoadFile(f);
            };
            return (object)item;
        }).ToList();
    }

    private void MenuClose_OnClick(object? sender, RoutedEventArgs e) => Close();
    private void MenuUndo_OnClick(object? sender, RoutedEventArgs e) => Undo();
    private void MenuRedo_OnClick(object? sender, RoutedEventArgs e) => Redo();
    private void MenuEditMode_OnClick(object? sender, RoutedEventArgs e) { SetEditMode(!Pattern.EditMode); KeepTypingFocus(); }
    private void MenuPlayFromRow_OnClick(object? sender, RoutedEventArgs e) => PlayPattern(Pattern.CursorRow);

    private const string ManualFile = "AMOS Like Basic - Manual.pdf";
    private const string ManualOnline = "https://github.com/MrSkutt/AmosLikeBasic/blob/main/Documentation/Manual/26-sound.md";

    // The manual: next to the programs in a download (the tracker app is in a folder of its own on
    // Windows, three folders down in a Mac app), or in Documentation when run from the source
    public static string? FindManual()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; dir != null && up < 8; up++, dir = dir.Parent)
            foreach (var path in new[] { Path.Combine(dir.FullName, ManualFile), Path.Combine(dir.FullName, "Documentation", ManualFile) })
                if (File.Exists(path)) return path;
        return null;
    }

    private async void MenuManual_OnClick(object? sender, RoutedEventArgs e)
    {
        if (FindManual() is { } path && await Launcher.LaunchFileInfoAsync(new FileInfo(path))) return;
        SetStatus("The manual was not found next to the program; the tracker's chapter opens online.");
        await Launcher.LaunchUriAsync(new Uri(ManualOnline));
    }

    private async void MenuManualOnline_OnClick(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(ManualOnline));

    private void MenuKeys_OnClick(object? sender, RoutedEventArgs e)
    {
        string c = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
        string alt = OperatingSystem.IsMacOS() ? "Option" : "Alt";
        _ = ShowReportAsync("Keys", string.Join("\n", new[]
        {
            "Notes: the bottom row of letters (Z S X D C V G B H N J M) is one octave, the top row (Q 2 W 3 E R 5 T 6 Y 7 U I) the next; 1 lets go of the note (===).",
            "F1 / F2   the octave down / up",
            "Space   edit mode on and off (stops the music when it plays)",
            "Arrows, Tab, Page Up / Down, Home / End   move around the pattern",
            "Delete   clears the cell",
            "",
            "F3 / F4   the pattern page / the instrument page",
            "F5   play the song      F6   play the pattern      F7   play from the row      Esc   stop",
            "",
            $"{c}+C / X / V   copy, cut and paste the selection (Shift and the arrows select)",
            $"{c}+A   select all      {c}+↑ / ↓   transpose a semitone (with Shift an octave)",
            $"{c}+I / {c}+Backspace   insert / delete a row in the channel (with Shift in all of them)",
            $"{c}+Z   undo      {c}+Shift+Z or {c}+Y   redo",
            $"{alt}+↑ / ↓   the channel plays another pattern at this place      {alt}+N   a pattern of its own",
            "",
            $"{c}+N   new      {c}+O   open      {c}+S   save      {c}+Shift+S   save as      {c}+W   close",
            "The number boxes: their arrows or the mouse wheel; type a number and Enter keeps it, Esc goes back."
        }));
    }

    private void MenuAbout_OnClick(object? sender, RoutedEventArgs e)
    {
        string version = System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "";
        if (version is "" or "1.0.0") version = "a build of its own";
        _ = ShowReportAsync("About AMOS Tracker", string.Join("\n", new[]
        {
            "AMOS Tracker, version " + version,
            "Part of AMOS Like Basic: github.com/MrSkutt/AmosLikeBasic",
            "",
            "A tracker for chip music with a synth of its own, which also reads and writes the music of",
            "the old machines:",
            "  MOD and XM modules (ProTracker, FastTracker 2), read and saved as XM",
            "  YM and SNDH from the Atari ST, played on an emulated 68000 and STe",
            "  maxYMiser songs in SNDH files, opened as maxYMiser has them, played by the sound",
            "  chip as its replay plays them, and saved for maxYMiser again",
            "  SID files from the Commodore 64, played on an emulated 6510 and SID",
            "",
            "Songs are played in BASIC with TRACK LOAD and TRACK PLAY.",
            "",
            "Thanks to the SNDH archive, the High Voltage SID Collection and gwEm's maxYMiser."
        }));
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task<bool> SaveAsync(bool askForName)
    {
        string? path = _path;
        if (path == null || askForName)
        {
            path = await SaveDialog.AskPathAsync(StorageProvider, new FilePickerSaveOptions
            {
                Title = "Save song",
                FileTypeChoices = new[] { SongFileType },
                SuggestedFileName = SafeFileName(_song.Title),
                SuggestedStartLocation = await StartFolderAsync()
            });
            if (path == null) return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _song.Save(path);
            _path = path;
            _dirty = false;
            AddRecent(path);
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

    // SIMPLIFY: fewer instruments, with their arpeggios and vibratos as effects
    private async void InstSimplify_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await LeaveYmModeAsync("instruments of the synth to simplify")) return;
        Simplify();
    }

    private void Simplify()
    {
        PushUndo();
        var r = InstrumentSimplifier.Simplify(_song);
        if (r.After == r.Before)
        {
            _undo.RemoveAt(_undo.Count - 1);
            SetStatus("Nothing to simplify: no instruments that are the same sound with an arpeggio or vibrato on top.");
            KeepTypingFocus();
            return;
        }
        Stop();
        LoadSongIntoUi(keepPositions: true);
        SongChanged();
        string effects = r.Arpeggios + r.Vibratos > 0
            ? $" {r.Arpeggios} arpeggios became 0xy and {r.Vibratos} vibratos 4xy in the patterns." : "";
        string patterns = r.PatternsMerged > 0 ? $" {r.PatternsMerged} patterns that became the same are written once." : "";
        SetStatus($"{r.Before} instruments became {r.After}.{effects}{patterns} It sounds nearly as before; undo ({(OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl")}+Z) brings the old ones back.");
        KeepTypingFocus();
    }

    // A window that tells what was found when a file was read, with a button for a next step
    private async Task ShowReportAsync(string title, string text, (string label, Action action)? extra = null)
    {
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(5) };
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        ok.Click += (_, _) => dialog.Close();
        var lines = new StackPanel { Margin = new Thickness(14), Spacing = 2 };
        foreach (var line in text.Split('\n'))
            lines.Children.Add(new TextBlock { Text = line.Length == 0 ? " " : line, TextWrapping = TextWrapping.Wrap });
        ((TextBlock)lines.Children[0]).FontWeight = FontWeight.Bold;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        if (extra is { } x)
        {
            lines.Children.Add(new TextBlock
            {
                Text = "Simplify instruments makes fewer instruments: the arpeggios and vibratos become effects in the patterns, " +
                       "and instruments that sound nearly the same become one. Easier to change, but it sounds nearly, not exactly, " +
                       "as the file does. Undo brings it back; the SIMPLIFY button does it later too.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0)
            });
            var more = new Button { Content = x.label, Margin = new Thickness(5) };
            more.Click += (_, _) => { dialog.Close(); x.action(); };
            buttons.Children.Add(more);
        }
        buttons.Children.Add(ok);
        lines.Children.Add(buttons);
        dialog.Content = lines;
        await dialog.ShowDialog(this);
        KeepTypingFocus();
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
        string mode = DhsMode.Is(_song) ? " (DHS)" : _song.Ym != null ? " (maxYMiser)" : _song.Mode != null ? $" ({_song.Mode})" : "";
        Title = $"Tracker - {name}{mode}{(_dirty ? " *" : "")}";
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
