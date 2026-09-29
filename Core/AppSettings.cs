using System.IO;
using System.Text.Json;

namespace IdleDash.Core;

/// <summary>Alle instellingen + de indeling van je widgets. Staat in %AppData%\IdleDash\settings.json</summary>
public class AppSettings
{
    // ── Scherm ──

    /// <summary>null = automatisch het bovenste scherm. Anders bv. "\\.\DISPLAY3".</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Hoe lang het scherm leeg moet zijn voordat het dashboard verschijnt.</summary>
    public double ShowDelaySeconds { get; set; } = 2;

    /// <summary>Programma's (zonder .exe) die niet meetellen als "venster op het scherm".</summary>
    public List<string> IgnoredProcesses { get; set; } = new();

    /// <summary>Verschuift alles af en toe een paar pixels, tegen inbranden.</summary>
    public bool PixelShift { get; set; } = true;

    // ── Weer en Buienradar ──

    public string WeatherPlace { get; set; } = "Tilburg";
    public double WeatherLatitude { get; set; } = 51.56;
    public double WeatherLongitude { get; set; } = 5.09;

    // ── Nachtmodus ──

    public bool NightModeEnabled { get; set; } = true;
    public string NightStart { get; set; } = "23:00";
    public string NightEnd { get; set; } = "07:00";

    /// <summary>Hoeveel procent donkerder 's nachts (100 = helemaal zwart).</summary>
    public int NightDimPercent { get; set; } = 70;

    // ── Focustimer ──

    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;

    // ── Nieuws en Google ──

    public string NewsFeed { get; set; } = "nosnieuwsalgemeen";

    /// <summary>null = je eerste takenlijst in Google Taken.</summary>
    public string? GoogleTaskListId { get; set; }

    public int CalendarDays { get; set; } = 7;

    // ── Meldingen ──

    /// <summary>Het welkomstbericht na de eerste keer opstarten is al getoond.</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Over deze versie hebben we al een update-melding gegeven.</summary>
    public string? LastNotifiedVersion { get; set; }

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
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
}

public class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
