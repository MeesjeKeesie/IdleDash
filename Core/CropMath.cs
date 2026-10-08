using System.Globalization;

namespace IdleDash.Core;

/// <summary>Uitsnede van een foto, als deel van de hele foto (0 tot 1): links, boven, breedte, hoogte.</summary>
public readonly record struct CropRect(double X, double Y, double W, double H)
{
    public static CropRect? Parse(string? text)
    {
        var parts = (text ?? "").Split(',');
        if (parts.Length != 4) return null;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        if (v[2] <= 0 || v[3] <= 0 || v[0] < -0.001 || v[1] < -0.001 || v[0] + v[2] > 1.001 || v[1] + v[3] > 1.001) return null;
        return new CropRect(v[0], v[1], v[2], v[3]);
    }

    public override string ToString() => string.Join(",", new[] { X, Y, W, H }.Select(d => Math.Round(d, 5).ToString(CultureInfo.InvariantCulture)));
}

/// <summary>Rekenwerk voor de uitsnede-kiezer: de foto ligt achter een kader en je kunt schuiven en zoomen.</summary>
public static class CropMath
{
    /// <summary>Kleinste zoom waarbij de foto het kader helemaal vult.</summary>
    public static double CoverScale(double imageW, double imageH, double frameW, double frameH) =>
        Math.Max(frameW / imageW, frameH / imageH);

    /// <summary>Verschuiving begrenzen, zodat er nergens een lege rand in het kader komt.</summary>
    public static (double X, double Y) Clamp(double x, double y, double imageW, double imageH, double scale, double frameW, double frameH) =>
        (Math.Clamp(x, Math.Min(0, frameW - imageW * scale), 0), Math.Clamp(y, Math.Min(0, frameH - imageH * scale), 0));

    public static CropRect ToCrop(double x, double y, double imageW, double imageH, double scale, double frameW, double frameH) =>
        new(-x / (imageW * scale), -y / (imageH * scale), frameW / (imageW * scale), frameH / (imageH * scale));

    public static (double Scale, double X, double Y) FromCrop(CropRect crop, double imageW, double imageH, double frameW) 
    {
        double scale = frameW / (crop.W * imageW);
        return (scale, -crop.X * imageW * scale, -crop.Y * imageH * scale);
    }

    /// <summary>Zoomen rond een punt (bv. waar de muis staat): dat punt van de foto blijft op dezelfde plek.</summary>
    public static (double X, double Y) ZoomAt(double scale, double newScale, double x, double y, double pointX, double pointY) =>
        (pointX - (pointX - x) * newScale / scale, pointY - (pointY - y) * newScale / scale);
}
