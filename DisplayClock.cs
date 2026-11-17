using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AmosLikeBasic;

// The beat that WAIT VBL follows.
//
// The screen shows a new picture only when it refreshes. If WAIT VBL kept time with a timer of
// its own, the two clocks would drift against each other: now and then a refresh would get no
// new frame (the picture stands still) and the next one two (it jumps), even at a steady 60 FPS.
// So the game window reports every refresh here (AmosGpuView asks Avalonia for one with
// RequestAnimationFrame), and WAIT VBL waits for those.
//
// The games are made for 60 frames per second. On a 120 Hz screen WAIT VBL therefore waits for
// every second refresh, and on a screen that is not a multiple of 60 Hz (e.g. 144 Hz) it picks
// refreshes so that the average is 60. Without a game window (tests, or a window that is hidden
// and no longer refreshed) it falls back to a timer.
public static class DisplayClock
{
    public const double FrameMs = 1000.0 / 60.0;

    // With no refresh for this long the screen is not being drawn, so use the timer
    private const double StaleMs = 100;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();

    private static TaskCompletionSource _nextTick = NewTcs();
    private static double _lastTickMs = double.NegativeInfinity;  // Clock time of the latest refresh
    private static long _tickCount;
    private static readonly Queue<double> RecentIntervals = new();
    private static double _refreshMs = FrameMs;                   // measured time between refreshes

    // The state of the frame pacing in WaitFrameAsync
    private static long _lastUsedTick = -1;
    private static double _nextFrameMs;                           // when the next frame is due
    private static double _timerLastMs = double.NegativeInfinity;

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // How long one refresh of the screen takes, measured (16.7 ms at 60 Hz, 8.3 ms at 120 Hz)
    public static double RefreshMs { get { lock (Gate) return _refreshMs; } }

    // True while a game window reports its refreshes
    public static bool Active { get { lock (Gate) return Clock.Elapsed.TotalMilliseconds - _lastTickMs < StaleMs; } }

    // Avalonia now and then calls the refresh callback twice for the same screen refresh, a fraction
    // of a millisecond apart. Counting both let a quick program finish two frames in one refresh, so
    // one was never shown and the picture seemed to stop and jump. A refresh within DuplicateMs of
    // the last one is the same refresh. (A fixed limit: one taken from the measured refresh rate could
    // lock itself at half the rate when a slow start made the measured rate low.)
    private const double DuplicateMs = 3;   // well below the 4.2 ms of a 240 Hz screen

    public static bool IsDuplicateRefresh() => IsDuplicateRefresh(Clock.Elapsed.TotalMilliseconds);

    internal static bool IsDuplicateRefresh(double nowMs)
    {
        lock (Gate) return nowMs - _lastTickMs < DuplicateMs;
    }

    // Called by the game window at every screen refresh (on the UI thread)
    public static void Tick()
    {
        FrameLog.Write("tick", 0);
        Tick(Clock.Elapsed.TotalMilliseconds);
    }

    internal static void Tick(double nowMs)
    {
        TaskCompletionSource done;
        lock (Gate)
        {
            double interval = nowMs - _lastTickMs;
            if (interval > 0 && interval < StaleMs)
            {
                RecentIntervals.Enqueue(interval);
                if (RecentIntervals.Count > 30) RecentIntervals.Dequeue();
                _refreshMs = Median(RecentIntervals);
            }
            _lastTickMs = nowMs;
            _tickCount++;
            done = _nextTick;
            _nextTick = NewTcs();
        }
        done.TrySetResult();
    }

    // WAIT VBL: returns at the refresh the next frame should be shown at
    public static async Task WaitFrameAsync(CancellationToken token)
    {
        while (true)
        {
            Task next;
            lock (Gate)
            {
                bool refreshing = Clock.Elapsed.TotalMilliseconds - _lastTickMs < StaleMs;
                if (!refreshing) next = Task.CompletedTask;
                else if (TryUseLatestTick()) return;
                else next = _nextTick.Task;
            }

            if (next.IsCompleted)
            {
                await WaitTimerAsync(token);
                return;
            }

            // Wait for the next refresh, but not forever: if the window stops refreshing, use the timer
            try
            {
                await next.WaitAsync(TimeSpan.FromMilliseconds(StaleMs), token);
            }
            catch (TimeoutException)
            {
                await WaitTimerAsync(token);
                return;
            }
        }
    }

    // Shows the next frame at the latest refresh, if that is the one it is due at
    internal static bool TryUseLatestTick()
    {
        lock (Gate)
        {
            if (_tickCount <= _lastUsedTick || !IsDue(_lastTickMs)) return false;
            Use(_lastTickMs);
            return true;
        }
    }

    // Is the refresh at tickMs the one to show the next frame at? (Gate is held)
    private static bool IsDue(double tickMs)
    {
        int perFrame = RefreshesPerFrame(_refreshMs);
        if (perFrame > 0)
        {
            // 60 Hz, 120 Hz ...: every refresh, every second refresh ...
            return _tickCount - _lastUsedTick >= perFrame || _lastUsedTick < 0;
        }
        // Another refresh rate: the refresh closest to when the frame is due
        return tickMs >= _nextFrameMs - _refreshMs / 2;
    }

    // Remember that the refresh at tickMs was used for a frame (Gate is held)
    private static void Use(double tickMs)
    {
        // A frame much later than planned (a slow program) starts the count again from now
        if (_lastUsedTick < 0 || tickMs - _nextFrameMs > FrameMs) _nextFrameMs = tickMs;
        _nextFrameMs += FrameMs;
        _lastUsedTick = _tickCount;
        _timerLastMs = Clock.Elapsed.TotalMilliseconds;
    }

    // 1 for a screen at about 60 Hz, 2 at about 120 Hz and so on; 0 when it is not near a multiple of 60
    internal static int RefreshesPerFrame(double refreshMs)
    {
        if (refreshMs <= 0) return 1;
        double ratio = FrameMs / refreshMs;
        int whole = (int)Math.Round(ratio);
        if (whole < 1) return 1;                         // slower than 60 Hz: every refresh
        return Math.Abs(ratio - whole) / whole < 0.03 ? whole : 0;
    }

    // Without a game window: 60 frames per second by the clock, as before
    private static async Task WaitTimerAsync(CancellationToken token)
    {
        double now = Clock.Elapsed.TotalMilliseconds;
        double due;
        lock (Gate)
        {
            due = _timerLastMs + FrameMs;
            if (due < now) due = now;                    // late (or the first frame): no catching up
            _timerLastMs = due;
            _lastUsedTick = _tickCount;
        }
        double delay = due - now;
        if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), token);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    // A program starts: its first WAIT VBL needs no waiting for a frame before it
    public static void StartRun()
    {
        lock (Gate)
        {
            _lastUsedTick = -1;
            _timerLastMs = double.NegativeInfinity;
        }
    }

    // For the tests: start from scratch
    internal static void Reset()
    {
        lock (Gate)
        {
            _lastTickMs = double.NegativeInfinity;
            _tickCount = 0;
            RecentIntervals.Clear();
            _refreshMs = FrameMs;
            _lastUsedTick = -1;
            _nextFrameMs = 0;
            _timerLastMs = double.NegativeInfinity;
            _nextTick = NewTcs();
        }
    }
}
