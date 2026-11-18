using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AmosLikeBasic;

// The elements of an array from the variable watch. While the program is paused an element can be
// chosen and changed; Refresh reads the values again while it runs.
public class ArrayViewerWindow : Window
{
    private readonly IAmosArray _array;
    private readonly Func<bool> _canEdit;
    private readonly ListBox _list;
    private readonly TextBox _filter, _editBox;
    private readonly TextBlock _editLabel, _error;
    private readonly Grid _editRow;

    private sealed record Element(int Flat, int[] Indices, string Text)
    {
        public override string ToString() => Text;
    }

    public ArrayViewerWindow(string arrayName, IAmosArray array, Func<bool>? canEdit = null, AmosTheme? theme = null)
    {
        _array = array;
        _canEdit = canEdit ?? (() => false);
        Title = $"Array: {arrayName}";
        Width = 400;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var bg = theme?.VarWatchBg ?? Color.Parse("#00134D");
        var fg = ThemeResources.Readable(bg, theme?.VarWatchFg ?? Colors.White, Colors.White, Colors.Black);
        var accent = ThemeResources.Readable(bg, theme?.VarWatchAccentFg ?? Color.Parse("#7FB2FF"), fg);
        Background = new SolidColorBrush(bg);

        var header = new TextBlock
        {
            Text = $"{arrayName}  {DebugVariables.Show(array)}  —  {array.Length} elements",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(10, 8, 10, 4),
            FontSize = 13
        };
        _filter = new TextBox { PlaceholderText = "Filter by value or index...", Margin = new Thickness(6, 4, 6, 4), FontSize = 12 };
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(0, 4, 6, 4), VerticalAlignment = VerticalAlignment.Center };
        refresh.Click += (_, _) => Fill();
        var filterRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        filterRow.Children.Add(_filter);
        Grid.SetColumn(refresh, 1);
        filterRow.Children.Add(refresh);

        _list = new ListBox
        {
            Margin = new Thickness(6, 0, 6, 6),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(fg),
            FontFamily = new FontFamily("Courier New, Monospace"),
            FontSize = 13
        };

        _editLabel = new TextBlock { Foreground = new SolidColorBrush(fg), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        _editBox = new TextBox { FontFamily = new FontFamily("Courier New, Monospace"), FontSize = 13 };
        var setButton = new Button { Content = "Set", Margin = new Thickness(6, 0, 0, 0) };
        _editRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), IsVisible = false };
        _editRow.Children.Add(_editBox);
        Grid.SetColumn(setButton, 1);
        _editRow.Children.Add(setButton);
        _error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")), FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var editPanel = new StackPanel { Margin = new Thickness(10, 6, 10, 10), Spacing = 4, Children = { _editLabel, _editRow, _error } };

        _filter.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => UpdateEditor();
        setButton.Click += (_, _) => SetElement();
        _editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SetElement(); e.Handled = true; }
        };
        Activated += (_, _) => UpdateEditor();

        var top = new StackPanel { Children = { header, filterRow } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(editPanel, Dock.Bottom);
        Content = new DockPanel { Children = { top, editPanel, _list } };
        Fill();
    }

    private void Fill()
    {
        var filter = _filter.Text?.Trim() ?? "";
        int? selected = (_list.SelectedItem as Element)?.Flat;
        var items = new List<Element>();
        for (int flat = 0; flat < _array.Length; flat++)
        {
            var indices = FlatToCoords(flat, _array.Dimensions);
            var text = $"({string.Join(", ", indices)})  =  {DebugVariables.Show(_array.Get(indices))}";
            if (filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                items.Add(new Element(flat, indices, text));
        }
        _list.ItemsSource = items;
        if (selected != null) _list.SelectedItem = items.FirstOrDefault(i => i.Flat == selected);
        UpdateEditor();
    }

    private void UpdateEditor()
    {
        _error.IsVisible = false;
        var element = _list.SelectedItem as Element;
        bool can = _canEdit() && element != null;
        _editRow.IsVisible = can;
        if (!_canEdit()) _editLabel.Text = "Pause the program to change an element";
        else if (element == null) _editLabel.Text = "Choose an element to change it";
        else
        {
            _editLabel.Text = $"({string.Join(", ", element.Indices)}) =";
            var value = _array.Get(element.Indices);
            _editBox.Text = value is string s ? s : DebugVariables.Show(value);
        }
    }

    private void SetElement()
    {
        if (!_canEdit() || _list.SelectedItem is not Element element) return;
        var error = DebugVariables.SetElement(_array, element.Indices, _editBox.Text ?? "");
        _error.Text = error ?? "";
        _error.IsVisible = error != null;
        if (error == null) Fill();
    }

    // Converts a flat index → coordinates for a multi-dimensional array
    private static int[] FlatToCoords(int flatIndex, int[] dimensions)
    {
        var coords = new int[dimensions.Length];
        int remaining = flatIndex;
        for (int i = dimensions.Length - 1; i >= 0; i--)
        {
            coords[i] = remaining % dimensions[i];
            remaining /= dimensions[i];
        }
        return coords;
    }
}
