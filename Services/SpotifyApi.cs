using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IdleDash.Services;

public sealed record SpotifyPlaylist(string Id, string Name, string Uri, string? ImageUrl, string? Owner, int TrackCount);
public sealed record SpotifyDevice(string Id, string Name, string Type, bool IsActive);
public sealed record SpotifyTokens(string AccessToken, string? RefreshToken, DateTime ExpiresAt);

public sealed class SpotifyException(HttpStatusCode status, string? reason, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? Reason { get; } = reason;
}

/// <summary>
/// De Spotify Web API volgens de regels van 2026: elke gebruiker heeft een eigen Spotify-app (Client ID),
/// inloggen gaat met PKCE (geen geheime sleutel) en een terugkeeradres op 127.0.0.1.
/// </summary>
public static class SpotifyApi
{
    public const int Port = 45631;
    public const string RedirectUri = "http://127.0.0.1:45631/callback";
    public const string Scopes = "playlist-read-private playlist-read-collaborative user-read-playback-state user-modify-playback-state";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ─────────────────────────── Inloggen (PKCE) ───────────────────────────

    public static string CreateVerifier() =>
        new(RandomNumberGenerator.GetItems<char>("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~", 64));

    public static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string AuthorizeUrl(string clientId, string challenge, string state) =>
        "https://accounts.spotify.com/authorize?response_type=code"
        + "&client_id=" + Uri.EscapeDataString(clientId)
        + "&scope=" + Uri.EscapeDataString(Scopes)
        + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
        + "&code_challenge_method=S256&code_challenge=" + challenge
        + "&state=" + Uri.EscapeDataString(state);

    /// <summary>De eerste regel van wat de browser na het inloggen stuurt, bv. "GET /callback?code=…&amp;state=… HTTP/1.1".</summary>
    public static (string? Code, string? State, string? Error) ParseCallback(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || !parts[1].StartsWith("/callback", StringComparison.Ordinal)) return (null, null, "invalid");
        var values = new Dictionary<string, string>();
        int question = parts[1].IndexOf('?');
        if (question >= 0)
        {
            foreach (string pair in parts[1][(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                values[eq < 0 ? pair : pair[..eq]] = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
        }
        return (values.GetValueOrDefault("code"), values.GetValueOrDefault("state"), values.GetValueOrDefault("error"));
    }

    public static SpotifyTokens? ParseToken(string json, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Str(root, "access_token") is not string access) return null;
            int seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
            return new SpotifyTokens(access, Str(root, "refresh_token"), now.AddSeconds(seconds - 60));
        }
        catch
        {
            return null;
        }
    }

    public static Task<SpotifyTokens?> ExchangeCodeAsync(string clientId, string code, string verifier) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        });

    /// <summary>Nieuwe toegang met de refresh token. Null als Spotify die niet meer accepteert: dan opnieuw koppelen.</summary>
    public static Task<SpotifyTokens?> RefreshAsync(string clientId, string refreshToken) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        });

    private static async Task<SpotifyTokens?> TokenRequestAsync(Dictionary<string, string> form)
    {
        using var response = await Http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
        string json = await response.Content.ReadAsStringAsync();
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return ParseToken(json, DateTime.Now);
    }

    // ─────────────────────────── Web API ───────────────────────────

    public static async Task<string> SendAsync(HttpMethod method, string path, string accessToken, string? json = null)
    {
        using var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (json != null || method == HttpMethod.Put) request.Content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) return body;
        var (message, reason) = ParseError(body);
        throw new SpotifyException(response.StatusCode, reason, message ?? response.ReasonPhrase ?? response.StatusCode.ToString());
    }

    public static (string? Message, string? Reason) ParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var error = doc.RootElement.GetProperty("error");
            return error.ValueKind == JsonValueKind.Object ? (Str(error, "message"), Str(error, "reason")) : (error.GetString(), null);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Een pagina van GET /me/playlists. Sinds februari 2026 heet het veld met het aantal nummers "items" in plaats van "tracks".</summary>
    public static (List<SpotifyPlaylist> Items, string? Next) ParsePlaylists(string json)
    {
        var list = new List<SpotifyPlaylist>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in items.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object || Str(p, "id") is not string id || Str(p, "uri") is not string uri) continue;
                string? image = null;
                if (p.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                {
                    image = images.EnumerateArray()
                        .Select(i => (Url: Str(i, "url"), Width: Num(i, "width") ?? 300))
                        .Where(i => i.Url != null)
                        .OrderBy(i => Math.Abs(i.Width - 300))
                        .Select(i => i.Url)
                        .FirstOrDefault();
                }
                string? owner = p.TryGetProperty("owner", out var o) ? Str(o, "display_name") : null;
                list.Add(new SpotifyPlaylist(id, Str(p, "name") ?? id, uri, image, owner, Count(p, "items") ?? Count(p, "tracks") ?? 0));
            }
        }
        return (list, Str(root, "next"));
    }

    public static List<SpotifyDevice> ParseDevices(string json)
    {
        var list = new List<SpotifyDevice>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array) return list;
        foreach (var d in devices.EnumerateArray())
        {
            if (Str(d, "id") is not string id) continue;   // sommige apparaten zijn niet te bedienen
            list.Add(new SpotifyDevice(id, Str(d, "name") ?? "", Str(d, "type") ?? "",
                d.TryGetProperty("is_active", out var a) && a.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    /// <summary>Afspelen op deze pc (zelfde naam); anders op het apparaat dat al speelt; anders op een andere computer.</summary>
    public static SpotifyDevice? ChooseDevice(IReadOnlyList<SpotifyDevice> devices, string machineName) =>
        devices.FirstOrDefault(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase) && d.Name.Equals(machineName, StringComparison.OrdinalIgnoreCase))
        ?? devices.FirstOrDefault(d => d.IsActive)
        ?? devices.FirstOrDefault(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase));

    /// <summary>Of Spotify nu shuffelt, uit GET /me/player. Null als er niets speelt (Spotify antwoordt dan leeg).</summary>
    public static bool? ParseShuffle(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("shuffle_state", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False ? s.GetBoolean() : null;
        }
        catch
        {
            return null;
        }
    }

    public static string PlayBody(string contextUri, int? offset) =>
        offset is int position
            ? JsonSerializer.Serialize(new { context_uri = contextUri, offset = new { position } })
            : JsonSerializer.Serialize(new { context_uri = contextUri });

    private static int? Count(JsonElement e, string field) =>
        e.TryGetProperty(field, out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("total", out var t) && t.TryGetInt32(out int n) ? n : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
