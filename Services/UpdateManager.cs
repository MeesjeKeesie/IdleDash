using System.IO;
using System.Windows;
using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// Houdt updates bij: zoekt een minuut na het opstarten en daarna elke 6 uur.
/// Geïnstalleerde versie: downloadt op de achtergrond en vraagt dan of je nu wilt bijwerken.
/// Portable-versie of automatisch bijwerken uit: alleen een melding met een downloadlink.
/// </summary>
public static class UpdateManager
{
    private static AppSettings? _settings;
    private static DispatcherTimer? _timer;
    private static bool _busy;
    private static string? _readyHash;
    private static string? _readyNotifiedFor;

    public static UpdateState State { get; private set; } = UpdateState.Idle;
    public static UpdateInfo? Latest { get; private set; }
    public static string? ReadySetupPath { get; private set; }

    public static event Action? StateChanged;

    public static void Start(AppSettings settings)
    {
        _settings = settings;
        if (AppInfo.IsDevBuild) return;   // zelf gebouwd: nooit bijwerken

        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _timer.Tick += async (_, _) => await CheckAsync(userInitiated: false);
        _timer.Start();
        _ = FirstCheckAsync();
    }

    private static async Task FirstCheckAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(1));   // eerst rustig opstarten
        CleanupOldDownloads();
        await CheckAsync(userInitiated: false);
    }

    /// <summary>Zoeken naar een update. userInitiated = via de knop, dan tonen we ook als het mislukt.</summary>
    public static async Task CheckAsync(bool userInitiated)
    {
        if (_busy || _settings == null || AppInfo.IsDevBuild) return;
        _busy = true;
        var previous = State;
        if (State != UpdateState.Ready) SetState(UpdateState.Checking);

        try
        {
            var (succeeded, update) = await UpdateService.CheckAsync();
            if (!succeeded)
            {
                if (State != UpdateState.Ready)
                    SetState(userInitiated ? UpdateState.Failed : previous == UpdateState.Checking ? UpdateState.Idle : previous);
                return;
            }
            if (update == null)
            {
                if (State != UpdateState.Ready) SetState(UpdateState.UpToDate);
                return;
            }
            if (State == UpdateState.Ready && Latest?.Version == update.Version) return;   // staat al klaar

            Latest = update;
            bool automatic = _settings.AutoUpdate && AppInfo.IsInstalledCopy
                             && update.SetupUrl != null && update.ChecksumUrl != null;
            if (!automatic)
            {
                SetState(UpdateState.Available);
                NotifyAvailable(update);
                return;
            }

            SetState(UpdateState.Downloading);
            var download = await UpdateService.DownloadAsync(update);
            if (download == null)
            {
                // Downloaden of controleren mislukt: dan maar zelf downloaden
                SetState(UpdateState.Available);
                NotifyAvailable(update);
                return;
            }

            ReadySetupPath = download.Value.Path;
            _readyHash = download.Value.Hash;
            SetState(UpdateState.Ready);
            NotifyReady(update);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>IdleDash afsluiten, bijwerken en weer starten.</summary>
    public static void InstallNow()
    {
        if (State != UpdateState.Ready || ReadySetupPath == null || _readyHash == null) return;

        // Vlak voor het starten nog één keer controleren of de installer niet veranderd is
        bool intact;
        try
        {
            intact = File.Exists(ReadySetupPath) && UpdateService.HashOf(ReadySetupPath) == _readyHash;
        }
        catch
        {
            intact = false;
        }
        if (!intact)
        {
            ReadySetupPath = null;
            _readyHash = null;
            SetState(UpdateState.Idle);
            return;
        }

        if (UpdateService.StartSilentInstall(ReadySetupPath))
            Application.Current.Shutdown();
    }

    private static void NotifyReady(UpdateInfo update)
    {
        if (_readyNotifiedFor == update.Version) return;   // één keer per keer opstarten
        _readyNotifiedFor = update.Version;
        App.Notify(Loc.T("IdleDash {0} staat klaar", update.Version),
            Loc.T("Klik hier om bij te werken. IdleDash start daarna vanzelf opnieuw."),
            InstallNow);
    }

    private static void NotifyAvailable(UpdateInfo update)
    {
        if (_settings == null || _settings.LastNotifiedVersion == update.Version) return;   // één keer per versie
        _settings.LastNotifiedVersion = update.Version;
        _settings.Save();
        App.Notify(Loc.T("IdleDash {0} is beschikbaar", update.Version),
            Loc.T("Klik hier om de nieuwe versie te downloaden."),
            () => Browser.Open(update.PageUrl));
    }

    /// <summary>Oude installers en halve downloads opruimen.</summary>
    private static void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(UpdateService.DownloadFolder)) return;
            foreach (string file in Directory.GetFiles(UpdateService.DownloadFolder))
            {
                var version = UpdateService.VersionFromFileName(Path.GetFileName(file));
                bool keep = version != null && AppInfo.Normalize(version) > AppInfo.Version;
                if (keep) continue;
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // nog in gebruik: volgende keer
                }
            }
        }
        catch
        {
            // map niet leesbaar: niet erg
        }
    }

    private static void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
