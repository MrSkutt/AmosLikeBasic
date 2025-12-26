using Avalonia.Media;

namespace AmosLikeBasic;

public record AmosTheme(
    string Name,
    Color WindowBg,
    Color ToolbarBg,
    Color EditorBg,
    Color EditorFg,
    Color EditorCursorPosBg,
    Color TitleBarBg,
    Color TitleBarFg,
    Color AccentColor
);

public static class AmosThemes
{
    public static readonly AmosTheme ClassicBlue = new(
        "Classic AMOS",
        Color.Parse("#00134D"), // WindowBg
        Color.Parse("#0A2D8F"), // ToolbarBg
        Color.Parse("#00134D"), // EditorBg
        Color.Parse("#7FB2FF"), // EditorFg
        Color.Parse("#06206F"), // CursorPosBg
        Color.Parse("#FFD400"), // TitleBarBg
        Colors.Black,           // TitleBarFg
        Color.Parse("#7FB2FF")  // Accent
    );

    public static readonly AmosTheme Workbench = new(
        "Workbench",
        Color.Parse("#AAAAAA"),
        Color.Parse("#777777"),
        Color.Parse("#FFFFFF"),
        Color.Parse("#000000"),
        Color.Parse("#999999"),
        Color.Parse("#0044AA"),
        Colors.White,
        Color.Parse("#0044AA")
    );

    public static readonly AmosTheme Emerald = new(
        "Emerald",
        Color.Parse("#002200"),
        Color.Parse("#004400"),
        Color.Parse("#001100"),
        Color.Parse("#00FF00"),
        Color.Parse("#003300"),
        Color.Parse("#00AA00"),
        Colors.Black,
        Color.Parse("#00FF00")
    );
    
    public static readonly AmosTheme NeonNight = new(
        "Neon Night",
        Color.Parse("#1A0033"), // WindowBg (Mörklila)
        Color.Parse("#2D0066"), // ToolbarBg
        Color.Parse("#0D001A"), // EditorBg
        Color.Parse("#FF00FF"), // EditorFg (Neonrosa)
        Color.Parse("#3D0080"), // CursorPosBg
        Color.Parse("#00FFFF"), // TitleBarBg (Cyan)
        Colors.Black,           // TitleBarFg
        Color.Parse("#00FFFF")  // Accent (Cyan)
    );
    
    public static readonly AmosTheme CatppuccinMocha = new(
        "Catppuccin Mocha",
        Color.Parse("#1e1e2e"), // Base
        Color.Parse("#181825"), // Mantle
        Color.Parse("#1e1e2e"), // Editor Bg
        Color.Parse("#cdd6f4"), // Text
        Color.Parse("#313244"), // Surface0
        Color.Parse("#cba6f7"), // Mauve (TitleBar)
        Color.Parse("#11111b"), // Crust (TitleText)
        Color.Parse("#89b4fa")  // Blue (Accent)
    );
}