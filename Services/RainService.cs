using System.Globalization;
using System.Net.Http;

namespace IdleDash.Services;

/// <summary>Verwachte neerslag voor één moment (per 5 minuten).</summary>
public record RainPoint(string Time, double MmPerHour);

/// <summary>Buienradar: regenverwachting voor de komende twee uur (alleen Nederland en België).</summary>
public static class RainService
{
    /// <summary>Vanaf deze hoeveelheid (mm per uur) telt het als regen.</summary>
    public const double RainThreshold = 0.1;

    private static readonly string[] Hosts = { "https://gadgets.buienradar.nl", "https://gpsgadget.buienradar.nl" };

    public static async Task<List<RainPoint>?> GetAsync(double latitude, double longitude)
    {
        // Buienradar wil precies twee decimalen
        string query = "/data/raintext?lat=" + latitude.ToString("0.00", CultureInfo.InvariantCulture)
                     + "&lon=" + longitude.ToString("0.00", CultureInfo.InvariantCulture);

        foreach (string host in Hosts)
        {
            try
            {
                var points = Parse(await Web.Client.GetStringAsync(host + query));
                if (points.Count > 0) return points;
            }
            catch
            {
                // deze server lukt niet: de volgende proberen
            }
        }
        return null;
    }

    /// <summary>Regels als "077|15:05": een waarde van 0 tot 255 en een tijd.</summary>
    public static List<RainPoint> Parse(string text)
    {
        var points = new List<RainPoint>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                continue;

            // Formule van Buienradar zelf: waarde omrekenen naar millimeter per uur
            double mm = value <= 0 ? 0 : Math.Pow(10, (value - 109) / 32.0);
            points.Add(new RainPoint(parts[1].Trim(), Math.Round(mm, 2)));
        }
        return points;
    }

    /// <summary>Korte zin voor bovenaan de widget, bv. "Lichte regen vanaf 16:20".</summary>
    public static string Summarize(IReadOnlyList<RainPoint> points)
    {
        if (points.Count == 0) return "Geen regendata";

        int firstRain = IndexOf(points, p => p.MmPerHour >= RainThreshold);
        if (firstRain < 0) return $"Droog tot minstens {points[points.Count - 1].Time}";

        if (firstRain == 0)
        {
            int firstDry = IndexOf(points, p => p.MmPerHour < RainThreshold);
            double max = MaxBetween(points, 0, firstDry < 0 ? points.Count : firstDry);
            return firstDry < 0
                ? $"{Intensity(max)} de komende twee uur"
                : $"{Intensity(max)} tot ongeveer {points[firstDry].Time}";
        }

        int dryAgain = IndexOf(points, p => p.MmPerHour < RainThreshold, firstRain);
        double peak = MaxBetween(points, firstRain, dryAgain < 0 ? points.Count : dryAgain);
        return $"{Intensity(peak)} vanaf {points[firstRain].Time}";
    }

    private static string Intensity(double mmPerHour) => mmPerHour switch
    {
        < 1.0 => "Lichte regen",
        < 2.5 => "Regen",
        < 10.0 => "Flinke regen",
        _ => "Zware regen",
    };

    private static int IndexOf(IReadOnlyList<RainPoint> points, Func<RainPoint, bool> match, int start = 0)
    {
        for (int i = start; i < points.Count; i++)
            if (match(points[i])) return i;
        return -1;
    }

    private static double MaxBetween(IReadOnlyList<RainPoint> points, int from, int to)
    {
        double max = 0;
        for (int i = from; i < to; i++) max = Math.Max(max, points[i].MmPerHour);
        return max;
    }
}
