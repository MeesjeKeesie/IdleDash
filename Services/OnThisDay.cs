using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdleDash.Services;

public sealed record HistoryEvent(int Year, string Text);

/// <summary>
/// "Vandaag in de geschiedenis" van Wikipedia. Engels via de feed van Wikipedia; Nederlands via de datumpagina's
/// van de Nederlandse Wikipedia (bv. "8 oktober"), want de feed bestaat maar in een paar talen.
/// </summary>
public static class OnThisDay
{
    public static string UserAgent { get; set; } = "IdleDash (+https://github.com/MeesjeKeesie/IdleDash)";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string[] DutchMonths =
        { "januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december" };
    private static (string Key, List<HistoryEvent> Events)? _cache;

    public static async Task<List<HistoryEvent>> GetAsync(DateTime date, bool dutch)
    {
        string key = (dutch ? "nl" : "en") + date.ToString("MMdd");
        if (_cache is { } cached && cached.Key == key) return cached.Events;
        string url = dutch
            ? $"https://nl.wikipedia.org/api/rest_v1/page/html/{date.Day}_{DutchMonths[date.Month - 1]}"
            : $"https://en.wikipedia.org/api/rest_v1/feed/onthisday/events/{date.Month:00}/{date.Day:00}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.TryParseAdd(UserAgent);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        var events = dutch ? ParseDutchPage(body) : ParseFeed(body);
        _cache = (key, events);
        return events;
    }

    public static List<HistoryEvent> ParseFeed(string json)
    {
        var list = new List<HistoryEvent>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in events.EnumerateArray())
        {
            if (e.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && e.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out int year))
                list.Add(new HistoryEvent(year, Clean(t.GetString()!)));
        }
        return list;
    }

    /// <summary>De lijst onder het kopje "Gebeurtenissen" van een Nederlandse datumpagina: regels als "1573 – Alkmaar wordt ontzet."</summary>
    public static List<HistoryEvent> ParseDutchPage(string html)
    {
        var list = new List<HistoryEvent>();
        int start = html.IndexOf("id=\"Gebeurtenissen\"", StringComparison.Ordinal);
        if (start < 0) return list;
        int end = html.IndexOf("<h2", start + 20, StringComparison.Ordinal);
        string part = end > 0 ? html[start..end] : html[start..];
        foreach (Match item in Regex.Matches(part, @"<li\b[^>]*>(.*?)</li>", RegexOptions.Singleline))
        {
            string text = WebUtility.HtmlDecode(Regex.Replace(item.Groups[1].Value, "<[^>]+>", "")).Trim();
            var line = Regex.Match(text, @"^(\d{1,4})\s*[-–—]\s*(.+)$", RegexOptions.Singleline);
            if (!line.Success) continue;
            string sentence = Clean(line.Groups[2].Value);
            if (sentence.Length >= 8) list.Add(new HistoryEvent(int.Parse(line.Groups[1].Value), sentence));
        }
        return list;
    }

    /// <summary>De gebeurtenis van vandaag: elke dag een andere, de hele dag dezelfde. Korte teksten krijgen voorrang.</summary>
    public static HistoryEvent? Pick(List<HistoryEvent> events, DateTime date)
    {
        var shortOnes = events.Where(e => e.Text.Length <= 220).ToList();
        var pool = shortOnes.Count > 0 ? shortOnes : events;
        return pool.Count == 0 ? null : pool[(date.Year * 37 + date.DayOfYear) % pool.Count];
    }

    private static string Clean(string text)
    {
        text = Regex.Replace(text, @"\[\d+\]", "");          // bronverwijzingen zoals [1]
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
