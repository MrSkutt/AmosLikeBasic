using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AmosLikeBasic;

// Project → Folders: where projects, graphics, sounds and exported games are. The file dialogs
// start in these folders, and a program's "Graphics/..." and "Sound/..." are looked for in them.
public class FoldersWindow : Window
{
    private readonly Dictionary<WorkspaceFolder, TextBox> _boxes = new();

    private static readonly (WorkspaceFolder folder, string what)[] Rows =
    {
        (WorkspaceFolder.Projects, "Your programs (.amosproj)"),
        (WorkspaceFolder.Graphics, "Pictures, sprites, tiles and maps: \"Graphics/...\" in a program"),
        (WorkspaceFolder.Sound, "Samples and songs: \"Sound/...\" in a program"),
        (WorkspaceFolder.Export, "Where Export app... puts the games"),
    };

    public FoldersWindow()
    {
        Title = "Folders";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosPanelBg");
        this[!ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosLabelFg");

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = $"The working folder is {Workspace.Root}. The file dialogs start in these folders.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var (folder, what) in Rows)
        {
            var title = new TextBlock { Text = folder.ToString(), FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) };
            title[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AmosTitleFg");
            panel.Children.Add(title);
            panel.Children.Add(new TextBlock { Text = what, FontSize = 11, Opacity = 0.8 });

            var box = new TextBox { Text = Workspace.FolderOf(folder), FontSize = 12 };
            _boxes[folder] = box;
            var choose = new Button { Content = "Choose...", Margin = new Thickness(6, 0, 0, 0) };
            choose.Click += async (_, _) =>
            {
                var start = await StorageProvider.TryGetFolderFromPathAsync(box.Text ?? Workspace.Root);
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = $"Choose the {folder} folder",
                    SuggestedStartLocation = start
                });
                if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) box.Text = path;
            };
            var reset = new Button { Content = "Default", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(reset, Workspace.DefaultOf(folder));
            reset.Click += (_, _) => box.Text = Workspace.DefaultOf(folder);
            var open = new Button { Content = "Show", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(open, "Open the folder");
            open.Click += (_, _) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(box.Text ?? "");
                    AppExporter.OpenFolder(box.Text ?? "");
                }
                catch (Exception) { }
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
            row.Children.Add(box);
            Grid.SetColumn(choose, 1); row.Children.Add(choose);
            Grid.SetColumn(reset, 2); row.Children.Add(reset);
            Grid.SetColumn(open, 3); row.Children.Add(open);
            panel.Children.Add(row);
        }

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        ok.Click += (_, _) =>
        {
            foreach (var (folder, box) in _boxes) Workspace.Choose(folder, box.Text);
            Close(true);
        };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel }
        });

        Content = panel;
    }
}
