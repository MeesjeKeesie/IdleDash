using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>
/// Spotify: koppelen via je browser, je playlists ophalen en een playlist afspelen op deze pc.
/// Afspelen bedienen kan alleen met Spotify Premium (regel van Spotify).
/// </summary>
public static class SpotifyService
{
    private static AppSettings _settings = new();
    private static SpotifyTokens? _tokens;

    public static event Action? StateChanged;

    public static void Configure(AppSettings settings)
    {
        _settings = settings;
        _tokens = null;
    }

    public static bool HasClientId => !string.IsNullOrWhiteSpace(_settings.Spotify.ClientId);
    public static bool IsConnected => HasClientId && !string.IsNullOrEmpty(_settings.Spotify.RefreshToken);

    /// <summary>Koppelen: de browser opent het inlogscherm van Spotify; IdleDash vangt het antwoord op 127.0.0.1 op.</summary>
    public static async Task<string?> ConnectAsync()
    {
        string clientId = (_settings.Spotify.ClientId ?? "").Trim();
        string verifier = SpotifyApi.CreateVerifier();
        string state = Guid.NewGuid().ToString("N");
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, SpotifyApi.Port);
            listener.Start();
        }
        catch
        {
            return Loc.T("Poort {0} is bezet door een ander programma. Sluit dat en probeer het opnieuw.", SpotifyApi.Port);
        }

        try
        {
            Browser.Open(SpotifyApi.AuthorizeUrl(clientId, SpotifyApi.Challenge(verifier), state));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = client.GetStream();
                string requestLine;
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true))
                    requestLine = await reader.ReadLineAsync(timeout.Token) ?? "";
                var (code, returnedState, error) = SpotifyApi.ParseCallback(requestLine);
                if (error == "invalid")
                {
                    await RespondAsync(stream, 404, "");   // bv. een verzoek om een icoontje
                    continue;
                }
                bool ok = code != null && returnedState == state;
                await RespondAsync(stream, 200, ok
                    ? Loc.T("Gekoppeld met Spotify. Je kunt dit venster sluiten.")
                    : Loc.T("Koppelen is niet gelukt. Probeer het opnieuw vanuit IdleDash."));
                if (!ok) return error == "access_denied" ? Loc.T("Je hebt geen toestemming gegeven.") : Loc.T("Koppelen is niet gelukt.");

                var tokens = await SpotifyApi.ExchangeCodeAsync(clientId, code!, verifier);
                if (tokens?.RefreshToken == null)
                    return Loc.T("Spotify gaf geen toegang. Controleer de Client ID en het adres bij Redirect URIs.");
                _tokens = tokens;
                _settings.Spotify.RefreshToken = Secrets.Protect(tokens.RefreshToken);
                _settings.Spotify.AccountName = await AccountNameAsync(tokens.AccessToken);
                _settings.Save();
                StateChanged?.Invoke();
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            return Loc.T("Het inloggen duurde te lang. Probeer het opnieuw.");
        }
        catch (Exception ex)
        {
            return Loc.T("Koppelen is niet gelukt: {0}", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    public static void Disconnect()
    {
        _settings.Spotify.RefreshToken = null;
        _settings.Spotify.AccountName = null;
        _tokens = null;
        _settings.Save();
        StateChanged?.Invoke();
    }

    /// <summary>Je eigen playlists en die je volgt. Geeft een foutmelding terug als het niet lukt.</summary>
    public static async Task<(List<SpotifyPlaylist> Playlists, string? Error)> GetPlaylistsAsync()
    {
        try
        {
            string? token = await AccessTokenAsync();
            if (token == null) return (new(), Loc.T("Koppel Spotify opnieuw in de instellingen."));
            var all = new List<SpotifyPlaylist>();
            string? path = "me/playlists?limit=50";
            for (int page = 0; page < 6 && path != null; page++)
            {
                var (items, next) = SpotifyApi.ParsePlaylists(await SpotifyApi.SendAsync(HttpMethod.Get, path, token));
                all.AddRange(items);
                path = next?.Replace("https://api.spotify.com/v1/", "");
            }
            return (all, null);
        }
        catch (Exception ex)
        {
            return (new(), Describe(ex));
        }
    }

    /// <summary>
    /// Een playlist afspelen op deze pc. Draait Spotify nog niet, dan start IdleDash het eerst.
    /// Met shuffle begint hij bij een willekeurig nummer. Geeft een foutmelding terug, of null.
    /// </summary>
    public static async Task<string?> PlayAsync(string uri, int trackCount, bool shuffle)
    {
        try
        {
            string? token = await AccessTokenAsync();
            if (token == null) return Loc.T("Koppel Spotify opnieuw in de instellingen.");

            var device = SpotifyApi.ChooseDevice(await DevicesAsync(token), Environment.MachineName);
            if (device == null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
                }
                catch
                {
                    return Loc.T("Spotify staat niet op deze pc.");
                }
                for (int i = 0; i < 15 && device == null; i++)
                {
                    await Task.Delay(1000);
                    device = SpotifyApi.ChooseDevice(await DevicesAsync(token), Environment.MachineName);
                }
                if (device == null) return Loc.T("Spotify reageert nog niet. Wacht even en probeer het opnieuw.");
            }

            string id = Uri.EscapeDataString(device.Id);
            int? offset = shuffle && trackCount > 1 ? Random.Shared.Next(trackCount) : null;
            try
            {
                await SpotifyApi.SendAsync(HttpMethod.Put, $"me/player/play?device_id={id}", token, SpotifyApi.PlayBody(uri, offset));
            }
            catch (SpotifyException ex) when (ex.Status == HttpStatusCode.BadRequest && offset != null)
            {
                await SpotifyApi.SendAsync(HttpMethod.Put, $"me/player/play?device_id={id}", token, SpotifyApi.PlayBody(uri, null));
            }
            try
            {
                await SpotifyApi.SendAsync(HttpMethod.Put, $"me/player/shuffle?state={(shuffle ? "true" : "false")}&device_id={id}", token);
            }
            catch
            {
                // shuffle is een bijzaak; de muziek speelt al
            }
            return null;
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static async Task<List<SpotifyDevice>> DevicesAsync(string token) =>
        SpotifyApi.ParseDevices(await SpotifyApi.SendAsync(HttpMethod.Get, "me/player/devices", token));

    private static async Task<string?> AccountNameAsync(string token)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await SpotifyApi.SendAsync(HttpMethod.Get, "me", token));
            return doc.RootElement.TryGetProperty("display_name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Geldige toegang, zo nodig vernieuwd. Null als opnieuw koppelen nodig is (Spotify laat refresh tokens soms verlopen).</summary>
    private static async Task<string?> AccessTokenAsync()
    {
        if (_tokens != null && DateTime.Now < _tokens.ExpiresAt) return _tokens.AccessToken;
        string? refresh = Secrets.Unprotect(_settings.Spotify.RefreshToken);
        if (!HasClientId || string.IsNullOrEmpty(refresh)) return null;
        var tokens = await SpotifyApi.RefreshAsync(_settings.Spotify.ClientId!.Trim(), refresh);
        if (tokens == null)
        {
            Disconnect();
            return null;
        }
        _tokens = tokens;
        if (tokens.RefreshToken != null && tokens.RefreshToken != refresh)
        {
            _settings.Spotify.RefreshToken = Secrets.Protect(tokens.RefreshToken);
            _settings.Save();
        }
        return tokens.AccessToken;
    }

    private static string Describe(Exception ex) => ex switch
    {
        SpotifyException { Reason: "PREMIUM_REQUIRED" } => Loc.T("Afspelen via IdleDash kan alleen met Spotify Premium."),
        SpotifyException { Status: HttpStatusCode.Unauthorized } => Loc.T("Koppel Spotify opnieuw in de instellingen."),
        SpotifyException { Status: HttpStatusCode.Forbidden } s => Loc.T("Spotify weigert: {0}", s.Message),
        SpotifyException { Status: HttpStatusCode.NotFound } => Loc.T("Spotify vond geen apparaat om op af te spelen. Open Spotify en probeer het opnieuw."),
        SpotifyException { Status: HttpStatusCode.TooManyRequests } => Loc.T("Spotify vraagt even te wachten. Probeer het zo opnieuw."),
        _ => Loc.T("Spotify is nu niet bereikbaar."),
    };

    private static async Task RespondAsync(NetworkStream stream, int status, string message)
    {
        string html = "<!doctype html><meta charset=\"utf-8\"><title>IdleDash</title>"
            + "<body style=\"font-family:Segoe UI,sans-serif;background:#10151F;color:#EEF1F6;display:grid;place-items:center;height:90vh;margin:0\">"
            + $"<p style=\"font-size:20px\">{WebUtility.HtmlEncode(message)}</p></body>";
        byte[] body = Encoding.UTF8.GetBytes(html);
        string header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
    }
}
