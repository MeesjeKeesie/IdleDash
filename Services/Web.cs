using System.Net.Http;

namespace IdleDash.Services;

/// <summary>Eén gedeelde internetverbinding voor alle widgets.</summary>
public static class Web
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdleDash/1.0");
        return client;
    }
}
