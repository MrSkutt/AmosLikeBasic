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

// The tracker's chip music: a folder of Atari ST music (YM and SNDH files) and Commodore 64 music (SID
// files), Sound unless another is chosen, by the folders in it (often one a composer). Choosing a song
// plays it at once: a YM file through the YM2149, an SNDH file's player on an emulated 68000 and a SID
// file's on an emulated C64. It can then be opened in the tracker. A search finds songs by name or
// folder in all of them.
public class YmBrowserWindow : Window
{
    private readonly Action<string, int> _open;   // the file, and the tune of an SNDH or SID file (0: its own first)
    private string _root;
    private List<string> _all = new();      // every YM, SNDH and SID file under the folder
    private List<string> _files = new();    // the ones of the kinds shown
    private readonly ListBox _folders, _songs;
    private readonly TextBox _search;
    private readonly CheckBox _showYm, _showSndh, _showSid;
    private readonly TextBlock _info, _where;
    private string? _chosen;
    private readonly NumericUpDown _tune;
    private readonly StackPanel _tuneRow;
    private bool _choosing;

    private sealed record Item(string Text, string Path)
    {
        public override string ToString() => Text;
    }

    public YmBrowserWindow(Action<string, int> open, string? folder = null)
    {
        _open = open;
        _root = folder ?? Workspace.Sound;
        Title = "Chip Music";
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
        // Which kinds to list: YM files (recorded registers), SNDH files (the Atari's own players) and SID files
        _showYm = new CheckBox { Content = "YM", IsChecked = true, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        _showSndh = new CheckBox { Content = "SNDH", IsChecked = true, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        ToolTip.SetTip(_showYm, "Show YM files: the sound chip's registers, recorded");
        _showSid = new CheckBox { Content = "SID", IsChecked = true, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        ToolTip.SetTip(_showSndh, "Show SNDH files: the Atari's own music players, run here (they play exactly; in the tracker some sound simpler)");
        ToolTip.SetTip(_showSid, "Show SID files: Commodore 64 music, its players run on an emulated C64");
        _showYm.IsCheckedChanged += (_, _) => Filter();
        _showSndh.IsCheckedChanged += (_, _) => Filter();
        _showSid.IsCheckedChanged += (_, _) => Filter();
        _where = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), MinHeight = 48 };

        var play = Button("▶ Play", "Play the song again from the start", () => Choose(play: true));
        // An SNDH or SID file can have several tunes: which one to hear and open
        _tune = new NumericUpDown { Minimum = 1, Maximum = 1, Increment = 1, FormatString = "0", Width = 110, Margin = new Thickness(0, 0, 6, 6) };
        _tune.ValueChanged += (_, _) => { if (!_choosing) Choose(play: true); };
        _tuneRow = new StackPanel { Orientation = Orientation.Horizontal, IsVisible = false, Children =
        {
            new TextBlock { Text = "Tune", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) }, _tune
        } };
        var stop = Button("■ Stop", null, () => ChiptuneSynth.PlayChipMusic(null));
        var openButton = Button("Open in tracker", "Read the song into the tracker (or double-click it)", OpenChosen);
        var folderButton = Button("Folder...", "Choose another folder of YM, SNDH or SID files", ChooseFolder);
        var close = Button("Close", null, Close);

        var searchRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(folderButton, Dock.Right);
        searchRow.Children.Add(folderButton);
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _showYm, _showSndh, _showSid } };
        DockPanel.SetDock(kinds, Dock.Right);
        searchRow.Children.Add(kinds);
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
        buttons.Children.Add(new WrapPanel { Children = { _tuneRow, play, stop, openButton } });

        var top = new StackPanel { Children = { Heading("CHIP MUSIC"), new TextBlock
        {
            Text = "Choose a song to hear it as the sound chip played it: YM and SNDH from the Atari ST, SID from the Commodore 64. Open it in the tracker to change it.",
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

    // All the YM, SNDH and SID files under the folder
    private void Scan()
    {
        _all = Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ym" or ".sndh" or ".snd" or ".sid")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        Filter();
    }

    private static bool IsSndhFile(string f) => Path.GetExtension(f).ToLowerInvariant() is ".sndh" or ".snd";
    private static bool IsSidFile(string f) => Path.GetExtension(f).Equals(".sid", StringComparison.OrdinalIgnoreCase);

    // The files of the kinds ticked, and the folders that have them (the chosen folder kept if it still has some)
    private void Filter()
    {
        bool ym = _showYm.IsChecked == true, sndh = _showSndh.IsChecked == true, sid = _showSid.IsChecked == true;
        _files = _all.Where(f => IsSndhFile(f) ? sndh : IsSidFile(f) ? sid : ym).ToList();
        _where.Text = $"{_files.Count} songs in {_root}";
        string? chosen = (_folders.SelectedItem as Item)?.Path;
        var folders = _files.GroupBy(f => Path.GetDirectoryName(f)!)
            .Select(g => new Item($"{Relative(g.Key)} ({g.Count()})", g.Key))
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToList();
        _folders.ItemsSource = folders;
        int keep = folders.FindIndex(i => i.Path == chosen);
        if (folders.Count > 0) _folders.SelectedIndex = keep >= 0 ? keep : 0;
        else
        {
            _songs.ItemsSource = null;
            _info.Text = !ym && !sndh && !sid ? "Tick YM, SNDH or SID to see songs."
                : "There are no such files here. Choose a folder that has some with Folder...";
        }
        ShowFolder();
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
    private int Tune => (int)(_tune.Value ?? 1);

    private void Choose(bool play)
    {
        if (_songs.SelectedItem is not Item song) return;
        bool newSong = _chosen != song.Path;
        _chosen = song.Path;
        try
        {
            var bytes = File.ReadAllBytes(song.Path);
            if (SndhFile.IsSndh(bytes))
            {
                ChooseSndh(SndhFile.Read(bytes), song.Path, newSong, play);
                return;
            }
            if (SidFile.IsSid(bytes))
            {
                ChooseSid(SidFile.Load(song.Path), song.Path, newSong, play);
                return;
            }
            _tuneRow.IsVisible = false;
            var ym = YmFile.Read(bytes);
            string kind = $"YM{(ym.Version > 0 ? ym.Version : 3)}";
            var length = TimeSpan.FromSeconds(ym.Seconds);
            string title = ym.Title.Length > 0 ? ym.Title : Path.GetFileNameWithoutExtension(song.Path);
            string by = ym.Author.Length > 0 ? " by " + ym.Author : "";
            string effects = ym.UsesSpecialEffects ? (ym.Drums.Count > 0 ? $", {ym.Drums.Count} digidrums" : ", special effects") : "";
            _info.Text = $"\"{title}\"{by}\n{(int)length.TotalMinutes}:{length.Seconds:00}, {ym.FrameRate} Hz, {kind}{effects}" +
                         (ym.Comment.Length > 0 ? "\n" + ym.Comment : "");
            if (play) ChiptuneSynth.PlayChipMusic(new YmPlayer(ym, 44100) { Loop = true });
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _info.Text = "Could not read it: " + e.Message;
            ChiptuneSynth.PlayChipMusic(null);
        }
    }

    // An SNDH file: what its header says, and its tune played live
    private void ChooseSndh(SndhFile sndh, string path, bool newSong, bool play)
    {
        _choosing = true;
        _tune.Maximum = sndh.Tunes;
        if (newSong) _tune.Value = sndh.DefaultTune;
        _tuneRow.IsVisible = sndh.Tunes > 1;
        _choosing = false;
        var length = TimeSpan.FromSeconds(SndhPlayer.LengthOf(sndh, Tune));
        bool known = sndh.Seconds.Length >= Tune && sndh.Seconds[Tune - 1] > 0;
        string title = sndh.Title.Length > 0 ? sndh.Title : Path.GetFileNameWithoutExtension(path);
        string by = sndh.Composer.Length > 0 ? " by " + sndh.Composer : "";
        string tunes = sndh.Tunes > 1 ? $", tune {Tune} of {sndh.Tunes}" : "";
        _info.Text = $"\"{title}\"{by}{(sndh.Year.Length > 0 ? ", " + sndh.Year : "")}\n" +
                     $"{(known ? $"{(int)length.TotalMinutes}:{length.Seconds:00}" : "length not given")}, {sndh.TimerHz} Hz, SNDH{tunes}";
        if (play) ChiptuneSynth.PlayChipMusic(new SndhSound(sndh, 44100, Tune) { Loop = true });
    }

    // A SID file: what its header says, and its tune played live on the C64
    private void ChooseSid(SidFile sid, string path, bool newSong, bool play)
    {
        _choosing = true;
        _tune.Maximum = sid.Tunes;
        if (newSong) _tune.Value = sid.StartTune;
        _tuneRow.IsVisible = sid.Tunes > 1;
        _choosing = false;
        bool known = sid.Seconds.Length >= Tune && sid.Seconds[Tune - 1] > 0;
        var length = TimeSpan.FromSeconds(known ? sid.Seconds[Tune - 1] : 0);
        string title = sid.Title.Length > 0 ? sid.Title : Path.GetFileNameWithoutExtension(path);
        string by = sid.Author.Length > 0 ? " by " + sid.Author : "";
        string tunes = sid.Tunes > 1 ? $", tune {Tune} of {sid.Tunes}" : "";
        string chips = sid.ExtraSids.Length > 0 ? $"{sid.ExtraSids.Length + 1} × " : "";
        _info.Text = $"\"{title}\"{by}{(sid.Released.Length > 0 ? ", " + sid.Released : "")}\n" +
                     $"{(known ? $"{(int)length.TotalMinutes}:{length.Seconds:00}" : "length not given")}, {chips}{(sid.Mos8580 ? "8580" : "6581")}, " +
                     $"{(sid.Rsid ? "RSID" : "PSID")}{tunes}" +
                     (sid.BasicTune ? "\nThis tune is partly a BASIC program, which needs the C64's own ROMs: it cannot be played here." : "");
        if (play) ChiptuneSynth.PlayChipMusic(new SidSound(sid, 44100, Tune) { Loop = true });
    }

    private void OpenChosen()
    {
        if (_chosen == null) return;
        ChiptuneSynth.PlayChipMusic(null);
        // Back to the tracker, which reads it and says what it found
        string chosen = _chosen;
        int tune = _tuneRow.IsVisible ? Tune : 0;
        Close();
        _open(chosen, tune);
    }

    private async void ChooseFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder of chip music" });
        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path == null) return;
        _root = path;
        Scan();
    }
}
