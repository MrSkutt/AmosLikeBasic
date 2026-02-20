using Avalonia.Controls;

namespace AmosLikeBasic;

public partial class SpritesheetConfigDialog : Window
{
    public SpritesheetConfigDialog() : this(null)
    {
    }

    public SpritesheetConfigDialog(SpritesheetConfig? initialConfig)
    {
        InitializeComponent();
        
        if (initialConfig != null)
        {
            FrameWidthUpDown.Value = initialConfig.FrameWidth;
            FrameHeightUpDown.Value = initialConfig.FrameHeight;
            FrameCountUpDown.Value = initialConfig.FrameCount;
        }
    }

    private void OkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var config = new SpritesheetConfig
        {
            FrameWidth = (int)(FrameWidthUpDown.Value ?? 32),
            FrameHeight = (int)(FrameHeightUpDown.Value ?? 32),
            FrameCount = (int)(FrameCountUpDown.Value ?? 8)
        };

        Close(config);
    }

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}