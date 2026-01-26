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
        var amosFont = new FontFamily("Topaz a600a1200a400");
        Console.FontFamily = amosFont;
        
        Console.LayoutUpdated += (_, _) => UpdateConsoleFont();
        
        Opened += (_, _) =>
        {
            ScreenControl?.Focus();
            GridControl?.Focus();
        };
    }

    private void UpdateConsoleFont()
    {
        const int Columns = 80;
        const int Rows = 30;

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
        // Uppdatera Console-texten för att visa prompt + det användaren skriver
        Console.Text = _inputBaseText + " " + _currentPrompt + _currentInput;
    }
    
}