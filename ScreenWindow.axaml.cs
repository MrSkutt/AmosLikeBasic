using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;


namespace AmosLikeBasic;

public partial class ScreenWindow : Window
{
    
    private TaskCompletionSource<string>? _inputCompletion;
    private string _currentInput = "";
    private string _currentPrompt = "";
    private bool _isInputMode = false;
    private string _inputBaseText = "";
    
    
    public ScreenWindow()
    {
        InitializeComponent();
        var amosFont = new FontFamily(ScreenDrawOperation.ConsoleFont);
        Console.FontFamily = amosFont;
        
        Console.LayoutUpdated += (_, _) => UpdateConsoleFont();

        // With the CRT filter the game view draws the console into the picture: the text box is then
        // hidden (but stays, for INPUT), and a change of text redraws the picture
        ScreenControl.ConsoleSource = () => new ConsoleOverlay(
            Console.Text ?? "",
            (Console.Foreground as ISolidColorBrush)?.Color ?? Colors.White,
            (ScreenGrid.Background as ISolidColorBrush)?.Color ?? Colors.Black,
            ConsoleColumns, ConsoleRows);
        // (after the render pass: a control may not be changed while it is being drawn)
        ScreenControl.ConsoleDrawnInPicture += inPicture =>
        {
            double opacity = inPicture ? 0 : 1;
            if (Console.Opacity != opacity)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Console.Opacity = opacity);
        };
        Console.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.ForegroundProperty)
                if (ScreenControl.Graphics?.CrtEnabled == true) ScreenControl.InvalidateVisual();
        };
        ScreenGrid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Panel.BackgroundProperty && ScreenControl.Graphics?.CrtEnabled == true)
                ScreenControl.InvalidateVisual();
        };
        
        
        Opened += (_, _) =>
        {
            ScreenControl?.Focus();
            GridControl?.Focus();
        };
        
    }

    public void UpdateScreenSize(int width, int height)
    {
        ScreenControl.Width = width;
        ScreenControl.Height = height;
        ScreenGrid.Width = width;
        ScreenGrid.Height = height;
    }
    
    private const int ConsoleColumns = 80, ConsoleRows = 30;

    private void UpdateConsoleFont()
    {
        const int Columns = ConsoleColumns;
        const int Rows = ConsoleRows;

        var bounds = Console.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        double charWidth = bounds.Width / Columns;
        double charHeight = bounds.Height / Rows;

        Console.FontSize = Math.Min(charWidth * 1.8, charHeight * 0.9);
    }
    
    private void OnScreenPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ScreenControl?.Focus();
        GridControl?.Focus();
    }
    
    public async Task<string> RequestInputAsync()
    {
        _isInputMode = true;
        _inputCompletion = new TaskCompletionSource<string>();
        _currentInput = "";
        StartInputMode(prompt: "");

        _inputBaseText = Console.Text ?? "";

        _inputBaseText = _inputBaseText.TrimEnd(
            ' ', '\t', '\r', '\n', '\u200B', '\uFEFF'
        );
        
        return await _inputCompletion.Task;
    }

    public void StartInputMode(string prompt)
    {
        _currentPrompt = prompt;
        _currentInput = "";
        _isInputMode = true;
        Console.IsReadOnly = true;
        
        ScreenControl?.Focus();
        GridControl?.Focus();
        Focus();
    }

    public void AppendInputChar(string ch)
    {
        if (_isInputMode)
        {
            _currentInput += ch;
            UpdateInputDisplay();
        }
    }

    public void BackspaceInput()
    {
        if (_isInputMode && _currentInput.Length > 0)
        {
            _currentInput = _currentInput[..(_currentInput.Length - 1)];
            UpdateInputDisplay();
        }
    }

    public void SubmitInput()
    {
        _isInputMode = false;
        _inputCompletion?.TrySetResult(_currentInput);
        Console.IsReadOnly = false;
        _currentInput = "";
        _currentPrompt = "";
    }


    private void UpdateInputDisplay()
    {
        // Update the console text to show the prompt + what the user types
        Console.Text = _inputBaseText + " " + _currentPrompt + _currentInput;
    }
    
}