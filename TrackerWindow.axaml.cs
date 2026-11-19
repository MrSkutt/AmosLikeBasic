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
        panel.Children.Add(cards);

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
        var o1 = KnobRow(osc);
        AddKnob(o1, "VOLUME", 0, 1, i => i.Volume, (i, v) => i.Volume = v, "volume", "0.00", step: 0.01);
        AddKnob(o1, "WIDTH", 1, 99, i => i.PulseWidth, (i, v) => i.PulseWidth = v, "width", unit: "%", step: 1);
        AddKnob(o1, "PWM", 0, 49, i => i.PwmDepth, (i, v) => i.PwmDepth = v, "pwm", unit: "%", step: 1);
        AddKnob(o1, "PWM RATE", 0, 20, i => i.PwmRate, (i, v) => i.PwmRate = v, "pwm rate", "0.0", "Hz", step: 0.1);
        AddSubHeading(osc, "OSC 2");
        AddChoice(osc, new[] { "Off", "Pulse", "Square", "Triangle", "Sawtooth", "Sine", "Noise" },
            new[] { "OFF", "PULSE", "SQUARE", "TRI", "SAW", "SINE", "NOISE" }, i => i.Osc2Wave ?? "Off", (i, v) => i.Osc2Wave = v, "osc2 wave");
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
        sampleCard.Children.Add(new TextBlock
        {
            Text = "A loop keeps a held note sounding; LOOP END at 0 plays the sample once. Effect 9xx starts it xx × 256 frames in.",
            Classes = { "amos-label" }, TextWrapping = TextWrapping.Wrap, FontSize = 11
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
        if (TryEngine(() => ChiptuneSynth.TrackPlayPattern(_song, Pattern.PatternIndex, row)))
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
            case Key.S when cmd: _ = SaveAsync(false); break;
            case Key.Z when cmd: if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void PlaySong_OnClick(object? sender, RoutedEventArgs e) => PlaySong();
    private void PlayPattern_OnClick(object? sender, RoutedEventArgs e) => PlayPattern(0);
    private void Stop_OnClick(object? sender, RoutedEventArgs e) { Stop(); KeepTypingFocus(); }
    private void EditToggle_OnClick(object? sender, RoutedEventArgs e) { SetEditMode(EditToggle.IsChecked == true); KeepTypingFocus(); }

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
    private InstrumentLibraryWindow? _library;

    // LIBRARY: one window at a time, which adds to or replaces in the song open here
    private void InstLibrary_OnClick(object? sender, RoutedEventArgs e)
    {
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

    private void InstDelete_OnClick(object? sender, RoutedEventArgs e)
    {
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
        if (!await ConfirmDiscardAsync()) return;
        Stop();
        _song = CreateNewSong();
        _path = null;
        _undo.Clear();
        _redo.Clear();
        LoadSongIntoUi();
        _dirty = false;
        UpdateTitle();
        KeepTypingFocus();
    }

    private async void Load_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load song",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Songs and modules") { Patterns = new[] { "*.amostrk", "*.mod", "*.xm" } },
                SongFileType,
                new FilePickerFileType("Modules (MOD, XM)") { Patterns = new[] { "*.mod", "*.xm" } }
            },
            SuggestedStartLocation = await StartFolderAsync()
        });
        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (path != null) LoadFile(path);
    }

    public void LoadFile(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".mod" or ".xm" || IsModFile(path))
        {
            ImportMod(path);
            return;
        }
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
            KeepTypingFocus();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not load: " + ex.Message);
        }
    }

    // A ProTracker module ("M.K." at byte 1080) or an XM file, whatever its file name
    private static bool IsModFile(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[17];
            if (f.Read(head, 0, 17) == 17 && XmImport.IsXm(head)) return true;
            if (f.Length < 1084) return false;
            var tag = new byte[4];
            f.Seek(1080, SeekOrigin.Begin);
            f.ReadExactly(tag);
            return System.Text.Encoding.ASCII.GetString(tag) is "M.K." or "M!K!" or "FLT4" or "4CHN" or "6CHN" or "8CHN";
        }
        catch (Exception) { return false; }
    }

    // A MOD or XM file becomes a new, unsaved song; its samples are written as WAV files in
    // Sound/Samples/<module name>/, which the song refers to
    private void ImportMod(string path)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string folder = Path.Combine(Workspace.Sound, "Samples", safe);
            string inSong = Path.GetRelativePath(Workspace.Root, folder).Replace('\\', '/');
            if (inSong.StartsWith("..")) inSong = folder;   // the Sound folder is moved outside the working folder
            var bytes = File.ReadAllBytes(path);
            var result = XmImport.IsXm(bytes)
                ? XmImport.Import(bytes, name, folder, inSong)
                : ModImport.Import(bytes, name, folder, inSong);
            Stop();
            _song = result.Song;
            _path = null;   // saved as a tracker song the first time SAVE is pressed
            _undo.Clear();
            _redo.Clear();
            LoadSongIntoUi();
            _dirty = true;
            UpdateTitle();
            UpdateStatus();
            KeepTypingFocus();
            SetStatus($"Read {Path.GetFileName(path)}: {result.Song.Patterns.Count} patterns, {result.Samples} samples (in Sound/Samples/{safe}). " +
                      string.Join(" ", result.Notes) + " Save it to keep it as a tracker song.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not read the module: " + ex.Message);
        }
    }

    // The song as a WAV file, rendered faster than it plays; muted channels are left out
    private async void ExportWav_OnClick(object? sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(_song.Title) ? "song" : _song.Title;
        if (_path != null) name = Path.GetFileNameWithoutExtension(_path);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the song as WAV",
            SuggestedFileName = name + ".wav",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound),
            FileTypeChoices = new[] { new FilePickerFileType("WAV sound") { Patterns = new[] { "*.wav" } } }
        });
        if (file?.TryGetLocalPath() is not { } path) return;

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
            // Some save dialogs add the extension to a name that already has it
            while (path.EndsWith(".amostrk.amostrk", StringComparison.OrdinalIgnoreCase)) path = path[..^".amostrk".Length];
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
