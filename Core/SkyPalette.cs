using System.Windows.Media;

namespace IdleDash.Core;

public readonly record struct SkyColors(Color Top, Color Bottom, Color Glow);

/// <summary>
/// De achtergrond volgt de lucht: diep inktblauw 's nachts, paarsroze bij zonsopkomst,
/// leisteenblauw overdag en een warme gloed bij zonsondergang. Altijd donker genoeg
/// om niet af te leiden vanuit je ooghoek.
/// </summary>
public static class SkyPalette
{
    private static readonly (double Hour, SkyColors Colors)[] Keys =
    {
        (0.0,  Sky("#0B1222", "#070B16", "#1A2A4A")),   // nacht
        (5.5,  Sky("#0B1222", "#070B16", "#1A2A4A")),
        (7.0,  Sky("#2A2034", "#10131F", "#6A4458")),   // ochtendgloren
        (9.0,  Sky("#15233A", "#0C1422", "#2F5277")),   // dag
        (17.0, Sky("#15233A", "#0C1422", "#2F5277")),
        (19.0, Sky("#2E2030", "#121321", "#7A4638")),   // zonsondergang
        (21.0, Sky("#0E1628", "#080C18", "#22335A")),   // avond
        (24.0, Sky("#0B1222", "#070B16", "#1A2A4A")),
    };

    public static SkyColors At(DateTime time)
    {
        double hour = time.TimeOfDay.TotalHours;
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var (h0, c0) = Keys[i];
            var (h1, c1) = Keys[i + 1];
            if (hour >= h0 && hour <= h1)
            {
                double t = h1 > h0 ? (hour - h0) / (h1 - h0) : 0;
                return new SkyColors(Lerp(c0.Top, c1.Top, t), Lerp(c0.Bottom, c1.Bottom, t), Lerp(c0.Glow, c1.Glow, t));
            }
        }
        return Keys[0].Colors;
    }

    private static SkyColors Sky(string top, string bottom, string glow) => new(Parse(top), Parse(bottom), Parse(glow));

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
