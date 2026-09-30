using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>Verwachte neerslag voor één moment (per 5 minuten).</summary>
public record RainPoint(string Time, double MmPerHour);

/// <summary>
/// Regen voor de komende twee uur. In Nederland en België via Buienradar (per 5 minuten),
/// daarbuiten via Open-Meteo (per kwartier).
/// </summary>
public static class RainService
{
    /// <summary>Vanaf deze hoeveelheid (mm per uur) telt het als regen.</summary>
    public const double RainThreshold = 0.1;

    private static readonly string[] Hosts = { "https://gadgets.buienradar.nl", "https://gpsgadget.buienradar.nl" };

    /// <summary>Ligt deze plek (ongeveer) in Nederland of België? Alleen daar werkt Buienradar.</summary>
    public static bool IsBuienradarArea(double latitude, double longitude) =>
        latitude is >= 49.4 and <= 53.7 && longitude is >= 2.5 and <= 7.3;

    /// <summary>Naam van de bron voor deze plek.</summary>
    public static string SourceName(double latitude, double longitude) =>
        IsBuienradarArea(latitude, longitude) ? "Buienradar" : "Open-Meteo";

    public static async Task<List<RainPoint>?> GetAsync(double latitude, double longitude)
    {
        if (!IsBuienradarArea(latitude, longitude)) return await GetOpenMeteoAsync(latitude, longitude);

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
        return await GetOpenMeteoAsync(latitude, longitude);
    }

    private static async Task<List<RainPoint>?> GetOpenMeteoAsync(double latitude, double longitude)
    {
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture)
            + "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture)
            + "&minutely_15=precipitation&forecast_minutely_15=9&timezone=auto";
        try
        {
            var points = ParseOpenMeteo(await Web.Client.GetStringAsync(url));
            return points.Count > 0 ? points : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Open-Meteo geeft millimeters per kwartier; omrekenen naar millimeter per uur.</summary>
    public static List<RainPoint> ParseOpenMeteo(string json)
    {
        var points = new List<RainPoint>();
        using var doc = JsonDocument.Parse(json);
        var block = doc.RootElement.GetProperty("minutely_15");
        var times = block.GetProperty("time");
        var values = block.GetProperty("precipitation");
        for (int i = 0; i < Math.Min(times.GetArrayLength(), values.GetArrayLength()); i++)
        {
            string time = times[i].GetString() ?? "";
            if (time.Length >= 16) time = time[11..16];   // "2026-09-30T14:15" -> "14:15"
            double mm = values[i].ValueKind == JsonValueKind.Number ? values[i].GetDouble() * 4 : 0;
            points.Add(new RainPoint(time, Math.Round(mm, 2)));
        }
        return points;
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
        if (points.Count == 0) return Loc.T("Geen regendata");

        int firstRain = IndexOf(points, p => p.MmPerHour >= RainThreshold);
        if (firstRain < 0) return Loc.T("Droog tot minstens {0}", TimeText(points[points.Count - 1].Time));

        if (firstRain == 0)
        {
            int firstDry = IndexOf(points, p => p.MmPerHour < RainThreshold);
            double max = MaxBetween(points, 0, firstDry < 0 ? points.Count : firstDry);
            return firstDry < 0
                ? Loc.T("{0} de komende twee uur", Intensity(max))
                : Loc.T("{0} tot ongeveer {1}", Intensity(max), TimeText(points[firstDry].Time));
        }

        int dryAgain = IndexOf(points, p => p.MmPerHour < RainThreshold, firstRain);
        double peak = MaxBetween(points, firstRain, dryAgain < 0 ? points.Count : dryAgain);
        return Loc.T("{0} vanaf {1}", Intensity(peak), TimeText(points[firstRain].Time));
    }

    /// <summary>"15:05" in de gekozen tijdnotatie (bv. "3:05 PM").</summary>
    public static string TimeText(string hhmm) =>
        DateTime.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? Loc.Time(t) : hhmm;

    private static string Intensity(double mmPerHour) => mmPerHour switch
    {
        < 1.0 => Loc.T("Lichte regen"),
        < 2.5 => Loc.T("Regen"),
        < 10.0 => Loc.T("Flinke regen"),
        _ => Loc.T("Zware regen"),
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
