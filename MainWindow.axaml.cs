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
using Avalonia.Media;

namespace AmosLikeBasic;

public partial class MainWindow : Window
{
    private ScreenWindow? _screenWindow; 
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource<bool>? _stepSignal;
    private bool _isPaused = false;
    private readonly AmosGraphics _gfx = new();
    private AudioEngine? _audioEngine = new(); 

    private readonly TextScreen _textScreen = new(rows: 30, cols: 80);
    private bool _uiReady;
    private IStorageFile? _currentProjectFile;
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();

        Opened += MainWindow_OnOpened;
        this.AddHandler(KeyDownEvent, HandleGlobalKeyDown, RoutingStrategies.Tunnel);
        this.AddHandler(KeyUpEvent, HandleGlobalKeyUp, RoutingStrategies.Tunnel);

        Editor.PropertyChanged += (s, e) => {
            if (e.Property.Name == nameof(TextBox.CaretIndex)) {
                UpdateCursorPosition();
            }
        };
    
        _gfx.Screen(640, 480);
        _gfx.Clear(Colors.Black);

        Editor.Text =
            "CLS\n" +
            "PRINT \"READY.\"\n" +
            "X = 0\n" +
            "REPEAT\n" +
            "  X = X + 1\n" +
            "  PRINT \"LINE \" + X\n" +
            "  WAIT 100\n" +
            "UNTIL X = 10\n" +
            "END\n";
    }

    private void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        _uiReady = true;
    }

    private void UpdateCursorPosition()
    {
        if (Editor.Text == null) return;
        int caretIndex = Editor.CaretIndex;
        string text = Editor.Text.Substring(0, Math.Min(caretIndex, Editor.Text.Length));
        int line = text.Count(c => c == '\n') + 1;
        int lastNewLine = text.LastIndexOf('\n');
        int col = caretIndex - lastNewLine;
        CursorPosText.Text = $"Line: {line}, Col: {col}";
    }

    private void HandleGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isPaused && RunButton.IsEnabled == false)
        {
            // Om händelsen kommer från editorn, hindra den
            if (e.Source == Editor)
            {
                e.Handled = true;
            }
        }
        
        // F5 - RUN / DEBUG
        if (e.Key == Key.F5) 
        { 
            bool debug = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            _ = StartProgramAsync(debug); 
            e.Handled = true; 
            return; 
        }

        // F6 - PAUSE / RESUME
        if (e.Key == Key.F6)
        {
            PauseButton_OnClick(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // F7 - STEP
        if (e.Key == Key.F7)
        {
            if (_isPaused) StepButton_OnClick(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // F9 - VARIABLE WATCH
        if (e.Key == Key.F9) 
        { 
            VariableWatchPanel.IsVisible = !VariableWatchPanel.IsVisible; 
            e.Handled = true; 
            return; 
        }
        
        // F10 - FULLSCREEN
        if (e.Key == Key.F10)
        {
            var win = (Window?)sender ?? this;
            win.WindowState = win.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        // ESC - STOP
        if (e.Key == Key.Escape) 
        { 
            StopButton_OnClick(null, new RoutedEventArgs()); 
            e.Handled = true; 
            return; 
        }
        _pressedKeys.Add(e.Key.ToString());
    }

    private void HandleGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key.ToString());
    }

    
    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { RunButton_OnClick(null, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.F9) { VariableWatchPanel.IsVisible = !VariableWatchPanel.IsVisible; e.Handled = true; return; }
        if (e.Key == Key.Escape) { StopButton_OnClick(null, new RoutedEventArgs()); e.Handled = true; return; }

        _pressedKeys.Add(e.Key.ToString());
    }

    private void MainWindow_OnKeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key.ToString());
    }

    private async Task AppendConsoleLineAsync(string line)
    {
        if (line.StartsWith("@@VSYNC", StringComparison.Ordinal))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (_screenWindow?.Console is null) return;

                if (line.StartsWith("@@LOCATE ", StringComparison.Ordinal))
                {
                    var rest = line.Substring(9).Trim();
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var r) && int.TryParse(parts[1], out var c))
                        _textScreen.Locate(r, c);
                }
                else if (line.StartsWith("@@PRINT ", StringComparison.Ordinal))
                {
                    _textScreen.Print(line.Substring(8));
                }
                else if (line.StartsWith("@@CLS", StringComparison.Ordinal))
                {
                    _textScreen.Clear();
                }

                _screenWindow.Console.Text = _textScreen.Render();
            }
            else if (LogBox is not null)
            {
                LogBox.Text += line + Environment.NewLine;
                LogBox.CaretIndex = LogBox.Text.Length;
            }
        });
    }

    private Task ClearConsoleAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _textScreen.Clear();
            if (_screenWindow?.Console is not null)
                _screenWindow.Console.Text = _textScreen.Render();
        }).GetTask();
    }

    private async Task StartProgramAsync(bool startPaused)
    {
        if (_screenWindow == null || !_screenWindow.IsVisible)
        {
            _screenWindow = new ScreenWindow();
            _screenWindow.Closed += (s, ev) => {
                StopButton_OnClick(null, new RoutedEventArgs());
                _screenWindow = null;
            };
            _screenWindow.AddHandler(KeyDownEvent, HandleGlobalKeyDown, RoutingStrategies.Tunnel);
            _screenWindow.AddHandler(KeyUpEvent, HandleGlobalKeyUp, RoutingStrategies.Tunnel);
            _screenWindow.Show();
        }
        _screenWindow.Activate();
        _screenWindow.Focus(); 

        _isPaused = startPaused; // Sätt paus-läget direkt här
        
        Dispatcher.UIThread.Post(() => {
            PauseButton.Content = _isPaused ? "[ RESUME ]" : "[ PAUSE ]";
            PauseButton.IsEnabled = true;
            StepButton.IsEnabled = _isPaused;
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = _isPaused ? "Status: DEBUG (Paused)" : "Status: RUNNING";
        });

        _gfx.Clear(Colors.Black);
        _textScreen.Clear();
        _screenWindow.Console.Text = "";
        _screenWindow.ScreenControl.Graphics = _gfx;
    
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        var program = Editor.Text ?? string.Empty;

        try
        {
               await Task.Run(async () =>
                {
                    var lastCpuUpdateTime = DateTime.MinValue;
                    var cpuUpdateInterval = TimeSpan.FromMilliseconds(500); 

                    await AmosRunner.ExecuteAsync(
                        programText: program,
                        appendLineAsync: AppendConsoleLineAsync,
                        clearAsync: ClearConsoleAsync,
                        graphics: _gfx,
                        onGraphicsChanged: () => {
                            Dispatcher.UIThread.InvokeAsync(() => {
                                if (_screenWindow != null) {
                                    var control = _screenWindow.FindControl<AmosGpuView>("ScreenControl");
                                    if (control != null)
                                    {
                                        control.InvalidateMeasure();
                                        control.InvalidateVisual();
                                    }
                                    
                                    var now = DateTime.Now;
                                    if (now - lastCpuUpdateTime > cpuUpdateInterval)
                                    {
                                        var cpu = _gfx.LastCpuUsagePercent;
                                        if (_screenWindow != null)
                                            _screenWindow.Title = $"AMOS Screen | GFX: {cpu:F1}%";
                                        StatusText.Text = $"Status: RUNNING | GFX: {cpu:F1}%";
                                        lastCpuUpdateTime = now;
                                    }
                                }
                            }, DispatcherPriority.Render);
                        },
 
                    getInkey: () => _pressedKeys.FirstOrDefault() ?? "",
                    isKeyDown: (k) => _pressedKeys.Contains(k),
                    audioEngine: _audioEngine,
                    token: token,
                    onVariablesChanged: (vars) => {
                        Dispatcher.UIThread.Post(() => {
                            VariableListBox.ItemsSource = vars.OrderBy(v => v.Key).ToList();
                        });
                    },
                    waitForStep: async (pc) => {
                        if (_isPaused)
                        {
                            Dispatcher.UIThread.Post(() => {
                                StatusText.Text = "Status: PAUSED";
                                CurrentLineText.Text = $"Line: {pc + 1}";
                                if (Editor.Text != null)
                                {
                                    var textLines = Editor.Text.Replace("\r\n", "\n").Split('\n');
                                    int charIndex = 0;
                                    for (int i = 0; i < pc && i < textLines.Length; i++) charIndex += textLines[i].Length + 1;
                                    
                                    int lineLength = (pc < textLines.Length) ? textLines[pc].Length : 0;
                                    Editor.SelectionStart = charIndex;
                                    Editor.SelectionEnd = charIndex + lineLength;
                                    Editor.CaretIndex = charIndex;
                                    Editor.Focus();
                                }
                            });
                            _stepSignal = new TaskCompletionSource<bool>();
                            await _stepSignal.Task;
                        }
                    });
            }, token);
            await AppendConsoleLineAsync("OK");
        }
        catch (OperationCanceledException) { await AppendConsoleLineAsync("STOPPED"); }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR: {ex.Message}"); }
        finally 
        { 
            Dispatcher.UIThread.Post(() => {
                StopButton.IsEnabled = false; 
                RunButton.IsEnabled = true; 
                PauseButton.IsEnabled = false; 
                StepButton.IsEnabled = false;
                StatusText.Text = "Status: Idle";
            });
        }
    }

    private async void RunButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Vi kollar om Shift var nedtryckt när vi klickade, eller anropar med true från kod
        bool startPaused = (sender == null && e == null) || 
                           (e is KeyEventArgs ke && (ke.KeyModifiers & KeyModifiers.Shift) != 0);
        Editor.IsEnabled = false;
        
        await StartProgramAsync(startPaused);
    }
    
    private void PauseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "[ RESUME ]" : "[ PAUSE ]";
        StepButton.IsEnabled = _isPaused;
        Editor.IsEnabled = _isPaused;
        if (!_isPaused) _stepSignal?.TrySetResult(true);
        if (_isPaused)
        {
            // Pausa musiken (BASS har en global paus eller så pausar vi mixern)
            ManagedBass.Bass.Pause(); 
        }
        else
        {
            // Starta musiken igen
            ManagedBass.Bass.Start();
            _stepSignal?.TrySetResult(true);
        }
    }

    private void StepButton_OnClick(object? sender, RoutedEventArgs e) => _stepSignal?.TrySetResult(true);

    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = false;
        Editor.IsEnabled = true;
        _stepSignal?.TrySetResult(false);
        _runCts?.Cancel();
        _audioEngine?.StopMod();
        ManagedBass.Bass.Start(); // Säkerställ att ljudet inte fastnar i paus

        // Stäng spelfönstret om det är öppet
        if (_screenWindow != null)
        {
            _screenWindow.Close();
            _screenWindow = null;
        }

        Dispatcher.UIThread.Post(() => {
            StopButton.IsEnabled = false;
            RunButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StepButton.IsEnabled = false;
            StatusText.Text = "Status: Idle";
        });
    }

    private void SpritesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new SpriteEditorWindow(_gfx);
        win.Show();
    }

    private void MapButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new MapEditorWindow(_gfx);
        win.Show();
    }

    private void ToggleConsole_OnClick(object? sender, RoutedEventArgs e)
    {
        _textScreen.Clear();
        if (_screenWindow?.Console != null) _screenWindow.Console.Text = "";
    }

    private void UpdateTitleBar()
    {
        string name = _currentProjectFile?.Name ?? "Untitled";
        FileNameText.Text = name;
        // Valfritt: Uppdatera även fönstertiteln (det som syns i Windows/macOS-listen)
        this.Title = $"AMOS Professional IDE - [{name}]";
    }

    private void NewProject_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Text = "";
        _currentProjectFile = null; // Nollställ filreferensen
        _gfx.Clear(Colors.Black);
        _textScreen.Clear();
        if (_screenWindow?.Console != null) _screenWindow.Console.Text = "";
        LogBox.Text = "New project started.\n";
        
        UpdateTitleBar(); // Uppdatera till "Untitled"
    }

    private async void SaveProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        IStorageFile? file = _currentProjectFile;
        if (file == null)
        {
            file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save AMOS Project",
                SuggestedFileName = "project.amosproj",
                FileTypeChoices = [new FilePickerFileType("AMOS Project") { Patterns = ["*.amosproj"] }]
            });
        }
        if (file is null) return;

        try 
        {
            var project = _gfx.ExportProject(Editor.Text ?? string.Empty);
            using var stream = await file.OpenWriteAsync();
            await AmosProjectSerializer.SaveAsync(stream, project);
            _currentProjectFile = file;
            
            UpdateTitleBar(); // Visa det nya namnet
            await AppendConsoleLineAsync($"Saved: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR saving: {ex.Message}"); }
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AMOS-like Project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("AMOS Project") { Patterns = ["*.amosproj"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        try 
        {
            using var stream = await file.OpenReadAsync();
            var project = await AmosProjectSerializer.LoadAsync(stream);
            _gfx.ImportProject(project);
            Editor.Text = project.ProgramText ?? string.Empty;
            _currentProjectFile = file; // Spara referensen
            
            UpdateTitleBar(); // Visa namnet på filen vi öppnade
            await AppendConsoleLineAsync($"Opened: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR loading: {ex.Message}"); }
    }

    private void ChangeTheme_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string themeName)
        {
            var theme = themeName switch
            {
                "Workbench" => AmosThemes.Workbench,
                "Emerald" => AmosThemes.Emerald,
                "NeonNight" => AmosThemes.NeonNight,
                "CatppuccinMocha" => AmosThemes.CatppuccinMocha,
                _ => AmosThemes.ClassicBlue
            };
            ApplyTheme(theme);
        }
    }

    private void ApplyTheme(AmosTheme theme)
    {
        var amosFont = new FontFamily("Topaz a600a1200a400");
        this.Background = new SolidColorBrush(theme.WindowBg);
        ToolbarBorder.Background = new SolidColorBrush(theme.ToolbarBg);
        Editor.FontFamily = amosFont;
        Editor.FontSize = 16;
        LogBox.FontFamily = amosFont;
        Editor.Background = new SolidColorBrush(theme.EditorBg);
        Editor.Foreground = new SolidColorBrush(theme.EditorFg);
        CursorPosText.Background = new SolidColorBrush(theme.EditorCursorPosBg);
        CursorPosText.Foreground = new SolidColorBrush(theme.AccentColor);
        AmosTitleBar.Background = new SolidColorBrush(theme.TitleBarBg);
        LogBox.Foreground = new SolidColorBrush(theme.AccentColor);
        ToolbarBorder.BorderBrush = new SolidColorBrush(theme.AccentColor);
    }

    private void ToggleFullscreen_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
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
                if (i != lines.Length - 1) NewLine();
            }
        }

        private void PrintSingleLine(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (_col >= _cols) break;
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
                for (var c = 0; c < _cols; c++) sb.Append(_buf[r, c]);
                if (r != _rows - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}