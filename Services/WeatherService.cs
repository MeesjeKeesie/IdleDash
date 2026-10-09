using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

public record DayForecast(DateTime Date, int Code, double Max, double Min, DateTime? Sunrise = null, DateTime? Sunset = null);

public record WeatherData(
    double Temperature,
    double FeelsLike,
    int Code,
    double Wind,
    int Humidity,
    bool IsDay,
    List<DayForecast> Days);

/// <summary>Een plaats uit de zoekfunctie in de instellingen.</summary>
public record Place(string Name, string Description, double Latitude, double Longitude);

/// <summary>Weer via Open-Meteo: gratis en zonder API-sleutel.</summary>
public static class WeatherService
{
    public static async Task<WeatherData?> GetAsync(double latitude, double longitude)
    {
        // Altijd een punt als decimaalteken, ook op een Nederlandse pc
        string lat = latitude.ToString(CultureInfo.InvariantCulture);
        string lon = longitude.ToString(CultureInfo.InvariantCulture);
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=4";

        try
        {
            return Parse(await Web.Client.GetStringAsync(url));
        }
        catch
        {
            return null;   // geen internet of weerdienst onbereikbaar
        }
    }

    /// <summary>Plaatsen zoeken op naam (voor het instellingenscherm). null = zoeken lukte niet.</summary>
    /// <summary>Het antwoord van Open-Meteo uitlezen.</summary>
    public static WeatherData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");

        var days = new List<DayForecast>();
        var dates = daily.GetProperty("time");
        for (int i = 0; i < dates.GetArrayLength(); i++)
        {
            days.Add(new DayForecast(
                DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                daily.GetProperty("weather_code")[i].GetInt32(),
                daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                TimeAt(daily, "sunrise", i),
                TimeAt(daily, "sunset", i)));
        }

        return new WeatherData(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.GetProperty("wind_speed_10m").GetDouble(),
            (int)Math.Round(current.GetProperty("relative_humidity_2m").GetDouble()),
            current.GetProperty("is_day").GetInt32() == 1,
            days);
    }

    private static DateTime? TimeAt(JsonElement daily, string name, int i) =>
        daily.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array && i < values.GetArrayLength()
        && values[i].GetString() is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : null;

    public static async Task<List<Place>?> SearchPlacesAsync(string query)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&format=json&language=" + Loc.Language + "&name="
            + Uri.EscapeDataString(query);
        try
        {
            using var doc = JsonDocument.Parse(await Web.Client.GetStringAsync(url));
            var places = new List<Place>();
            if (!doc.RootElement.TryGetProperty("results", out var results)) return places;

            foreach (var result in results.EnumerateArray())
            {
                string name = Text(result, "name");
                var parts = new[] { name, Text(result, "admin1"), Text(result, "country") }
                    .Where(p => p.Length > 0)
                    .Distinct();
                places.Add(new Place(
                    name,
                    string.Join(", ", parts),
                    result.GetProperty("latitude").GetDouble(),
                    result.GetProperty("longitude").GetDouble()));
            }
            return places;
        }
        catch
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>Weercode (WMO) omzetten naar Nederlandse tekst en een icoon.</summary>
    public static (string Text, string Icon) Describe(int code, bool isDay = true)
    {
        var (text, icon) = code switch
        {
            0 => (isDay ? "Zonnig" : "Helder", isDay ? "\u2600" : "\u263E"),
            1 => (isDay ? "Overwegend zonnig" : "Overwegend helder", isDay ? "\u26C5" : "\u2601"),
            2 => ("Half bewolkt", isDay ? "\u26C5" : "\u2601"),
            3 => ("Bewolkt", "\u2601"),
            45 or 48 => ("Mist", "\U0001F32B"),
            51 or 53 or 55 => ("Motregen", "\U0001F326"),
            56 or 57 or 66 or 67 => ("IJzel", "\U0001F327"),
            61 => ("Lichte regen", "\U0001F327"),
            63 => ("Regen", "\U0001F327"),
            65 => ("Zware regen", "\U0001F327"),
            71 => ("Lichte sneeuw", "\u2744"),
            73 or 75 or 77 => ("Sneeuw", "\u2744"),
            80 => ("Lichte buien", "\U0001F326"),
            81 or 82 => ("Regenbuien", "\U0001F327"),
            85 or 86 => ("Sneeuwbuien", "\u2744"),
            95 => ("Onweer", "\u26C8"),
            96 or 99 => ("Onweer met hagel", "\u26C8"),
            _ => ("Onbekend", "\u2601"),
        };
        return (Loc.T(text), icon);
    }
}
