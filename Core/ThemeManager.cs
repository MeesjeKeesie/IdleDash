using System.Collections;
using System.Windows;
using System.Windows.Media;

namespace IdleDash.Core;

/// <summary>Zet een thema om naar kleuren voor het dashboard (live, zonder herstart).</summary>
public static class ThemeManager
{
    public static Color ToColor(string? hex, Color fallback) =>
        ThemeColors.Parse(hex) is { } c ? Color.FromArgb(c.A, c.R, c.G, c.B) : fallback;

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Alle themakleuren als resources. Voor één widget of het hele dashboard.</summary>
    public static ResourceDictionary CreateBrushes(ThemeSettings theme)
    {
        var text = ToColor(theme.Text, Color.FromRgb(0xEE, 0xF1, 0xF6));
        var accent = ToColor(theme.Accent, Color.FromRgb(0xDC, 0xE4, 0xF0));
        bool light = ThemeColors.IsLight(theme);
        byte card = (byte)Math.Clamp(theme.CardOpacity * 255 / 100, 0, 255);

        return new ResourceDictionary
        {
            ["TextPrimaryBrush"] = Brush(text),
            ["TextSecondaryBrush"] = Brush(WithAlpha(text, 0xA6)),
            ["TextMutedBrush"] = Brush(WithAlpha(text, 0x70)),
            ["BarBrush"] = Brush(accent),
            ["EditOutlineBrush"] = Brush(accent),
            ["TrackBrush"] = Brush(WithAlpha(text, 0x1A)),
            ["HoverBrush"] = Brush(WithAlpha(text, 0x1F)),
            ["PressedBrush"] = Brush(WithAlpha(text, 0x33)),
            ["SubtleBrush"] = Brush(WithAlpha(text, 0x14)),
            ["CardBrush"] = Brush(light ? Color.FromArgb(card, 255, 255, 255) : Color.FromArgb(card, 8, 12, 20)),
            ["PanelBrush"] = Brush(light ? Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xF2, 0x16, 0x1C, 0x28)),
            ["PanelBorderBrush"] = Brush(WithAlpha(text, 0x26)),
            ["AccentForegroundBrush"] = Brush(ThemeColors.Luminance(theme.Accent) > 0.55 ? Color.FromRgb(0x0C, 0x14, 0x22) : Colors.White),
        };
    }

    /// <summary>Het algemene thema op de hele app toepassen.</summary>
    public static void ApplyGlobal(ThemeSettings theme)
    {
        var resources = Application.Current.Resources;
        foreach (DictionaryEntry entry in CreateBrushes(theme)) resources[entry.Key] = entry.Value;
    }

    /// <summary>Thema op naam: kant-en-klaar, zelfgemaakt, of anders het algemene thema.</summary>
    public static ThemeSettings Resolve(AppSettings settings, string? name) =>
        name == null ? settings.Theme
        : ThemePresets.All.FirstOrDefault(t => t.Name == name)?.Clone()
          ?? settings.CustomThemes.FirstOrDefault(t => t.Name == name)
          ?? settings.Theme;
}
