using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace AmosLikeBasic;

// What makes the code editor a modern one: syntax colours, completion as you type, help when you
// hover a word (and a variable's value while the program runs), F1 for the manual's section, and
// a wavy line under the line where the program stopped with an error.
public sealed class CodeAssist
{
    private readonly TextEditor _editor;
    private readonly Func<string, object?> _variable;   // a variable's value now, null if unknown
    private readonly BasicColorizer _colorizer = new();
    private readonly ErrorRenderer _errors = new();
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    // The program's procedures and labels are read again a moment after typing stops
    private readonly DispatcherTimer _namesTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Point _hoverPoint;
    private CompletionWindow? _completion;
    private HelpWindow? _help;

    public CodeAssist(TextEditor editor, Func<string, object?> variable)
    {
        _editor = editor;
        _variable = variable;
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Add(_errors);
        editor.TextArea.TextEntered += OnTextEntered;
        editor.Document.Changed += OnDocumentChanged;
        editor.DocumentChanged += (_, _) => editor.Document.Changed += OnDocumentChanged;
        editor.TextChanged += (_, _) => { _namesTimer.Stop(); _namesTimer.Start(); };
        _namesTimer.Tick += (_, _) => { _namesTimer.Stop(); ReadOwnNames(); };

        // Hovering: wait until the pointer rests, then show what is under it
        editor.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            var p = e.GetPosition(editor);
            if (Math.Abs(p.X - _hoverPoint.X) + Math.Abs(p.Y - _hoverPoint.Y) > 3) HideTip();
            _hoverPoint = p;
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        editor.PointerExited += (_, _) => { _hoverTimer.Stop(); HideTip(); };
        editor.AddHandler(InputElement.KeyDownEvent, (_, _) => { _hoverTimer.Stop(); HideTip(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); ShowTip(); };
    }

    // ── Colours ──────────────────────────────────────────────────────

    // The syntax colours that are readable on the theme's editor background
    public void SetTheme(AmosTheme theme)
    {
        var bg = theme.EditorBg;
        Color Pick(params string[] candidates)
        {
            foreach (var c in candidates.Select(Color.Parse))
                if (ThemeResources.Contrast(bg, c) >= 3.0) return c;
            return theme.EditorFg;
        }
        _colorizer.Keyword = new SolidColorBrush(Pick("#FFD75F", theme.AccentColor.ToString(), "#7B2CBF", "#0033AA"));
        _colorizer.Function = new SolidColorBrush(Pick("#7FE0FF", "#00A0C8", "#006A8E"));
        _colorizer.String = new SolidColorBrush(Pick("#A6E22E", "#7EC850", "#2E7D32", "#1B5E20"));
        _colorizer.Number = new SolidColorBrush(Pick("#FFB86C", "#F08C3C", "#B35C00", "#8A4500"));
        _colorizer.Label = new SolidColorBrush(Pick("#FF8FD8", "#E05CC0", "#A0207C"));
        var fg = theme.EditorFg;
        _colorizer.Comment = new SolidColorBrush(Color.FromArgb(150, fg.R, fg.G, fg.B));
        _editor.TextArea.TextView.Redraw();
    }

    // The procedures, functions and labels of the program, coloured where they are used
    private void ReadOwnNames()
    {
        var names = new HashSet<string>(ProgramNames().Where(n => n.kind != "variable").Select(n => n.name), StringComparer.OrdinalIgnoreCase);
        if (names.SetEquals(_colorizer.OwnNames)) return;
        _colorizer.OwnNames = names;
        _editor.TextArea.TextView.Redraw();
    }

    // ── Errors ───────────────────────────────────────────────────────

    // A wavy line under the line (1-based) where the program stopped, with the message on hover
    public void ShowError(int line, string message)
    {
        if (line < 1 || line > _editor.Document.LineCount) return;
        _errors.Line = line;
        _errors.Message = message;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void ClearError()
    {
        if (_errors.Line == 0) return;
        _errors.Line = 0;
        _errors.Message = "";
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    // Typing on the error line means it is being fixed: the mark goes
    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_errors.Line == 0) return;
        int changed = _editor.Document.GetLineByOffset(Math.Min(e.Offset, _editor.Document.TextLength)).LineNumber;
        if (changed == _errors.Line || e.InsertedText.Text.Contains('\n') || e.RemovedText.Text.Contains('\n')) ClearError();
    }

    // ── Words ────────────────────────────────────────────────────────

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '%';

    // The word at an offset, and the word after it on the same line (for "MAP OBJECTS")
    private (string word, string? next, int start) WordAt(int offset)
    {
        var doc = _editor.Document;
        if (offset < 0 || offset > doc.TextLength) return ("", null, offset);
        int start = offset, end = offset;
        while (start > 0 && IsWordChar(doc.GetCharAt(start - 1))) start--;
        while (end < doc.TextLength && IsWordChar(doc.GetCharAt(end))) end++;
        string word = doc.GetText(start, end - start);
        var line = doc.GetLineByOffset(Math.Min(offset, doc.TextLength));
        string rest = doc.GetText(end, line.EndOffset - end);
        var next = Regex.Match(rest, @"^\s+([A-Za-z][A-Za-z0-9]*)");
        return (word, next.Success ? next.Groups[1].Value.ToUpperInvariant() : null, start);
    }

    // Is the offset inside a string or a comment, where words mean nothing?
    private bool InTextOrComment(int offset)
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(Math.Min(offset, doc.TextLength));
        string before = doc.GetText(line.Offset, offset - line.Offset);
        bool quoted = false;
        for (int i = 0; i < before.Length; i++)
        {
            char c = before[i];
            if (c == '"') quoted = !quoted;
            else if (!quoted && BasicColorizer.StartsComment(before, i)) return true;
        }
        return quoted;
    }

    // ── Hovering ─────────────────────────────────────────────────────

    private void ShowTip()
    {
        var pos = _editor.GetPositionFromPoint(_hoverPoint);
        if (pos == null) return;
        int offset = _editor.Document.GetOffset(pos.Value.Location);
        var parts = new List<Control>();

        if (_errors.Line == pos.Value.Line && _errors.Message.Length > 0)
            parts.Add(TipText("⚠ " + _errors.Message, Brushes.OrangeRed, bold: true));

        if (!InTextOrComment(offset))
        {
            var (word, next, _) = WordAt(offset);
            if (word.Length > 0 && !char.IsDigit(word[0]))
            {
                var value = _variable(word);
                if (value != null)
                    parts.Add(TipText($"{word} = {Describe(value)}", null, bold: true));
                else if (BasicHelp.Find(word.ToUpperInvariant(), next) is { } help)
                {
                    if (help.Syntax.Length > 0) parts.Add(TipText(help.Syntax, null, mono: true));
                    parts.Add(TipText(BasicHelp.Short(help), null));
                    parts.Add(TipText("F1: the whole section of the manual", Brushes.Gray, small: true));
                }
            }
        }
        if (parts.Count == 0) return;

        var panel = new StackPanel { Spacing = 4, MaxWidth = 520 };
        foreach (var p in parts) panel.Children.Add(p);
        ToolTip.SetTip(_editor, panel);
        ToolTip.SetPlacement(_editor, PlacementMode.Pointer);
        ToolTip.SetIsOpen(_editor, true);
    }

    private void HideTip()
    {
        if (ToolTip.GetIsOpen(_editor)) ToolTip.SetIsOpen(_editor, false);
        ToolTip.SetTip(_editor, null);
    }

    private static TextBlock TipText(string text, IBrush? brush, bool bold = false, bool mono = false, bool small = false)
    {
        var t = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = small ? 11 : 13,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal
        };
        if (mono) t.FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace");
        if (brush != null) t.Foreground = brush;
        return t;
    }

    // A variable as the tooltip shows it: arrays with their size and first values
    private static string Describe(object value)
    {
        if (value is not IAmosArray a) return DebugVariables.Show(value);
        var first = new List<string>();
        for (int i = 0; i < Math.Min(8, a.Length); i++)
        {
            object v = a is AmosNumericArray n ? n.Data[i] : ((AmosStringArray)a).Data[i] ?? "";
            first.Add(DebugVariables.Show(v));
        }
        return $"{DebugVariables.Show(a)}: {string.Join(", ", first)}{(a.Length > 8 ? ", ..." : "")}";
    }

    // ── F1 ───────────────────────────────────────────────────────────

    // The manual's section for the word at the caret (or the selected word); false if there is none
    public bool ShowHelpAtCaret(Window owner)
    {
        int offset = _editor.CaretOffset;
        var (word, next, _) = WordAt(offset);
        if (word.Length == 0 && offset > 0) (word, next, _) = WordAt(offset - 1);
        var help = word.Length > 0 ? BasicHelp.Find(word.ToUpperInvariant(), next) : null;
        if (_help == null)
        {
            _help = new HelpWindow();
            _help.Closed += (_, _) => _help = null;
            _help.Show(owner);
        }
        _help.ShowEntry(help, word);
        _help.Activate();
        return help != null;
    }

    // ── Completion ───────────────────────────────────────────────────

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completion != null || string.IsNullOrEmpty(e.Text)) return;
        int caret = _editor.CaretOffset;
        if (InTextOrComment(caret)) return;
        char ch = e.Text[^1];

        // After a command with sub-commands and a space: its words (SPRITE → POS, ON ...)
        if (ch == ' ')
        {
            string statement = StatementBefore(caret);
            var m = Regex.Match(statement, @"^([A-Za-z]+)\s$");
            if (m.Success && BasicHelp.SubCommandsOf(m.Groups[1].Value).Count > 0)
                Open(caret, Items(m.Groups[1].Value.ToUpperInvariant(), subCommandsOnly: true));
            return;
        }

        // Two letters into a word: everything that fits
        if (!char.IsLetter(ch)) return;
        var (word, _, start) = WordAt(caret);
        if (word.Length != 2 || caret != start + word.Length) return;
        string before = StatementBefore(start);
        var cmd = Regex.Match(before, @"^([A-Za-z]+)\s+$");
        Open(start, Items(cmd.Success ? cmd.Groups[1].Value.ToUpperInvariant() : null, subCommandsOnly: false));
    }

    // The text of the statement up to an offset (after the last ':' outside strings on the line)
    private string StatementBefore(int offset)
    {
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(offset);
        string text = doc.GetText(line.Offset, offset - line.Offset);
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            else if (text[i] == ':' && !quoted) start = i + 1;
        }
        return text[start..].TrimStart();
    }

    private List<CompletionItem> Items(string? command, bool subCommandsOnly)
    {
        var items = new List<CompletionItem>();
        if (command != null)
            foreach (var w in BasicHelp.SubCommandsOf(command).OrderBy(w => w))
                items.Add(new CompletionItem(w, "word", BasicHelp.Find(command, w), 4));
        if (subCommandsOnly) return items;

        foreach (var k in BasicHelp.Keywords.OrderBy(k => k))
            items.Add(new CompletionItem(k, "command", BasicHelp.Find(k), 3));
        foreach (var f in BasicHelp.Functions.OrderBy(f => f))
            items.Add(new CompletionItem(f, "function", BasicHelp.Find(f), 2.5));
        // The program's own names: variables, procedures, functions and labels
        foreach (var (name, kind) in ProgramNames())
            items.Add(new CompletionItem(name, kind, null, 2));
        return items.GroupBy(i => i.Text, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(i => i.Priority).First()).ToList();
    }

    private IEnumerable<(string name, string kind)> ProgramNames()
    {
        string text = _editor.Document.Text;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"^\s*(?:PROC|FUNCTION)\s+([A-Za-z_][A-Za-z0-9_]*[$%]?)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (seen.Add(m.Groups[1].Value)) yield return (m.Groups[1].Value, "procedure");
        foreach (Match m in Regex.Matches(text, @"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Multiline))
            if (seen.Add(m.Groups[1].Value)) yield return (m.Groups[1].Value, "label");
        foreach (Match m in Regex.Matches(text, @"(?:^|[\s:])(?:FOR\s+|DIM\s+|LOCAL\s+|CONST\s+)?([A-Za-z_][A-Za-z0-9_]*[$%]?)\s*(?:\(|=)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            string name = m.Groups[1].Value;
            if (BasicHelp.Keywords.Contains(name) || BasicHelp.Functions.Contains(name) || !seen.Add(name)) continue;
            yield return (name, "variable");
        }
    }

    private void Open(int startOffset, List<CompletionItem> items)
    {
        if (items.Count == 0) return;
        _completion = new CompletionWindow(_editor.TextArea) { StartOffset = startOffset, CloseWhenCaretAtBeginning = true };
        foreach (var i in items) _completion.CompletionList.CompletionData.Add(i);
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
        // Typing filters the list; a word that is not in it closes the window
        string typed = _editor.Document.GetText(startOffset, _editor.CaretOffset - startOffset);
        if (typed.Length > 0) _completion.CompletionList.SelectItem(typed);
    }

    private sealed class CompletionItem : ICompletionData
    {
        public CompletionItem(string text, string kind, BasicHelp.Entry? help, double priority)
        {
            Text = text;
            Kind = kind;
            Help = help;
            Priority = priority;
        }

        public string Kind { get; }
        public BasicHelp.Entry? Help { get; }
        public IImage? Image => null;
        public string Text { get; }
        public object Content => $"{Text}   ({Kind})";
        // The command's syntax and what it does; nothing for the program's own names
        public object Description => Help == null ? null! : (Help.Syntax.Length > 0 ? Help.Syntax + "\n\n" : "") + BasicHelp.Short(Help);
        public double Priority { get; }

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, Text);
    }
}

// The syntax colours: commands, functions, strings, numbers, labels and comments
public sealed class BasicColorizer : DocumentColorizingTransformer
{
    public IBrush Keyword = Brushes.Gold, Function = Brushes.Cyan, String = Brushes.LightGreen,
        Number = Brushes.Orange, Label = Brushes.Pink, Comment = Brushes.Gray;
    public HashSet<string> OwnNames = new(StringComparer.OrdinalIgnoreCase);   // PROCs, FUNCTIONs and labels

    // A comment starts with ; or ' at the start of a statement or after a space (as the interpreter reads it:
    // PRINT A;B joins the values, PRINT A ; text is a comment)
    public static bool StartsComment(string line, int i)
    {
        char c = line[i];
        if (c != ';' && c != '\'') return false;
        if (i == 0 || char.IsWhiteSpace(line[i - 1])) return true;
        return line[..i].TrimEnd().EndsWith(':');
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int start = line.Offset;
        int i = 0;
        string? command = null;   // the first word of the statement, for its sub-commands

        void Paint(int from, int to, IBrush brush)
        {
            if (to > from) ChangeLinePart(start + from, start + to, el => el.TextRunProperties.SetForegroundBrush(brush));
        }

        // A label on its own: "Loop:"
        var label = Regex.Match(text, @"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*$");
        if (label.Success) { Paint(label.Groups[1].Index, label.Groups[1].Index + label.Groups[1].Length + 1, Label); return; }

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"')
            {
                int end = text.IndexOf('"', i + 1);
                end = end < 0 ? text.Length : end + 1;
                Paint(i, end, String);
                i = end;
            }
            else if (StartsComment(text, i))
            {
                Paint(i, text.Length, Comment);
                return;
            }
            else if (c == ':') { command = null; i++; }
            else if (char.IsDigit(c) || (c is '$' or '%' or '&' && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))))
            {
                int end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '.')) end++;
                Paint(i, end, Number);
                i = end;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int end = i;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '$' or '%')) end++;
                string word = text[i..end];
                if (word.Equals("REM", StringComparison.OrdinalIgnoreCase) && command == null)
                {
                    Paint(i, text.Length, Comment);
                    return;
                }
                bool isSub = command != null && BasicHelp.SubCommandsOf(command).Contains(word);
                if (isSub || BasicHelp.Keywords.Contains(word)) Paint(i, end, Keyword);
                else if (BasicHelp.Functions.Contains(word)) Paint(i, end, Function);
                else if (OwnNames.Contains(word)) Paint(i, end, Label);
                command ??= word;
                i = end;
            }
            else i++;
        }
    }
}

// The wavy red line under the line where the program stopped with an error
public sealed class ErrorRenderer : IBackgroundRenderer
{
    public int Line;
    public string Message = "";
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Line < 1 || textView.Document == null || Line > textView.Document.LineCount) return;
        var docLine = textView.Document.GetLineByNumber(Line);
        var pen = new Pen(Brushes.OrangeRed, 1.4);
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
        {
            drawingContext.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 69, 0)), rect);
            // The wave along the bottom of the line
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                double y = rect.Bottom - 2;
                g.BeginFigure(new Point(rect.Left, y), false);
                for (double x = rect.Left; x < Math.Max(rect.Right, rect.Left + 40); x += 3)
                    g.LineTo(new Point(x + 3, y + ((int)((x - rect.Left) / 3) % 2 == 0 ? -2 : 0)));
                g.EndFigure(false);
            }
            drawingContext.DrawGeometry(null, pen, geo);
        }
    }
}
