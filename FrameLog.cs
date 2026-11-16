using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AmosLikeBasic;

// A timing log for finding out why movement is not smooth. Off unless the environment variable
// AMOS_FRAMELOG names a file; then every screen refresh, every WAIT VBL and every time the game
// screen is drawn is written there with its time in milliseconds:
//
//   AMOS_FRAMELOG=frames.csv dotnet run -- --play Projects/parallax_layers.amosproj
//
// Columns: time, event, frame number, extra. Events: tick (screen refresh reported to
// DisplayClock), vbl (WAIT VBL done, frame N presented; extra = the .NET garbage collector's
// total pause time so far in ms), paint (the view built its draw call for frame N, UI thread;
// extra = the layers it drew, as bits: layer 0 = 1, layer 1 = 2, layer 2 = 4 ...),
// draw (the frame was drawn on the render thread; extra = ms it took).
public static class FrameLog
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("AMOS_FRAMELOG");
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Timer? _flush;

    public static bool On { get; } = !string.IsNullOrEmpty(Path);

    public static void Write(string evt, long frame, double extra = 0)
    {
        if (!On) return;
        Lines.Enqueue(FormattableString.Invariant($"{Clock.Elapsed.TotalMilliseconds:F3},{evt},{frame},{extra:F3}"));
        _flush ??= new Timer(_ => Flush(), null, 1000, 1000);
    }

    private static void Flush()
    {
        if (Lines.IsEmpty) return;
        using var w = File.AppendText(Path!);
        while (Lines.TryDequeue(out var line)) w.WriteLine(line);
    }
}
