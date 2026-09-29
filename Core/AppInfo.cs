namespace IdleDash.Core;

/// <summary>Versie en projectgegevens van IdleDash.</summary>
public static class AppInfo
{
    /// <summary>Je GitHub-repository. Heet hij anders? Pas het dan hier aan en in installer/IdleDash.iss.</summary>
    public const string GitHubRepo = "MeesjeKeesie/IdleDash";

    public static string RepoUrl => "https://github.com/" + GitHubRepo;

    /// <summary>Versie van dit programma. Op GitHub gebouwd = het versienummer van de release, zelf gebouwd = 0.0.0.</summary>
    public static Version Version { get; } = Normalize(typeof(AppInfo).Assembly.GetName().Version);

    /// <summary>Zelf gebouwd in Visual Studio, geen officiële download.</summary>
    public static bool IsDevBuild => Version == new Version(0, 0, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>Altijd drie delen (1.2.3), zodat 1.2.3 en 1.2.3.0 als gelijk tellen.</summary>
    public static Version Normalize(Version? version) =>
        version == null
            ? new Version(0, 0, 0)
            : new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));
}
