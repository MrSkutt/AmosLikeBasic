using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace AmosLikeBasic;

// Player mode: runs a finished BASIC program in a ScreenWindow, without the editor.
// Used when the app has a built-in game (game.amosproj, see Tools/export_app.py)
// or is started with --play <file.amosproj>.
public sealed class GamePlayer
{
    public const string EmbeddedGameFile = "game.amosproj";

    private readonly ScreenWindow _window;
    private readonly AmosGraphics _gfx = new();
    private readonly AudioEngine _audio = new();
    private readonly TextScreen _textScreen = new(rows: 30, cols: 80);
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    // The game to run: --play <file> or game.amosproj next to the app (or in
    // Resources in a macOS bundle). null means the IDE should start as usual.
    public static string? FindGame(string[] args)
    {
        int i = Array.IndexOf(args, "--play");
        if (i >= 0 && i + 1 < args.Length)
            return Path.GetFullPath(args[i + 1]);

        string baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "..", "Resources", EmbeddedGameFile),
                     Path.Combine(baseDir, EmbeddedGameFile),
                     Path.Combine(baseDir, "Resources", EmbeddedGameFile)
                 })
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    // Reads the program text and an optional title from a .amosproj file
    public static (string Program, string Title) LoadGame(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        string program = root.GetProperty("ProgramText").GetString() ?? "";
        string title = root.TryGetProperty("Title", out var t) && t.GetString() is { Length: > 0 } s
            ? s
            : Path.GetFileNameWithoutExtension(path);
        return (program, title);
    }

    public GamePlayer(ScreenWindow window)
    {
        _window = window;
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _window.Closed += (_, _) => _cts.Cancel();
        _gfx.OnError = msg => Dispatcher.UIThread.Post(() => ShowMessage(msg));
    }

    public async Task RunAsync(string program)
    {
        MainWindow.InitializeAudio();

        _gfx.ConfigureText(8, 16, "Topaz a600a1200a400");
        _gfx.OnScreenResolutionChanged += (w, h) =>
            Dispatcher.UIThread.Post(() => _window.UpdateScreenSize(w, h));
        _gfx.Screen(640, 480);
        _window.ScreenControl.Graphics = _gfx;
        _window.Console.Text = "";

        var token = _cts.Token;
        try
        {
            await Task.Run(() => AmosRunner.ExecuteAsync(
                programText: program,
                appendLineAsync: AppendConsoleLineAsync,
                clearAsync: () => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _textScreen.Clear();
                    _window.Console.Text = _textScreen.Render();
                }).GetTask(),
                graphics: _gfx,
                onGraphicsChanged: () => Dispatcher.UIThread.Post(() =>
                {
                    _window.ScreenControl.InvalidateMeasure();
                    _window.ScreenControl.InvalidateVisual();
                }, DispatcherPriority.Render),
                getInkey: () => { lock (_pressedKeys) return _pressedKeys.FirstOrDefault() ?? ""; },
                isKeyDown: k => { lock (_pressedKeys) return _pressedKeys.Contains(k); },
                audioEngine: _audio,
                token: token,
                onVariablesChanged: _ => { },
                waitForStep: _ => Task.CompletedTask,
                getConsoleInputAsync: () => Dispatcher.UIThread.InvokeAsync(() => _window.RequestInputAsync())
            ), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => ShowMessage($"ERROR: {ex.Message}"));
        }
    }

    private Task AppendConsoleLineAsync(string line)
    {
        if (line.StartsWith("@@VSYNC", StringComparison.Ordinal))
            return Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();

        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
                ScreenConsole.Apply(_window, _textScreen, line);
            else
                ShowMessage(line); // Error messages etc. that the IDE shows in the log
        }).GetTask();
    }

    // Without the IDE there is no log, so messages are written in the game's text console
    private void ShowMessage(string message)
    {
        _textScreen.Print(message);
        _window.Console.Text = _textScreen.Render() + "\n";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F12 toggles full screen, Escape leaves full screen
        if (e.Key == Key.F12 || (e.Key == Key.Escape && _window.WindowState == WindowState.FullScreen))
        {
            _window.WindowState = _window.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        // F8 turns the CRT filter on and off
        if (e.Key == Key.F8)
        {
            _gfx.CrtEnabled = !_gfx.CrtEnabled;
            _window.ScreenControl.InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Always register the key first, so that KEYSTATE works
        lock (_pressedKeys) _pressedKeys.Add(e.Key.ToString());

        // INPUT: type text in the console
        if (e.Key == Key.Return)
            _window.SubmitInput();
        else if (e.Key == Key.Back)
            _window.BackspaceInput();
        else if (e.Key != Key.Escape && e.Key != Key.Tab)
        {
            string ch = ScreenConsole.CharFromKey(e.Key, (e.KeyModifiers & KeyModifiers.Shift) != 0);
            if (!string.IsNullOrEmpty(ch))
                _window.AppendInputChar(ch);
        }

        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        lock (_pressedKeys) _pressedKeys.Remove(e.Key.ToString());
    }
}
