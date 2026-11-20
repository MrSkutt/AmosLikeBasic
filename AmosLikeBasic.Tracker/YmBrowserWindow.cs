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

// The tracker's YM MUSIC: a folder of YM files (Sound/Tracker/YM unless another is chosen), by the
// folders in it (often one a composer). Choosing a song plays it through the YM2149 at once; it can
// then be opened in the tracker. A search finds songs by name or folder in all of them.
public class YmBrowserWindow : Window
{
    private readonly Action<string> _open;
    private string _root;
    private List<string> _files = new();
    private readonly ListBox _folders, _songs;
    private readonly TextBox _search;
    private readonly TextBlock _info, _where;
    private string? _chosen;

    private sealed record Item(string Text, string Path)
    {
        public override string ToString() => Text;
    }

    public YmBrowserWindow(Action<string> open, string? folder = null)
    {
        _open = open;
        string ymFolder = Path.Combine(Workspace.Sound, "Tracker", "YM");
        _root = folder ?? (Directory.Exists(ymFolder) ? ymFolder : Workspace.Sound);
        Title = "YM Music";
        Width = 820;
        Height = 560;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");
        var row = new Style(x => x.OfType<ListBoxItem>());
        row.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 3)));
        row.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg")));
        Styles.Add(row);

        _folders = List();
        _songs = List();
        _folders.SelectionChanged += (_, _) => ShowFolder();
        _songs.SelectionChanged += (_, _) => Choose(play: true);
        _songs.DoubleTapped += (_, _) => OpenChosen();
        _search = new TextBox { PlaceholderText = "Search songs and folders", Margin = new Thickness(0, 0, 8, 0) };
        _search.TextChanged += (_, _) => ShowFolder();
        _where = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), MinHeight = 48 };

        var play = Button("▶ Play", "Play the song again from the start", () => Choose(play: true));
        var stop = Button("■ Stop", null, () => ChiptuneSynth.PlayChipMusic(null));
        var openButton = Button("Open in tracker", "Read the song into the tracker (or double-click it)", OpenChosen);
        var folderButton = Button("Folder...", "Choose another folder of YM files", ChooseFolder);
        var close = Button("Close", null, Close);

        var searchRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(folderButton, Dock.Right);
        searchRow.Children.Add(folderButton);
        searchRow.Children.Add(_search);

        var lists = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), Margin = new Thickness(0, 6, 0, 0) };
        lists.Children.Add(Framed("FOLDER", _folders));
        var right = Framed("SONG", _songs);
        right.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(right, 1);
        lists.Children.Add(right);

        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);
        buttons.Children.Add(new WrapPanel { Children = { play, stop, openButton } });

        var top = new StackPanel { Children = { Heading("YM MUSIC"), new TextBlock
        {
            Text = "Choose a song to hear it, as the Atari ST's sound chip played it. Open it in the tracker to change it.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        }, _where, searchRow } };
        var foot = new StackPanel { Children = { _info, buttons } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, foot, lists } };

        Closed += (_, _) => ChiptuneSynth.PlayChipMusic(null);
        Scan();
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

    // All the YM files under the folder, and the folders that have them
    private void Scan()
    {
        _files = Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).Equals(".ym", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        _where.Text = $"{_files.Count} songs in {_root}";
        var folders = _files.Select(f => Path.GetDirectoryName(f)!).Distinct()
            .Select(d => new Item($"{Relative(d)} ({_files.Count(f => Path.GetDirectoryName(f) == d)})", d))
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToList();
        _folders.ItemsSource = folders;
        if (folders.Count > 0) _folders.SelectedIndex = 0;
        else _info.Text = "There are no YM files here. Choose a folder that has some with Folder...";
    }

    private string Relative(string dir)
    {
        string rel = Path.GetRelativePath(_root, dir).Replace('\\', '/');
        return rel == "." ? "(this folder)" : rel;
    }

    // The songs of the chosen folder, or every song whose name or folder has the search text
    private void ShowFolder()
    {
        string search = _search.Text?.Trim() ?? "";
        List<Item> songs;
        if (search.Length > 0)
            songs = _files.Where(f => Path.GetRelativePath(_root, f).Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(f => new Item($"{Relative(Path.GetDirectoryName(f)!)}/{Path.GetFileNameWithoutExtension(f)}", f)).ToList();
        else if (_folders.SelectedItem is Item folder)
            songs = _files.Where(f => Path.GetDirectoryName(f) == folder.Path)
                .Select(f => new Item(Path.GetFileNameWithoutExtension(f), f)).ToList();
        else songs = new List<Item>();
        _songs.ItemsSource = songs;
    }

    // Shows what the song is, and plays it
    private void Choose(bool play)
    {
        if (_songs.SelectedItem is not Item song) return;
        _chosen = song.Path;
        try
        {
            var ym = YmFile.Read(File.ReadAllBytes(song.Path));
            var length = TimeSpan.FromSeconds(ym.Seconds);
            string title = ym.Title.Length > 0 ? ym.Title : Path.GetFileNameWithoutExtension(song.Path);
            string by = ym.Author.Length > 0 ? " by " + ym.Author : "";
            string effects = ym.UsesSpecialEffects ? (ym.Drums.Count > 0 ? $", {ym.Drums.Count} digidrums" : ", special effects") : "";
            _info.Text = $"\"{title}\"{by}\n{(int)length.TotalMinutes}:{length.Seconds:00}, {ym.FrameRate} Hz, YM{(ym.Version > 0 ? ym.Version : 3)}{effects}" +
                         (ym.Comment.Length > 0 ? "\n" + ym.Comment : "");
            if (play) ChiptuneSynth.PlayChipMusic(new YmPlayer(ym, 44100) { Loop = true });
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _info.Text = "Could not read it: " + e.Message;
            ChiptuneSynth.PlayChipMusic(null);
        }
    }

    private void OpenChosen()
    {
        if (_chosen == null) return;
        ChiptuneSynth.PlayChipMusic(null);
        _open(_chosen);
    }

    private async void ChooseFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder of YM files" });
        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path == null) return;
        _root = path;
        Scan();
    }
}
