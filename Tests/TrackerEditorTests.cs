using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AmosLikeBasic;

namespace AmosLikeBasic.Tests;

// The tracker window, driven with real key presses
public class TrackerEditorTests
{
    private static (TrackerWindow window, PatternView pattern) Open()
    {
        var window = new TrackerWindow { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var pattern = window.FindControl<PatternView>("Pattern")!;
        pattern.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(pattern.EditMode);   // the tracker opens with edit off ...
        Press(window, Key.Space);         // ... and Space turns it on, so that notes are written
        Assert.True(pattern.EditMode);
        return (window, pattern);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Cell(PatternView p, int row, int channel) => p.Song!.Patterns[p.PatternIndex][row, channel].ToString();

    private static void Close(TrackerWindow w)
    {
        // Throw away the changes without asking
        typeof(TrackerWindow).GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(w, false);
        w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void PianoKeysWriteNotesAndMoveDown()
    {
        var (w, p) = Open();
        Press(w, Key.Z);
        Press(w, Key.S);
        Press(w, Key.Q);
        Press(w, Key.D1);
        Assert.Equal("C-4 01 .. ...", Cell(p, 0, 0));
        Assert.Equal("C#4 01 .. ...", Cell(p, 1, 0));
        Assert.Equal("C-5 01 .. ...", Cell(p, 2, 0));
        Assert.Equal("=== .. .. ...", Cell(p, 3, 0));
        Assert.Equal(4, p.CursorRow);
        Close(w);
    }

    [AvaloniaFact]
    public void HexDigitsWriteTheEffect()
    {
        var (w, p) = Open();
        Press(w, Key.Z);
        Press(w, Key.Home);
        for (int i = 0; i < 5; i++) Press(w, Key.Right);
        Press(w, Key.A);
        Press(w, Key.Up);
        Press(w, Key.Right);
        Press(w, Key.D0);
        Press(w, Key.Up);
        Press(w, Key.Right);
        Press(w, Key.D4);
        Assert.Equal("C-4 01 .. A04", Cell(p, 0, 0));
        Close(w);
    }

    [AvaloniaFact]
    public void HexDigitsWriteTheVolume()
    {
        var (w, p) = Open();
        Press(w, Key.Z);
        Press(w, Key.Home);
        for (int i = 0; i < 3; i++) Press(w, Key.Right);   // the volume column
        Press(w, Key.D3);
        Press(w, Key.Up);
        Press(w, Key.Right);
        Press(w, Key.D2);
        Assert.Equal("C-4 01 32 ...", Cell(p, 0, 0));
        Press(w, Key.Up);
        Press(w, Key.Left);
        Press(w, Key.D7);                                   // above 40 is 40
        Assert.Equal("C-4 01 40 ...", Cell(p, 0, 0));
        Press(w, Key.Up);
        Press(w, Key.Delete);
        Assert.Equal("C-4 01 .. ...", Cell(p, 0, 0));
        Close(w);
    }

    [AvaloniaFact]
    public void TransposeCopyPasteAndUndo()
    {
        var (w, p) = Open();
        Press(w, Key.Z);
        Press(w, Key.X);
        Press(w, Key.Home);
        Press(w, Key.Down, RawInputModifiers.Shift);
        Press(w, Key.Up, RawInputModifiers.Meta | RawInputModifiers.Shift);
        Assert.Equal("C-5 01 .. ...", Cell(p, 0, 0));
        Assert.Equal("D-5 01 .. ...", Cell(p, 1, 0));

        Press(w, Key.C, RawInputModifiers.Meta);
        Press(w, Key.Tab);
        Press(w, Key.Home);
        Press(w, Key.V, RawInputModifiers.Meta);
        Assert.Equal("C-5 01 .. ...", Cell(p, 0, 1));

        Press(w, Key.Z, RawInputModifiers.Meta);
        Assert.Equal("--- .. .. ...", Cell(p, 0, 1));
        Press(w, Key.Z, RawInputModifiers.Meta);
        Assert.Equal("C-4 01 .. ...", Cell(p, 0, 0));
        Press(w, Key.Z, RawInputModifiers.Meta | RawInputModifiers.Shift);
        Assert.Equal("C-5 01 .. ...", Cell(p, 0, 0));
        Close(w);
    }

    [AvaloniaFact]
    public void RowsCanBeInsertedAndRemoved()
    {
        var (w, p) = Open();
        Press(w, Key.Z);
        Press(w, Key.Home);
        Press(w, Key.I, RawInputModifiers.Meta);
        Assert.Equal("--- .. .. ...", Cell(p, 0, 0));
        Assert.Equal("C-4 01 .. ...", Cell(p, 1, 0));
        Press(w, Key.Back, RawInputModifiers.Meta);
        Assert.Equal("C-4 01 .. ...", Cell(p, 0, 0));
        Press(w, Key.Back);
        Assert.Equal("--- .. .. ...", Cell(p, 0, 0));
        Close(w);
    }

    [AvaloniaFact]
    public void EditModeOffOnlyPreviews()
    {
        var (w, p) = Open();
        Press(w, Key.Space);
        Assert.False(p.EditMode);
        Press(w, Key.Z);
        Assert.Equal("--- .. .. ...", Cell(p, 0, 0));
        Close(w);
    }

    [AvaloniaFact]
    public void SongsLoadIntoTheEditor()
    {
        var (w, p) = Open();
        w.LoadFile(Repo.PathOf("Sound/Tracker/forest_song.amostrk"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Tracker - forest_song.amostrk", w.Title);
        Assert.Equal("G-5 01 .. ...", Cell(p, 0, 0));
        Close(w);
    }
}
