using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AmosLikeBasic;

// Puts the chosen theme's colours in the app's resources, so that all windows (the sprite and
// map editors, the tile palette, dialogs) follow the theme. The windows get the colours with
// {DynamicResource Amos...}, so they change colour straight away even when already open.
//
// Text colours are chosen so that they can always be read: the theme's own colour is used if the
// contrast against the background is enough, otherwise the next candidate.
public static class ThemeResources
{
    // WCAG: 4.5 is the limit for readable normal text
    private const double ReadableContrast = 4.5;

    public sealed record Palette(
        Color WindowBg, Color PanelBg, Color PanelBorder, Color LabelFg, Color TitleFg,
        Color ButtonBg, Color ButtonFg, Color ButtonHoverBg, Color ButtonPressedBg,
        Color InputBg, Color InputFg, Color CanvasBg, bool IsDark);

    public static Palette Create(AmosTheme t)
    {
        var buttonFg = Readable(t.ButtonBg, t.ButtonFg, t.MenuFg, t.EditorFg);
        bool isDark = Luminance(t.WindowBg) < 0.25;

        // The input fields (number boxes) should be light in light themes and dark in dark ones, otherwise
        // the arrows do not show. The theme's editor colour is used when it fits, e.g. not StosEditor's
        // black editor in an otherwise light grey theme.
        var inputBg = (Luminance(t.EditorBg) < 0.25) == isDark
            ? t.EditorBg
            : Mix(t.ToolbarBg, isDark ? Colors.Black : Colors.White, isDark ? 0.5 : 0.7);

        return new Palette(
            WindowBg: t.WindowBg,
            PanelBg: t.ToolbarBg,
            PanelBorder: t.AccentColor,
            LabelFg: Readable(t.ToolbarBg, t.AccentColor, t.MenuFg, t.EditorFg),
            TitleFg: Readable(t.ToolbarBg, t.MenuFg, t.AccentColor, t.EditorFg),
            ButtonBg: t.ButtonBg,
            ButtonFg: buttonFg,
            ButtonHoverBg: Mix(t.ButtonBg, buttonFg, 0.15),
            ButtonPressedBg: Mix(t.ButtonBg, buttonFg, 0.30),
            InputBg: inputBg,
            InputFg: Readable(inputBg, t.EditorFg, t.MenuFg, buttonFg),
            CanvasBg: t.EditorBg,
            IsDark: isDark);
    }

    public static Palette Apply(AmosTheme theme)
    {
        var p = Create(theme);
        var app = Application.Current!;
        var res = app.Resources;

        void Set(string key, Color c) => res[key] = new SolidColorBrush(c);

        Set("AmosWindowBg", p.WindowBg);
        Set("AmosPanelBg", p.PanelBg);
        Set("AmosPanelBorder", p.PanelBorder);
        Set("AmosLabelFg", p.LabelFg);
        Set("AmosTitleFg", p.TitleFg);
        Set("AmosInputBg", p.InputBg);
        Set("AmosInputFg", p.InputFg);
        Set("AmosCanvasBg", p.CanvasBg);
        Set("AmosButtonBg", p.ButtonBg);
        Set("AmosButtonFg", p.ButtonFg);

        // The Fluent theme's own button colours, so that all buttons without a colour of their own follow the theme
        // (before: a half-transparent dark button with black text on top of the dark blue panels)
        Set("ButtonBackground", p.ButtonBg);
        Set("ButtonBackgroundPointerOver", p.ButtonHoverBg);
        Set("ButtonBackgroundPressed", p.ButtonPressedBg);
        Set("ButtonBackgroundDisabled", Mix(p.ButtonBg, p.PanelBg, 0.5));
        Set("ButtonForeground", p.ButtonFg);
        Set("ButtonForegroundPointerOver", p.ButtonFg);
        Set("ButtonForegroundPressed", p.ButtonFg);
        Set("ButtonForegroundDisabled", Mix(p.ButtonFg, p.ButtonBg, 0.5));
        Set("ButtonBorderBrush", p.PanelBorder);
        Set("ButtonBorderBrushPointerOver", p.ButtonFg);
        Set("ButtonBorderBrushPressed", p.ButtonFg);

        // Light or dark look on the other controls (fields, scroll bars) according to the theme,
        // not the computer's setting
        app.RequestedThemeVariant = p.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        return p;
    }

    // The first candidate that is readable against the background, otherwise black or white
    public static Color Readable(Color bg, params Color[] candidates)
    {
        foreach (var c in candidates)
            if (Contrast(bg, c) >= ReadableContrast)
                return c;
        return Contrast(bg, Colors.White) >= Contrast(bg, Colors.Black) ? Colors.White : Colors.Black;
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
