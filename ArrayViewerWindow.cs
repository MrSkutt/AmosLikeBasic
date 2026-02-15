using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AmosLikeBasic;

public class ArrayViewerWindow : Window
{
    public ArrayViewerWindow(string arrayName, IAmosArray array)
    {
        Title = $"Array: {arrayName}";
        Width = 380;
        Height = 500;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Bygg visningslista — iterera platt index och räkna ut koordinater
        var items = new List<string>();

        if (array is AmosNumericArray numArr)
        {
            for (int flat = 0; flat < numArr.Data.Length; flat++)
            {
                var coords = FlatToCoords(flat, numArr.Dimensions);
                var coordStr = string.Join(", ", coords);
                items.Add($"[{coordStr}]  =  {numArr.Data[flat].ToString("G10", CultureInfo.InvariantCulture)}");
            }
        }
        else if (array is AmosStringArray strArr)
        {
            for (int flat = 0; flat < strArr.Data.Length; flat++)
            {
                var coords = FlatToCoords(flat, strArr.Dimensions);
                var coordStr = string.Join(", ", coords);
                items.Add($"[{coordStr}]  =  \"{strArr.Data[flat] ?? ""}\"");
            }
        }

        var allItems = items; // spara för filtrering

        var header = new TextBlock
        {
            Text = $"{arrayName}  [{string.Join("×", array.Dimensions.Select(d => d - 1))}]  —  {array.Length} element",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(10, 8, 10, 4),
            FontSize = 13
        };

        var searchBox = new TextBox
        {
            Watermark = "Filter värde eller index...",
            Margin = new Thickness(6, 4, 6, 4),
            FontSize = 12
        };

        var listBox = new ListBox
        {
            Margin = new Thickness(6, 0, 6, 6),
            FontFamily = new FontFamily("Courier New, Monospace"),
            FontSize = 12,
            ItemsSource = allItems
        };

        searchBox.TextChanged += (_, _) =>
        {
            var filter = searchBox.Text?.ToUpperInvariant() ?? "";
            listBox.ItemsSource = string.IsNullOrWhiteSpace(filter)
                ? allItems
                : allItems.Where(s => s.ToUpperInvariant().Contains(filter)).ToList();
        };

        var closeBtn = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(6, 4, 6, 8)
        };
        closeBtn.Click += (_, _) => Close();

        var topPanel = new StackPanel
        {
            Children = { header, searchBox }
        };
        DockPanel.SetDock(topPanel, Dock.Top);
        DockPanel.SetDock(closeBtn, Dock.Bottom);

        Content = new DockPanel
        {
            Children = { topPanel, closeBtn, listBox }
        };
    }

// Konverterar platt index → koordinater för multidim-array
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