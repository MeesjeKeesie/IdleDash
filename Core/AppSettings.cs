using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdleDash.Core;

/// <summary>Alle instellingen + de indeling van je widgets. Staat in %AppData%\IdleDash\settings.json</summary>
public class AppSettings
{
    // ── Taal en eenheden ("auto" = volgens Windows) ──

    public string Language { get; set; } = "auto";          // auto, nl, en
    public string TimeFormat { get; set; } = "auto";        // auto, 24, 12
    public string TemperatureUnit { get; set; } = "auto";   // auto, c, f
    public string WindUnit { get; set; } = "auto";          // auto, kmh, mph

    // ── Scherm ──

    /// <summary>null = automatisch het bovenste extra scherm. Anders bv. "\\.\DISPLAY3".</summary>
    public string? MonitorDeviceName { get; set; }
    public double ShowDelaySeconds { get; set; } = 2;
    public List<string> IgnoredProcesses { get; set; } = new();
    public bool PixelShift { get; set; } = true;

    // ── Thema ──

    public ThemeSettings Theme { get; set; } = ThemePresets.Sky();
    public List<ThemeSettings> CustomThemes { get; set; } = new();

    // ── Weer en Buienradar ──

    public string WeatherPlace { get; set; } = "Tilburg";
    public double WeatherLatitude { get; set; } = 51.56;
    public double WeatherLongitude { get; set; } = 5.09;

    // ── Nachtmodus ──

    public bool NightModeEnabled { get; set; } = true;
    public string NightStart { get; set; } = "23:00";
    public string NightEnd { get; set; } = "07:00";
    public int NightDimPercent { get; set; } = 70;

    // ── Focustimer ──

    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;

    // ── Agenda's ──

    /// <summary>Apple iCloud-agenda (CalDAV). Het wachtwoord staat versleuteld.</summary>
    public AppleCalendarAccount? Apple { get; set; }

    /// <summary>Losse agenda-links (ICS), bv. Outlook, afvalkalender of rooster.</summary>
    public List<IcsFeedSettings> IcsFeeds { get; set; } = new();

    // ── Smarthome ──

    public SmartHomeSettings SmartHome { get; set; } = new();

    // ── Oude instellingen (uit 1.0/1.1): nu per widget, alleen nog als beginwaarde ──

    public string NewsFeed { get; set; } = "nosnieuwsalgemeen";
    public string? GoogleTaskListId { get; set; }
    public int CalendarDays { get; set; } = 7;

    // ── Meldingen en updates ──

    public bool WelcomeShown { get; set; }
    public string? LastNotifiedVersion { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public string? LastRunVersion { get; set; }

    // ── Indeling ──

    public List<WidgetConfig> Widgets { get; set; } = new();

    /// <summary>Gaat af als er iets is aangepast in het instellingenscherm.</summary>
    public event Action? Changed;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IdleDash");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Kapot of onleesbaar bestand: gewoon met standaardinstellingen verder
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string json = JsonSerializer.Serialize(this, JsonOptions);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);          // eerst apart wegschrijven: bij stroomuitval blijft het oude bestand heel
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Opslaan mislukt (bv. bestand even op slot): volgende keer opnieuw
        }
    }

    /// <summary>Opslaan en iedereen laten weten dat er iets veranderd is.</summary>
    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    public static T? FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);
}

public class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Thema voor alleen deze widget, of null voor het algemene thema.</summary>
    public string? ThemeName { get; set; }

    /// <summary>Instellingen van deze widget (verschilt per soort widget).</summary>
    public JsonObject Options { get; set; } = new();

    public T Get<T>(string key, T fallback)
    {
        try
        {
            return Options.TryGetPropertyValue(key, out var node) && node != null ? node.Deserialize<T>() ?? fallback : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public void Set<T>(string key, T value) => Options[key] = JsonSerializer.SerializeToNode(value);
}

public class AppleCalendarAccount
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";   // versleuteld met Windows (DPAPI)
    public List<RemoteCalendar> Calendars { get; set; } = new();
}

public class RemoteCalendar
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public class IcsFeedSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Color { get; set; } = "#8FB3E6";
}

public class SmartHomeSettings
{
    public string? HomeAssistantUrl { get; set; }
    public string? HomeAssistantToken { get; set; }   // versleuteld
    public string? HueBridge { get; set; }
    public string? HueUser { get; set; }             // versleuteld
    public List<string> ShellyDevices { get; set; } = new();

    /// <summary>Sensoren die een melding geven (bv. deurbel, beweging).</summary>
    public List<string> NotifyDevices { get; set; } = new();

    /// <summary>Sloten, alarm en garagedeuren mogen bediend worden (altijd met bevestiging).</summary>
    public bool AllowSensitive { get; set; } = true;
}
