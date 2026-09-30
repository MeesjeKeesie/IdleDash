using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>
/// CalDAV: de manier waarop Apple iCloud (en bv. Nextcloud) agenda's deelt.
/// Voor iCloud log je in met je Apple ID en een app-specifiek wachtwoord (niet je gewone wachtwoord).
/// </summary>
public static class CalDav
{
    public const string ICloudServer = "https://caldav.icloud.com/";

    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalNs = "urn:ietf:params:xml:ns:caldav";
    private static readonly XNamespace AppleNs = "http://apple.com/ns/ical/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Zelf doorverwijzingen volgen: anders valt de login weg als iCloud naar een andere server verwijst
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdleDash/1.0");
        return client;
    }

    // ─────────────────────────── Agenda's vinden ───────────────────────────

    public static async Task<List<RemoteCalendar>> DiscoverAsync(string server, string user, string password)
    {
        var root = new Uri(server);
        string principalBody = "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:current-user-principal/></d:prop></d:propfind>";
        var (_, principalXml, rootUri) = await SendAsync("PROPFIND", root, principalBody, "0", user, password);
        string principalHref = ParseHref(principalXml, "current-user-principal") ?? throw new InvalidOperationException(Loc.T("Agenda-server gaf geen account terug."));

        var principal = new Uri(rootUri, principalHref);
        string homeBody = "<d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><c:calendar-home-set/></d:prop></d:propfind>";
        var (_, homeXml, principalUri) = await SendAsync("PROPFIND", principal, homeBody, "0", user, password);
        string homeHref = ParseHref(homeXml, "calendar-home-set") ?? throw new InvalidOperationException(Loc.T("Agenda-server gaf geen agenda's terug."));

        var home = new Uri(principalUri, homeHref);
        string listBody = "<d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\" xmlns:a=\"http://apple.com/ns/ical/\">"
                          + "<d:prop><d:displayname/><d:resourcetype/><a:calendar-color/><c:supported-calendar-component-set/></d:prop></d:propfind>";
        var (_, listXml, homeUri) = await SendAsync("PROPFIND", home, listBody, "1", user, password);
        return ParseCalendars(listXml, homeUri);
    }

    /// <summary>Leest de href binnen een eigenschap, bv. current-user-principal of calendar-home-set.</summary>
    public static string? ParseHref(string xml, string property)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == property)?
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value.Trim();
    }

    public static List<RemoteCalendar> ParseCalendars(string xml, Uri baseUri)
    {
        var result = new List<RemoteCalendar>();
        foreach (var response in XDocument.Parse(xml).Descendants(Dav + "response"))
        {
            string? href = response.Element(Dav + "href")?.Value.Trim();
            var prop = response.Descendants(Dav + "prop").FirstOrDefault();
            if (href == null || prop == null) continue;

            bool isCalendar = prop.Element(Dav + "resourcetype")?.Element(CalNs + "calendar") != null;
            if (!isCalendar) continue;

            // Alleen agenda's met afspraken (niet de herinneringenlijsten)
            var components = prop.Element(CalNs + "supported-calendar-component-set");
            if (components != null && components.Elements().Any()
                && !components.Elements().Any(c => (string?)c.Attribute("name") == "VEVENT"))
                continue;

            string name = prop.Element(Dav + "displayname")?.Value.Trim() ?? "";
            string? color = ThemeColors.FromRgba(prop.Element(AppleNs + "calendar-color")?.Value);
            result.Add(new RemoteCalendar
            {
                Url = new Uri(baseUri, href).ToString(),
                Name = name.Length > 0 ? name : Loc.T("Agenda"),
                Color = color,
            });
        }
        return result;
    }

    // ─────────────────────────── Afspraken lezen en toevoegen ───────────────────────────

    /// <summary>Alle afspraken (als .ics-tekst) in een periode.</summary>
    public static async Task<List<string>> QueryAsync(string calendarUrl, string user, string password, DateTime fromUtc, DateTime toUtc)
    {
        string Stamp(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string body =
            "<c:calendar-query xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\">"
            + "<d:prop><c:calendar-data/></d:prop>"
            + "<c:filter><c:comp-filter name=\"VCALENDAR\"><c:comp-filter name=\"VEVENT\">"
            + $"<c:time-range start=\"{Stamp(fromUtc)}\" end=\"{Stamp(toUtc)}\"/>"
            + "</c:comp-filter></c:comp-filter></c:filter></c:calendar-query>";
        var (_, xml, _) = await SendAsync("REPORT", new Uri(calendarUrl), body, "1", user, password);
        return ParseCalendarData(xml);
    }

    public static List<string> ParseCalendarData(string xml) =>
        XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "calendar-data").Select(e => e.Value).ToList();

    public static async Task PutAsync(string calendarUrl, string user, string password, string uid, string ics)
    {
        var uri = new Uri(calendarUrl.TrimEnd('/') + "/" + uid + ".ics");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new StringContent(ics, Encoding.UTF8, "text/calendar") };
        request.Headers.Authorization = Basic(user, password);
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await Http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
    }

    // ─────────────────────────── Verzenden ───────────────────────────

    private static async Task<(HttpStatusCode Status, string Body, Uri FinalUri)> SendAsync(
        string method, Uri uri, string body, string depth, string user, string password)
    {
        for (int redirects = 0; redirects < 5; redirects++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            };
            request.Headers.Authorization = Basic(user, password);
            request.Headers.TryAddWithoutValidation("Depth", depth);
            using var response = await Http.SendAsync(request);

            if ((int)response.StatusCode is 301 or 302 or 307 or 308 && response.Headers.Location != null)
            {
                uri = new Uri(uri, response.Headers.Location);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
            response.EnsureSuccessStatusCode();
            return (response.StatusCode, await response.Content.ReadAsStringAsync(), uri);
        }
        throw new InvalidOperationException(Loc.T("Te veel doorverwijzingen van de agenda-server."));
    }

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
}
