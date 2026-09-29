using System.Net.Http;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

public record UpdateInfo(string Version, string Url);

/// <summary>Kijkt op GitHub of er een nieuwere versie van IdleDash is. Stuurt geen persoonlijke gegevens mee.</summary>
public static class UpdateService
{
    /// <summary>Succeeded = GitHub kon bereikt worden. Update = null als je al de nieuwste versie hebt.</summary>
    public static async Task<(bool Succeeded, UpdateInfo? Update)> CheckAsync()
    {
        if (AppInfo.IsDevBuild) return (false, null);   // zelf gebouwd: niet zeuren over updates
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Web.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (false, null);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var update = Compare(
                doc.RootElement.GetProperty("tag_name").GetString(),
                doc.RootElement.GetProperty("html_url").GetString(),
                AppInfo.Version);
            return (true, update);
        }
        catch
        {
            return (false, null);   // geen internet of GitHub onbereikbaar: volgende keer
        }
    }

    /// <summary>Geeft de update terug als de tag (bv. "v1.2.0") nieuwer is dan de huidige versie.</summary>
    public static UpdateInfo? Compare(string? tag, string? url, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url)) return null;
        string text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var latest)) return null;

        latest = AppInfo.Normalize(latest);
        return latest > AppInfo.Normalize(current)
            ? new UpdateInfo($"{latest.Major}.{latest.Minor}.{latest.Build}", url)
            : null;
    }
}
