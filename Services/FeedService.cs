using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IdleDash.Services;

public record FeedItem(string Title, string Link, DateTimeOffset? Published, string? Summary, string? ImageUrl, string Source);

/// <summary>Kant-en-klare nieuwsbron in de keuzelijst.</summary>
public record FeedPreset(string Group, string Name, string Url, string Language);

/// <summary>Een gekozen bron in een nieuws-widget (wordt bewaard in de widget-instellingen).</summary>
public class FeedSource
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Nieuws en RSS: leest RSS 2.0, RSS 1.0 en Atom, en vindt de feed van een website.</summary>
public static class FeedService
{
    public static IReadOnlyList<FeedPreset> Presets { get; } = new List<FeedPreset>
    {
        new("NOS", "NOS Algemeen", "https://feeds.nos.nl/nosnieuwsalgemeen", "nl"),
        new("NOS", "NOS Binnenland", "https://feeds.nos.nl/nosnieuwsbinnenland", "nl"),
        new("NOS", "NOS Buitenland", "https://feeds.nos.nl/nosnieuwsbuitenland", "nl"),
        new("NOS", "NOS Politiek", "https://feeds.nos.nl/nosnieuwspolitiek", "nl"),
        new("NOS", "NOS Economie", "https://feeds.nos.nl/nosnieuwseconomie", "nl"),
        new("NOS", "NOS Tech", "https://feeds.nos.nl/nosnieuwstech", "nl"),
        new("NOS", "NOS Opmerkelijk", "https://feeds.nos.nl/nosnieuwsopmerkelijk", "nl"),
        new("NOS", "NOS Sport", "https://feeds.nos.nl/nossportalgemeen", "nl"),
        new("NOS", "NOS Voetbal", "https://feeds.nos.nl/nosvoetbal", "nl"),
        new("BBC", "BBC News", "https://feeds.bbci.co.uk/news/rss.xml", "en"),
        new("BBC", "BBC World", "https://feeds.bbci.co.uk/news/world/rss.xml", "en"),
        new("BBC", "BBC UK", "https://feeds.bbci.co.uk/news/uk/rss.xml", "en"),
        new("BBC", "BBC Business", "https://feeds.bbci.co.uk/news/business/rss.xml", "en"),
        new("BBC", "BBC Technology", "https://feeds.bbci.co.uk/news/technology/rss.xml", "en"),
        new("BBC", "BBC Science", "https://feeds.bbci.co.uk/news/science_and_environment/rss.xml", "en"),
        new("BBC", "BBC Entertainment", "https://feeds.bbci.co.uk/news/entertainment_and_arts/rss.xml", "en"),
        new("BBC", "BBC Sport", "https://feeds.bbci.co.uk/sport/rss.xml", "en"),
        new("Meer", "NU.nl", "https://www.nu.nl/rss/Algemeen", "nl"),
        new("Meer", "The Guardian World", "https://www.theguardian.com/world/rss", "en"),
        new("Meer", "The Verge", "https://www.theverge.com/rss/index.xml", "en"),
        new("Meer", "Hacker News", "https://news.ycombinator.com/rss", "en"),
    };

    /// <summary>Oude NOS-instelling (uit 1.0/1.1) omzetten naar een bron.</summary>
    public static FeedSource FromLegacyNos(string feedId)
    {
        var preset = Presets.FirstOrDefault(p => p.Url.EndsWith("/" + feedId, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
        return new FeedSource { Url = preset.Url, Name = preset.Name };
    }

    // ─────────────────────────── Ophalen ───────────────────────────

    private static readonly Dictionary<string, (DateTime Time, List<FeedItem> Items)> Cache = new();

    /// <summary>Koppen van één feed. Bij een fout: de vorige koppen, of null.</summary>
    public static async Task<List<FeedItem>?> GetAsync(FeedSource source, TimeSpan maxAge)
    {
        if (Cache.TryGetValue(source.Url, out var cached) && DateTime.Now - cached.Time < maxAge) return cached.Items;
        try
        {
            var items = Parse(await Web.Client.GetStringAsync(source.Url), source.Name);
            Cache[source.Url] = (DateTime.Now, items);
            return items;
        }
        catch
        {
            return cached.Items;
        }
    }

    public static List<FeedItem> Parse(string xml, string source)
    {
        var doc = XDocument.Parse(xml);
        var items = new List<FeedItem>();
        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string title = Clean(Child(element, "title"));
            if (title.Length == 0) continue;
            var published = ParseDate(Child(element, "pubDate") ?? Child(element, "published")
                                      ?? Child(element, "updated") ?? Child(element, "date"));
            string? html = Child(element, "description") ?? Child(element, "summary") ?? Child(element, "content");
            items.Add(new FeedItem(title, LinkOf(element), published, Summary(html), ImageOf(element, html), source));
        }
        return items;
    }

    private static string? Child(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string LinkOf(XElement element)
    {
        foreach (var link in element.Elements().Where(e => e.Name.LocalName == "link"))
        {
            string? href = (string?)link.Attribute("href");
            string rel = (string?)link.Attribute("rel") ?? "alternate";
            if (href != null && rel == "alternate") return href.Trim();
            if (href == null && !string.IsNullOrWhiteSpace(link.Value)) return link.Value.Trim();
        }
        string? guid = Child(element, "guid") ?? Child(element, "id");
        return guid != null && guid.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? guid.Trim() : "";
    }

    private static string? ImageOf(XElement element, string? html)
    {
        foreach (var node in element.Descendants())
        {
            string? url = (string?)node.Attribute("url");
            if (url == null || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            string name = node.Name.LocalName;
            string type = (string?)node.Attribute("type") ?? "";
            string medium = (string?)node.Attribute("medium") ?? "";
            if (name == "thumbnail") return url;
            if (name is "content" or "enclosure" && (type.StartsWith("image/") || medium == "image")) return url;
        }
        if (html == null) return null;
        var match = Regex.Match(html, "<img[^>]+src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        string? src = match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
        return src != null && src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : null;
    }

    private static string? Summary(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        string text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return null;
        return text.Length > 220 ? text[..220].TrimEnd() + "…" : text;
    }

    private static string Clean(string? text) =>
        Regex.Replace(WebUtility.HtmlDecode(text ?? ""), @"\s+", " ").Trim();

    /// <summary>"Mon, 28 Sep 2026 15:43:31 +0200" of "2026-09-28T15:43:31Z" omzetten naar een datum.</summary>
    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = Regex.Replace(text.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    // ─────────────────────────── Feed van een website vinden ───────────────────────────

    /// <summary>
    /// Accepteert een feed-adres of gewoon een website ("tweakers.net"). Geeft het feed-adres en de naam terug,
    /// of null als er geen feed gevonden is.
    /// </summary>
    public static async Task<(string Url, string Title)?> DiscoverAsync(string input)
    {
        string url = input.Trim();
        if (url.StartsWith("feed://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[7..];
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        // Reddit: /r/naam heeft een vaste feed
        if (uri.Host.EndsWith("reddit.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/r/", StringComparison.OrdinalIgnoreCase) && !uri.AbsolutePath.EndsWith(".rss"))
            uri = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/.rss");

        try
        {
            string text = await Web.Client.GetStringAsync(uri);
            if (TryFeedTitle(text, out string? title)) return (uri.ToString(), title ?? uri.Host);

            string? feed = FindFeedLink(text, uri);
            if (feed == null) return null;
            string feedText = await Web.Client.GetStringAsync(feed);
            return TryFeedTitle(feedText, out title) ? (feed, title ?? uri.Host) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Is deze tekst een feed? Zo ja, ook de naam ervan.</summary>
    public static bool TryFeedTitle(string text, out string? title)
    {
        title = null;
        try
        {
            var doc = XDocument.Parse(text);
            if (doc.Root == null || doc.Root.Name.LocalName is not ("rss" or "feed" or "RDF")) return false;
            var element = doc.Root.Descendants().FirstOrDefault(e => e.Name.LocalName == "title");
            title = element != null ? Clean(element.Value) : null;
            if (string.IsNullOrWhiteSpace(title)) title = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Zoekt in een webpagina naar &lt;link rel="alternate" type="application/rss+xml" href="…"&gt;.</summary>
    public static string? FindFeedLink(string html, Uri baseUri)
    {
        foreach (Match match in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
        {
            string tag = match.Value;
            string? rel = Attribute(tag, "rel"), type = Attribute(tag, "type"), href = Attribute(tag, "href");
            if (href == null || rel == null || !rel.Contains("alternate", StringComparison.OrdinalIgnoreCase)) continue;
            if (type == null || !(type.Contains("rss", StringComparison.OrdinalIgnoreCase) || type.Contains("atom", StringComparison.OrdinalIgnoreCase)))
                continue;
            return Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var absolute) ? absolute.ToString() : null;
        }
        return null;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, @"\b" + name + @"\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
