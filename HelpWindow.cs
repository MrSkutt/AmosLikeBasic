using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AmosLikeBasic;

// F1 in the code editor: the manual's section for the word at the caret
public class HelpWindow : Window
{
    private readonly TextBlock _title, _syntax, _text;

    public HelpWindow()
    {
        Title = "Help";
        Width = 560;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");

        _title = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 8) };
        _title[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosTitleFg");
        _syntax = new TextBlock
        {
            FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"), FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var syntaxBox = new Border
        {
            Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 10), Child = _syntax,
            BorderThickness = new Thickness(1)
        };
        syntaxBox[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosInputBg");
        syntaxBox[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBorder");

        Content = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(16), Children = { _title, syntaxBox, _text } }
        };
        _syntaxBox = syntaxBox;
    }

    private readonly Border _syntaxBox;

    public void ShowEntry(BasicHelp.Entry? entry, string word)
    {
        if (entry == null)
        {
            _title.Text = word.Length > 0 ? word.ToUpperInvariant() : "Help";
            _syntaxBox.IsVisible = false;
            _text.Text = word.Length > 0
                ? $"There is no command or function called {word.ToUpperInvariant()} in the manual. If it is one of your own variables, hover over it while the program runs to see its value."
                : "Put the cursor on a command or function and press F1 to see its part of the manual.";
            return;
        }
        Title = "Help: " + entry.Name;
        _title.Text = entry.Name + (entry.IsFunction ? "  (function)" : "");
        _syntaxBox.IsVisible = entry.Syntax.Length > 0;
        _syntax.Text = entry.Syntax;
        _text.Text = entry.Text;
    }
}
