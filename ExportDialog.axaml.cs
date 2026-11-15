using System;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AmosLikeBasic;

public sealed record ExportOptions(string Name, string Platform);

public partial class ExportDialog : Window
{
    public ExportDialog() : this("MyGame")
    {
    }

    public ExportDialog(string defaultName)
    {
        InitializeComponent();
        NameBox.Text = defaultName;
        // Default: the own platform on Windows (the Mac app has to be built on a Mac), both on a Mac
        PlatformBox.SelectedIndex = OperatingSystem.IsWindows() ? 2 : 0;
        Opened += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void ExportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? "").Trim();
        // The same rule as Tools/export_app.py
        if (!Regex.IsMatch(name, @"^[\w .-]+$"))
        {
            ErrorText.Text = "The name may only contain letters, digits, spaces, dots and dashes.";
            ErrorText.IsVisible = true;
            return;
        }

        var platform = (PlatformBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "both";
        Close(new ExportOptions(name, platform));
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);
}
