namespace IdleDash.Core;

/// <summary>Rekenwerk voor lampkleuren: tint en verzadiging, wittint (Kelvin) en de eenheden van Philips Hue.</summary>
public static class ColorMath
{
    /// <summary>Tint (0-360) en verzadiging (0-100) naar RGB.</summary>
    public static (byte R, byte G, byte B) HsToRgb(double hue, double saturation, double value = 100)
    {
        double h = (hue % 360 + 360) % 360 / 60;
        double s = Math.Clamp(saturation, 0, 100) / 100, v = Math.Clamp(value, 0, 100) / 100;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return (ToByte((r + m) * 255), ToByte((g + m) * 255), ToByte((b + m) * 255));
    }

    /// <summary>De kleur van wit licht bij een bepaald aantal Kelvin (warm geel tot koel blauwwit).</summary>
    public static (byte R, byte G, byte B) KelvinToRgb(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100;   // benadering van Tanner Helland
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (ToByte(r), ToByte(g), ToByte(b));
    }

    public static int KelvinToMired(int kelvin) => (int)Math.Round(1_000_000.0 / Math.Max(1000, kelvin));
    public static int MiredToKelvin(int mired) => (int)Math.Round(1_000_000.0 / Math.Max(50, mired));

    // Philips Hue rekent met tint 0-65535 en verzadiging 0-254
    public static int ToHueApiHue(double hue) => (int)Math.Round((hue % 360 + 360) % 360 / 360 * 65535);
    public static int ToHueApiSat(double saturation) => (int)Math.Round(Math.Clamp(saturation, 0, 100) / 100 * 254);
    public static double FromHueApiHue(double value) => value / 65535 * 360;
    public static double FromHueApiSat(double value) => value / 254 * 100;

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
