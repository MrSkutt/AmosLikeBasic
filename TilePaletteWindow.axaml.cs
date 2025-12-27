using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace AmosLikeBasic;

public partial class TilePaletteWindow : Window
{
    public event Action<int>? TileSelected;

    public TilePaletteWindow(List<WriteableBitmap> tiles, int tilesInWidth)
    {
        InitializeComponent();
            
        // Säkerhetskontroll: tilesInWidth får inte vara 0 eller mindre
        if (tilesInWidth <= 0) tilesInWidth = 1;

        int rows = (int)Math.Ceiling((double)tiles.Count / tilesInWidth);
        this.Width = tilesInWidth * 32 + 40;
        this.Height = rows * 32 + 60;

        double tileSide = 32.0;
        PaletteCanvas.Width = tilesInWidth * tileSide;
        PaletteCanvas.Height = Math.Ceiling(tiles.Count / (double)tilesInWidth) * tileSide;

        // Vi placerar ut varje tile manuellt på sin exakta plats
        for (int i = 0; i < tiles.Count; i++)
        {
            int row = i / tilesInWidth;
            int col = i % tilesInWidth;

            var img = new Image
            {
                Source = tiles[i],
                Width = tileSide,
                Height = tileSide,
                DataContext = i // Spara indexet här
            };

            // Vi lägger bilden i en knapp för att kunna klicka
            var btn = new Button
            {
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0.5),
                BorderBrush = Avalonia.Media.Brushes.DimGray,
                Content = img,
                DataContext = i 
            };

            btn.Click += (s, e) => {
                if (s is Button b && b.DataContext is int idx)
                    TileSelected?.Invoke(idx);
            };

            Canvas.SetLeft(btn, col * tileSide);
            Canvas.SetTop(btn, row * tileSide);
            PaletteCanvas.Children.Add(btn);
        }

        this.Width = PaletteCanvas.Width + 30;
        this.Height = 600;
    }
}