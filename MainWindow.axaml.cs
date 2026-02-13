using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AmoslikeBasic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;


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
    private bool _isDirty = false;
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConfigService configService;
    private readonly HashSet<int> _breakpoints = new(); // breakpoints list

    public MainWindow()
    {
        InitializeComponent();
        
        Editor.Options.IndentationSize = 2;
        Editor.Options.ConvertTabsToSpaces = true;
        
        Opened += MainWindow_OnOpened;
        this.Closing += MainWindow_Closing;
        
        _ = EnsureExampleProjectsExistAsync();
        
        AmosAudioCommands.InitializeAudio();
        
        string userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AmosLikeBasic",
            "Projects"
        );
        
        configService = new ConfigService("AmosProject");
        configService.Load();
        ChangeTheme(configService.Config.DefaultTheme);
        this.Width = configService.Config.WindowWidth;
        this.Height = configService.Config.WindowHeight;
        this.Position = new PixelPoint(configService.Config.WindowTop, configService.Config.WindowLeft);
        if (!string.IsNullOrWhiteSpace(configService.Config.LastProjectPath))
        {
            _ = OpenProjectFromPathAsync(configService.Config.LastProjectPath);
        }
        
        // KOPPLA IHOP AMOSGRAPHICS MED LOGBOX HÄR
        _gfx.OnError = (msg) => {
            // Se till att vi är på UI-tråden eftersom vi ska ändra text i en TextBox
            Dispatcher.UIThread.Post(() => {
                Log(msg + Environment.NewLine);
                // Om du vill att LogBox ska scrolla till slutet automatiskt:
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
            });
        };

        this.AddHandler(KeyDownEvent, HandleGlobalKeyDown, RoutingStrategies.Tunnel);
        this.AddHandler(KeyUpEvent, HandleGlobalKeyUp, RoutingStrategies.Tunnel);
        Editor.AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        Editor.TextArea.Caret.PositionChanged += (s, e) => {
            UpdateCursorPosition();
        };

        // Check for text changed
        Editor.TextChanged += (s, e) => {
            if (!_isDirty)
            {
                
                _isDirty = true;
                UpdateTitleBar();
            }
        };
        
        // Manage breakpoints
        var breakpointMargin = new BreakpointMargin(this);
        Editor.TextArea.LeftMargins.Insert(0, breakpointMargin);
        Editor.ShowLineNumbers = true; // Aktivera linjenummer för att marginalen ska synas
        
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
        
        // Nollställ flaggan efter att vi satt starttexten, så den inte tror att vi ändrat något direkt
        _isDirty = false;
        UpdateTitleBar();
    }

    private async void OnCut(object? sender, RoutedEventArgs e)
    {
        string selected = Editor.SelectedText;
        if (string.IsNullOrEmpty(selected)) return;

        // Klipp till clipboard
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(selected);

        // Spara startpositionen (där texten börjar, oavsett vilket håll man markerat)
        int startPos = Editor.SelectionStart;
        int len = Editor.SelectionLength;

        // Ta bort den valda texten
        Editor.Document.Replace(startPos, len, "");

        // Placera caret där texten togs bort
        Editor.CaretOffset = startPos;
        Editor.SelectionLength = 0;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        string selected = Editor.SelectedText;
        if (!string.IsNullOrEmpty(selected))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(selected);
        }
    }

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            string? text = await clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                // Om vi har en markering, skriv över den (både Insert och Replace hanteras av Replace)
                int startPos = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
                int lengthToReplace = Editor.SelectionLength;

                Editor.Document.Replace(startPos, lengthToReplace, text);
                    
                // Flytta caret till slutet av den inklistrade texten
                Editor.CaretOffset = startPos + text.Length;
                Editor.SelectionLength = 0;
            }
        }
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
    }
    
    private void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        _uiReady = true;
    }

    private void UpdateCursorPosition()
    {
        if (Editor.Text == null) return;
        // TextEditor använder CaretOffset istället för CaretIndex
        int caretIndex = Editor.CaretOffset;
        string text = Editor.Text.Substring(0, Math.Min(caretIndex, Editor.Text.Length));
        int line = text.Count(c => c == '\n') + 1;
        int lastNewLine = text.LastIndexOf('\n');
        int col = caretIndex - lastNewLine;
        CursorPosText.Text = $"Line: {line}, Col: {col}";
    }

    public void Log(string message)
    {
        LogBox.Text += message;
    }
    
    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.PageUp or Key.PageDown))
            return;

        var text = Editor.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var currentLine = text[..Editor.CaretOffset].Count(c => c == '\n');

        const int page = 20; // AMOS-känsla

        var targetLine = e.Key == Key.PageUp
            ? Math.Max(0, currentLine - page)
            : Math.Min(lines.Length - 1, currentLine + page);

        int charIndex = 0;
        for (int i = 0; i < targetLine; i++)
            charIndex += lines[i].Length + 1;

        Editor.CaretOffset = charIndex;
        // TextEditor använder SelectionStart och SelectionLength
        Editor.SelectionStart = charIndex;
        Editor.SelectionLength = 0;
        Editor.Focus();

        e.Handled = true;
    }
    
    private void HandleGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        // 1) Registrera alltid tangenten först (så KEYSTATE funkar även om vi "äter" eventet sen)
        _pressedKeys.Add(e.Key.ToString());
        
        // Om ScreenWindow är aktiv och väntar på INPUT
        if (_screenWindow?.IsActive == true && _screenWindow != null)
        {
            // Kolla om vi är i INPUT-läge (du kan lägga till en flag i ScreenWindow)
            if (e.Key == Key.Return)
            {
                _screenWindow.SubmitInput();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Back)
            {
                _screenWindow.BackspaceInput();
                e.Handled = true;
                return;
            }
            else if (e.Key != Key.Escape && e.Key != Key.Tab)
            {
                // Lägg till ett tecken
                string ch = GetCharFromKey(e.Key, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                if (!string.IsNullOrEmpty(ch))
                {
                    _screenWindow.AppendInputChar(ch);
                    e.Handled = true;
                    return;
                }
            }
        }
        
        if (Editor.IsKeyboardFocusWithin)
        {
            // Låt editorn hantera navigationstangenter själv, inklusive PageUp/Down
            if (e.Key is Key.PageUp or Key.PageDown
                or Key.Up or Key.Down
                or Key.Left or Key.Right
                or Key.Home or Key.End
                or Key.Tab)
            {
                return; // Avbryt här så att e.Handled INTE sätts till true längre ner
            }
        }
        
        if (!_isPaused && RunButton.IsEnabled == false)
        {
            // Om händelsen kommer från editorn, hindra den
            if (Editor.IsKeyboardFocusWithin)
            {
                e.Handled = true;
            }
        }
        
        // F1 - VARIABLE WATCH
        if (e.Key == Key.F1) 
        { 
            VariableWatchPanel.IsVisible = !VariableWatchPanel.IsVisible; 
            e.Handled = true; 
            return; 
        }
        
        // F5 - RUN / DEBUG
        if (e.Key == Key.F5) 
        { 
            // Editor.IsEnabled = false;
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

        // F9 - TOGGLE BREAKPOINT (NYTT!)
        if (e.Key == Key.F9)
        {
            ToggleBreakpointAtCurrentLine();
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

    private async Task EnsureExampleProjectsExistAsync()
    {
        try 
        {
            // 1. Destination: Documents/AmosLikeBasic/Projects
            string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string targetDir = Path.Combine(docPath, "AmosLikeBasic", "Projects");

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 2. Källa: Inuti AppBundle
            // AppContext.BaseDirectory pekar oftast på .../AmosLikeBasic.app/Contents/MacOS/
            // Vi vill gå upp ett steg och in i Resources
            string bundleBase = AppContext.BaseDirectory;
            string sourceDir = Path.Combine(bundleBase, "..", "Resources", "Projects");
                
            // Normalisera sökvägen (tar bort ".." ur strängen)
            sourceDir = Path.GetFullPath(sourceDir);

            if (Directory.Exists(sourceDir))
            {
                var files = Directory.GetFiles(sourceDir);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(targetDir, fileName);

                    // Kopiera bara om filen inte redan finns (så vi inte skriver över användarens eventuella ändringar)
                    if (!File.Exists(destFile))
                    {
                        // Async copy för att inte låsa UI vid start
                        await Task.Run(() => File.Copy(file, destFile));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Logga eller ignorera tyst om något går fel med rättigheter
            System.Diagnostics.Debug.WriteLine($"Could not copy example projects: {ex.Message}");
        }
    }
           
    private void HandleGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key.ToString());
    }

    private string GetCharFromKey(Key key, bool shift)
    {
        return key switch
        {
            Key.A => shift ? "A" : "a",
            Key.B => shift ? "B" : "b",
            Key.C => shift ? "C" : "c",
            Key.D => shift ? "D" : "d",
            Key.E => shift ? "E" : "e",
            Key.F => shift ? "F" : "f",
            Key.G => shift ? "G" : "g",
            Key.H => shift ? "H" : "h",
            Key.I => shift ? "I" : "i",
            Key.J => shift ? "J" : "j",
            Key.K => shift ? "K" : "k",
            Key.L => shift ? "L" : "l",
            Key.M => shift ? "M" : "m",
            Key.N => shift ? "N" : "n",
            Key.O => shift ? "O" : "o",
            Key.P => shift ? "P" : "p",
            Key.Q => shift ? "Q" : "q",
            Key.R => shift ? "R" : "r",
            Key.S => shift ? "S" : "s",
            Key.T => shift ? "T" : "t",
            Key.U => shift ? "U" : "u",
            Key.V => shift ? "V" : "v",
            Key.W => shift ? "W" : "w",
            Key.X => shift ? "X" : "x",
            Key.Y => shift ? "Y" : "y",
            Key.Z => shift ? "Z" : "z",
            Key.OemOpenBrackets => shift ? "Å" : "å",
            Key.OemQuotes       => shift ? "Ä" : "ä",
            Key.OemSemicolon    => shift ? "Ö" : "ö",
            Key.D0 => shift ? ")" : "0",
            Key.D1 => shift ? "!" : "1",
            Key.D2 => shift ? "@" : "2",
            Key.D3 => shift ? "#" : "3",
            Key.D4 => shift ? "$" : "4",
            Key.D5 => shift ? "%" : "5",
            Key.D6 => shift ? "^" : "6",
            Key.D7 => shift ? "&" : "7",
            Key.D8 => shift ? "*" : "8",
            Key.D9 => shift ? "(" : "9",
            Key.Space => " ",
            Key.OemMinus => shift ? "_" : "-",
            Key.OemPlus => shift ? "+" : "=",
            _ => ""
        };
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

                if (line.StartsWith("@@PAPER ", StringComparison.Ordinal))
                {
                    var arg = line.Substring(8).Trim(); // efter "@@PAPER "
                    try
                    {
                        var c = Avalonia.Media.Color.Parse(arg);
                        _screenWindow.ScreenGrid.Background = new SolidColorBrush(c);
                    }
                    catch
                    {
                        // Om färgen inte kan tolkas: ignorera eller sätt default
                        _screenWindow.Console.Background = Brushes.Black;
                    }
                }
                else if (line.StartsWith("@@INK ", StringComparison.Ordinal))
                {
                    var arg = line.Substring(6).Trim();
                    try
                    {
                        var c = Avalonia.Media.Color.Parse(arg);
                        _screenWindow.Console.Foreground = new SolidColorBrush(c);
                    }
                    catch
                    {
                        _screenWindow.Console.Foreground = Brushes.White;
                    }
                }               
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

                // Viktigt: trailing newline ger ScrollViewer “plats” att scrolla så sista raden blir hel
                _screenWindow.Console.Text = _textScreen.Render() + "\n";

                // Flytta caret sist
                _screenWindow.Console.CaretIndex = _screenWindow.Console.Text?.Length ?? 0;
                _screenWindow.Console.SelectionStart = _screenWindow.Console.CaretIndex;
                _screenWindow.Console.SelectionEnd = _screenWindow.Console.CaretIndex;

                // Scrolla längst ner (låt ScrollViewer själv klampa till max)
                Dispatcher.UIThread.Post(() =>
                {
                    var sv = _screenWindow.Console
                        .GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .FirstOrDefault();

                    if (sv is null) return;

                    sv.Offset = new Vector(sv.Offset.X, double.MaxValue);
                }, DispatcherPriority.Render);

                // En extra “sen” post kan hjälpa om font/layout uppdateras efter Render-pass
                Dispatcher.UIThread.Post(() =>
                {
                    var sv = _screenWindow.Console
                        .GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .FirstOrDefault();

                    if (sv is null) return;

                    sv.Offset = new Vector(sv.Offset.X, double.MaxValue);
                }, DispatcherPriority.Background);
            }
            else if (LogBox is not null)
            {
                LogBox.Text += line + Environment.NewLine;
                LogBox.CaretIndex = LogBox.Text.Length;
            }
        });
    }

    private async void Exit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await CheckUnsavedChangesAsync())
        {
            // Om användaren svarade "Nej" (vill inte förlora ändringar), avbryt stängningen helt
            return;
        }
        
        var result = await MessageBox(
            "Exit program",
            "Do you really want to quit?",
            "Yes",
            "No"
        );

        if (result)
        {
            configService.Config.WindowTop = this.Position.X;
            configService.Config.WindowLeft = this.Position.Y;
            configService.Config.WindowWidth = (int)this.Width;
            configService.Config.WindowHeight = (int)this.Height;
            configService.Save();
            
            Environment.Exit(0);
        }
    }
    
    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true; // Stoppa fönstret direkt

        if (!await CheckUnsavedChangesAsync())
        {
            // Om användaren svarade "Nej" (vill inte förlora ändringar), avbryt stängningen helt
            return;
        }
        
        var result = await ShowExitDialog();

        if (result)
        {
            configService.Config.WindowTop = this.Position.X;
            configService.Config.WindowLeft = this.Position.Y;
            configService.Config.WindowWidth = (int)this.Width;
            configService.Config.WindowHeight = (int)this.Height;
            configService.Save();
            // Stänger fönstret efter dialogen
            this.Closing -= MainWindow_Closing; // Undvik loop
            this.Close();
        }
    }
    
    // NYTT: Hjälpmetod för att kolla osparade ändringar
    private async Task<bool> CheckUnsavedChangesAsync()
    {
        if (!_isDirty) return true; // Inga ändringar, kör på!

        var result = await MessageBox(
            "Unsaved changes",
            "You have unsaved changes. Do you want to continue?",
            "Yes",  // Fortsätt (släng ändringar)
            "No"  // Avbryt (gå tillbaka och spara)
        );

        return result;
    }
    
    private async Task<bool> ShowExitDialog()
    {
        var dialog = new Window
        {
            Title = "Exit program",
            Width = 300,
            Height = 75,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var yesButton = new Button { Content = "Yes", Width = 80, Margin = new Thickness(5) };
        var noButton  = new Button { Content = "No", Width = 80, Margin = new Thickness(5) };

        var tcs = new TaskCompletionSource<bool>();

        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click  += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = "Do you really want to quit?", Margin = new Thickness(0,0,0,10) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { yesButton, noButton }
                }
            }
        };

        await dialog.ShowDialog(this); // Ägare = MainWindow

        return await tcs.Task;
    }
    
    private async Task<bool> MessageBox(string title, string message, string yesText, string noText)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 370,
            Height = 75,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var yesButton = new Button { Content = yesText, Width = 80, Margin = new Thickness(5) };
        var noButton  = new Button { Content = noText,  Width = 80, Margin = new Thickness(5) };

        var tcs = new TaskCompletionSource<bool>();

        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click  += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = message, Margin = new Thickness(0,0,0,10) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yesButton, noButton }
                }
            }
        };

        if (this is Window owner)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return await tcs.Task;
    }
    
    private Task ClearConsoleAsync()
    {
        // Vi använder InvokeAsync direkt och returnerar Tasken.
        // (Det går också att göra metoden 'async Task' och använda 'await' inuti)
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

        _isPaused = startPaused; 
        
        Dispatcher.UIThread.Post(() => {
            PauseButton.Content = _isPaused ? "[ RESUME ]" : "[ PAUSE ]";
            PauseButton.IsEnabled = true;
            StepButton.IsEnabled = _isPaused;
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = _isPaused ? "Status: DEBUG (Paused)" : "Status: RUNNING";
        });

        _gfx.Clear(Colors.Black);
        _gfx.ClearFrames();
        _textScreen.Clear();
        _gfx.CursorX = 0;
        _gfx.CursorY = 0;
        _gfx.PaperColor = Colors.Transparent;
        _gfx.Ink = Colors.White;
        _gfx.ConfigureText(8,16,"Topaz a600a1200a400");
        _gfx.Screen(640,480);
        foreach(var id in _gfx.GetSpriteIds()) {
            _gfx.SpriteOff(id);
        }
        foreach(var id in _gfx.GetBobIds()) {
            _gfx.BobOff(id);
        }
        if (_screenWindow.Console != null) _screenWindow.Console.Text = "";
        _screenWindow.ScreenControl.Graphics = _gfx;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        var program = Editor.Text ?? string.Empty;

        try
        {
            // VIKTIGT: async här i lambdan till Task.Run
            await Task.Run(async () =>
            {
                var lastCpuUpdateTime = DateTime.MinValue;
                var cpuUpdateInterval = TimeSpan.FromMilliseconds(500); 

                await AmosRunner.ExecuteAsync(
                    programText: program,
                    appendLineAsync: AppendConsoleLineAsync,
                    // VIKTIGT: async här också för input
                    getConsoleInputAsync: async () => {
                        if (_screenWindow == null) return "";
                        return await Dispatcher.UIThread.InvokeAsync(() => _screenWindow.RequestInputAsync());
                    },
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
                                    //StatusText.Text = $"Status: RUNNING | GFX: {cpu:F1}%"; // UI-thread access error risk fixad i ExecuteAsync
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
                            // NYTT: Filtrera bort interna variabler (som börjar med __)
                            var userVars = vars
                                .Where(kvp => !kvp.Key.StartsWith("__"))
                                .OrderBy(v => v.Key)
                                .ToList();
                                
                            VariableListBox.ItemsSource = userVars;
                        });
                    },
                    // VIKTIGT: async här för debug-steget
                    waitForStep: async (pc) => {
                        // NYTT: Kolla om vi träffade en breakpoint
                        bool hitBreakpoint = _breakpoints.Contains(pc + 1);
                            
                        if (hitBreakpoint && !_isPaused)
                        {
                            // Aktivera paus-läge automatiskt
                            _isPaused = true;
                            Dispatcher.UIThread.Post(() => {
                                PauseButton.Content = "[ RESUME ]";
                                StepButton.IsEnabled = true;
                            });
                        }   
                        
                        if (_isPaused)
                        {
                                Dispatcher.UIThread.Post(() => { 
                                    StatusText.Text = hitBreakpoint ? "Status: BREAKPOINT" : "Status: PAUSED";
                                    CurrentLineText.Text = $"Line: {pc + 1}";
                                    
                                    if (Editor.Text != null && Editor.Document != null)
                                    {
                                        try
                                        {
                                            // FIXAT: Använd Document API istället för manuell beräkning
                                            if (pc >= 0 && pc < Editor.Document.LineCount)
                                            {
                                                // Få raden från dokumentet (1-baserad -> 0-baserad)
                                                var docLine = Editor.Document.GetLineByNumber(pc + 1);
                                                int lineStart = docLine.Offset;
                                                int lineLength = docLine.Length;
                                                
                                                // VIKTIGT: Använd Select() istället för att sätta properties direkt
                                                Editor.CaretOffset = lineStart;
                                                
                                                if (lineLength > 0)
                                                {
                                                    Editor.Select(lineStart, lineLength);
                                                }
                                                
                                                Editor.Focus();
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            // Säkerhetsåtgärd: Om något går fel, sätt bara caret
                                            System.Diagnostics.Debug.WriteLine($"Error selecting line: {ex.Message}");
                                            if (pc >= 0 && pc < Editor.Document.LineCount)
                                            {
                                                var docLine = Editor.Document.GetLineByNumber(pc + 1);
                                                Editor.CaretOffset = docLine.Offset;
                                            }
                                        }
                                    }
                            });
                            _stepSignal = new TaskCompletionSource<bool>();
                            await _stepSignal.Task;
                        }
                    }
                ); // Slut på ExecuteAsync
            }, token); // Slut på Task.Run

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
                Editor.Select(Editor.CaretOffset , 0);
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
    
    public bool HasBreakpoint(int lineNumber)
    {
        return _breakpoints.Contains(lineNumber);
    }
    
    // NYTT: Kolla om en rad är tom eller bara innehåller whitespace/kommentarer
    public bool IsEmptyOrCommentLine(int lineNumber)
    {
        if (Editor.Document == null) return true;
            
        // Kolla om radnumret är giltigt
        if (lineNumber < 1 || lineNumber > Editor.Document.LineCount) return true;
            
        var line = Editor.Document.GetLineByNumber(lineNumber);
        var text = Editor.Document.GetText(line.Offset, line.Length).Trim();
            
        // Tom rad
        if (string.IsNullOrWhiteSpace(text)) return true;
            
        // Bara kommentar (börjar med ;)
        if (text.StartsWith(";")) return true;
            
        // Bara en label (slutar med :)
        if (text.EndsWith(":")) return true;
            
        // Hantera radnummer först (t.ex. "100 REM comment")
        // Ta bort eventuellt radnummer i början
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            // Om första ordet är ett tal, skippa det
            if (int.TryParse(parts[0], out _) && parts.Length > 1)
            {
                var commandPart = parts[1].ToUpperInvariant();
                    
                // REM-rader är också kommentarer
                if (commandPart == "REM" || commandPart.StartsWith("REM"))
                    return true;
            }
            else
            {
                // Ingen radnummer, kolla första ordet direkt
                var commandPart = parts[0].ToUpperInvariant();
                if (commandPart == "REM" || commandPart.StartsWith("REM"))
                    return true;
            }
        }
            
        return false;
    }
    
    // NYTT: Toggle breakpoint på aktuell rad (F9)
    private void ToggleBreakpointAtCurrentLine()
    {
        if (Editor.Document == null) return;
            
        // Få aktuell rad från caret-positionen
        int offset = Editor.CaretOffset;
        var location = Editor.Document.GetLocation(offset);
        int lineNumber = location.Line;
            
        // NYTT: Hindra breakpoint på tomma rader
        if (IsEmptyOrCommentLine(lineNumber))
        {
            return; // Gör ingenting
        }
            
        if (_breakpoints.Contains(lineNumber))
            _breakpoints.Remove(lineNumber);
        else
            _breakpoints.Add(lineNumber);
            
        // Tvinga en uppdatering av marginalen
        Editor.TextArea.TextView.Redraw();
    }
    
    private void StepButton_OnClick(object? sender, RoutedEventArgs e) => _stepSignal?.TrySetResult(true);

    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPaused = false;
        Editor.IsEnabled = true;
        _stepSignal?.TrySetResult(false);
        _runCts?.Cancel();
        _audioEngine?.StopMod();
        _audioEngine?.StopAllSamples();
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
        LogBox.Clear();
        VariableListBox.ItemsSource = null;
        //_textScreen.Clear();
        //if (_screenWindow?.Console != null) _screenWindow.Console.Text = "";
    }

    private void UpdateTitleBar()
    {
        string name = _currentProjectFile?.Name ?? "Untitled";
        string dirtyMarker = _isDirty ? "*" : "";
            
        FileNameText.Text = name + dirtyMarker;
        // Valfritt: Uppdatera även fönstertiteln (det som syns i Windows/macOS-listen)
        this.Title = $"AMOS Professional IDE - [{name}]";
    }

    private async void NewProject_OnClick(object? sender, RoutedEventArgs e)
    {
        // NYTT: Kolla innan vi rensar
        if (!await CheckUnsavedChangesAsync()) return;
        
        Editor.Text = "";
        _currentProjectFile = null; // Nollställ filreferensen
        _gfx.Clear(Colors.Black);
        _textScreen.Clear();
        if (_screenWindow?.Console != null) _screenWindow.Console.Text = "";
        LogBox.Text = "New project started.\n";
        _isDirty = false; // Nollställ flaggan
        UpdateTitleBar(); // Uppdatera till "Untitled"
    }

    private async void SaveAsProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        IStorageFile? file = _currentProjectFile;
        //if ()
        //{
        file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AMOS Project",
            SuggestedFileName = "project.amosproj",
            FileTypeChoices = [new FilePickerFileType("AMOS Project") { Patterns = ["*.amosproj"] }]
        });
        //}
        if (file is null) return;

        try 
        {
            var project = _gfx.ExportProject(Editor.Text ?? string.Empty);
            using var stream = await file.OpenWriteAsync();
            await AmosProjectSerializer.SaveAsync(stream, project);
            _currentProjectFile = file;
            
            _isDirty = false; // NYTT: Markera som sparad
            UpdateTitleBar(); // Visa det nya namnet (utan stjärna)
            await AppendConsoleLineAsync($"Saved: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR saving: {ex.Message}"); }
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
            
            _isDirty = false; // NYTT: Markera som sparad
            UpdateTitleBar(); // Visa det nya namnet (utan stjärna)
            await AppendConsoleLineAsync($"Saved: {file.Name}");
        }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR saving: {ex.Message}"); }
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        // Unsaved check
        if (!await CheckUnsavedChangesAsync()) return;

        var sp = StorageProvider;
        if (sp is null) return;

        // FÖRBERED START-MAPPEN
        string userProjectsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AmosLikeBasic",
            "Projects"
        );
            
        // Försök hämta en IStorageFolder referens till mappen
        var startLocation = await sp.TryGetFolderFromPathAsync(userProjectsPath);

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AMOS-like Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("AMOS Project")
                {
                    Patterns = ["*.amosproj"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        await OpenProjectFromStorageFileAsync(file);
    }
    
    public async Task OpenProjectFromStorageFileAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            var project = await AmosProjectSerializer.LoadAsync(stream);

            _gfx.ImportProject(project);
            Editor.Text = project.ProgramText ?? string.Empty;

            _currentProjectFile = file;

            _isDirty = false;
            UpdateTitleBar();

            await AppendConsoleLineAsync($"Opened: {file.Name}");
            
            // ✅ SPARA SENAST ÖPPNADE FIL
            configService.Config.LastProjectPath = file.Path.LocalPath;
            configService.Save();
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR loading: {ex.Message}");
        }
    }

    public async Task OpenProjectFromPathAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                await AppendConsoleLineAsync($"Project not found: {filePath}");
                return;
            }

            // Unsaved check
            if (!await CheckUnsavedChangesAsync()) 
                return;

            await using var stream = File.OpenRead(filePath);

            var project = await AmosProjectSerializer.LoadAsync(stream);

            _gfx.ImportProject(project);
            Editor.Text = project.ProgramText ?? string.Empty;

            // Skapa IStorageFile-referens från path
            _currentProjectFile = await StorageProvider.TryGetFileFromPathAsync(filePath);

            _isDirty = false;
            UpdateTitleBar();

            await AppendConsoleLineAsync($"Opened: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR loading: {ex.Message}");
        }
    }
    
    
    private void ChangeTheme_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string themeName)
        {
            ApplyThemeByName(themeName);
        }
    }
    
    public void ChangeTheme(string themeName)
    {
        ApplyThemeByName(themeName);
    }
    
    private void ApplyThemeByName(string themeName)
    {
        var theme = themeName switch
        {
            "Workbench" => AmosThemes.Workbench,
            "C64" => AmosThemes.C64,
            "StosClassic"  => AmosThemes.StosClassic,
            "StosEditor"   => AmosThemes.StosEditor,
            "Emerald" => AmosThemes.Emerald,
            "NeonNight" => AmosThemes.NeonNight,
            "CatppuccinMocha" => AmosThemes.CatppuccinMocha,
            _ => AmosThemes.ClassicBlue
        };
    
        ApplyTheme(theme);

        // Spara temat i config
        configService.Config.DefaultTheme = themeName;
        configService.Save();
    }
    
    
    
    private async void About_OnClick(object? sender, RoutedEventArgs e)
    {
        var aboutWin = new AboutWindow();
        await aboutWin.ShowDialog(this);
    }

    private void ApplyTheme(AmosTheme theme)
    {
        var amosFont = new FontFamily(theme.font);
        this.Background = new SolidColorBrush(theme.WindowBg);
        ToolbarBorder.Background = new SolidColorBrush(theme.ToolbarBg);
        Editor.FontFamily = amosFont;
        Editor.FontSize = 20;
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

    // NYTT: Breakpoint-marginal för editorn
        private class BreakpointMargin : AvaloniaEdit.Editing.AbstractMargin
        {
            private readonly MainWindow _window;
            
            public BreakpointMargin(MainWindow window)
            {
                _window = window;
            }
            
            public override void Render(DrawingContext context)
            {
                var textView = TextView;
                if (textView == null || !textView.VisualLinesValid)
                    return;

                // Rita bakgrund för marginalen
                context.FillRectangle(
                    new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    new Rect(0, 0, Bounds.Width, Bounds.Height));

                foreach (var line in textView.VisualLines)
                {
                    int lineNumber = line.FirstDocumentLine.LineNumber;
                    
                    if (_window._breakpoints.Contains(lineNumber))
                    {
                        // Rita röd cirkel för breakpoint
                        var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                        
                        // Fyllda cirkel
                        context.DrawEllipse(
                            Brushes.Red,
                            new Pen(Brushes.DarkRed, 1),
                            new Point(Bounds.Width / 2, y + 10),
                            7, 7);
                    }
                }
            }
            
            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                
                var textView = TextView;
                if (textView == null) return;
                
                // ÄNDRAT: Få position relativt till DENNA margin, inte textView
                var pos = e.GetPosition(this);
                
                // Hitta vilken rad som klickades på
                var line = textView.GetVisualLineFromVisualTop(pos.Y + textView.VerticalOffset);
                if (line != null)
                {
                    int lineNumber = line.FirstDocumentLine.LineNumber;
                    
                    // NYTT: Hindra breakpoint på tomma rader
                    if (_window.IsEmptyOrCommentLine(lineNumber))
                    {
                        e.Handled = true;
                        return; // Gör ingenting
                    }
                    
                    if (_window._breakpoints.Contains(lineNumber))
                        _window._breakpoints.Remove(lineNumber);
                    else
                        _window._breakpoints.Add(lineNumber);
                    
                    InvalidateVisual();
                    
                    // Uppdatera även textView så att allt ritas om
                    textView.Redraw();
                }
                
                e.Handled = true;
            }
            
            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(20, 0);
            }
        }
    }
