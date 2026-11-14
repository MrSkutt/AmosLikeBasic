using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AmosLikeBasic;

// Det som både IDE:n (MainWindow) och spelarläget (GamePlayer) behöver för att köra ett
// program i ett ScreenWindow: textkonsolen och tolkningen av tangenter.

// Textkonsolen (80x30 tecken) som PRINT, LOCATE och CLS skriver till
internal sealed class TextScreen
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

internal static class ScreenConsole
{
    // Utför ett "@@"-kommando från interpretatorn (PRINT, LOCATE, CLS, INK, PAPER) i fönstrets
    // textkonsol. Måste anropas på UI-tråden.
    public static void Apply(ScreenWindow window, TextScreen textScreen, string line)
    {
        if (window.Console is null) return;

        if (line.StartsWith("@@PAPER ", StringComparison.Ordinal))
        {
            var arg = line.Substring(8).Trim(); // efter "@@PAPER "
            try
            {
                var c = Color.Parse(arg);
                window.ScreenGrid.Background = new SolidColorBrush(c);
            }
            catch
            {
                // Om färgen inte kan tolkas: ignorera eller sätt default
                window.Console.Background = Brushes.Black;
            }
        }
        else if (line.StartsWith("@@INK ", StringComparison.Ordinal))
        {
            var arg = line.Substring(6).Trim();
            try
            {
                var c = Color.Parse(arg);
                window.Console.Foreground = new SolidColorBrush(c);
            }
            catch
            {
                window.Console.Foreground = Brushes.White;
            }
        }

        if (line.StartsWith("@@LOCATE ", StringComparison.Ordinal))
        {
            var rest = line.Substring(9).Trim();
            var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var r) && int.TryParse(parts[1], out var c))
                textScreen.Locate(r, c);
        }
        else if (line.StartsWith("@@PRINT ", StringComparison.Ordinal))
        {
            textScreen.Print(line.Substring(8));
        }
        else if (line.StartsWith("@@CLS", StringComparison.Ordinal))
        {
            textScreen.Clear();
        }

        // Viktigt: trailing newline ger ScrollViewer “plats” att scrolla så sista raden blir hel
        window.Console.Text = textScreen.Render() + "\n";

        // Flytta caret sist
        window.Console.CaretIndex = window.Console.Text?.Length ?? 0;
        window.Console.SelectionStart = window.Console.CaretIndex;
        window.Console.SelectionEnd = window.Console.CaretIndex;

        // Scrolla längst ner (låt ScrollViewer själv klampa till max)
        Dispatcher.UIThread.Post(() => ScrollToEnd(window), DispatcherPriority.Render);

        // En extra “sen” post kan hjälpa om font/layout uppdateras efter Render-pass
        Dispatcher.UIThread.Post(() => ScrollToEnd(window), DispatcherPriority.Background);
    }

    private static void ScrollToEnd(ScreenWindow window)
    {
        var sv = window.Console
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (sv is null) return;

        sv.Offset = new Vector(sv.Offset.X, double.MaxValue);
    }

    // Tecknet en tangent ger när man skriver vid INPUT
    public static string CharFromKey(Key key, bool shift)
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
}
