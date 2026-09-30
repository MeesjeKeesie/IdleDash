using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>
/// Een nieuwere release op GitHub. SetupUrl en ChecksumUrl zijn er alleen als de release
/// een installer met bijbehorend .sha256-bestand heeft (vanaf versie 1.1.0).
/// </summary>
public record UpdateInfo(string Version, string PageUrl, string? SetupName, string? SetupUrl, string? ChecksumUrl);

/// <summary>
/// Zoeken, downloaden en controleren van nieuwe versies via GitHub. Stuurt geen persoonlijke gegevens mee.
/// </summary>
public static class UpdateService
{
    private const long MaxDownloadSize = 500L * 1024 * 1024;

    private static readonly HttpClient Downloads = CreateDownloadClient();

    /// <summary>Hier worden nieuwe installers neergezet tot je ze installeert.</summary>
    public static string DownloadFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleDash", "updates");

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdleDash/1.0");
        return client;
    }

    // ─────────────────────────── Zoeken ───────────────────────────

    /// <summary>Succeeded = GitHub kon bereikt worden. Update = null als je al de nieuwste versie hebt.</summary>
    public static async Task<(bool Succeeded, UpdateInfo? Update)> CheckAsync()
    {
        if (AppInfo.IsDevBuild) return (false, null);   // zelf gebouwd: niet zoeken
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Web.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (false, null);
            return (true, Parse(await response.Content.ReadAsStringAsync(), AppInfo.Version));
        }
        catch
        {
            return (false, null);   // geen internet of GitHub onbereikbaar: volgende keer
        }
    }

    /// <summary>Leest het antwoord van GitHub. Geeft alleen iets terug als de release nieuwer is.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var update = Compare(Text(root, "tag_name"), Text(root, "html_url"), current);
        if (update == null) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return update;

        // De installer zoeken, en daarna het .sha256-bestand met dezelfde naam
        string? setupName = null, setupUrl = null, checksumUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = Text(asset, "name") ?? "";
            string? url = Text(asset, "browser_download_url");
            if (IsGitHubUrl(url)
                && name.StartsWith("IdleDash-Setup-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                setupName = name;
                setupUrl = url;
            }
        }
        if (setupName != null)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? url = Text(asset, "browser_download_url");
                if (IsGitHubUrl(url) && string.Equals(Text(asset, "name"), setupName + ".sha256", StringComparison.OrdinalIgnoreCase))
                    checksumUrl = url;
            }
        }
        return update with { SetupName = setupName, SetupUrl = setupUrl, ChecksumUrl = checksumUrl };
    }

    /// <summary>Geeft een update terug als de tag (bv. "v1.2.0") nieuwer is dan de huidige versie.</summary>
    public static UpdateInfo? Compare(string? tag, string? url, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return null;
        if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var latest)) return null;

        latest = AppInfo.Normalize(latest);
        return latest > AppInfo.Normalize(current)
            ? new UpdateInfo($"{latest.Major}.{latest.Minor}.{latest.Build}", url, null, null, null)
            : null;
    }

    // ─────────────────────────── Downloaden en controleren ───────────────────────────

    /// <summary>
    /// Downloadt de installer en controleert hem met de SHA-256-code uit de release.
    /// Geeft het pad en de code terug, of null als er iets mis is (dan wordt er niets bewaard).
    /// </summary>
    public static async Task<(string Path, string Hash)?> DownloadAsync(UpdateInfo update, CancellationToken cancel = default)
    {
        if (update.SetupName == null || update.SetupUrl == null || update.ChecksumUrl == null) return null;
        try
        {
            string? expected = ParseChecksum(await Downloads.GetStringAsync(update.ChecksumUrl, cancel));
            if (expected == null) return null;

            Directory.CreateDirectory(DownloadFolder);
            string target = Path.Combine(DownloadFolder, Path.GetFileName(update.SetupName));

            // Al eerder (helemaal) gedownload? Dan niet opnieuw
            if (File.Exists(target) && HashOf(target) == expected) return (target, expected);

            string partial = target + ".part";
            using (var response = await Downloads.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxDownloadSize) return null;

                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var file = File.Create(partial);
                await source.CopyToAsync(file, cancel);
            }

            if (HashOf(partial) != expected)
            {
                File.Delete(partial);   // beschadigd of niet het goede bestand
                return null;
            }
            File.Move(partial, target, overwrite: true);
            return (target, expected);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>SHA-256 van een bestand, als 64 kleine letters en cijfers.</summary>
    public static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Leest "abc123…  IdleDash-Setup-1.2.0.exe" en geeft alleen de code terug.</summary>
    public static string? ParseChecksum(string? text)
    {
        string first = (text ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return Regex.IsMatch(first, "^[0-9a-fA-F]{64}$") ? first.ToLowerInvariant() : null;
    }

    /// <summary>Versie uit een bestandsnaam als "IdleDash-Setup-1.2.0.exe", of null.</summary>
    public static Version? VersionFromFileName(string fileName)
    {
        var match = Regex.Match(fileName, @"^IdleDash-Setup-(\d+\.\d+\.\d+)\.exe$", RegexOptions.IgnoreCase);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    // ─────────────────────────── Installeren ───────────────────────────

    /// <summary>
    /// Start de installer onzichtbaar, een paar seconden nadat IdleDash is afgesloten.
    /// De installer zet IdleDash daarna zelf weer aan. "!autostart" zorgt dat je eigen
    /// keuze voor "Starten met Windows" niet wordt overschreven.
    /// </summary>
    public static bool StartSilentInstall(string setupPath)
    {
        try
        {
            string arguments =
                $"/c ping 127.0.0.1 -n 3 > nul & start \"\" \"{setupPath}\" " +
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /MERGETASKS=\"!autostart\"";
            Process.Start(new ProcessStartInfo("cmd.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGitHubUrl(string? url) =>
        url != null && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
