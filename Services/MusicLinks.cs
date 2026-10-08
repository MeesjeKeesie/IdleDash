using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdleDash.Services;

/// <summary>Een playlist van een andere dienst (Apple Music, YouTube Music, Deezer…), als link op de widget.</summary>
public sealed class MusicLink
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string Service { get; set; } = "";
}

/// <summary>
/// Playlist-links van andere muziekdiensten. Die diensten laten zich op Windows niet van buitenaf bedienen,
/// dus IdleDash opent de playlist in de app of de browser. YouTube Music begint dan meestal meteen.
/// </summary>
public static class MusicLinks
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl,en;q=0.8");
        return client;
    }

    /// <summary>Een geplakte link netjes maken (https:// erbij als die ontbreekt). Null als het geen webadres is.</summary>
    public static string? Normalize(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.Length < 4) return null;
        if (!s.Contains("://")) s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.') ? uri.ToString() : null;
    }

    public static string ServiceName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        string host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return host switch
        {
            "music.apple.com" => "Apple Music",
            "music.youtube.com" => "YouTube Music",
            "youtube.com" or "m.youtube.com" or "youtu.be" => "YouTube",
            "open.spotify.com" => "Spotify",
            "deezer.com" or "link.deezer.com" => "Deezer",
            "tidal.com" or "listen.tidal.com" => "Tidal",
            "soundcloud.com" or "on.soundcloud.com" => "SoundCloud",
            _ => host,
        };
    }

    /// <summary>
    /// Het adres om te openen. Een playlist-link van YouTube Music wordt een afspeel-link,
    /// zodat de muziek meteen begint in plaats van alleen de lijst te tonen.
    /// </summary>
    public static string OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath == "/playlist" && QueryValue(uri, "list") is string list)
            return "https://music.youtube.com/watch?list=" + Uri.EscapeDataString(list);
        return url;
    }

    /// <summary>Voor YouTube en Spotify: het officiële oEmbed-adres met naam en plaatje. Voor de rest: null (dan de webpagina zelf).</summary>
    public static string? OEmbedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        string service = ServiceName(url);
        if (service is "YouTube Music" or "YouTube" && QueryValue(uri, "list") is string list)
            return "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/playlist?list=" + list);
        if (service == "Spotify")
            return "https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(url);
        return null;
    }

    public static (string? Title, string? Image) ParseOEmbed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (Str(root, "title"), Str(root, "thumbnail_url"));
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Naam en plaatje uit de og:title- en og:image-gegevens van een webpagina.</summary>
    public static (string? Title, string? Image) ParseMeta(string html)
    {
        string? Meta(string property)
        {
            foreach (Match tag in MetaTag().Matches(html))
            {
                string attributes = tag.Value;
                var name = Attribute(attributes, "property") ?? Attribute(attributes, "name");
                if (!string.Equals(name, property, StringComparison.OrdinalIgnoreCase)) continue;
                return Attribute(attributes, "content") is string content ? WebUtility.HtmlDecode(content).Trim() : null;
            }
            return null;
        }
        return (Clean(Meta("og:title")), Meta("og:image"));
    }

    /// <summary>Naam, dienst en hoesje van een geplakte link opzoeken. Lukt dat niet, dan wordt de dienst de naam.</summary>
    public static async Task<MusicLink?> LookupAsync(string text)
    {
        if (Normalize(text) is not string url) return null;
        var link = new MusicLink { Url = url, Service = ServiceName(url) };
        try
        {
            var (title, image) = OEmbedUrl(url) is string oembed
                ? ParseOEmbed(await Http.GetStringAsync(oembed))
                : ParseMeta(await Http.GetStringAsync(url));
            link.Name = Clean(title) ?? link.Service;
            link.ImageUrl = image;
        }
        catch
        {
            link.Name = link.Service;
        }
        return link;
    }

    public static void Open(MusicLink link) =>
        Process.Start(new ProcessStartInfo(OpenUrl(link.Url)) { UseShellExecute = true });

    /// <summary>Overbodige toevoegingen uit een titel halen, zoals "op Apple Music" of " - YouTube".</summary>
    private static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        string t = title.Replace("\u200E", "").Trim();
        foreach (string suffix in new[] { " on Apple Music", " op Apple Music", " - Apple Music", " – Apple Music", " - YouTube Music", " - YouTube", " | Deezer", " - Deezer", " on TIDAL", " | Listen on SoundCloud" })
            if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) t = t[..^suffix.Length].Trim();
        return t.Length > 0 ? t : null;
    }

    private static string? QueryValue(Uri uri, string key)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == key) return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{name}\s*=\s*(""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static readonly Regex MetaTagPattern = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex MetaTag() => MetaTagPattern;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
