namespace IdleDash.Core;

/// <summary>Kaartrekenwerk zoals bij OpenStreetMap: graden omzetten naar pixels op een bepaald zoomniveau (tegels van 256 pixels).</summary>
public static class WebMercator
{
    public const int TileSize = 256;

    public static (double X, double Y) ToPixel(double lat, double lon, double zoom)
    {
        double scale = TileSize * Math.Pow(2, zoom);
        double sin = Math.Clamp(Math.Sin(lat * Math.PI / 180), -0.9999, 0.9999);
        return ((lon + 180) / 360 * scale, (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale);
    }

    public static (double Lat, double Lon) ToLatLon(double x, double y, double zoom)
    {
        double scale = TileSize * Math.Pow(2, zoom);
        double n = Math.PI - 2 * Math.PI * y / scale;
        return (180 / Math.PI * Math.Atan(Math.Sinh(n)), x / scale * 360 - 180);
    }
}
