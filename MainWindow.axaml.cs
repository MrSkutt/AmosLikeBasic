using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit.Rendering;
using Un4seen.Bass;


namespace AmosLikeBasic;

public partial class MainWindow : Window
{
    private ScreenWindow? _screenWindow; 
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource<bool>? _stepSignal;
    private bool _isPaused = false;
    private readonly AmosGraphics _gfx = new(); 
    private AudioEngine? _audioEngine = new(); 
    private bool _isProjectLoading = false;

    private readonly TextScreen _textScreen = new(rows: 30, cols: 80);
    private bool _uiReady;
    private IStorageFile? _currentProjectFile;
    private bool _isDirty = false;
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConfigService configService;
    private readonly HashSet<int> _breakpoints = new(); // breakpoints list
    private Dictionary<string, object>? _currentVars;
    // The running program's variables, and whether it stands still at a pause so they may be changed
    private DebugVariables? _debugVars;
    private volatile bool _stoppedAtPause;

    private sealed record WatchItem(string Name, string Value, string Kind, bool IsArray);

    // Syntax colours, completion, hover help and values, F1 and error marks in the code editor
    private readonly CodeAssist _assist;

    public MainWindow()
    {
        InitializeComponent();
        
        Editor.Options.IndentationSize = 2;
        Editor.Options.ConvertTabsToSpaces = true;
        _assist = new CodeAssist(Editor, VariableForHover);
        
        Opened += MainWindow_OnOpened;
        this.Closing += MainWindow_Closing;
        
        
        InitializeAudio();
        
        VariableListBox.DoubleTapped += VariableListBox_DoubleTapped;

        BuildExamplesMenu();
        
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
        
        // CONNECT AMOSGRAPHICS TO THE LOGBOX HERE
        _gfx.OnError = (msg) => {
            // Make sure we are on the UI thread since we change text in a TextBox
            Dispatcher.UIThread.Post(() => {
                Log(msg + Environment.NewLine);
                // If you want the LogBox to scroll to the end automatically:
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
        Editor.ShowLineNumbers = true; // Turn on line numbers so that the margin shows
        
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
        
        // Reset the flag after setting the start text, so it does not think we changed something straight away
        _isDirty = false;
        UpdateTitleBar();
    }

    public static void InitializeAudio()
    {
        // BASS on the sound card and channels chosen in View → Sound Output (if not started already)
        AudioOutput.Start();
    }

    private async void SoundOutput_OnClick(object? sender, RoutedEventArgs e)
    {
        if (await new SoundOutputWindow().ShowDialog<bool>(this))
            Log("Sound output: the new choice is used when AMOS Like Basic starts again." + Environment.NewLine);
    }
    
    private async void OnCut(object? sender, RoutedEventArgs e)
    {
        string selected = Editor.SelectedText;
        if (string.IsNullOrEmpty(selected)) return;

        // Cut to the clipboard
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(selected);

        // Keep the start position (where the text starts, whichever way the selection was made)
        int startPos = Editor.SelectionStart;
        int len = Editor.SelectionLength;

        // Remove the selected text
        Editor.Document.Replace(startPos, len, "");

        // Place the caret where the text was removed
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
            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                // If there is a selection, overwrite it (both Insert and Replace are handled by Replace)
                int startPos = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
                int lengthToReplace = Editor.SelectionLength;

                Editor.Document.Replace(startPos, lengthToReplace, text);
                    
                // Move the caret to the end of the pasted text
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
        // Where the sound goes, when it is not the usual card and channels 1 – 2
        if (AudioOutput.Current.Pair > 1 || !string.IsNullOrEmpty(AudioOutput.Current.Device))
            Log($"Sound output: {AudioOutput.Description}" + Environment.NewLine);
    }

    private void UpdateCursorPosition()
    {
        if (Editor.Text == null) return;
        // TextEditor uses CaretOffset instead of CaretIndex
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

        var doc = Editor.Document;
        if (doc == null || doc.LineCount == 0)
            return;

        // Page by one screen: as many lines as are visible, minus one so you can see where you were
        var textView = Editor.TextArea.TextView;
        double lineHeight = textView.DefaultLineHeight;
        int page = lineHeight > 0 ? Math.Max(1, (int)(textView.Bounds.Height / lineHeight) - 1) : 20;
        int direction = e.Key == Key.PageUp ? -1 : 1;

        var caret = Editor.TextArea.Caret;
        int column = caret.Column;
        int targetLine = Math.Clamp(caret.Line + direction * page, 1, doc.LineCount);

        // Scroll the text as much as the cursor moves, so that it stays at the same place in the window.
        // (TextEditor.ScrollToVerticalOffset has no effect, so the editor's ScrollViewer is used directly.)
        var scrollViewer = Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer != null)
        {
            double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            double newOffset = Math.Clamp(scrollViewer.Offset.Y + direction * page * lineHeight, 0, maxOffset);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newOffset);
        }

        Editor.TextArea.ClearSelection();
        caret.Line = targetLine;
        caret.Column = Math.Min(column, doc.GetLineByNumber(targetLine).Length + 1);
        Editor.Focus();

        e.Handled = true;
    }
    
    private void HandleGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        // 1) Always register the key first (so KEYSTATE works even if we "eat" the event later),
        //    but F12, which switches to full screen, is the IDE's
        if (e.Key != Key.F12) _pressedKeys.Add(e.Key.ToString());
        
        // If the ScreenWindow is active and waiting for INPUT
        if (_screenWindow?.IsActive == true && _screenWindow != null)
        {
            // Check if we are in INPUT mode (you can add a flag in ScreenWindow)
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
                // Add a character
                string ch = ScreenConsole.CharFromKey(e.Key, (e.KeyModifiers & KeyModifiers.Shift) != 0);
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
            // Let the editor handle navigation keys itself, including PageUp/Down
            if (e.Key is Key.PageUp or Key.PageDown
                or Key.Up or Key.Down
                or Key.Left or Key.Right
                or Key.Home or Key.End
                or Key.Tab)
            {
                return; // Stop here so that e.Handled is NOT set to true further down
            }
        }
        
        if (!_isPaused && RunButton.IsEnabled == false)
        {
            // If the event comes from the editor, stop it
            if (Editor.IsKeyboardFocusWithin)
            {
                e.Handled = true;
            }
        }
        
        // F1 - HELP for the word at the cursor in the code editor
        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            _assist.ShowHelpAtCaret(this);
            e.Handled = true;
            return;
        }

        // F2 - VARIABLE WATCH
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
        { 
            ToggleVariableWatch();
            e.Handled = true; 
            return; 
        }
    
        // SHIFT + F5 STOP
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.Shift) 
        { 
            StopButton_OnClick(null, new RoutedEventArgs()); 
            e.Handled = true; 
            return; 
        }
 
        // SHIFT + F5 STOP
        if (e.Key == Key.Escape) 
        { 
            StopButton_OnClick(null, new RoutedEventArgs()); 
            e.Handled = true; 
            return; 
        }
        
        // F5 - RUN / Option/Alt + F5 DEBUG
        if (e.Key == Key.F5) 
        { 
            if (_isProjectLoading) { e.Handled = true; return; }

            bool debug = (e.KeyModifiers & KeyModifiers.Alt) != 0;
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

        // F9 - TOGGLE BREAKPOINT (NEW!)
        // F8 - CRT FILTER IN THE GAME WINDOW
        if (e.Key == Key.F8)
        {
            ToggleCrt();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            ToggleBreakpointAtCurrentLine();
            e.Handled = true;
            return;
        }
        
        // F10 - STEP
        if (e.Key == Key.F10)
        {
            if (_isPaused) StepButton_OnClick(null, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        
        // F12 - FULLSCREEN
        if (e.Key == Key.F12)
        {
            var win = sender as Window ?? this;
            FullScreen.Toggle(win, () => _pressedKeys.Clear());
            e.Handled = true;
            return;
        }
        
        _pressedKeys.Add(e.Key.ToString());
    }

    private void VariableListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (VariableListBox.SelectedItem is not WatchItem selected) return;
        if (_currentVars == null || !_currentVars.TryGetValue(selected.Name, out var value)) return;

        if (value is IAmosArray arr)
        {
            // Elements can be changed in the window while the program is paused
            var viewer = new ArrayViewerWindow(selected.Name, arr, () => _stoppedAtPause, _currentTheme);
            viewer.Show(this);
        }
        else if (_stoppedAtPause)
        {
            WatchEditBox.Focus();
            WatchEditBox.SelectAll();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  VARIABLE WATCH
    // ══════════════════════════════════════════════════════════════

    private GridLength _watchWidth = new(350);

    // F1: hides the panel and its splitter, and brings it back at the width it had
    private void ToggleVariableWatch()
    {
        var column = MainSplitGrid.ColumnDefinitions[2];
        bool show = !VariableWatchPanel.IsVisible;
        if (!show) _watchWidth = column.Width;
        VariableWatchPanel.IsVisible = show;
        WatchSplitter.IsVisible = show;
        MainSplitGrid.ColumnDefinitions[1].Width = new GridLength(show ? 4 : 0);
        column.Width = show ? _watchWidth : new GridLength(0);
    }

    private void ShowVariables(Dictionary<string, object> vars)
    {
        _currentVars = new Dictionary<string, object>(vars, StringComparer.OrdinalIgnoreCase);
        var filter = WatchFilterBox.Text?.Trim() ?? "";
        var selected = (VariableListBox.SelectedItem as WatchItem)?.Name;

        var items = _currentVars
            .Where(kvp => !kvp.Key.StartsWith("__") && !kvp.Key.StartsWith("@@"))
            .Where(kvp => filter.Length == 0 || kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            // Plain variables first, arrays after them
            .OrderBy(kvp => kvp.Value is IAmosArray)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new WatchItem(kvp.Key, DebugVariables.Show(kvp.Value), KindOf(kvp.Key, kvp.Value), kvp.Value is IAmosArray))
            .ToList();

        VariableListBox.ItemsSource = items;
        if (selected != null)
            VariableListBox.SelectedItem = items.FirstOrDefault(i => i.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
    }

    private static string KindOf(string name, object? value) => value switch
    {
        IAmosArray => "array",
        string => "text",
        _ => name.EndsWith('%') ? "whole" : "number"
    };

    private void WatchFilterBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentVars != null) ShowVariables(_currentVars);
    }

    private void VariableListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateWatchEditor();

    // The edit row under the list: for the selected plain variable, while the program is paused
    private void UpdateWatchEditor()
    {
        var item = VariableListBox.SelectedItem as WatchItem;
        WatchEditError.IsVisible = false;
        bool canEdit = _stoppedAtPause && item != null && !item.IsArray;
        WatchEditRow.IsVisible = canEdit;
        if (!_stoppedAtPause)
            WatchEditLabel.Text = RunButton.IsEnabled ? "Run the program to watch its variables" : "Pause the program to change a value";
        else if (item == null)
            WatchEditLabel.Text = "Choose a variable to change it";
        else if (item.IsArray)
            WatchEditLabel.Text = $"Double-click {item.Name} to see and change its elements";
        else
        {
            WatchEditLabel.Text = $"{item.Name} =";
            var value = _debugVars?.Get(item.Name);
            WatchEditBox.Text = value is string s ? s : DebugVariables.Show(value);
        }
    }

    private void WatchSetButton_OnClick(object? sender, RoutedEventArgs e) => SetWatchedVariable();

    private void WatchEditBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SetWatchedVariable(); e.Handled = true; }
        else if (e.Key == Key.Escape) { UpdateWatchEditor(); e.Handled = true; }
    }

    private void SetWatchedVariable()
    {
        if (!_stoppedAtPause || _debugVars == null || VariableListBox.SelectedItem is not WatchItem item) return;
        var error = _debugVars.Set(item.Name, WatchEditBox.Text ?? "");
        WatchEditError.Text = error ?? "";
        WatchEditError.IsVisible = error != null;
        if (error == null) ShowVariables(_debugVars.Snapshot());
    }
    
    private void HandleGlobalKeyUp(object? sender, KeyEventArgs e)
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
                if (_screenWindow != null)
                    ScreenConsole.Apply(_screenWindow, _textScreen, line);
            }
            else if (LogBox is not null)
            {
                LogBox.Text += line + Environment.NewLine;
                LogBox.CaretIndex = LogBox.Text.Length;
                // An error with its line: a wavy line under it in the editor, with the message on hover
                var err = System.Text.RegularExpressions.Regex.Match(line, @"(?i)error.*?\bat line (\d+)\s*:?\s*(.*)$");
                if (err.Success && int.TryParse(err.Groups[1].Value, out int errLine))
                    _assist.ShowError(errLine, err.Groups[2].Value.Length > 0 ? err.Groups[2].Value : line);
            }
        });
    }

    // A variable's value for hovering in the editor, while a program runs or is paused
    private object? VariableForHover(string name)
    {
        if (RunButton.IsEnabled) return null;   // no program running
        if (_stoppedAtPause && _debugVars != null) return _debugVars.Get(name);
        return _currentVars != null && _currentVars.TryGetValue(name, out var v) ? v : null;
    }

    private async void Exit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await CheckUnsavedChangesAsync())
        {
            // If the user answered "No" (does not want to lose changes), cancel the closing completely
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
        e.Cancel = true; // Stop the window straight away

        if (!await CheckUnsavedChangesAsync())
        {
            // If the user answered "No" (does not want to lose changes), cancel the closing completely
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
            // Closes the window after the dialog
            this.Closing -= MainWindow_Closing; // Undvik loop
            this.Close();
        }
    }
    
    // NEW: Helper method that checks for unsaved changes
    private async Task<bool> CheckUnsavedChangesAsync()
    {
        if (!_isDirty) return true; // No changes, go ahead!

        var result = await MessageBox(
            "Unsaved changes",
            "You have unsaved changes. Do you want to continue?",
            "Yes",  // Continue (throw away the changes)
            "No"  // Cancel (go back and save)
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

        await dialog.ShowDialog(this); // Owner = MainWindow

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
        // We use InvokeAsync directly and return the Task.
        // (The method could also be 'async Task' and use 'await' inside)
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
            // Keys let go of in another window are never heard here: none is held when it is left
            _screenWindow.Deactivated += (_, _) => _pressedKeys.Clear();
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
        _gfx.OnScreenResolutionChanged += (w, h) => {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                _screenWindow?.UpdateScreenSize(w, h);
            });
        };
        _gfx.Screen(640,480);
        foreach(var id in _gfx.GetSpriteIds()) {
            _gfx.SpriteOff(id);
        }
        // ✅ NEW: Clear ALL sprites and bobs completely
        _gfx.ClearAllResources();
        if (_screenWindow.Console != null) _screenWindow.Console.Text = "";
        _screenWindow.ScreenControl.Graphics = _gfx;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;

        // FPS, BASIC and CPU load in the game window's title, twice a second
        _gfx.TakeFrameStats();
        var statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        statsTimer.Tick += (_, _) => UpdateScreenTitle(_gfx.TakeFrameStats());
        statsTimer.Start();
        var program = Editor.Text ?? string.Empty;
        _assist.ClearError();
        _debugVars = new DebugVariables();
        _stoppedAtPause = false;

        try
        {
            // IMPORTANT: async here in the lambda for Task.Run
            await Task.Run(async () =>
            {

                await AmosRunner.ExecuteAsync(
                    programText: program,
                    appendLineAsync: AppendConsoleLineAsync,
                    // IMPORTANT: async here too for input
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
                            }
                        }, DispatcherPriority.Render);
                    },
 
                    getInkey: () => _pressedKeys.FirstOrDefault() ?? "",
                    isKeyDown: (k) => _pressedKeys.Contains(k),
                    audioEngine: _audioEngine,
                    token: token,
                    onVariablesChanged: (vars) => {
                        Dispatcher.UIThread.Post(() =>
                        {
                            // While paused the list shows what the pause read, which is newer
                            if (!_stoppedAtPause) ShowVariables(vars);
                        });
                    },
                    debugVariables: _debugVars,
                    // IMPORTANT: async here for the debug step
                    waitForStep: async (pc) => {
                        // NEW: Check whether we hit a breakpoint
                        bool hitBreakpoint = _breakpoints.Contains(pc + 1);
                            
                        if (hitBreakpoint && !_isPaused)
                        {
                            // Turn on pause mode automatically
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
                                        // FIXED: Use the Document API instead of working it out by hand
                                        if (pc >= 0 && pc < Editor.Document.LineCount)
                                        {
                                            // Get the line from the document (1-based -> 0-based)
                                            var docLine = Editor.Document.GetLineByNumber(pc + 1);
                                            int lineStart = docLine.Offset;
                                            int lineLength = docLine.Length;
                                                
                                            // IMPORTANT: Use Select() instead of setting the properties directly
                                            Editor.CaretOffset = lineStart;
                                                
                                            if (lineLength > 0)
                                            {
                                                Editor.Select(lineStart, lineLength);
                                            }
                                            
                                            Editor.ScrollToLine(pc + 1);
                                            
                                            Editor.Focus();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Safety measure: if something goes wrong, just set the caret
                                        System.Diagnostics.Debug.WriteLine($"Error selecting line: {ex.Message}");
                                        if (pc >= 0 && pc < Editor.Document.LineCount)
                                        {
                                            var docLine = Editor.Document.GetLineByNumber(pc + 1);
                                            Editor.CaretOffset = docLine.Offset;
                                        }
                                    }
                                }
                            });
                            // The program stands still here: show the variables as they are now and let them be changed
                            var snapshot = _debugVars?.Snapshot();
                            _stepSignal = new TaskCompletionSource<bool>();
                            _stoppedAtPause = true;
                            Dispatcher.UIThread.Post(() => { if (snapshot != null) ShowVariables(snapshot); UpdateWatchEditor(); });
                            await _stepSignal.Task;
                            _stoppedAtPause = false;
                            Dispatcher.UIThread.Post(UpdateWatchEditor);
                        }
                    }
                ); // End of ExecuteAsync
            }, token); // End of Task.Run

            await AppendConsoleLineAsync("OK");
        }
        catch (OperationCanceledException) { await AppendConsoleLineAsync("STOPPED"); }
        catch (Exception ex) { await AppendConsoleLineAsync($"ERROR: {ex.Message}"); }
        finally 
        { 
            statsTimer.Stop();
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

    private void ToggleCrt_OnClick(object? sender, RoutedEventArgs e) => ToggleCrt();

    private async void Folders_OnClick(object? sender, RoutedEventArgs e)
    {
        if (await new FoldersWindow().ShowDialog<bool>(this))
            Log($"Folders: projects {Workspace.Projects}, graphics {Workspace.Graphics}, sound {Workspace.Sound}, export {Workspace.Export}" + Environment.NewLine);
    }

    private CrtSettingsWindow? _crtSettings;

    // One settings window at a time; it changes the game window while it is open
    private void CrtSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_crtSettings != null) { _crtSettings.Activate(); return; }
        _crtSettings = new CrtSettingsWindow(_gfx, () => _screenWindow?.ScreenControl.InvalidateVisual());
        _crtSettings.Closed += (_, _) => _crtSettings = null;
        _crtSettings.Show(this);
    }

    // Turns the CRT filter (picture tube look) on and off in the game window
    private void ToggleCrt()
    {
        _gfx.CrtEnabled = !_gfx.CrtEnabled;
        _screenWindow?.ScreenControl.InvalidateVisual();
        Log($"CRT effect {(_gfx.CrtEnabled ? "on" : "off")}" + Environment.NewLine);
    }

    private void UpdateScreenTitle(AmosGraphics.FrameStats stats)
    {
        if (_screenWindow == null) return;
        // BASIC = the share of time the interpreter works between WAIT VBL ("-" without WAIT VBL)
        var basic = stats.BasicPercent < 0 ? "-" : $"{stats.BasicPercent:F0}%";
        // SYNC = what WAIT VBL keeps time with: the screen's refresh rate, or a timer when the screen is not refreshing
        var sync = DisplayClock.Active ? $"{1000 / DisplayClock.RefreshMs:F0} Hz" : "timer";
        _screenWindow.Title = $"AMOS Screen | FPS {stats.Fps:F0} | BASIC {basic} | CPU {stats.CpuPercent:F0}% | SYNC {sync}";
    }

    private async void RunButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isProjectLoading) return;
        
        // We check whether Shift was down when we clicked, or call with true from code
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
            // Pause the music (BASS has a global pause, or we pause the mixer)
            ManagedBass.Bass.Pause(); 
        }
        else
        {
            // Start the music again
            ManagedBass.Bass.Start();
            _stepSignal?.TrySetResult(true);
        }
    }
    
    public bool HasBreakpoint(int lineNumber)
    {
        return _breakpoints.Contains(lineNumber);
    }
    
    // NEW: Check whether a line is empty or only holds whitespace/comments
    public bool IsEmptyOrCommentLine(int lineNumber)
    {
        if (Editor.Document == null) return true;
            
        // Check that the line number is valid
        if (lineNumber < 1 || lineNumber > Editor.Document.LineCount) return true;
            
        var line = Editor.Document.GetLineByNumber(lineNumber);
        var text = Editor.Document.GetText(line.Offset, line.Length).Trim();
            
        // Empty line
        if (string.IsNullOrWhiteSpace(text)) return true;
            
        // Only a comment (starts with ;)
        if (text.StartsWith(";")) return true;
            
        // Only a label (ends with :)
        if (text.EndsWith(":")) return true;
            
        // Handle line numbers first (e.g. "100 REM comment")
        // Remove a line number at the start, if any
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            // If the first word is a number, skip it
            if (int.TryParse(parts[0], out _) && parts.Length > 1)
            {
                var commandPart = parts[1].ToUpperInvariant();
                    
                // REM lines are comments too
                if (commandPart == "REM" || commandPart.StartsWith("REM"))
                    return true;
            }
            else
            {
                // No line number, check the first word directly
                var commandPart = parts[0].ToUpperInvariant();
                if (commandPart == "REM" || commandPart.StartsWith("REM"))
                    return true;
            }
        }
            
        return false;
    }
    
    // NEW: Toggle a breakpoint on the current line (F9)
    private void ToggleBreakpointAtCurrentLine()
    {
        if (Editor.Document == null) return;
            
        // Get the current line from the caret position
        int offset = Editor.CaretOffset;
        var location = Editor.Document.GetLocation(offset);
        int lineNumber = location.Line;
            
        // NEW: Prevent breakpoints on empty lines
        if (IsEmptyOrCommentLine(lineNumber))
        {
            return; // Do nothing
        }
            
        if (_breakpoints.Contains(lineNumber))
            _breakpoints.Remove(lineNumber);
        else
            _breakpoints.Add(lineNumber);
            
        // Force an update of the margin
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
        ManagedBass.Bass.Start(); // Make sure the sound does not get stuck in pause

        // Close the game window if it is open
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
        var win = new SpriteEditorWindow(_gfx, () => Editor.Text ?? "");
        win.Show();
    }

    private void MapButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var win = new MapEditorWindow(_gfx, () => Editor.Text ?? "");
        win.Show();
    }

    // Only one tracker window at a time, since they share the synth's channels
    private TrackerWindow? _trackerWindow;

    private void TrackerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_trackerWindow != null)
        {
            _trackerWindow.Activate();
            return;
        }
        TrackerWindow.ThemeChanger = ChangeTheme;
        TrackerWindow.RecentSettings = "AmosTracker";
        _trackerWindow = new TrackerWindow();
        _trackerWindow.Closed += (_, _) => _trackerWindow = null;
        _trackerWindow.Show();
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
        // Optional: also update the window title (what shows in the Windows/macOS bar)
        this.Title = $"AMOS Professional IDE - [{name}]";
    }

    private async void NewProject_OnClick(object? sender, RoutedEventArgs e)
    {
        // NEW: Check before we clear
        if (!await CheckUnsavedChangesAsync()) return;
        
        Editor.Text = "";
        _currentProjectFile = null; // Reset the file reference
        _gfx.Clear(Colors.Black);
        _textScreen.Clear();
        if (_screenWindow?.Console != null) _screenWindow.Console.Text = "";
        LogBox.Text = "New project started.\n";
        _isDirty = false; // Reset the flag
        
        LogBox.Clear();
        _breakpoints.Clear();
        VariableListBox.ItemsSource = null;
        _gfx.ClearAllResources();
        Editor.TextArea.Caret.Offset = 0;
        Editor.TextArea.Caret.BringCaretToView();
        UpdateTitleBar(); // Update to "Untitled"
    }


    
    private async void SaveAsProject_OnClick(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        IStorageFile? file = _currentProjectFile;
        //if ()
        //{
        file = await SaveDialog.AskFileAsync(sp, new FilePickerSaveOptions
        {
            Title = "Save AMOS Project",
            SuggestedFileName = _currentProjectFile?.Name ?? "project.amosproj",
            SuggestedStartLocation = await Workspace.StartFolderAsync(sp, WorkspaceFolder.Projects),
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
            
            _isDirty = false; // NEW: Mark as saved
            UpdateTitleBar(); // Show the new name (without the star)
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
            file = await SaveDialog.AskFileAsync(sp, new FilePickerSaveOptions
            {
                Title = "Save AMOS Project",
                SuggestedFileName = "project.amosproj",
                SuggestedStartLocation = await Workspace.StartFolderAsync(sp, WorkspaceFolder.Projects),
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
            
            _isDirty = false; // NEW: Mark as saved
            UpdateTitleBar(); // Show the new name (without the star)
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

        // Always start in the Projects folder (without it the system opens the folder used last,
        // which could be the tracker's songs)
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AMOS-like Project",
            AllowMultiple = false,
            SuggestedStartLocation = await Workspace.StartFolderAsync(sp, WorkspaceFolder.Projects),
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
        
        LogBox.Clear();
        VariableListBox.ItemsSource = null;
        Editor.TextArea.Caret.Offset = 0;
        Editor.TextArea.Caret.BringCaretToView();
    }
    
    // Project → Examples: one submenu per group, with the description as a tooltip
    private void BuildExamplesMenu()
    {
        var groups = new List<MenuItem>();
        foreach (var group in Examples.Groups)
        {
            var items = new List<MenuItem>();
            foreach (var example in group.Items)
            {
                var item = new MenuItem { Header = example.Title };
                ToolTip.SetTip(item, example.Description);
                item.Click += async (_, _) => await OpenExampleAsync(example);
                items.Add(item);
            }
            groups.Add(new MenuItem { Header = group.Name, ItemsSource = items });
        }
        ExamplesMenuItem.ItemsSource = groups;
    }

    private async Task OpenExampleAsync(Examples.Example example)
    {
        // The examples stay in the working folder's Projects, also when your own Projects folder is moved
        string path = Path.Combine(Workspace.DefaultOf(WorkspaceFolder.Projects), example.File + ".amosproj");
        await OpenProjectFromPathAsync(path);
        string? opened = _currentProjectFile?.Path.LocalPath;
        if (opened == null || !string.Equals(Path.GetFullPath(opened), Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase))
            return;   // not found, or the user cancelled

        configService.Config.LastProjectPath = path;
        configService.Save();
        Editor.TextArea.Caret.Offset = 0;
        Editor.TextArea.Caret.BringCaretToView();
        await AppendConsoleLineAsync(example.Description);
    }

    public async Task OpenProjectFromStorageFileAsync(IStorageFile file)
    {
        SetProjectLoading(true);
        try
        {
            await using var stream = await file.OpenReadAsync();
            var project = await AmosProjectSerializer.LoadAsync(stream)
                ?? throw new InvalidDataException("Invalid project file");

            Editor.Text = project.ProgramText ?? string.Empty;

            _currentProjectFile = file;

            _isDirty = false;
            UpdateTitleBar();

            await AppendConsoleLineAsync($"Opened: {file.Name}");
            
            // ✅ SAVE LAST OPENED FILE
            configService.Config.LastProjectPath = file.Path.LocalPath;
            configService.Save();
            
            // CLEAR BREAKPOINTS
            _breakpoints.Clear();
            Editor.TextArea.TextView.Redraw();
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR loading: {ex.Message}");
        }
        finally
        {
            SetProjectLoading(false);
        }
    }

    public async Task OpenProjectFromPathAsync(string filePath)
    {
        SetProjectLoading(true);
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

            var project = await AmosProjectSerializer.LoadAsync(stream)
                ?? throw new InvalidDataException("Invalid project file");

            Editor.Text = project.ProgramText ?? string.Empty;

            // Create an IStorageFile reference from the path
            _currentProjectFile = await StorageProvider.TryGetFileFromPathAsync(filePath);

            _isDirty = false;
            UpdateTitleBar();

            await AppendConsoleLineAsync($"Opened: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            await AppendConsoleLineAsync($"ERROR loading: {ex.Message}");
        }
        finally
        {
            SetProjectLoading(false);
        }
    }
    
    private void SetProjectLoading(bool loading)
    {
        _isProjectLoading = loading;
        Dispatcher.UIThread.Post(() =>
        {
            RunButton.IsEnabled = !loading;
            LoadButton.IsEnabled = !loading;
            SaveButton.IsEnabled = !loading;
            SpriteButton.IsEnabled = !loading;
            MapButton.IsEnabled = !loading;
            RunButton.Content = loading ? "[ LOADING... ]" : "[ RUN ]";
            RunButton.Background = loading 
                ? new SolidColorBrush(Color.Parse("#666666"))
                : new SolidColorBrush(Color.Parse("#00CC00"));
        });
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
        ApplyTheme(AmosThemes.ByName(themeName));

        // Save the theme in the config
        configService.Config.DefaultTheme = themeName;
        configService.Save();
    }
    
    
    
    // Project > Export app...: builds a stand-alone app from the program in the editor with
    // Tools/export_app.py and shows how it goes in the log
    private async void ExportApp_OnClick(object? sender, RoutedEventArgs e)
    {
        var sourceRoot = AppExporter.FindSourceRoot();
        if (sourceRoot == null)
        {
            Log("EXPORT: Exporting needs the AmosLikeBasic source code and the .NET SDK. " +
                $"Run the IDE from the project folder (the one with {AppExporter.ScriptPath}).\n");
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(_currentProjectFile?.Name ?? "MyGame");
        var options = await new ExportDialog(defaultName).ShowDialog<ExportOptions?>(this);
        if (options == null) return;

        // Export what is in the editor, even if it is not saved
        var tempDir = Path.Combine(Path.GetTempPath(), "AmosLikeBasicExport");
        Directory.CreateDirectory(tempDir);
        var projectFile = Path.Combine(tempDir, options.Name + ".amosproj");
        await File.WriteAllTextAsync(projectFile, System.Text.Json.JsonSerializer.Serialize(
            new { Version = 2, ProgramText = Editor.Text ?? "" }));

        void LogLine(string line) => Dispatcher.UIThread.Post(() =>
        {
            Log(line + Environment.NewLine);
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });

        ExportAppMenuItem.IsEnabled = false;
        LogLine($"EXPORT: Building '{options.Name}' ({options.Platform}), this takes a minute...");
        try
        {
            var exportDir = Workspace.Export;
            int exitCode = await AppExporter.RunAsync(sourceRoot, projectFile, options.Name, options.Platform, LogLine, exportDir);
            if (exitCode == 0)
            {
                LogLine($"EXPORT: Done. The app is in {exportDir}");
                AppExporter.OpenFolder(exportDir);
            }
            else
            {
                LogLine($"EXPORT: Failed (exit code {exitCode}), see the messages above.");
            }
        }
        catch (Exception ex)
        {
            LogLine($"EXPORT: {ex.Message}");
        }
        finally
        {
            ExportAppMenuItem.IsEnabled = true;
            try { File.Delete(projectFile); } catch (IOException) { }
        }
    }

    private async void About_OnClick(object? sender, RoutedEventArgs e)
    {
        var aboutWin = new AboutWindow();
        await aboutWin.ShowDialog(this);
    }


    private List<Button>? _neutralToolbarButtons;

    private AmosTheme? _currentTheme;

    private void ApplyTheme(AmosTheme theme)
    {
        _currentTheme = theme;
        _assist.SetTheme(theme);
        var font = new FontFamily(theme.Font);

        // Theme colours for all windows (the sprite and map editors etc.), with readable text colours
        var palette = ThemeResources.Apply(theme);

        // Window & editor
        this.Background = new SolidColorBrush(theme.WindowBg);
        Editor.Background = new SolidColorBrush(theme.EditorBg);
        Editor.Foreground = new SolidColorBrush(theme.EditorFg);
        Editor.FontFamily = font;
        Editor.FontSize = 20;

        // Toolbar
        ToolbarBorder.Background = new SolidColorBrush(theme.ToolbarBg);
        ToolbarBorder.BorderBrush = new SolidColorBrush(theme.SplitterColor);

        // Titelrad
        AmosTitleBar.Background = new SolidColorBrush(theme.TitleBarBg);
        FileNameText.Foreground = new SolidColorBrush(theme.TitleBarFg);

        // Status bar (line number)
        CursorPosText.Background = new SolidColorBrush(theme.EditorCursorPosBg);
        CursorPosText.Foreground = new SolidColorBrush(theme.AccentColor);

        // Loggbox
        LogBox.Background = new SolidColorBrush(theme.LogBg);
        LogBox.Foreground = new SolidColorBrush(theme.AccentColor);
        LogBox.FontFamily = font;

        // Variabelbox
        VariableWatchPanel.Background = new SolidColorBrush(theme.VarWatchBg);
        VariableWatchPanel.BorderBrush = new SolidColorBrush(theme.SplitterColor);
        WatchSplitter.Background = new SolidColorBrush(theme.SplitterColor);
        LogSplitter.Background = new SolidColorBrush(theme.SplitterColor);
        VariableListBox.Background = new SolidColorBrush(theme.VarWatchBg);
        VariableListBox.Foreground = new SolidColorBrush(theme.VarWatchFg);
        // Names in the accent colour, values in the text colour, both readable on the theme's background
        var watchBg = theme.VarWatchBg;
        var watchFg = ThemeResources.Readable(watchBg, theme.VarWatchFg, theme.EditorFg, Colors.White, Colors.Black);
        var watchName = ThemeResources.Readable(watchBg, theme.VarWatchAccentFg, theme.AccentColor, watchFg);
        Application.Current!.Resources["WatchValueBrush"] = new SolidColorBrush(watchFg);
        Application.Current!.Resources["WatchNameBrush"] = new SolidColorBrush(watchName);
        Application.Current!.Resources["WatchKindBrush"] = new SolidColorBrush(Color.FromArgb(150, watchFg.R, watchFg.G, watchFg.B));
        WatchEditPanel.Background = new SolidColorBrush(watchBg);
        WatchEditPanel.BorderBrush = new SolidColorBrush(theme.SplitterColor);
        WatchEditLabel.Foreground = new SolidColorBrush(watchFg);
        var headerFg = ThemeResources.Readable(theme.TopBarBg, theme.VarWatchFg, theme.MenuFg, Colors.White, Colors.Black);
        WatchTitle.Foreground = new SolidColorBrush(ThemeResources.Readable(theme.TopBarBg, Colors.Yellow, theme.AccentColor, headerFg));
        StatusText.Foreground = new SolidColorBrush(headerFg);
        CurrentLineText.Foreground = new SolidColorBrush(ThemeResources.Readable(theme.TopBarBg, theme.VarWatchAccentFg, Colors.Lime, headerFg));

        // Toolbar buttons (those without an explicit colour). They are recognised by their XAML colour the first time;
        // after that they have the theme's colour, so the list is kept for later theme changes.
        _neutralToolbarButtons ??= ToolbarBorder.GetVisualDescendants().OfType<Button>()
            .Where(btn => btn.Background is SolidColorBrush b && b.Color == Color.Parse("#0A2D8F"))
            .ToList();
        foreach (var btn in _neutralToolbarButtons)
        {
            btn.Background = new SolidColorBrush(theme.ButtonBg);
            btn.Foreground = new SolidColorBrush(palette.ButtonFg);
        }

        // Find the header border in VarWatch
        if (VariableWatchPanel.Child is DockPanel dp)
        {
            var header = dp.Children.OfType<Border>().FirstOrDefault();
            if (header != null)
                header.Background = new SolidColorBrush(theme.TopBarBg);
        }

        // Action-knappar (SPRITES, MAP, LOAD, SAVE)
        foreach (var btn in ToolbarBorder.GetVisualDescendants().OfType<Button>())
        {
            var name = btn.Name ?? "";
            if (name is "SpriteButton" or "MapButton" or "LoadButton" or "SaveButton"  or "ClearLogs")
            {
                btn.Background = new SolidColorBrush(theme.ActionButtonBg);
                btn.Foreground = new SolidColorBrush(
                    ThemeResources.Readable(theme.ActionButtonBg, theme.ActionButtonFg, theme.MenuFg));
            }
        }
        
        // Meny
        TopMenu.Background = new SolidColorBrush(theme.MenuBg);
        TopMenu.Foreground = new SolidColorBrush(theme.MenuFg);
        Application.Current!.Resources["MenuBgBrush"] = new SolidColorBrush(theme.MenuBg);
        Application.Current!.Resources["MenuFgBrush"] = new SolidColorBrush(theme.MenuFg);
    }

    private void ToggleFullscreen_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    // NEW: Breakpoint margin for the editor
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

            // Draw the background of the margin
            context.FillRectangle(
                new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            foreach (var line in textView.VisualLines)
            {
                int lineNumber = line.FirstDocumentLine.LineNumber;
                    
                if (_window._breakpoints.Contains(lineNumber))
                {
                    // Draw a red circle for a breakpoint
                    var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                        
                    // Filled circle
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
                
            // CHANGED: Get the position relative to THIS margin, not the textView
            var pos = e.GetPosition(this);
                
            // Find which line was clicked
            var line = textView.GetVisualLineFromVisualTop(pos.Y + textView.VerticalOffset);
            if (line != null)
            {
                int lineNumber = line.FirstDocumentLine.LineNumber;
                    
                // NEW: Prevent breakpoints on empty lines
                if (_window.IsEmptyOrCommentLine(lineNumber))
                {
                    e.Handled = true;
                    return; // Do nothing
                }
                    
                if (_window._breakpoints.Contains(lineNumber))
                    _window._breakpoints.Remove(lineNumber);
                else
                    _window._breakpoints.Add(lineNumber);
                    
                InvalidateVisual();
                    
                // Also update the textView so that everything is redrawn
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