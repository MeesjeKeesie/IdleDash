using System.Globalization;

namespace IdleDash.Core;

/// <summary>
/// Vertalingen en notaties. Alle teksten staan in de code in het Nederlands; T("...") geeft
/// de Engelse versie uit Strings.cs als Engels is gekozen. Ontbreekt een vertaling, dan blijft het Nederlands.
/// </summary>
public static partial class Loc
{
    public static string Language { get; private set; } = "nl";
    public static CultureInfo Culture { get; private set; } = new("nl-NL");
    public static bool Use12Hour { get; private set; }
    public static bool UseFahrenheit { get; private set; }
    public static bool UseMph { get; private set; }
    public static bool IsEnglish => Language == "en";

    /// <summary>Taal of eenheden zijn veranderd: teksten opnieuw opbouwen.</summary>
    public static event Action? Changed;

    /// <summary>Taal en eenheden instellen. "auto" volgt Windows. Geeft true als er iets veranderde.</summary>
    public static bool Configure(AppSettings settings)
    {
        var system = CultureInfo.CurrentCulture;
        string language = settings.Language switch
        {
            "nl" => "nl",
            "en" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl" ? "nl" : "en",
        };
        var culture = language == "nl"
            ? new CultureInfo("nl-NL")
            : system.TwoLetterISOLanguageName == "en" ? system : new CultureInfo("en-GB");
        string region = Region();

        bool twelve = settings.TimeFormat switch
        {
            "12" => true,
            "24" => false,
            _ => system.DateTimeFormat.ShortTimePattern.Contains('h'),
        };
        bool fahrenheit = settings.TemperatureUnit switch { "f" => true, "c" => false, _ => region == "US" };
        bool mph = settings.WindUnit switch { "mph" => true, "kmh" => false, _ => region is "US" or "GB" };

        bool changed = language != Language || culture.Name != Culture.Name
                       || twelve != Use12Hour || fahrenheit != UseFahrenheit || mph != UseMph;
        Language = language;
        Culture = culture;
        Use12Hour = twelve;
        UseFahrenheit = fahrenheit;
        UseMph = mph;
        if (changed) Changed?.Invoke();
        return changed;
    }

    private static string Region()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch
        {
            return "";
        }
    }

    // ── Teksten ──

    public static string T(string dutch) =>
        IsEnglish && Strings.English.TryGetValue(dutch, out var english) ? english : dutch;

    public static string T(string dutch, params object?[] args) => string.Format(Culture, T(dutch), args);

    // ── Notaties ──

    public static string Time(DateTime time) =>
        Use12Hour
            ? time.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"))
            : time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Temperatuur uit graden Celsius, in de gekozen eenheid.</summary>
    public static string Degrees(double celsius) =>
        $"{(int)Math.Round(UseFahrenheit ? celsius * 9 / 5 + 32 : celsius)}°";

    /// <summary>Windsnelheid uit km/u, in de gekozen eenheid.</summary>
    public static string Wind(double kmh) =>
        UseMph ? $"{Math.Round(kmh * 0.621371):0} mph" : $"{Math.Round(kmh):0} {T("km/u")}";

    /// <summary>Datum als tekst met hoofdletter, bv. "Maandag 28 september".</summary>
    public static string Date(DateTime date, string format = "dddd d MMMM") => Capitalize(date.ToString(format, Culture));

    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Culture) + text[1..];
}
