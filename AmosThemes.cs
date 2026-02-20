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
    Color AccentColor,
    string Font,
    // Nya:
    Color MenuBg,
    Color MenuFg,
    Color VarWatchBg,
    Color VarWatchHeaderBg,
    Color VarWatchFg,
    Color VarWatchAccentFg,
    Color LogBg,
    Color SplitterColor,
    Color ButtonBg,
    Color ButtonFg,
    Color ActionButtonBg,    // SPRITES, MAP, LOAD, SAVE
    Color ActionButtonFg,
    Color TopBarBg    
);

public static class AmosThemes
{
    public static readonly AmosTheme LightClean = new(
        "Light Clean",
        Color.Parse("#F0F2F5"), // WindowBg
        Color.Parse("#E0E4EC"), // ToolbarBg
        Color.Parse("#FFFFFF"), // EditorBg
        Color.Parse("#1A1A2E"), // EditorFg
        Color.Parse("#D0D4DE"), // CursorPosBg
        Color.Parse("#4A90D9"), // TitleBarBg
        Colors.White,           // TitleBarFg
        Color.Parse("#4A90D9"), // Accent
        "Courier New",
        Color.Parse("#E0E4EC"), // MenuBg
        Color.Parse("#1A1A2E"), // MenuFg
        Color.Parse("#F5F6FA"), // VarWatchBg
        Color.Parse("#D0D4DE"), // VarWatchHeaderBg
        Color.Parse("#1A1A2E"), // VarWatchFg
        Color.Parse("#4A90D9"), // VarWatchAccentFg
        Color.Parse("#EAEDF2"), // LogBg
        Color.Parse("#B0B8CC"), // SplitterColor
        Color.Parse("#E0E4EC"), // ButtonBg
        Color.Parse("#1A1A2E"), // ButtonFg
        Color.Parse("#4A90D9"), // ActionButtonBg
        Colors.White,           // ActionButtonFg
        Color.Parse("#D0D4DE")  // TopBarBg
    );

    public static readonly AmosTheme DarkPro = new(
        "Dark Pro",
        Color.Parse("#1E1E1E"), // WindowBg
        Color.Parse("#252526"), // ToolbarBg
        Color.Parse("#1E1E1E"), // EditorBg
        Color.Parse("#D4D4D4"), // EditorFg
        Color.Parse("#2D2D2D"), // CursorPosBg
        Color.Parse("#007ACC"), // TitleBarBg
        Colors.White,           // TitleBarFg
        Color.Parse("#007ACC"), // Accent
        "Courier New",
        Color.Parse("#252526"), // MenuBg
        Color.Parse("#CCCCCC"), // MenuFg
        Color.Parse("#1E1E1E"), // VarWatchBg
        Color.Parse("#2D2D2D"), // VarWatchHeaderBg
        Color.Parse("#D4D4D4"), // VarWatchFg
        Color.Parse("#569CD6"), // VarWatchAccentFg
        Color.Parse("#1A1A1A"), // LogBg
        Color.Parse("#3E3E42"), // SplitterColor
        Color.Parse("#3C3C3C"), // ButtonBg
        Color.Parse("#D4D4D4"), // ButtonFg
        Color.Parse("#007ACC"), // ActionButtonBg
        Colors.White,           // ActionButtonFg
        Color.Parse("#2D2D2D")  // TopBarBg
    );
    
public static readonly AmosTheme ClassicBlue = new(
    "Classic AMOS",
    Color.Parse("#00134D"), // WindowBg
    Color.Parse("#0A2D8F"), // ToolbarBg
    Color.Parse("#00134D"), // EditorBg
    Color.Parse("#7FB2FF"), // EditorFg
    Color.Parse("#06206F"), // CursorPosBg
    Color.Parse("#FFD400"), // TitleBarBg
    Colors.Black,           // TitleBarFg
    Color.Parse("#7FB2FF"), // Accent
    "Courier New",
    Color.Parse("#0A2D8F"), // MenuBg
    Color.Parse("#FFD400"), // MenuFg
    Color.Parse("#00134D"), // VarWatchBg
    Color.Parse("#06206F"), // VarWatchHeaderBg
    Color.Parse("#7FB2FF"), // VarWatchFg
    Colors.Yellow,          // VarWatchAccentFg
    Color.Parse("#000A26"), // LogBg
    Color.Parse("#7FB2FF"), // SplitterColor
    Color.Parse("#0A2D8F"), // ButtonBg
    Colors.White,    
Color.Parse("#0055AA"), // ActionButtonBg
Colors.White,           // ActionButtonFg
Color.Parse("#06206F")  // TopBarBg// ButtonFg
);

public static readonly AmosTheme Workbench = new(
    "Workbench",
    Color.Parse("#AAAAAA"), // WindowBg
    Color.Parse("#777777"), // ToolbarBg
    Color.Parse("#FFFFFF"), // EditorBg
    Color.Parse("#000000"), // EditorFg
    Color.Parse("#999999"), // CursorPosBg
    Color.Parse("#0044AA"), // TitleBarBg
    Colors.White,           // TitleBarFg
    Color.Parse("#0044AA"), // Accent
    "Topaz a600a1200a400",
    Color.Parse("#888888"), // MenuBg
    Colors.White,           // MenuFg
    Color.Parse("#BBBBBB"), // VarWatchBg
    Color.Parse("#999999"), // VarWatchHeaderBg
    Color.Parse("#000000"), // VarWatchFg
    Color.Parse("#0044AA"), // VarWatchAccentFg
    Color.Parse("#DDDDDD"), // LogBg
    Color.Parse("#0044AA"), // SplitterColor
    Color.Parse("#777777"), // ButtonBg
    Colors.White,
    Color.Parse("#0044AA"), // ActionButtonBg
    Colors.White,           // ActionButtonFg
    Color.Parse("#888888")  // TopBarBg// ButtonFg
);

public static readonly AmosTheme C64 = new(
    "C64 Classic",
    Color.Parse("#0A1A8F"), // WindowBg
    Color.Parse("#0A1A8F"), // ToolbarBg
    Color.Parse("#0A1A8F"), // EditorBg
    Color.Parse("#A0A0FF"), // EditorFg
    Color.Parse("#4040A0"), // CursorPosBg
    Color.Parse("#FFD800"), // TitleBarBg
    Color.Parse("#000000"), // TitleBarFg
    Color.Parse("#5FCDE4"), // Accent
    "C64 PRO MONO",
    Color.Parse("#0A1A8F"), // MenuBg
    Color.Parse("#FFD800"), // MenuFg
    Color.Parse("#0A1A8F"), // VarWatchBg
    Color.Parse("#4040A0"), // VarWatchHeaderBg
    Color.Parse("#A0A0FF"), // VarWatchFg
    Color.Parse("#5FCDE4"), // VarWatchAccentFg
    Color.Parse("#050D4A"), // LogBg
    Color.Parse("#5FCDE4"), // SplitterColor
    Color.Parse("#0A1A8F"), // ButtonBg
    Color.Parse("#FFD800"),
    Color.Parse("#5FCDE4"), // ActionButtonBg
    Color.Parse("#000000"), // ActionButtonFg
    Color.Parse("#4040A0")  // TopBarBg// ButtonFg
);

public static readonly AmosTheme StosClassic = new(
    "STOS Atari ST",
    Color.Parse("#BFBFBF"), // WindowBg
    Color.Parse("#9FA6B2"), // ToolbarBg
    Color.Parse("#FFFFFF"), // EditorBg
    Color.Parse("#000000"), // EditorFg
    Color.Parse("#0000AA"), // CursorPosBg
    Color.Parse("#0000AA"), // TitleBarBg
    Color.Parse("#FFFFFF"), // TitleBarFg
    Color.Parse("#0000AA"), // Accent
    "Atari ST 8x16 System Font",
    Color.Parse("#BFBFBF"), // MenuBg
    Color.Parse("#000000"), // MenuFg
    Color.Parse("#CCCCCC"), // VarWatchBg
    Color.Parse("#9FA6B2"), // VarWatchHeaderBg
    Color.Parse("#000000"), // VarWatchFg
    Color.Parse("#0000AA"), // VarWatchAccentFg
    Color.Parse("#EEEEEE"), // LogBg
    Color.Parse("#0000AA"), // SplitterColor
    Color.Parse("#9FA6B2"), // ButtonBg
    Colors.White,
    Color.Parse("#0000AA"), // ActionButtonBg
    Colors.White,           // ActionButtonFg
    Color.Parse("#9FA6B2")  // TopBarBg// ButtonFg
);

public static readonly AmosTheme StosEditor = new(
    "STOS Editor",
    Color.Parse("#BFBFBF"), // WindowBg
    Color.Parse("#A0A0A0"), // ToolbarBg
    Color.Parse("#000000"), // EditorBg
    Color.Parse("#00FF00"), // EditorFg
    Color.Parse("#003300"), // CursorPosBg
    Color.Parse("#0000AA"), // TitleBarBg
    Color.Parse("#FFFFFF"), // TitleBarFg
    Color.Parse("#00FF00"), // Accent
    "Atari ST 8x16 System Font",
    Color.Parse("#A0A0A0"), // MenuBg
    Color.Parse("#000000"), // MenuFg
    Color.Parse("#111111"), // VarWatchBg
    Color.Parse("#003300"), // VarWatchHeaderBg
    Color.Parse("#00FF00"), // VarWatchFg
    Color.Parse("#00FF00"), // VarWatchAccentFg
    Color.Parse("#000000"), // LogBg
    Color.Parse("#00FF00"), // SplitterColor
    Color.Parse("#A0A0A0"), // ButtonBg
    Color.Parse("#00FF00"),  // ButtonFg
    Color.Parse("#005500"), // ActionButtonBg
    Color.Parse("#00FF00"), // ActionButtonFg
    Color.Parse("#003300")  // TopBarBg
    );

public static readonly AmosTheme Emerald = new(
    "Emerald",
    Color.Parse("#002200"), // WindowBg
    Color.Parse("#004400"), // ToolbarBg
    Color.Parse("#001100"), // EditorBg
    Color.Parse("#00FF00"), // EditorFg
    Color.Parse("#003300"), // CursorPosBg
    Color.Parse("#00AA00"), // TitleBarBg
    Colors.Black,           // TitleBarFg
    Color.Parse("#00FF00"), // Accent
    "Courier New",
    Color.Parse("#004400"), // MenuBg
    Color.Parse("#00FF00"), // MenuFg
    Color.Parse("#002200"), // VarWatchBg
    Color.Parse("#003300"), // VarWatchHeaderBg
    Color.Parse("#00CC00"), // VarWatchFg
    Color.Parse("#00FF00"), // VarWatchAccentFg
    Color.Parse("#000A00"), // LogBg
    Color.Parse("#00AA00"), // SplitterColor
    Color.Parse("#004400"), // ButtonBg
    Color.Parse("#00FF00"),  // ButtonFg
    Color.Parse("#006600"), // ActionButtonBg
    Color.Parse("#00FF00"), // ActionButtonFg
    Color.Parse("#003300")  // TopBarBg
    );

public static readonly AmosTheme NeonNight = new(
    "Neon Night",
    Color.Parse("#1A0033"), // WindowBg
    Color.Parse("#2D0066"), // ToolbarBg
    Color.Parse("#0D001A"), // EditorBg
    Color.Parse("#FF00FF"), // EditorFg
    Color.Parse("#3D0080"), // CursorPosBg
    Color.Parse("#00FFFF"), // TitleBarBg
    Colors.Black,           // TitleBarFg
    Color.Parse("#00FFFF"), // Accent
    "Courier New",
    Color.Parse("#2D0066"), // MenuBg
    Color.Parse("#00FFFF"), // MenuFg
    Color.Parse("#1A0033"), // VarWatchBg
    Color.Parse("#3D0080"), // VarWatchHeaderBg
    Color.Parse("#FF00FF"), // VarWatchFg
    Color.Parse("#00FFFF"), // VarWatchAccentFg
    Color.Parse("#0D001A"), // LogBg
    Color.Parse("#00FFFF"), // SplitterColor
    Color.Parse("#2D0066"), // ButtonBg
    Color.Parse("#00FFFF"),  // ButtonFg
    Color.Parse("#6600CC"), // ActionButtonBg
    Color.Parse("#00FFFF"), // ActionButtonFg
    Color.Parse("#3D0080")  // TopBarBg
    );

public static readonly AmosTheme CatppuccinMocha = new(
    "Catppuccin Mocha",
    Color.Parse("#1e1e2e"), // Base
    Color.Parse("#181825"), // Mantle
    Color.Parse("#1e1e2e"), // EditorBg
    Color.Parse("#cdd6f4"), // Text
    Color.Parse("#313244"), // Surface0
    Color.Parse("#cba6f7"), // Mauve (TitleBar)
    Color.Parse("#11111b"), // Crust (TitleText)
    Color.Parse("#89b4fa"), // Accent / Blue
    "Courier New",
    Color.Parse("#181825"), // MenuBg      (Mantle)
    Color.Parse("#cdd6f4"), // MenuFg      (Text)
    Color.Parse("#1e1e2e"), // VarWatchBg  (Base)
    Color.Parse("#313244"), // VarWatchHeaderBg (Surface0)
    Color.Parse("#cdd6f4"), // VarWatchFg  (Text)
    Color.Parse("#cba6f7"), // VarWatchAccentFg (Mauve)
    Color.Parse("#11111b"), // LogBg       (Crust)
    Color.Parse("#45475a"), // SplitterColor (Surface1)
    Color.Parse("#313244"), // ButtonBg    (Surface0)
    Color.Parse("#cdd6f4"),  // ButtonFg    (Text)
    Color.Parse("#cba6f7"), // ActionButtonBg (Mauve)
    Color.Parse("#11111b"), // ActionButtonFg (Crust)
    Color.Parse("#313244")  // TopBarBg (Surface0)
    );
}