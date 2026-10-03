using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IdleDash.Core;

namespace IdleDash.Services;

public sealed record NtfyMessage(string Id, DateTimeOffset Time, string? Title, string? Message);

/// <summary>
/// Luistert naar een ntfy-onderwerp. Je deurbel (bv. een ESP32) stuurt daar een bericht naartoe;
/// je telefoon en IdleDash krijgen het dan allebei tegelijk.
/// </summary>
public static class NtfyService
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly HashSet<string> Seen = new();
    private static CancellationTokenSource? _stop;

    /// <summary>Er kwam een bericht binnen (op de schermdraad, als de dienst daar gestart is).</summary>
    public static event Action<NtfyMessage>? Received;

    /// <summary>De verbinding veranderde. Status is null als alles goed gaat, anders een uitleg.</summary>
    public static event Action? StatusChanged;

    public static string? Problem { get; private set; }
    public static bool IsListening => _stop != null;

    public static bool IsValidTopic(string? topic) =>
        topic is { Length: > 0 and <= 64 } && topic.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static string NormalizeServer(string? server)
    {
        string s = string.IsNullOrWhiteSpace(server) ? "https://ntfy.sh" : server.Trim().TrimEnd('/');
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = "https://" + s;
        return s;
    }

    /// <summary>Eén regel uit de ntfy-stroom. Alleen echte berichten tellen (geen "open" of "keepalive").</summary>
    public static NtfyMessage? ParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (Str(root, "event") != "message") return null;
            long time = root.TryGetProperty("time", out var t) && t.TryGetInt64(out long seconds) ? seconds : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new NtfyMessage(Str(root, "id") ?? Guid.NewGuid().ToString("N"), DateTimeOffset.FromUnixTimeSeconds(time),
                Str(root, "title"), Str(root, "message"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Gaan luisteren (of stoppen als topic leeg is). Roep dit aan vanaf de schermdraad.</summary>
    public static void Configure(bool enabled, string? server, string? topic, string? token)
    {
        _stop?.Cancel();
        _stop = null;
        SetProblem(null);
        if (!enabled || !IsValidTopic(topic)) return;
        _stop = new CancellationTokenSource();
        _ = ListenAsync(NormalizeServer(server), topic!, token, _stop.Token);
    }

    private static async Task ListenAsync(string server, string topic, string? token, CancellationToken stop)
    {
        string? lastId = null;
        int delay = 2;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                string url = $"{server}/{topic}/json" + (lastId != null ? "?since=" + Uri.EscapeDataString(lastId) : "");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    SetProblem(Loc.T("De ntfy-server weigert de toegang. Controleer het token."));
                    await Task.Delay(TimeSpan.FromMinutes(1), stop);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                SetProblem(null);
                delay = 2;

                using var stream = await response.Content.ReadAsStreamAsync(stop);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (!stop.IsCancellationRequested)
                {
                    // ntfy stuurt elke 45 seconden een teken van leven; blijft dat 2 minuten uit, dan opnieuw verbinden
                    using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    watchdog.CancelAfter(TimeSpan.FromMinutes(2));
                    string? line = await reader.ReadLineAsync(watchdog.Token);
                    if (line == null) break;
                    if (ParseLine(line) is not { } message) continue;
                    lastId = message.Id;
                    if (!Seen.Add(message.Id)) continue;                                        // al gezien
                    if (DateTimeOffset.UtcNow - message.Time > TimeSpan.FromMinutes(2)) continue; // gemist tijdens een storing: niet meer bellen
                    Received?.Invoke(message);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // te lang stil: gewoon opnieuw verbinden
            }
            catch
            {
                SetProblem(Loc.T("Geen verbinding met de ntfy-server. IdleDash probeert het steeds opnieuw."));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            delay = Math.Min(delay * 2, 60);
        }
    }

    /// <summary>Een testbericht naar het onderwerp sturen. Geeft een foutmelding terug, of null als het gelukt is.</summary>
    public static async Task<string?> SendTestAsync(string? server, string? topic, string? token, string message)
    {
        if (!IsValidTopic(topic)) return Loc.T("Vul een geldig onderwerp in: letters, cijfers, - en _.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeServer(server)}/{topic}")
            {
                Content = new StringContent(message, Encoding.UTF8, "text/plain"),
            };
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await Client.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode ? null : Loc.T("De ntfy-server gaf een fout ({0}).", (int)response.StatusCode);
        }
        catch
        {
            return Loc.T("De ntfy-server is niet bereikbaar.");
        }
    }

    private static void SetProblem(string? problem)
    {
        if (problem == Problem) return;
        Problem = problem;
        StatusChanged?.Invoke();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
