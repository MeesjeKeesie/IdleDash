using System.Net;
using System.Net.Http;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

public sealed record StockQuote(string Symbol, string? Name, double Price, double? PreviousClose, string? Currency,
    DateTimeOffset? Time, List<double> Points)
{
    public double? ChangePercent => PreviousClose is > 0 ? (Price - PreviousClose.Value) / PreviousClose.Value * 100 : null;
}

public sealed record StockSearchResult(string Symbol, string Name, string? Exchange, string? Type);

/// <summary>Een aandeel op de widget, zoals "ASML.AS".</summary>
public sealed class StockSymbol
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Koersen via de grafiek-API van Yahoo Finance: geen sleutel nodig, wereldwijd (ook AEX en crypto),
/// meestal ongeveer 15 minuten vertraagd. Het is geen officiële API, dus IdleDash vraagt zuinig op.
/// </summary>
public static class StockService
{
    public const string SourceName = "Yahoo Finance";

    private static readonly HttpClient Client = Create();
    private static readonly Dictionary<string, (DateTime Time, StockQuote? Quote)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _pausedUntil = DateTime.MinValue;

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>Koers ophalen (uit de cache als die jonger is dan maxAge). Null als het niet lukt.</summary>
    public static async Task<StockQuote?> GetQuoteAsync(string symbol, TimeSpan maxAge)
    {
        symbol = symbol.Trim();
        if (symbol.Length == 0) return null;
        Cache.TryGetValue(symbol, out var cached);
        if (cached.Quote != null && DateTime.Now - cached.Time < maxAge) return cached.Quote;
        if (DateTime.Now < _pausedUntil) return cached.Quote;   // Yahoo vroeg om rustiger aan te doen

        try
        {
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m";
            using var response = await Client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _pausedUntil = DateTime.Now.AddMinutes(15);
                return cached.Quote;
            }
            var quote = ParseChart(await response.Content.ReadAsStringAsync());
            if (quote == null) return cached.Quote;
            Cache[symbol] = (DateTime.Now, quote);
            return quote;
        }
        catch
        {
            return cached.Quote;
        }
    }

    public static StockQuote? ParseChart(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("chart", out var chart)
                || !chart.TryGetProperty("result", out var results)
                || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;
            var result = results[0];
            var meta = result.GetProperty("meta");
            if (Num(meta, "regularMarketPrice") is not double price) return null;

            var points = new List<double>();
            if (result.TryGetProperty("indicators", out var indicators)
                && indicators.TryGetProperty("quote", out var quotes) && quotes.ValueKind == JsonValueKind.Array && quotes.GetArrayLength() > 0
                && quotes[0].TryGetProperty("close", out var closes) && closes.ValueKind == JsonValueKind.Array)
            {
                foreach (var close in closes.EnumerateArray())
                    if (close.ValueKind == JsonValueKind.Number) points.Add(close.GetDouble());
            }

            return new StockQuote(
                Str(meta, "symbol") ?? "",
                Str(meta, "shortName") ?? Str(meta, "longName"),
                price,
                Num(meta, "previousClose") ?? Num(meta, "chartPreviousClose"),   // bij range=1d is dat de slotkoers van gisteren
                Str(meta, "currency"),
                Num(meta, "regularMarketTime") is double time ? DateTimeOffset.FromUnixTimeSeconds((long)time) : null,
                points);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Zoeken op naam of code, bv. "asml" of "apple".</summary>
    public static async Task<List<StockSearchResult>> SearchAsync(string query)
    {
        try
        {
            string url = $"https://query1.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query.Trim())}&quotesCount=8&newsCount=0";
            return ParseSearch(await Client.GetStringAsync(url));
        }
        catch
        {
            return new List<StockSearchResult>();
        }
    }

    public static List<StockSearchResult> ParseSearch(string json)
    {
        var list = new List<StockSearchResult>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array) return list;
            foreach (var q in quotes.EnumerateArray())
            {
                string? symbol = Str(q, "symbol");
                if (string.IsNullOrEmpty(symbol)) continue;
                list.Add(new StockSearchResult(symbol, Str(q, "longname") ?? Str(q, "shortname") ?? symbol,
                    Str(q, "exchDisp") ?? Str(q, "exchange"), Str(q, "typeDisp") ?? Str(q, "quoteType")));
            }
        }
        catch
        {
            // ongeldige reactie: geen resultaten
        }
        return list;
    }

    /// <summary>Koers met valuta, bv. "€ 612,40" of "$189.25". Londen noteert in pence (GBp).</summary>
    public static string FormatPrice(double price, string? currency)
    {
        if (currency == "GBp")
        {
            price /= 100;
            currency = "GBP";
        }
        string number = price.ToString(price >= 1000 ? "N0" : price >= 1 ? "N2" : "N4", Loc.Culture);
        string? sign = currency switch { "EUR" => "€", "USD" => "$", "GBP" => "£", "JPY" => "¥", _ => null };
        if (sign == null) return currency == null ? number : $"{number} {currency}";
        return Loc.IsEnglish ? sign + number : $"{sign} {number}";
    }

    public static string FormatChange(double percent) =>
        (percent > 0 ? "+" : percent < 0 ? "−" : "") + Math.Abs(percent).ToString("0.00", Loc.Culture) + "%";

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
