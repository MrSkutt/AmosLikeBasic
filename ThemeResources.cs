using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AmosLikeBasic;

// Lägger det valda temats färger i appens resurser, så att alla fönster (sprite- och
// karteditorn, tile-paletten, dialoger) följer temat. Fönstren hämtar färgerna med
// {DynamicResource Amos...}, så de byter färg direkt även när de redan är öppna.
//
// Textfärger väljs så att de alltid går att läsa: temats egen färg används om kontrasten
// mot bakgrunden räcker, annars nästa kandidat.
public static class ThemeResources
{
    // WCAG: 4.5 är gränsen för läsbar vanlig text
    private const double ReadableContrast = 4.5;

    public sealed record Palette(
        Color WindowBg, Color PanelBg, Color PanelBorder, Color LabelFg, Color TitleFg,
        Color ButtonBg, Color ButtonFg, Color ButtonHoverBg, Color ButtonPressedBg,
        Color InputBg, Color InputFg, Color CanvasBg, bool IsDark);

    public static Palette Create(AmosTheme t)
    {
        var buttonFg = Readable(t.ButtonBg, t.ButtonFg, t.MenuFg, t.EditorFg);
        bool isDark = Luminance(t.WindowBg) < 0.25;

        // Inmatningsfälten (sifferfält) ska vara ljusa i ljusa teman och mörka i mörka, annars
        // syns inte pilarna. Temats editorfärg används när den passar, t.ex. inte StosEditors
        // svarta editor i ett i övrigt ljusgrått tema.
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

        // Fluent-temats egna knappfärger, så att alla knappar utan egen färg följer temat
        // (förr: halvgenomskinlig mörk knapp med svart text ovanpå de mörkblå panelerna)
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

        // Ljust eller mörkt utseende på övriga kontroller (fält, rullningslister) efter temat,
        // inte efter datorns inställning
        app.RequestedThemeVariant = p.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        return p;
    }

    // Första kandidaten som är läsbar mot bakgrunden, annars svart eller vitt
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
