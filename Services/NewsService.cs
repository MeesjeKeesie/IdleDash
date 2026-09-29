using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IdleDash.Services;

public record NewsItem(string Title, string Link, DateTimeOffset? Published);

/// <summary>Nieuwskoppen uit de RSS-feeds van de NOS (feeds.nos.nl).</summary>
public static class NewsService
{
    public static readonly IReadOnlyList<(string Id, string Name)> Feeds = new List<(string, string)>
    {
        ("nosnieuwsalgemeen", "Algemeen"),
        ("nosnieuwsbinnenland", "Binnenland"),
        ("nosnieuwsbuitenland", "Buitenland"),
        ("nosnieuwspolitiek", "Politiek"),
        ("nosnieuwseconomie", "Economie"),
        ("nosnieuwstech", "Tech"),
        ("nosnieuwsopmerkelijk", "Opmerkelijk"),
        ("nossportalgemeen", "Sport"),
        ("nosvoetbal", "Voetbal"),
    };

    public static string FeedName(string id) =>
        Feeds.FirstOrDefault(f => f.Id == id).Name ?? "Algemeen";

    public static async Task<List<NewsItem>?> GetAsync(string feedId)
    {
        if (!Feeds.Any(f => f.Id == feedId)) feedId = "nosnieuwsalgemeen";
        try
        {
            return Parse(await Web.Client.GetStringAsync("https://feeds.nos.nl/" + feedId));
        }
        catch
        {
            return null;
        }
    }

    public static List<NewsItem> Parse(string xml)
    {
        return XDocument.Parse(xml)
            .Descendants("item")
            .Select(item => new NewsItem(
                ((string?)item.Element("title"))?.Trim() ?? "",
                ((string?)item.Element("link"))?.Trim() ?? "",
                ParseDate((string?)item.Element("pubDate"))))
            .Where(item => item.Title.Length > 0)
            .ToList();
    }

    /// <summary>"Mon, 28 Sep 2026 15:43:31 +0200" omzetten naar een datum.</summary>
    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // .NET begrijpt "+02:00" wel, maar "+0200" niet altijd
        text = Regex.Replace(text.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
