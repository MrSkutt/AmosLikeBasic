using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace AmosLikeBasic;

// The tracker's LIBRARY: ready-made instruments by category. Choosing one plays it; it can then be
// added to the song or put in place of the current instrument. The current instrument can be
// saved to the library, and instruments loaded from and saved to any file.
public class InstrumentLibraryWindow : Window
{
    private readonly Func<TrackerInstrument?> _current;
    private readonly Action<TrackerInstrument> _add, _replace, _preview;
    private readonly ListBox _categories, _instruments;
    private readonly TextBlock _info;
    private TrackerInstrument? _chosen;

    public InstrumentLibraryWindow(Func<TrackerInstrument?> current, Action<TrackerInstrument> add,
        Action<TrackerInstrument> replace, Action<TrackerInstrument> preview)
    {
        _current = current;
        _add = add;
        _replace = replace;
        _preview = preview;
        Title = "Instrument Library";
        Width = 620;
        Height = 520;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");
        // The rows of the lists in the theme's text colour (the default grey is hard to read on blue)
        var row = new Style(x => x.OfType<ListBoxItem>());
        row.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 3)));
        row.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg")));
        Styles.Add(row);

        _categories = List();
        _instruments = List();
        _categories.SelectionChanged += (_, _) => ShowCategory();
        _instruments.SelectionChanged += (_, _) => ChooseInstrument(play: true);
        _instruments.DoubleTapped += (_, _) => { if (_chosen != null) _add(InstrumentLibrary.Copy(_chosen)); };

        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };

        var listen = Button("▶ Listen", "Play the instrument again", () => { if (_chosen != null) _preview(_chosen); });
        var addButton = Button("Add to song", "A new instrument in the song (or double-click)", () =>
        {
            if (_chosen != null) _add(InstrumentLibrary.Copy(_chosen));
        });
        var replaceButton = Button("Replace current", "Put it in place of the instrument chosen in the tracker", () =>
        {
            if (_chosen != null) _replace(InstrumentLibrary.Copy(_chosen));
        });
        var save = Button("Save current...", "Save the tracker's current instrument in a category of the library", SaveCurrentToLibrary);
        var loadFile = Button("Load file...", "Load an instrument file (.amosinst) from anywhere", LoadFromFile);
        var saveFile = Button("Save file...", "Save the tracker's current instrument to a file", SaveToFile);
        var close = Button("Close", null, Close);

        var lists = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(0, 6, 0, 0) };
        lists.Children.Add(Framed("CATEGORY", _categories));
        var right = Framed("INSTRUMENT", _instruments);
        right.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(right, 1);
        lists.Children.Add(right);

        var row1 = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { listen, addButton, replaceButton } };
        var row2 = new WrapPanel { Children = { save, loadFile, saveFile } };
        var bottom = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(row2);

        var top = new StackPanel { Children = { Heading("INSTRUMENT LIBRARY"), new TextBlock
        {
            Text = "Choose an instrument to hear it. Add it to the song, or put it in place of the current one.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        } } };
        var foot = new StackPanel { Children = { _info, row1, bottom } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, foot, lists } };

        FillCategories();
    }

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };
        t[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosTitleFg");
        return t;
    }

    private static ListBox List()
    {
        var list = new ListBox { FontFamily = new FontFamily("Menlo, Consolas, Courier New, monospace"), FontSize = 13 };
        list[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosInputBg");
        return list;
    }

    private static DockPanel Framed(string title, Control content)
    {
        var heading = Heading(title);
        DockPanel.SetDock(heading, Dock.Top);
        return new DockPanel { Children = { heading, content } };
    }

    private static Button Button(string text, string? tip, Action click)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private void FillCategories(string? select = null)
    {
        var categories = InstrumentLibrary.Categories();
        _categories.ItemsSource = categories;
        if (categories.Count == 0)
        {
            _info.Text = $"The library is empty ({InstrumentLibrary.Folder}).";
            return;
        }
        _categories.SelectedItem = select != null && categories.Contains(select) ? select : categories[0];
    }

    private void ShowCategory()
    {
        if (_categories.SelectedItem is not string category) return;
        _instruments.ItemsSource = InstrumentLibrary.Instruments(category).Select(i => new Item(i.name, i.file)).ToList();
        _chosen = null;
        _info.Text = "";
    }

    private sealed record Item(string Name, string File)
    {
        public override string ToString() => Name;
    }

    private void ChooseInstrument(bool play)
    {
        if (_instruments.SelectedItem is not Item item) return;
        try
        {
            _chosen = InstrumentLibrary.Load(item.File);
            _info.Text = Describe(_chosen);
            if (play) _preview(_chosen);
        }
        catch (Exception ex)
        {
            _chosen = null;
            _info.Text = $"Could not read {Path.GetFileName(item.File)}: {ex.Message}";
        }
    }

    // A short line about what makes up the sound
    private static string Describe(TrackerInstrument i)
    {
        var parts = new List<string> { i.Wave };
        if (!string.Equals(i.Osc2Wave ?? "Off", "Off", StringComparison.OrdinalIgnoreCase))
            parts.Add($"+ {i.Osc2Wave} {i.Osc2Semitones:+0;-0;0} st {i.Osc2Detune:+0;-0;0} c");
        if (!string.Equals(i.Filter, "Off", StringComparison.OrdinalIgnoreCase)) parts.Add($"{i.Filter} filter {i.Cutoff:0} Hz");
        foreach (var l in new[] { i.Lfo1, i.Lfo2 })
            if (l != null && l.Target != "Off" && l.Depth != 0) parts.Add($"LFO on {l.Target.ToLowerInvariant()}");
        if (i.Table.Count > 0) parts.Add($"table of {i.Table.Count} steps");
        return string.Join(", ", parts);
    }

    private async void SaveCurrentToLibrary()
    {
        var inst = _current();
        if (inst == null) { _info.Text = "The song has no instrument to save."; return; }

        // Which category: the one shown, or a new one typed in
        var categories = InstrumentLibrary.Categories();
        var box = new AutoCompleteBox { ItemsSource = categories, Text = _categories.SelectedItem as string ?? "My Instruments", MinWidth = 260 };
        var name = new TextBox { Text = inst.Name, MinWidth = 260 };
        var ok = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var dialog = new Window
        {
            Title = "Save to the library", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Name" }, name,
                    new TextBlock { Text = "Category (a new name makes a new one)" }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                     Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel } }
                }
            }
        };
        dialog[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        dialog[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;

        var copy = InstrumentLibrary.Copy(inst);
        copy.Name = string.IsNullOrWhiteSpace(name.Text) ? inst.Name : name.Text.Trim();
        string category = string.IsNullOrWhiteSpace(box.Text) ? "My Instruments" : box.Text.Trim();
        try
        {
            string file = InstrumentLibrary.SaveToLibrary(copy, category);
            FillCategories(Path.GetFileName(Path.GetDirectoryName(file)));
            _info.Text = $"Saved {copy.Name} in {category}.";
        }
        catch (Exception ex) { _info.Text = $"Could not save: {ex.Message}"; }
    }

    private static readonly FilePickerFileType InstrumentFiles = new("AMOS Instrument") { Patterns = new[] { "*" + InstrumentLibrary.Extension } };

    private async void LoadFromFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Instrument",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound, "Instruments"),
            FileTypeFilter = new[] { InstrumentFiles }
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _chosen = InstrumentLibrary.Load(path);
            _instruments.SelectedItem = null;
            _info.Text = $"{_chosen.Name}: {Describe(_chosen)}";
            _preview(_chosen);
        }
        catch (Exception ex) { _info.Text = $"Could not read {Path.GetFileName(path)}: {ex.Message}"; }
    }

    private async void SaveToFile()
    {
        var inst = _current();
        if (inst == null) { _info.Text = "The song has no instrument to save."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Instrument",
            SuggestedFileName = (string.IsNullOrWhiteSpace(inst.Name) ? "Instrument" : inst.Name) + InstrumentLibrary.Extension,
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Sound, "Instruments"),
            FileTypeChoices = new[] { InstrumentFiles }
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            InstrumentLibrary.Save(inst, path);
            _info.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) { _info.Text = $"Could not save: {ex.Message}"; }
    }
}
