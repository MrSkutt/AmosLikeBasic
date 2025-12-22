using System;
using System.Collections.Generic;
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
    private int _selectedTileIndex = 0;

    public MapEditorWindow(AmosGraphics gfx)
    {
        _gfx = gfx;
        InitializeComponent();
        
        // Synka UI med motorns faktiska data direkt vid start
        MapWidthUpDown.Value = _gfx.GetMapWidth();
        MapHeightUpDown.Value = _gfx.GetMapHeight();
        
        RefreshTileList();
        RedrawMap();
    }

    private async void LoadTiles_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sp = StorageProvider;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions 
        { 
            Title = "Select Tileset PNG", 
            FileTypeFilter = [FilePickerFileTypes.ImageAll] 
        });

        if (files.Count > 0)
        {
            try {
                // VIKTIGT: Använd Stream för macOS-kompatibilitet
                using var stream = await files[0].OpenReadAsync();
                
                // Vi behöver uppdatera LoadTileBank i AmosGraphics att ta emot Stream
                _gfx.LoadTileBank(stream, 32, 32); 
                
                RefreshTileList();
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Tile error: {ex.Message}");
            }
        }
    }

    private void RefreshTileList()
    {
        // För att ListBox ska fatta att den ska rita om, nollställer vi den först
        TileList.ItemsSource = null;
        var tiles = _gfx.GetTileBitmaps();
        
        // Skapa en kopia av listan så att Avalonia ser den som "ny"
        TileList.ItemsSource = new List<WriteableBitmap>(tiles);
    }

    private void MapCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e) => PaintTile(e);
    private void MapCanvas_OnPointerMoved(object? sender, PointerEventArgs e) 
    {
        if (e.GetCurrentPoint(MapCanvas).Properties.IsLeftButtonPressed) PaintTile(e);
        if (e.GetCurrentPoint(MapCanvas).Properties.IsRightButtonPressed) RemoveTile(e); 
    }

    
    private void ResizeMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        int w = (int)(MapWidthUpDown.Value ?? 20);
        int h = (int)(MapHeightUpDown.Value ?? 15);
        _gfx.SetMapSize(w, h);
        
        // Uppdatera storleken på ritytan (32 pixlar per tile)
        MapCanvas.Width = w * 32;
        MapCanvas.Height = h * 32;
        
        RedrawMap();
    }
    
    private void PaintTile(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(MapCanvas).Properties.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(MapCanvas);
            int tx = (int)(pos.X / 32);
            int ty = (int)(pos.Y / 32);

            _selectedTileIndex = TileList.SelectedIndex;

            if (_selectedTileIndex >= 0 && tx >= 0 && tx < _gfx.GetMapWidth() && ty >= 0 && ty < _gfx.GetMapHeight())
            {
                if (_gfx.GetMapTile(tx, ty) != _selectedTileIndex)
                {
                    _gfx.SetMapTile(tx, ty, _selectedTileIndex);
                    // Istället för att rita om hela banan, rita bara den nya tilen för snabbhet
                    UpdateSingleTileOnCanvas(tx, ty, _selectedTileIndex);
                }
            }
        }
    }

    private void RemoveTile(PointerEventArgs e)
    {
        var pos = e.GetPosition(MapCanvas);
        int tx = (int)(pos.X / 32);
        int ty = (int)(pos.Y / 32);
        
        _selectedTileIndex = -1;
        
        if (tx >= 0 && tx < _gfx.GetMapWidth() && ty >= 0 && ty < _gfx.GetMapHeight())
        {
            if (_gfx.GetMapTile(tx, ty) != _selectedTileIndex)
            {
                _gfx.SetMapTile(tx, ty, _selectedTileIndex);
                // Istället för att rita om hela banan, rita bara den nya tilen för snabbhet
                RemoveSingleTileOnCanvas(tx, ty, _selectedTileIndex);
            }
        }
    }
    
    private void RedrawMap()
    {
        MapCanvas.Children.Clear();
        int mw = _gfx.GetMapWidth();
        int mh = _gfx.GetMapHeight();
        
        // Uppdatera storleken på Canvas så scrollbaren stämmer
        MapCanvas.Width = mw * 32;
        MapCanvas.Height = mh * 32;

        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                int tid = _gfx.GetMapTile(x, y);
                if (tid >= 0) UpdateSingleTileOnCanvas(x, y, tid);
            }
        }
    }

    private void UpdateSingleTileOnCanvas(int x, int y, int tid)
    {
        var tiles = _gfx.GetTileBitmaps();
        if (tid < 0 || tid >= tiles.Count) return;

        // Om det redan finns en bild på denna plats, ta bort den först
        // (Enkel optimering: vi letar efter objektet på rätt position)
        var existing = MapCanvas.Children
            .FirstOrDefault(c => Canvas.GetLeft(c) == x * 32 && Canvas.GetTop(c) == y * 32);
        if (existing != null) MapCanvas.Children.Remove(existing);

        var img = new Image {
            Source = tiles[tid],
            Width = 32,
            Height = 32
        };
        Canvas.SetLeft(img, x * 32);
        Canvas.SetTop(img, y * 32);
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
        MapCanvas.Children.Add(img);
    }

    private void RemoveSingleTileOnCanvas(int x, int y, int tid)
    {
        var tiles = _gfx.GetTileBitmaps();
        if (tid >= tiles.Count) return;
        
        var existing = MapCanvas.Children
            .FirstOrDefault(c => Canvas.GetLeft(c) == x * 32 && Canvas.GetTop(c) == y * 32);
        if (existing != null) MapCanvas.Children.Remove(existing);
    }

    private async void SaveMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sp = StorageProvider;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AMOS Map",
            SuggestedFileName = "level.amosmap",
            FileTypeChoices = [new FilePickerFileType("AMOS Map") { Patterns = ["*.amosmap"] }]
        });

        if (file != null)
        {
            var mapData = new List<int>();
            int mw = _gfx.GetMapWidth();
            int mh = _gfx.GetMapHeight();
            for (int y = 0; y < mh; y++)
                for (int x = 0; x < mw; x++)
                    mapData.Add(_gfx.GetMapTile(x, y));

            var dto = new { Width = mw, Height = mh, Data = mapData };
            using var stream = await file.OpenWriteAsync();
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, dto);
        }
    }

    private async void LoadMap_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sp = StorageProvider;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions 
        { 
            Title = "Load AMOS Map", 
            FileTypeFilter = [new FilePickerFileType("AMOS Map") { Patterns = ["*.amosmap"] }] 
        });

        if (files.Count > 0)
        {
            using var stream = await files[0].OpenReadAsync();
            var dto = await System.Text.Json.JsonSerializer.DeserializeAsync<MapDto>(stream);
            if (dto != null)
            {
                _gfx.SetMapSize(dto.Width, dto.Height);
                int idx = 0;
                for (int y = 0; y < dto.Height; y++)
                    for (int x = 0; x < dto.Width; x++)
                        _gfx.SetMapTile(x, y, dto.Data[idx++]);
                
                RedrawMap();
            }
        }
    }

    private void OnClearMapClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _gfx.ClearMap();
        RedrawMap();
    }
    
    // En liten hjälp-klass för laddning
    private record MapDto(int Width, int Height, List<int> Data);
}