using System.Diagnostics;

namespace IdleDash.Core;

/// <summary>Een webpagina openen in je standaardbrowser.</summary>
public static class Browser
{
    public static void Open(string? url)
    {
        if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // geen standaardbrowser ingesteld
        }
    }
}
