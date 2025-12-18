using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AmosLikeBasic;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _runCts;
    private readonly AmosGraphics _gfx = new();

    private readonly TextScreen _textScreen = new(rows: 30, cols: 80);
    private bool _uiReady;

    private string _lastInkey = ""; // Track keyboard input for BASIC
    //private readonly System.Collections.Generic.HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);


    public MainWindow()
    {
        InitializeComponent();

        Opened += MainWindow_OnOpened;
        // Use Tunneling strategy so the Window sees the key BEFORE any child controls (like Menu)
        this.AddHandler(KeyDownEvent, MainWindow_OnKeyDown, RoutingStrategies.Tunnel);
        this.AddHandler(KeyUpEvent, MainWindow_OnKeyUp, RoutingStrategies.Tunnel);


        _gfx.Screen(640, 480);
        _gfx.Clear(Avalonia.Media.Colors.Black);

        // Don't touch Console/ConsoleOverlay here (they may not exist yet if SCREEN tab is lazy)
        Editor.Text =
            "CLS\n" +
            "LOCATE 1 1\n" +
            "PRINT \"RAD 1\"\n" +
            "PRINT \"RAD 2\"\n" +
            "PRINT \"RAD 3\"\n" +
            "END\n";
    }

    private void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        if (_uiReady)
            return;

        _uiReady = true;

        // Force-create tab contents by switching to SCREEN once
        var originalIndex = MainTabs.SelectedIndex;
        MainTabs.SelectedIndex = 1; // SCREEN
        MainTabs.SelectedIndex = originalIndex;

        // Now these should exist
        ScreenImage.Source = _gfx.Bitmap;

        _textScreen.Clear();
        if (Console is not null)
            Console.Text = _textScreen.Render();
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) 
        { 
            ToggleConsole(); 
            e.Handled = true; 
            return; 
        }

        // Add the key to our set of pressed keys
        string keyName = e.Key.ToString();
        _pressedKeys.Add(keyName);

        // If we are in the SCREEN tab and a program is running, 
        // prevent the key from moving focus or scrolling UI
        if (MainTabs.SelectedIndex == 1)
        {
            e.Handled = true;
        }
    }

    private void MainWindow_OnKeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key.ToString());
        
        // Also mark as handled here if we are on the screen
        if (MainTabs.SelectedIndex == 1)
        {
            e.Handled = true;
        }
    }

    private void ToggleConsole_OnClick(object? sender, RoutedEventArgs e) => ToggleConsole();

    private void ToggleConsole()
    {
        if (ConsoleOverlay is null)
            return;

        ConsoleOverlay.IsVisible = !ConsoleOverlay.IsVisible;
    }

    private async Task AppendConsoleLineAsync(string line)
    {
        // VSYNC: wait until next render tick on UI thread
        if (line.StartsWith("@@VSYNC", StringComparison.Ordinal))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Console is null)
                return;

            if (line.StartsWith("@@LOCATE ", StringComparison.Ordinal))
            {
                var rest = line.Substring("@@LOCATE ".Length).Trim();
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], out var r) &&
                    int.TryParse(parts[1], out var c))
                {
                    _textScreen.Locate(r, c);
                }
            }
            else if (line.StartsWith("@@PRINT ", StringComparison.Ordinal))
            {
                var text = line.Substring("@@PRINT ".Length);
                _textScreen.Print(text);
            }
            else if (line.StartsWith("@@CLS", StringComparison.Ordinal))
            {
                _textScreen.Clear();
            }
            else
            {
                _textScreen.Print(line);
            }

            Console.Text = _textScreen.Render();
        });
    }

    private Task ClearConsoleAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _textScreen.Clear();
            if (Console is not null)
                Console.Text = _textScreen.Render();
        }).GetTask();
    }

    private Task SetConsoleTextAsync(string text)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _textScreen.Clear();
            _textScreen.Print(text.Replace("\r\n", "\n").Replace('\r', '\n'));
            if (Console is not null)
                Console.Text = _textScreen.Render();
        }).GetTask();
    }

    private sealed class TextScreen
    {
        private readonly int _rows;
        private readonly int _cols;
        private readonly char[,] _buf;
        private int _row;
        private int _col;

        public TextScreen(int rows, int cols)
        {
            _rows = rows;
            _cols = cols;
            _buf = new char[rows, cols];
            Clear();
        }

        public void Clear()
        {
            for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
                _buf[r, c] = ' ';

            _row = 0;
            _col = 0;
        }

        public void Locate(int row, int col)
        {
            _row = Math.Clamp(row - 1, 0, _rows - 1);
            _col = Math.Clamp(col - 1, 0, _cols - 1);
        }

        public void Print(string text)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                PrintSingleLine(lines[i] ?? string.Empty);
                if (i != lines.Length - 1)
                    NewLine();
            }
        }

        private void PrintSingleLine(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (_col >= _cols)
                    break;

                _buf[_row, _col] = s[i];
                _col++;
            }

            NewLine();
        }

        private void NewLine()
        {
            _row++;
            _col = 0;

            if (_row >= _rows)
            {
                ScrollUp();
                _row = _rows - 1;
            }
        }

        private void ScrollUp()
        {
            for (var r = 1; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
                _buf[r - 1, c] = _buf[r, c];

            for (var c = 0; c < _cols; c++)
                _buf[_rows - 1, c] = ' ';
        }

        public string Render()
        {
            var sb = new StringBuilder(_rows * (_cols + 1));
            for (var r = 0; r < _rows; r++)
            {
                for (var c = 0; c < _cols; c++)
                    sb.Append(_buf[r, c]);
                if (r != _rows - 1)
                    sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    // ... keep your Save/Open/Run/Stop methods as they are (but do not duplicate console helpers) ...

    
    private async void SaveProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null)
        {
            await AppendConsoleLineAsync("ERROR: No StorageProvider available.");
            return;
        }

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AMOS-like Project",
            SuggestedFileName = "project.amosproj",
            FileTypeChoices =
            [
                new FilePickerFileType("AMOS Project") { Patterns = ["*.amosproj"] }
            ]
        });

        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            await AppendConsoleLineAsync("ERROR: Could not get local file path.");
            return;
        }

        var project = _gfx.ExportProject(Editor.Text ?? string.Empty);
        await AmosProjectSerializer.SaveAsync(path, project);

        await AppendConsoleLineAsync($"Saved: {path}");
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null)
        {
            await AppendConsoleLineAsync("ERROR: No StorageProvider available.");
            return;
        }

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AMOS-like Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("AMOS Project") { Patterns = ["*.amosproj"] }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            await AppendConsoleLineAsync("ERROR: Could not get local file path.");
            return;
        }

        var project = await AmosProjectSerializer.LoadAsync(path);

        // Apply graphics + sprites
        _gfx.ImportProject(project);

        // Update UI screen
        ScreenImage.Source = _gfx.Bitmap;
        ScreenImage.InvalidateVisual();

        // Load program text
        Editor.Text = project.ProgramText ?? string.Empty;

        await AppendConsoleLineAsync($"Opened: {path}");
    }

    // ... existing code (Run/Stop/console helpers) ...




private void SpritesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new SpriteEditorWindow(_gfx);
        win.Show();
    }

    private async void RunButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Switch to SCREEN tab when running
        MainTabs.SelectedIndex = 1;

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();

        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;

        await SetConsoleTextAsync("Running...\n");

        var program = Editor.Text ?? string.Empty;

        try
        {
            var token = _runCts.Token;

            await Task.Run(async () =>
            {
                await AmosRunner.ExecuteAsync(
                    programText: program,
                    appendLineAsync: AppendConsoleLineAsync,
                    clearAsync: ClearConsoleAsync,
                    graphics: _gfx,
                    onGraphicsChanged: () => {
                        Dispatcher.UIThread.Post(() => {
                            ScreenImage.Source = _gfx.Bitmap;
                            ScreenImage.InvalidateVisual();
                        });
                    },
                    getInkey: () => _pressedKeys.FirstOrDefault() ?? "",
                    isKeyDown: (k) => _pressedKeys.Contains(k),
                    token: token);
            }, token);

            await AppendConsoleLineAsync("OK");
        }
        catch (OperationCanceledException)
        {
            await AppendConsoleLineAsync("STOPPED");
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR: {ex.Message}");
        }
        finally
        {
            StopButton.IsEnabled = false;
            RunButton.IsEnabled = true;
        }
    }
    
    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 0;
        _runCts?.Cancel();
    }
    
}