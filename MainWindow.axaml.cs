using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource<bool>? _stepSignal; // Signalen för stepping
    private bool _isPaused = false;
    private readonly AmosGraphics _gfx = new();
    private AudioEngine? _audioEngine = new(); 
    
    private readonly TextScreen _textScreen = new(rows: 30, cols: 80);
    private bool _uiReady;
    private IStorageFile? _currentProjectFile;
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

        Editor.PropertyChanged += (s, e) => {
            if (e.Property.Name == nameof(TextBox.CaretIndex)) {
                UpdateCursorPosition();
            }
        };
        
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

    private void Editor_SelectionChanged(object? sender, RoutedEventArgs e)
    {
        UpdateCursorPosition();
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
        if (e.Key == Key.F5) { RunButton_OnClick(null, new RoutedEventArgs()); e.Handled = true; return; }

        if (e.Key == Key.F9)
        {
            VariableWatchPanel.IsVisible = !VariableWatchPanel.IsVisible;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape) 
        { 
            // 1. Stoppa programmet
            StopButton_OnClick(null, new RoutedEventArgs()); 
            
            // 2. Om vi är i fullskärm, gå tillbaka till fönsterläge
            if (WindowState == WindowState.FullScreen)
            {
                ToggleFullscreen();
            }
            
            e.Handled = true; 
            return; 
        }
 
        // Växla fullskärm med F11
        if (e.Key == Key.F10)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        
        if (e.Key == Key.F1) 
        { 
            ToggleConsole(); 
            e.Handled = true; 
            return; 
        }

        // Lagra tangentnamnet (t.ex. "Left", "Space", "A")
        string keyName = e.Key.ToString();
        _pressedKeys.Add(keyName);

        // Debug: Avkommentera raden nedan om du vill se i Debug-utgången vad tangenten heter
        System.Diagnostics.Debug.WriteLine($"Key Pressed: {keyName}");

        if (MainTabs.SelectedIndex == 1)
        {
            e.Handled = true;
        }
        _isPaused = false;
        StatusText.Text = "Status: RUNNING";
        CurrentLineText.Text = "Line: -";
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

    private void ToggleFullscreen_OnClick(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            TopMenu.IsVisible = true;
            AmosTitleBar.IsVisible = true;
            ToolbarBorder.IsVisible = true;
            FullScreenOverlay.IsVisible = false;
            MainTabs.IsVisible = true;
        }
        else
        {
            WindowState = WindowState.FullScreen;
            TopMenu.IsVisible = false;
            AmosTitleBar.IsVisible = false;
            ToolbarBorder.IsVisible = false;
            
            // Om vi inte redan är på Screen-fliken, växla till den
            MainTabs.SelectedIndex = 1;
                
            // Dölj hela flikkontrollen (inklusive flik-raden högst upp)
            MainTabs.IsVisible = false;
            FullScreenOverlay.IsVisible = true;
            
            FullScreenImage.Focus(); // Se till att input hamnar rätt

        }
    }
    
    private void ToggleConsole_OnClick(object? sender, RoutedEventArgs e) => ToggleConsole();

    private void ToggleConsole()
    {
        _textScreen.Clear();
        if (Console is not null)
            Console.Text = "";
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
            // Om det är ett internt kommando (@@), använd TextScreen och rita på SCREEN
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (Console is null) return;

                if (line.StartsWith("@@LOCATE ", StringComparison.Ordinal))
                {
                    var rest = line.Substring("@@LOCATE ".Length).Trim();
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var r) && int.TryParse(parts[1], out var c))
                        _textScreen.Locate(r, c);
                }
                else if (line.StartsWith("@@PRINT ", StringComparison.Ordinal))
                {
                    _textScreen.Print(line.Substring("@@PRINT ".Length));
                }
                else if (line.StartsWith("@@CLS", StringComparison.Ordinal))
                {
                    _textScreen.Clear();
                }

                Console.Text = _textScreen.Render();
            }
            else
            {
                // Vanliga meddelanden (Running, OK, Error) hamnar i LogBox under editorn
                if (LogBox is not null)
                {
                    LogBox.Text += line + Environment.NewLine;
                    LogBox.CaretIndex = LogBox.Text.Length;
                }
            }
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
            // När vi startar programmet, logga i LogBox 
            if (LogBox is not null)
                LogBox.Text += text + Environment.NewLine;
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

    
    private void NewProject_OnClick(object? sender, RoutedEventArgs e)
    {
        // 1. Rensa editorn
        Editor.Text = "";
        
        // 2. Glöm den aktuella filen (så nästa SAVE frågar efter namn igen)
        _currentProjectFile = null;
        
        // 3. Nollställ grafikmotorn helt
        _gfx.Clear(Avalonia.Media.Colors.Black);
        
        // 4. Rensa konsolerna
        _textScreen.Clear();
        if (Console is not null) Console.Text = "";
        if (LogBox is not null) LogBox.Text = "New project started.\n";
        
        // 5. Gå till EDITOR-fliken
        MainTabs.SelectedIndex = 0;
    }
    
    private async void SaveProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;

        IStorageFile? file = _currentProjectFile;

        // Om vi inte har en fil sedan tidigare, eller om vi håller ner Shift (Save As), fråga efter namn
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
            
            _currentProjectFile = file; // Kom ihåg filen
            await AppendConsoleLineAsync($"Saved: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR saving: {ex.Message}"); }
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
        if (file is null) return;

        try 
        {
            using var stream = await file.OpenReadAsync();
            var project = await AmosProjectSerializer.LoadAsync(stream);

            _gfx.ImportProject(project);
            ScreenImage.Source = _gfx.Bitmap;
            Editor.Text = project.ProgramText ?? string.Empty;
            
            _currentProjectFile = file; // Kom ihåg filen!
            await AppendConsoleLineAsync($"Opened: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR loading: {ex.Message}"); }
    }

    // ... existing code (Run/Stop/console helpers) ...




private void SpritesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new SpriteEditorWindow(_gfx);
        win.Show();
    }

    private async void RunButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 1. Förbered UI
        _isPaused = false;
        PauseButton.Content = "[ PAUSE ]";
        PauseButton.IsEnabled = true;
        StepButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        MainTabs.SelectedIndex = 1;
        ScreenImage.Focus();
        
        // 2. Rensa allt gammalt skräp
        _gfx.Clear(Avalonia.Media.Colors.Black); // Rensa grafik
        _textScreen.Clear();                     // Rensa text-buffer
        if (Console is not null) Console.Text = ""; // Rensa text-skärm
        if (LogBox is not null) LogBox.Text = "";   // Rensa loggen under editorn
        
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
                        Dispatcher.UIThread.InvokeAsync(() => {
                            ScreenImage.Source = _gfx.Bitmap;
                            if (FullScreenOverlay.IsVisible) {
                                FullScreenImage.Source = _gfx.Bitmap;
                            }
                            ScreenImage.InvalidateVisual();
                            FullScreenImage.InvalidateVisual();
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
                        int currentLine = pc + 1;

                        if (_isPaused)
                        {
                            Dispatcher.UIThread.Post(() => {
                                StatusText.Text = "Status: PAUSED";
                                CurrentLineText.Text = $"Line: {currentLine}";

                                if (Editor.Text != null)
                                {
                                    var textLines = Editor.Text.Replace("\r\n", "\n").Split('\n');
                                    int charIndex = 0;
                                    for (int i = 0; i < pc && i < textLines.Length; i++)
                                    {
                                        charIndex += textLines[i].Length + 1;
                                    }

                                    // Växla till EDITOR-fliken så vi faktiskt ser markeringen
                                    MainTabs.SelectedIndex = 0;

                                    // Markera hela raden
                                    int lineLength = (pc < textLines.Length) ? textLines[pc].Length : 0;
                                    Editor.SelectionStart = charIndex;
                                    Editor.SelectionEnd = charIndex + lineLength;
                                    Editor.CaretIndex = charIndex; // Sätt även caret i början av raden

                                    // Tvinga fokus så markeringen syns (blir blå/markerad)
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
        catch (OperationCanceledException)
        {
            await AppendConsoleLineAsync("STOPPED");
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR: {ex.Message}");
            Dispatcher.UIThread.Post(() => {
                MainTabs.SelectedIndex = 0; // Hoppa till EDITOR
                if (WindowState == WindowState.FullScreen) WindowState = WindowState.Normal; // Gå ur fullskärm
                AmosRunner.StopAllSounds();
            });
        }
        finally
        {
            StopButton.IsEnabled = false;
            RunButton.IsEnabled = true;
        }
    }

    private void PauseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "[ RESUME ]" : "[ PAUSE ]";
        StepButton.IsEnabled = _isPaused;
            
        if (!_isPaused) _stepSignal?.TrySetResult(true); // Släpp lös runnern om vi kör RESUME
    }

    private void StepButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _stepSignal?.TrySetResult(true); // Släpp igenom exakt EN rad
    }

    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = false;
        _stepSignal?.TrySetResult(false); // Avbryt eventuell väntan
        MainTabs.SelectedIndex = 0;
        _runCts?.Cancel();
        _audioEngine?.StopMod();
        PauseButton.IsEnabled = false;
        StepButton.IsEnabled = false;
    }


    private void MapButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new MapEditorWindow(_gfx);
        win.Show();
    }
    
    private void ChangeTheme_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
        var amosFont = new FontFamily("Topaz a600a1200a400"); // Här kan du byta till "TopazPlus" senare

        this.Background = new SolidColorBrush(theme.WindowBg);
        ToolbarBorder.Background = new SolidColorBrush(theme.ToolbarBg);
            
        Editor.FontFamily = amosFont;
        Editor.FontSize = 16;
        
        Console.FontFamily = amosFont;
        Console.FontSize = 16;
        
        LogBox.FontFamily = amosFont;
        
        Editor.Background = new SolidColorBrush(theme.EditorBg);
        Editor.Foreground = new SolidColorBrush(theme.EditorFg);
            
        CursorPosText.Background = new SolidColorBrush(theme.EditorCursorPosBg);
        CursorPosText.Foreground = new SolidColorBrush(theme.AccentColor);
            
        AmosTitleBar.Background = new SolidColorBrush(theme.TitleBarBg);
        // Om du har textblock inuti titlebar, kan du hitta dem via namn eller Grid.Children
        foreach (var child in ((Grid)AmosTitleBar.Child).Children) {
            if (child is StackPanel sp) {
                foreach (var inner in sp.Children)
                    if (inner is TextBlock tb) tb.Foreground = new SolidColorBrush(theme.TitleBarFg);
            }
        }

        LogBox.Foreground = new SolidColorBrush(theme.AccentColor);
        ToolbarBorder.BorderBrush = new SolidColorBrush(theme.AccentColor);
            
        // Uppdatera flikarnas utseende om möjligt, eller spara temat i en variabel
    }
}
