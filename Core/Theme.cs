using System.Globalization;

namespace IdleDash.Core;

/// <summary>Een thema: kleuren en achtergrond. Wordt als JSON bewaard en kan gedeeld worden.</summary>
public class ThemeSettings
{
    public string Name { get; set; } = "Lucht";
    public string Accent { get; set; } = "#DCE4F0";
    public string Text { get; set; } = "#EEF1F6";

    /// <summary>sky = kleurt mee met de tijd, solid = effen, gradient = verloop, photo = eigen foto.</summary>
    public string Background { get; set; } = "sky";
    public string Color1 { get; set; } = "#15233A";
    public string Color2 { get; set; } = "#0C1422";
    public string? Photo { get; set; }

    /// <summary>Hoeveel procent donkerder de foto wordt, zodat tekst leesbaar blijft.</summary>
    public int PhotoDim { get; set; } = 40;

    /// <summary>Doorzichtigheid van het kaartje achter elke widget (0 = geen kaartje).</summary>
    public int CardOpacity { get; set; }

    public ThemeSettings Clone() => (ThemeSettings)MemberwiseClone();
}

/// <summary>Kant-en-klare thema's.</summary>
public static class ThemePresets
{
    public static ThemeSettings Sky() => new();

    public static IReadOnlyList<ThemeSettings> All { get; } = new List<ThemeSettings>
    {
        Sky(),
        new() { Name = "AMOLED-zwart", Accent = "#8FB3E6", Text = "#C9CFD8", Background = "solid", Color1 = "#000000", Color2 = "#000000" },
        new() { Name = "Licht", Accent = "#2B5DB5", Text = "#18202C", Background = "gradient", Color1 = "#F4F6FA", Color2 = "#DCE3EE", CardOpacity = 50 },
        new() { Name = "Zonsondergang", Accent = "#FFD6A5", Text = "#FFF4EA", Background = "gradient", Color1 = "#2E1F47", Color2 = "#B65E57" },
        new() { Name = "Bos", Accent = "#A8D5BA", Text = "#E6F2EA", Background = "gradient", Color1 = "#12291F", Color2 = "#08140F" },
    };

    public static bool IsPreset(string name) => All.Any(t => t.Name == name);
}

/// <summary>Rekenwerk met kleuren (zonder WPF, zodat het te testen is).</summary>
public static class ThemeColors
{
    /// <summary>"#RRGGBB" of "#AARRGGBB" lezen. Geeft null bij ongeldige invoer.</summary>
    public static (byte A, byte R, byte G, byte B)? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#');
        if (h.Length is not (6 or 8) || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return null;
        return h.Length == 6
            ? ((byte)255, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Hoe licht een kleur is (0 = zwart, 1 = wit).</summary>
    public static double Luminance(string? hex)
    {
        var c = Parse(hex);
        if (c == null) return 0;
        return (0.2126 * c.Value.R + 0.7152 * c.Value.G + 0.0722 * c.Value.B) / 255.0;
    }

    /// <summary>Is dit thema licht (donkere tekst op lichte achtergrond)?</summary>
    public static bool IsLight(ThemeSettings theme) => Luminance(theme.Text) < 0.5;

    /// <summary>Apple-kleuren zijn "#RRGGBBAA"; WPF wil "#AARRGGBB".</summary>
    public static string? FromRgba(string? rgba)
    {
        if (string.IsNullOrWhiteSpace(rgba)) return null;
        string h = rgba.Trim().TrimStart('#');
        return h.Length == 8 ? "#" + h[6..8] + h[..6] : h.Length == 6 ? "#" + h : null;
    }
}
