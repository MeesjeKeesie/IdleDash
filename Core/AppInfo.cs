using System.IO;

namespace IdleDash.Core;

/// <summary>Versie en projectgegevens van IdleDash.</summary>
public static class AppInfo
{
    /// <summary>Je GitHub-repository. Heet hij anders? Pas het dan hier aan en in installer/IdleDash.iss.</summary>
    public const string GitHubRepo = "MeesjeKeesie/IdleDash";

    public static string RepoUrl => "https://github.com/" + GitHubRepo;

    /// <summary>Pagina van één release op GitHub, bv. voor "Wat is er nieuw".</summary>
    public static string ReleasePageUrl(string version) => $"{RepoUrl}/releases/tag/v{version}";

    /// <summary>Versie van dit programma. Op GitHub gebouwd = het versienummer van de release, zelf gebouwd = 0.0.0.</summary>
    public static Version Version { get; } = Normalize(typeof(AppInfo).Assembly.GetName().Version);

    /// <summary>Zelf gebouwd in Visual Studio, geen officiële download.</summary>
    public static bool IsDevBuild => Version == new Version(0, 0, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>De map waar de installer IdleDash neerzet.</summary>
    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "IdleDash");

    /// <summary>Draait deze IdleDash vanuit de installatiemap? (De losse portable-versie kan zichzelf niet bijwerken.)</summary>
    public static bool IsInstalledCopy
    {
        get
        {
            string? folder = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            return folder != null && string.Equals(
                folder.TrimEnd(Path.DirectorySeparatorChar),
                InstallFolder.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Altijd drie delen (1.2.3), zodat 1.2.3 en 1.2.3.0 als gelijk tellen.</summary>
    public static Version Normalize(Version? version) =>
        version == null
            ? new Version(0, 0, 0)
            : new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));
}
