using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>
/// Back-ups van alle instellingen (indeling, widgets, thema's, koppelingen): als bestand dat je zelf bewaart,
/// en elke dag automatisch (de laatste 10). Je Google-login zit er niet in; die blijft gewoon staan.
/// </summary>
public static class BackupService
{
    public const string Format = "IdleDash-backup";
    private const int KeepAutomatic = 10;

    public static string AutoFolder => Path.Combine(AppSettings.Folder, "backups");
    private static string SettingsFile => Path.Combine(AppSettings.Folder, "settings.json");
    private static string PendingFile => Path.Combine(AppSettings.Folder, "restore-pending.json");

    public static string Create(AppSettings settings, string appVersion, DateTime now) =>
        Wrap(AppSettings.ToJson(settings), appVersion, now);

    private static string Wrap(string settingsJson, string appVersion, DateTime now) => new JsonObject
    {
        ["format"] = Format,
        ["formatVersion"] = 1,
        ["appVersion"] = appVersion,
        ["created"] = now.ToString("yyyy-MM-ddTHH:mm:ss"),
        ["settings"] = JsonNode.Parse(settingsJson),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Controleert een back-up en geeft de instellingen eruit terug (als JSON), of null met een uitleg.</summary>
    public static string? Validate(string content, out string? error)
    {
        error = Loc.T("Dit is geen back-up van IdleDash.");
        try
        {
            if (JsonNode.Parse(content) is not JsonObject root || (string?)root["format"] != Format || root["settings"] is not JsonObject settings)
                return null;
            string json = settings.ToJsonString();
            if (AppSettings.FromJson<AppSettings>(json) == null) return null;
            error = null;
            return json;
        }
        catch
        {
            return null;
        }
    }

    public static DateTime? CreatedAt(string content)
    {
        try
        {
            return JsonNode.Parse(content)?["created"] is JsonValue value && DateTime.TryParse((string?)value, out var date) ? date : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Elke dag één automatische back-up; oudere dan de laatste 10 worden opgeruimd.</summary>
    public static void AutoBackup(AppSettings settings, string appVersion, DateTime now)
    {
        Directory.CreateDirectory(AutoFolder);
        string file = Path.Combine(AutoFolder, $"auto-{now:yyyy-MM-dd}.json");
        if (!File.Exists(file)) File.WriteAllText(file, Create(settings, appVersion, now));
        foreach (var old in ListAutomatic().Skip(KeepAutomatic))
        {
            try { File.Delete(old.Path); } catch { /* volgende keer opnieuw */ }
        }
    }

    public static List<(string Path, DateTime Date)> ListAutomatic()
    {
        if (!Directory.Exists(AutoFolder)) return new();
        return Directory.GetFiles(AutoFolder, "auto-*.json")
            .Select(path => (Path: path, Date: DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path)[5..], "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : File.GetLastWriteTime(path)))
            .OrderByDescending(x => x.Date)
            .ToList();
    }

    /// <summary>
    /// Terugzetten gebeurt bij de volgende start van IdleDash, zodat niets van de huidige sessie er nog
    /// overheen kan schrijven.
    /// </summary>
    public static void ScheduleRestore(string settingsJson, string appVersion)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(PendingFile, settingsJson);
        // De huidige instellingen eerst veiligstellen, zodat terugzetten zelf ook ongedaan kan
        if (File.Exists(SettingsFile))
        {
            Directory.CreateDirectory(AutoFolder);
            File.WriteAllText(Path.Combine(AutoFolder, $"voor-terugzetten-{DateTime.Now:yyyy-MM-dd-HHmmss}.json"),
                Wrap(File.ReadAllText(SettingsFile), appVersion, DateTime.Now));
        }
    }

    /// <summary>Bij het opstarten: staat er een back-up klaar om terug te zetten, dan gebeurt dat nu.</summary>
    public static bool ApplyPendingRestore()
    {
        try
        {
            if (!File.Exists(PendingFile)) return false;
            File.Move(PendingFile, SettingsFile, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
