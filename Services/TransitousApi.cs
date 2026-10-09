using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace IdleDash.Services;

public sealed record TransitStop(string Id, string Name, double Lat, double Lon, string? Area, string? Country);

public sealed record TransitStation(string Name, double Lat, double Lon, double Importance);

public sealed record Departure(DateTimeOffset Planned, DateTimeOffset? Expected, string Line, string Destination, string? Track,
    bool TrackChanged, bool Cancelled, string Mode, string? Color, bool RealTime)
{
    public int DelayMinutes => Expected is { } expected ? (int)Math.Round((expected - Planned).TotalMinutes) : 0;
}

/// <summary>Een trein op weg van het ene naar het volgende station, met de lijn waarover hij rijdt.</summary>
public sealed record TrainSegment(string Name, string Mode, string? Color, double[] Lats, double[] Lons, double[] Distances,
    DateTimeOffset Departure, DateTimeOffset Arrival, TransitStation From, TransitStation To);

/// <summary>
/// Transitous: open, internationaal reisinformatieplatform (draait op MOTIS). Gratis voor opensource-apps, mits elke
/// aanvraag een herkenbare naam met contactgegevens heeft en de bron zichtbaar blijft. IdleDash vraagt zuinig op.
/// </summary>
public static class TransitousApi
{
    public const string Source = "Transitous";
    public static string UserAgent { get; set; } = "IdleDash (+https://github.com/MeesjeKeesie/IdleDash)";

    private const string Base = "https://api.transitous.org";
    private static readonly string[] Versions = { "v6", "v5", "v4" };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static string _version = "v6";
    private static bool? _cornersAsDocumented;

    public static readonly HashSet<string> TrainModes = new()
    {
        "RAIL", "HIGHSPEED_RAIL", "LONG_DISTANCE", "NIGHT_RAIL", "REGIONAL_FAST_RAIL", "REGIONAL_RAIL", "SUBURBAN",
    };

    /// <summary>Haalt een adres op. {v} wordt de API-versie; draait de server een oudere versie, dan wordt die geprobeerd.</summary>
    private static async Task<string> GetAsync(string pathAndQuery)
    {
        var versions = pathAndQuery.Contains("{v}") ? new[] { _version }.Concat(Versions.Where(v => v != _version)) : new[] { "" };
        foreach (string version in versions)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Base + pathAndQuery.Replace("{v}", version));
            request.Headers.UserAgent.TryParseAdd(UserAgent);
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound && version.Length > 0) continue;
            response.EnsureSuccessStatusCode();
            if (version.Length > 0) _version = version;
            return await response.Content.ReadAsStringAsync();
        }
        throw new HttpRequestException("Transitous: geen werkende API-versie gevonden.");
    }

    // ─────────────────────────── Zoeken ───────────────────────────

    /// <summary>Stations zoeken (stopsOnly) of ook plaatsen, bijvoorbeeld voor het midden van de kaart.</summary>
    public static async Task<List<TransitStop>> SearchAsync(string text, bool stopsOnly, string language) =>
        ParseMatches(await GetAsync($"/api/v1/geocode?text={Uri.EscapeDataString(text.Trim())}&language={language}&numResults=8"
            + (stopsOnly ? "&type=STOP" : "")));

    public static List<TransitStop> ParseMatches(string json)
    {
        var list = new List<TransitStop>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            if (Str(m, "id") is not string id || Str(m, "name") is not string name || Num(m, "lat") is not double lat || Num(m, "lon") is not double lon) continue;
            string? area = null;
            if (m.TryGetProperty("areas", out var areas) && areas.ValueKind == JsonValueKind.Array)
            {
                var all = areas.EnumerateArray().ToList();
                area = all.Where(a => a.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.True).Select(a => Str(a, "name")).FirstOrDefault()
                       ?? all.Select(a => Str(a, "name")).LastOrDefault(n => n != null && n != name);
            }
            list.Add(new TransitStop(id, name, lat, lon, area, Str(m, "country")));
        }
        return list;
    }

    // ─────────────────────────── Vertrektijden ───────────────────────────

    public static async Task<List<Departure>> GetDeparturesAsync(string stopId, int count, string language) =>
        ParseDepartures(await GetAsync($"/api/{{v}}/stoptimes?stopId={Uri.EscapeDataString(stopId)}&n={count}&language={language}"));

    public static List<Departure> ParseDepartures(string json)
    {
        var list = new List<Departure>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("stopTimes", out var times) || times.ValueKind != JsonValueKind.Array) return list;
        foreach (var t in times.EnumerateArray())
        {
            if (!t.TryGetProperty("place", out var place)) continue;
            if (Time(place, "scheduledDeparture") is not DateTimeOffset planned) continue;   // alleen vertrekken, geen aankomst op het eindpunt
            string line = First(Str(t, "displayName"), Str(t, "routeShortName"), Str(t, "tripShortName")) ?? "";
            string destination = First(Str(t, "headsign"), t.TryGetProperty("tripTo", out var to) ? Str(to, "name") : null) ?? "";
            string? scheduledTrack = Str(place, "scheduledTrack");
            string? track = First(Str(place, "track"), scheduledTrack);
            bool changed = Str(place, "track") is string actual && scheduledTrack != null && actual != scheduledTrack;
            bool cancelled = Bool(t, "cancelled") || Bool(t, "tripCancelled") || Bool(place, "cancelled");
            list.Add(new Departure(planned, Time(place, "departure"), line, destination, track, changed, cancelled,
                Str(t, "mode") ?? "", ColorOf(Str(t, "routeColor")), Bool(t, "realTime")));
        }
        return list;
    }

    // ─────────────────────────── Treinen op de kaart ───────────────────────────

    /// <summary>Alle treinen die de komende minuten in dit gebied rijden (posities zijn geschat uit dienstregeling en vertragingen).</summary>
    public static async Task<List<TrainSegment>> GetTrainsAsync(double south, double west, double north, double east, int zoom, DateTimeOffset now)
    {
        // De documentatie noemt min "rechtsonder" en max "linksboven". Levert dat niets op, dan de gebruikelijke volgorde.
        foreach (bool documented in _cornersAsDocumented is bool known ? new[] { known } : new[] { true, false })
        {
            string min = documented ? Point(south, east) : Point(south, west);
            string max = documented ? Point(north, west) : Point(north, east);
            string json = await GetAsync($"/api/{{v}}/map/trips?zoom={zoom}&min={min}&max={max}&startTime={Stamp(now)}&endTime={Stamp(now.AddMinutes(3))}&precision=5");
            var segments = ParseSegments(json);
            if (segments.Count == 0 && _cornersAsDocumented == null) continue;
            if (segments.Count > 0) _cornersAsDocumented = documented;
            return segments.Where(s => TrainModes.Contains(s.Mode)).ToList();
        }
        return new List<TrainSegment>();
    }

    public static List<TrainSegment> ParseSegments(string json)
    {
        var list = new List<TrainSegment>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            if (Time(s, "departure") is not DateTimeOffset departure || Time(s, "arrival") is not DateTimeOffset arrival
                || Str(s, "polyline") is not string polyline) continue;
            var (lats, lons) = DecodePolyline(polyline);
            if (lats.Length < 2) continue;
            string name = "";
            if (s.TryGetProperty("trips", out var trips) && trips.ValueKind == JsonValueKind.Array && trips.GetArrayLength() > 0)
                name = First(Str(trips[0], "displayName"), Str(trips[0], "routeShortName")) ?? "";
            list.Add(new TrainSegment(name, Str(s, "mode") ?? "", ColorOf(Str(s, "routeColor")), lats, lons, Cumulative(lats, lons),
                departure, arrival, Station(s, "from"), Station(s, "to")));
        }
        return list;
    }

    /// <summary>Google-polyline uitpakken (standaard 5 decimalen).</summary>
    public static (double[] Lats, double[] Lons) DecodePolyline(string encoded, int precision = 5)
    {
        var lats = new List<double>();
        var lons = new List<double>();
        double factor = Math.Pow(10, precision);
        int index = 0, lat = 0, lon = 0;
        while (index < encoded.Length)
        {
            lat += Next();
            if (index >= encoded.Length) break;
            lon += Next();
            lats.Add(lat / factor);
            lons.Add(lon / factor);
        }
        return (lats.ToArray(), lons.ToArray());

        int Next()
        {
            int result = 0, shift = 0, b;
            do
            {
                if (index >= encoded.Length) return 0;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            }
            while (b >= 0x20);
            return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
        }
    }

    /// <summary>Waar de trein nu is, als hij op dit stuk rijdt (gelijkmatig verdeeld over de afstand). Anders null.</summary>
    public static (double Lat, double Lon)? PositionAt(TrainSegment s, DateTimeOffset time)
    {
        if (time < s.Departure || time > s.Arrival) return null;
        double total = (s.Arrival - s.Departure).TotalSeconds;
        double target = (total <= 0 ? 1 : (time - s.Departure).TotalSeconds / total) * s.Distances[^1];
        for (int i = 1; i < s.Distances.Length; i++)
        {
            if (s.Distances[i] < target) continue;
            double part = s.Distances[i] - s.Distances[i - 1];
            double f = part <= 0 ? 0 : (target - s.Distances[i - 1]) / part;
            return (s.Lats[i - 1] + (s.Lats[i] - s.Lats[i - 1]) * f, s.Lons[i - 1] + (s.Lons[i] - s.Lons[i - 1]) * f);
        }
        return (s.Lats[^1], s.Lons[^1]);
    }

    private static double[] Cumulative(double[] lats, double[] lons)
    {
        var d = new double[lats.Length];
        for (int i = 1; i < lats.Length; i++)
        {
            double meanLat = (lats[i] + lats[i - 1]) / 2 * Math.PI / 180;
            double dx = (lons[i] - lons[i - 1]) * Math.Cos(meanLat) * 111.32, dy = (lats[i] - lats[i - 1]) * 110.57;
            d[i] = d[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        return d;
    }

    private static TransitStation Station(JsonElement s, string field) =>
        s.TryGetProperty(field, out var p) ? new TransitStation(Str(p, "name") ?? "", Num(p, "lat") ?? 0, Num(p, "lon") ?? 0, Num(p, "importance") ?? 0)
                                           : new TransitStation("", 0, 0, 0);

    private static string Point(double lat, double lon) =>
        lat.ToString("0.#####", CultureInfo.InvariantCulture) + "," + lon.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? ColorOf(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? null : hex.StartsWith('#') ? hex : "#" + hex;

    private static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        Str(e, name) is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
