using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace AmosLikeBasic;

public partial class MapEditorWindow : Window
{
    private readonly AmosGraphics _gfx;
    private readonly Func<string>? _programText;
    private int _selectedTileIndex;
    private DrawTool _tool = DrawTool.Pencil;
    private string? _mapFile;

    // A stroke works on a copy of the map (x + y * width), drawn afresh for shapes
    private int[]? _before, _work;
    private (int x, int y) _start, _last;
    private bool _erasing;
    private readonly Stack<(int w, int h, int[] cells)> _undo = new(), _redo = new();

    public MapEditorWindow() : this(new AmosGraphics()) { }

    public MapEditorWindow(AmosGraphics gfx, Func<string>? programText = null)
    {
        _gfx = gfx;
        _programText = programText;
        InitializeComponent();

        Map.Gfx = _gfx;
        Palette.Gfx = _gfx;
        Palette.TileSelected += SelectTile;
        KeyDown += OnEditorKeyDown;

        // Sync the UI with the engine's actual data straight away at start
        MapWidthUpDown.Value = _gfx.GetMapWidth();
        MapHeightUpDown.Value = _gfx.GetMapHeight();
        RefreshTiles();
        RefreshMap();
    }

    private void RefreshTiles()
    {
        Palette.Refresh();
        SelectTile(Math.Min(_selectedTileIndex, Math.Max(0, _gfx.GetTileBitmaps().Count - 1)));
    }

    private void RefreshMap()
    {
        Map.ShowGrid = GridCheck.IsChecked == true;
        Map.Zoom = ZoomSlider.Value;
        Map.Refresh();
    }

    // ══════════════════════════════════════════════════════════════
    //  FIGURES: marker tiles shown as the sprite the program puts there
    // ══════════════════════════════════════════════════════════════

    // Kept in the map file ("Figures"); MAP LOAD does not read them, so the game is not changed
    public sealed record MapFigure(int Tile, string File, int FrameWidth, int FrameHeight, int OffsetX = 0, int OffsetY = 0);

    private readonly Dictionary<int, MapFigure> _figures = new();
    private bool _fillingFigures;

    private void SetFigures(IEnumerable<MapFigure>? figures)
    {
        _figures.Clear();
        foreach (var f in figures ?? Array.Empty<MapFigure>()) _figures[f.Tile] = f;
        ShowFigures();
    }

    private void ShowFigures()
    {
        var pictures = new Dictionary<int, (Bitmap bitmap, MapFigure figure)>();
        foreach (var f in _figures.Values)
            if (FigureBitmap(f.File) is { } b) pictures[f.Tile] = (b, f);
        Map.Figures = pictures;
        Palette.Figures = pictures;
        Map.InvalidateVisual();
        Palette.InvalidateVisual();
        ShowFigureChoice();
    }

    private readonly Dictionary<string, Bitmap?> _figureBitmaps = new(StringComparer.OrdinalIgnoreCase);

    private Bitmap? FigureBitmap(string file)
    {
        if (_figureBitmaps.TryGetValue(file, out var cached)) return cached;
        Bitmap? b = null;
        try { b = new Bitmap(ResourceLoader.GetPath(file)); }
        catch (Exception) { }
        return _figureBitmaps[file] = b;
    }

    private sealed record FigureChoice(string Text, ProgramAssets.Asset? Asset)
    {
        public override string ToString() => Text;
    }

    // The sprites and images the program loads; choosing one shows the selected tile as it
    private void FigureBox_OnDropDownOpened(object? sender, EventArgs e)
    {
        _fillingFigures = true;
        var items = new List<FigureChoice> { new("(the tile itself)", null) };
        foreach (var a in ProgramAssets.Find(_programText?.Invoke() ?? "").Where(a => a.Kind is "SPRITE" or "IMAGE"))
            if (items.All(i => i.Asset?.File != a.File))
                items.Add(new FigureChoice(Path.GetFileName(a.File) + (a.HasFrameSize ? $"  {a.FrameWidth}×{a.FrameHeight}" : ""), a));
        FigureBox.ItemsSource = items;
        FigureBox.SelectedItem = items.FirstOrDefault(i => _figures.TryGetValue(_selectedTileIndex, out var f) && i.Asset?.File == f.File);
        _fillingFigures = false;
    }

    private void FigureBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_fillingFigures || FigureBox.SelectedItem is not FigureChoice choice || _selectedTileIndex < 0) return;
        if (choice.Asset == null) _figures.Remove(_selectedTileIndex);
        else
        {
            var a = choice.Asset;
            var bitmap = FigureBitmap(a.File);
            int w = a.FrameWidth ?? bitmap?.PixelSize.Width ?? 32, h = a.FrameHeight ?? bitmap?.PixelSize.Height ?? 32;
            _figures[_selectedTileIndex] = new MapFigure(_selectedTileIndex, a.File, w, h);
        }
        ShowFigures();
        StatusText.Text = "Save the map to keep the figures.";
    }

    // The combo box shows the figure of the tile selected
    private void ShowFigureChoice()
    {
        _fillingFigures = true;
        FigureBox.PlaceholderText = _figures.TryGetValue(_selectedTileIndex, out var f)
            ? Path.GetFileName(f.File) : "(the tile itself)";
        FigureBox.SelectedItem = null;
        _fillingFigures = false;
    }

    private void SelectTile(int index)
    {
        var tiles = _gfx.GetTileBitmaps();
        _selectedTileIndex = index;
        Palette.Selected = index;
        Palette.InvalidateVisual();
        bool has = index >= 0 && index < tiles.Count;
        SelectedTileImage.Source = has ? tiles[index] : null;
        var (tw, th) = _gfx.GetTileSize();
        SelectedTileText.Text = tiles.Count == 0 ? "No tiles loaded" : $"Tile {index}   ({tiles.Count} tiles, {tw}×{th})";
        ShowFigureChoice();
    }

    // ══════════════════════════════════════════════════════════════
    //  DRAWING
    // ══════════════════════════════════════════════════════════════

    private void Tool_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender == LineTool) SelectTool(DrawTool.Line);
        else if (sender == RectangleTool) SelectTool(DrawTool.Rectangle);
        else if (sender == FillTool) SelectTool(DrawTool.Fill);
        else if (sender == PickerTool) SelectTool(DrawTool.Picker);
        else SelectTool(DrawTool.Pencil);
    }

    private void SelectTool(DrawTool tool)
    {
        _tool = tool;
        PencilTool.IsChecked = tool == DrawTool.Pencil;
        LineTool.IsChecked = tool == DrawTool.Line;
        RectangleTool.IsChecked = tool == DrawTool.Rectangle;
        FillTool.IsChecked = tool == DrawTool.Fill;
        PickerTool.IsChecked = tool == DrawTool.Picker;
    }

    private int[] ReadMap()
    {
        int w = _gfx.GetMapWidth(), h = _gfx.GetMapHeight();
        var cells = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[y * w + x] = _gfx.GetMapTile(x, y);
        return cells;
    }

    private void WriteMap(int[] cells)
    {
        int w = _gfx.GetMapWidth(), h = _gfx.GetMapHeight();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _gfx.SetMapTile(x, y, cells[y * w + x]);
    }

    private (int x, int y) CellAt(PointerEventArgs e)
    {
        var pos = e.GetPosition(Map);
        var (tw, th) = _gfx.GetTileSize();
        return ((int)Math.Floor(pos.X / (tw * Map.Zoom)), (int)Math.Floor(pos.Y / (th * Map.Zoom)));
    }

    private void Map_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var cell = CellAt(e);
        var props = e.GetCurrentPoint(Map).Properties;
        _erasing = props.IsRightButtonPressed;

        if (_tool == DrawTool.Picker && !_erasing)
        {
            int tile = _gfx.GetMapTile(cell.x, cell.y);
            if (tile >= 0) SelectTile(tile);
            SelectTool(DrawTool.Pencil);
            return;
        }

        e.Pointer.Capture(Map);
        _before = ReadMap();
        _work = (int[])_before.Clone();
        _start = _last = cell;
        if (_tool == DrawTool.Fill)
        {
            GridTools.Fill(_work, _gfx.GetMapWidth(), _gfx.GetMapHeight(), cell.x, cell.y, _erasing ? -1 : _selectedTileIndex);
            WriteMap(_work);
            EndStroke();
        }
        else
            Stroke(cell);
        e.Handled = true;
    }

    private void Map_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var cell = CellAt(e);
        Map.Hover = cell;
        Map.InvalidateVisual();
        int w = _gfx.GetMapWidth(), h = _gfx.GetMapHeight();
        if (cell.x >= 0 && cell.y >= 0 && cell.x < w && cell.y < h)
        {
            int tile = _gfx.GetMapTile(cell.x, cell.y);
            string size = _work != null && _tool is DrawTool.Rectangle or DrawTool.Line
                ? $"   size {Math.Abs(cell.x - _start.x) + 1} × {Math.Abs(cell.y - _start.y) + 1}" : "";
            StatusText.Text = $"x {cell.x}, y {cell.y}   tile {(tile < 0 ? "empty" : tile.ToString())}{size}";
        }
        else StatusText.Text = " ";

        if (_work != null) Stroke(cell);
    }

    private void Map_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured == Map) e.Pointer.Capture(null);
        if (_work != null) EndStroke();
    }

    private void Map_OnPointerExited(object? sender, PointerEventArgs e)
    {
        Map.Hover = (-1, -1);
        Map.InvalidateVisual();
        StatusText.Text = " ";
    }

    private void Stroke((int x, int y) cell)
    {
        if (_work == null || _before == null) return;
        int w = _gfx.GetMapWidth(), h = _gfx.GetMapHeight();
        int tile = _erasing ? -1 : _selectedTileIndex;
        var work = _work;
        void Plot(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < w && y < h) work[y * w + x] = tile;
        }

        if (_tool == DrawTool.Pencil)
            GridTools.Line(_last.x, _last.y, cell.x, cell.y, Plot);
        else
        {
            Array.Copy(_before, work, work.Length);
            if (_tool == DrawTool.Line) GridTools.Line(_start.x, _start.y, cell.x, cell.y, Plot);
            else GridTools.Rectangle(_start.x, _start.y, cell.x, cell.y, FilledCheck.IsChecked == true, Plot);
        }
        _last = cell;
        WriteMap(work);
        Map.InvalidateVisual();
    }

    private void EndStroke()
    {
        if (_before != null && _work != null && !_before.AsSpan().SequenceEqual(_work))
        {
            _undo.Push((_gfx.GetMapWidth(), _gfx.GetMapHeight(), _before));
            _redo.Clear();
        }
        _before = _work = null;
        Map.InvalidateVisual();
    }

    private void Undo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoRedo(_undo, _redo);
    private void Redo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoRedo(_redo, _undo);

    // Undo also brings back the map size from before a resize
    private void UndoRedo(Stack<(int w, int h, int[] cells)> from, Stack<(int w, int h, int[] cells)> to)
    {
        if (from.Count == 0) return;
        var (w, h, cells) = from.Pop();
        to.Push((_gfx.GetMapWidth(), _gfx.GetMapHeight(), ReadMap()));
        if (w != _gfx.GetMapWidth() || h != _gfx.GetMapHeight()) _gfx.SetMapSize(w, h);
        WriteMap(cells);
        MapWidthUpDown.Value = w;
        MapHeightUpDown.Value = h;
        RefreshMap();
    }

    private void SaveForUndo()
    {
        _undo.Push((_gfx.GetMapWidth(), _gfx.GetMapHeight(), ReadMap()));
        _redo.Clear();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Z when cmd && shift:
            case Key.Y when cmd: UndoRedo(_redo, _undo); break;
            case Key.Z when cmd: UndoRedo(_undo, _redo); break;
            case Key.P when !cmd: SelectTool(DrawTool.Pencil); break;
            case Key.L when !cmd: SelectTool(DrawTool.Line); break;
            case Key.R when !cmd: SelectTool(DrawTool.Rectangle); break;
            case Key.F when !cmd: SelectTool(DrawTool.Fill); break;
            case Key.I when !cmd: SelectTool(DrawTool.Picker); break;
            case Key.G when !cmd: GridCheck.IsChecked = GridCheck.IsChecked != true; break;
            default: return;
        }
        e.Handled = true;
    }

    private void View_OnChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RefreshMap();

    private void Zoom_OnChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ZoomText.Text = $"{e.NewValue:0.##}x";
        RefreshMap();
    }

    private void ResizeMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveForUndo();
        _gfx.SetMapSize((int)(MapWidthUpDown.Value ?? 20), (int)(MapHeightUpDown.Value ?? 15));
        RefreshMap();
    }

    private void OnClearMapClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveForUndo();
        _gfx.ClearMap();
        RefreshMap();
    }

    // ══════════════════════════════════════════════════════════════
    //  TILES, MAPS AND THE PROGRAM
    // ══════════════════════════════════════════════════════════════

    private void ProgramFilesBox_OnDropDownOpened(object? sender, EventArgs e)
    {
        var assets = ProgramAssets.Find(_programText?.Invoke() ?? "").Where(a => a.Kind is "TILE" or "MAP").ToList();
        ProgramFilesBox.ItemsSource = assets.Count > 0
            ? assets.ConvertAll(a => new ComboBoxItem { Content = a.Label, Tag = a })
            : new List<ComboBoxItem> { new() { Content = "(the program loads no tiles or maps)", IsEnabled = false } };
        ProgramFilesBox.SelectedIndex = -1;
    }

    private async void ProgramFilesBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProgramFilesBox.SelectedItem is not ComboBoxItem { Tag: ProgramAssets.Asset asset }) return;
        string path;
        try { path = ResourceLoader.GetPath(asset.File); }
        catch (FileNotFoundException) { path = asset.File; }
        if (!File.Exists(path))
        {
            StatusText.Text = $"Cannot find {asset.File}";
            return;
        }
        try
        {
            if (asset.Kind == "TILE")
            {
                _gfx.LoadTileBank(path, asset.FrameWidth ?? 32, asset.FrameHeight ?? 32);
                RefreshTiles();
                RefreshMap();
            }
            else
            {
                await using var stream = File.OpenRead(path);
                LoadMapFrom(await System.Text.Json.JsonSerializer.DeserializeAsync<MapDto>(stream), path);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {asset.File}: {ex.Message}";
        }
    }

    private async void LoadTiles_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Tileset PNG",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Graphics),
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;

        // The same dialog as for sprite sheets shows the tile grid on the picture
        Bitmap bitmap;
        await using (var s = await files[0].OpenReadAsync())
            bitmap = new Bitmap(s);
        var (tw, th) = _gfx.GetTileSize();
        SpritesheetConfig? suggested = ProgramAssets.FromFileName(files[0].Name) is { } n
            ? new SpritesheetConfig { FrameWidth = n.w ?? tw, FrameHeight = n.h ?? th, FrameCount = n.frames ?? 1 } : null;
        var config = await new SpritesheetConfigDialog(bitmap, suggested, $"Tile size for {files[0].Name}")
            .ShowDialog<SpritesheetConfig?>(this);
        bitmap.Dispose();
        if (config == null) return;

        await using var stream = await files[0].OpenReadAsync();
        _gfx.LoadTileBank(stream, config.FrameWidth, config.FrameHeight);
        RefreshTiles();
        RefreshMap();
    }

    private void LoadMapFrom(MapDto? dto, string? path)
    {
        if (dto == null) return;
        SaveForUndo();
        _gfx.SetMapSize(dto.Width, dto.Height);
        int idx = 0;
        for (int y = 0; y < dto.Height; y++)
            for (int x = 0; x < dto.Width; x++)
                _gfx.SetMapTile(x, y, idx < dto.Data.Count ? dto.Data[idx++] : -1);
        MapWidthUpDown.Value = dto.Width;
        MapHeightUpDown.Value = dto.Height;
        SetMapFile(path);
        SetFigures(dto.Figures);
        RefreshMap();
    }

    private void SetMapFile(string? path)
    {
        _mapFile = path;
        MapFileText.Text = path == null ? "(no file)" : Path.GetFileName(path);
    }

    // Save writes back to the map's own file, so a map from the program is updated where it lies
    private async void SaveMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_mapFile == null) { SaveMapAs_OnClick(sender, e); return; }
        await using var stream = File.Create(_mapFile);
        await WriteMapTo(stream);
        StatusText.Text = $"Saved {Path.GetFileName(_mapFile)}";
    }

    private async void SaveMapAs_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await SaveDialog.AskFileAsync(StorageProvider, new FilePickerSaveOptions
        {
            Title = "Save AMOS Map",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Graphics),
            SuggestedFileName = _mapFile != null ? Path.GetFileName(_mapFile) : "level.amosmap",
            FileTypeChoices = [new FilePickerFileType("AMOS Map") { Patterns = ["*.amosmap"] }]
        });
        if (file == null) return;
        await using (var stream = await file.OpenWriteAsync())
        {
            stream.SetLength(0);
            await WriteMapTo(stream);
        }
        SetMapFile(file.Path.LocalPath);
    }

    private async System.Threading.Tasks.Task WriteMapTo(Stream stream)
    {
        var dto = new MapDto(_gfx.GetMapWidth(), _gfx.GetMapHeight(), ReadMap().ToList(),
                             _figures.Count > 0 ? _figures.Values.OrderBy(f => f.Tile).ToList() : null);
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, dto);
    }

    private async void LoadMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load AMOS Map",
            SuggestedStartLocation = await Workspace.StartFolderAsync(StorageProvider, WorkspaceFolder.Graphics),
            FileTypeFilter = [new FilePickerFileType("AMOS Map") { Patterns = ["*.amosmap"] }]
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        LoadMapFrom(await System.Text.Json.JsonSerializer.DeserializeAsync<MapDto>(stream), files[0].Path.LocalPath);
    }

    // The map file format, the same as MAP LOAD reads
    private record MapDto(int Width, int Height, List<int> Data, List<MapFigure>? Figures = null);
}

// The map, drawn tile by tile, with a grid and the cell under the pointer marked
public class MapView : Control
{
    public AmosGraphics? Gfx { get; set; }
    public double Zoom { get; set; } = 1;
    public bool ShowGrid { get; set; } = true;
    public (int x, int y) Hover { get; set; } = (-1, -1);
    // Marker tiles drawn as figures: frame 0 of the picture, at its own size
    public Dictionary<int, (Bitmap bitmap, MapEditorWindow.MapFigure figure)> Figures { get; set; } = new();

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private (double w, double h) Cell()
    {
        var (tw, th) = Gfx?.GetTileSize() ?? (32, 32);
        return (tw * Zoom, th * Zoom);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Gfx == null) return default;
        var (cw, ch) = Cell();
        return new Size(Gfx.GetMapWidth() * cw, Gfx.GetMapHeight() * ch);
    }

    public override void Render(DrawingContext context)
    {
        if (Gfx == null) return;
        var (cw, ch) = Cell();
        int mw = Gfx.GetMapWidth(), mh = Gfx.GetMapHeight();
        var tiles = Gfx.GetTileBitmaps();
        context.FillRectangle(Brushes.Black, new Rect(0, 0, mw * cw, mh * ch));

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            for (int y = 0; y < mh; y++)
                for (int x = 0; x < mw; x++)
                {
                    int t = Gfx.GetMapTile(x, y);
                    if (Figures.TryGetValue(t, out var fig))
                    {
                        var f = fig.figure;
                        var src = new Rect(0, 0, Math.Min(f.FrameWidth, fig.bitmap.PixelSize.Width), Math.Min(f.FrameHeight, fig.bitmap.PixelSize.Height));
                        context.DrawImage(fig.bitmap, src,
                            new Rect(x * cw + f.OffsetX * Zoom, y * ch + f.OffsetY * Zoom, src.Width * Zoom, src.Height * Zoom));
                    }
                    else if (t >= 0 && t < tiles.Count)
                        context.DrawImage(tiles[t], new Rect(x * cw, y * ch, cw, ch));
                }

        if (ShowGrid)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x7F, 0xB2, 0xFF)), 1);
            for (int x = 0; x <= mw; x++) context.DrawLine(pen, new Point(x * cw + 0.5, 0), new Point(x * cw + 0.5, mh * ch));
            for (int y = 0; y <= mh; y++) context.DrawLine(pen, new Point(0, y * ch + 0.5), new Point(mw * cw, y * ch + 0.5));
        }

        if (Hover.x >= 0 && Hover.y >= 0 && Hover.x < mw && Hover.y < mh)
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(255, 212, 0)), 2),
                new Rect(Hover.x * cw + 1, Hover.y * ch + 1, cw - 2, ch - 2));
    }
}

// The tile bank laid out as in its picture; a click picks a tile
public class TilePaletteView : Control
{
    public AmosGraphics? Gfx { get; set; }
    public int Selected { get; set; }
    public event Action<int>? TileSelected;
    public Dictionary<int, (Bitmap bitmap, MapEditorWindow.MapFigure figure)> Figures { get; set; } = new();

    private (int cols, double cw, double ch) Layout()
    {
        var (tw, th) = Gfx?.GetTileSize() ?? (32, 32);
        int cols = Math.Max(1, Gfx?.GetTilesInWidth() ?? 1);
        // Small tiles are shown bigger, big ones smaller, so they are easy to click
        double scale = Math.Clamp(32.0 / Math.Max(tw, th), 0.5, 2);
        return (cols, tw * scale, th * scale);
    }

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Gfx?.GetTileBitmaps().Count ?? 0;
        if (count == 0) return default;
        var (cols, cw, ch) = Layout();
        return new Size(Math.Min(cols, count) * cw, Math.Ceiling(count / (double)cols) * ch);
    }

    public override void Render(DrawingContext context)
    {
        var tiles = Gfx?.GetTileBitmaps();
        if (tiles == null || tiles.Count == 0) return;
        var (cols, cw, ch) = Layout();
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            for (int i = 0; i < tiles.Count; i++)
            {
                var cell = new Rect(i % cols * cw, i / cols * ch, cw, ch);
                if (Figures.TryGetValue(i, out var fig))
                {
                    // The figure shrunk to fit the cell, keeping its shape
                    var f = fig.figure;
                    var src = new Rect(0, 0, Math.Min(f.FrameWidth, fig.bitmap.PixelSize.Width), Math.Min(f.FrameHeight, fig.bitmap.PixelSize.Height));
                    double scale = Math.Min(cw / src.Width, ch / src.Height);
                    var size = new Size(src.Width * scale, src.Height * scale);
                    context.DrawImage(fig.bitmap, src, new Rect(cell.Center.X - size.Width / 2, cell.Center.Y - size.Height / 2, size.Width, size.Height));
                }
                else context.DrawImage(tiles[i], cell);
            }
        if (Selected >= 0 && Selected < tiles.Count)
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(255, 212, 0)), 2),
                new Rect(Selected % cols * cw + 1, Selected / cols * ch + 1, cw - 2, ch - 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int count = Gfx?.GetTileBitmaps().Count ?? 0;
        var (cols, cw, ch) = Layout();
        var p = e.GetPosition(this);
        int i = (int)(p.Y / ch) * cols + (int)(p.X / cw);
        if (p.X < cols * cw && i >= 0 && i < count) TileSelected?.Invoke(i);
    }
}
