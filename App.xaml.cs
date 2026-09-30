using System.Threading;
using System.Windows;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private MainWindow? _dashboard;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Snelle toegang tot de app vanuit widgets en vensters.</summary>
    public static App Instance => (App)Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Zorgt dat IdleDash maar één keer tegelijk draait
        _mutex = new Mutex(true, "IdleDash.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();

        // Taal, thema, agenda's en smarthome klaarzetten voordat er iets op het scherm komt
        Loc.Configure(Settings);
        ThemeManager.ApplyGlobal(Settings.Theme);
        CalendarHub.Configure(Settings);
        SmartHomeService.Configure(Settings);
        Loc.Changed += () => Dispatcher.BeginInvoke(ReopenSettings);

        var dashboard = new MainWindow(Settings);
        _dashboard = dashboard;

        var tray = new TrayIcon();
        _tray = tray;
        tray.SettingsRequested += ShowSettings;
        tray.PauseToggled += () =>
        {
            dashboard.SetPaused(!dashboard.Paused);
            tray.Paused = dashboard.Paused;
        };
        tray.ExitRequested += () => Shutdown();

        dashboard.Start();
        ShowWhatsNewIfUpdated();
        ShowWelcomeOnce();

        // Updates: zoeken, op de achtergrond downloaden en "Bijwerken" in het systeemvak-menu zetten
        tray.UpdateRequested += UpdateManager.InstallNow;
        UpdateManager.StateChanged += () =>
            tray.UpdateMenuText = UpdateManager.State == UpdateState.Ready && UpdateManager.Latest != null
                ? Loc.T("Bijwerken naar versie {0}", UpdateManager.Latest.Version)
                : null;
        UpdateManager.Start(Settings);

        // Eerder gekoppeld met Google? Dan stil opnieuw verbinden (zonder browser)
        try
        {
            await GoogleService.RestoreAsync();
        }
        catch
        {
            // Lukt het niet, dan vragen de widgets vanzelf om opnieuw te koppelen
        }
    }

    /// <summary>De allereerste keer: laten weten dat IdleDash draait en waar je hem vindt.</summary>
    private void ShowWelcomeOnce()
    {
        if (Settings.WelcomeShown) return;
        Settings.WelcomeShown = true;
        Settings.Save();

        bool hasScreen = MonitorHelper.FindTarget(Settings.MonitorDeviceName) != null;
        Notify(Loc.T("IdleDash draait"),
            hasScreen
                ? Loc.T("Het dashboard verschijnt zodra je bovenste scherm leeg is. Klik op het IdleDash-icoon bij de klok voor de instellingen.")
                : Loc.T("Klik op het IdleDash-icoon bij de klok en kies op welk scherm het dashboard moet komen."));
    }

    /// <summary>Na een update één keer laten weten dat IdleDash is bijgewerkt, met een link naar wat er nieuw is.</summary>
    private void ShowWhatsNewIfUpdated()
    {
        string? previous = Settings.LastRunVersion;
        Settings.LastRunVersion = AppInfo.VersionText;
        Settings.Save();
        if (AppInfo.IsDevBuild) return;

        // Geen vorige versie bekend maar wel al eerder gebruikt? Dan kwam je van 1.0.0
        bool updated = previous == null
            ? Settings.WelcomeShown
            : Version.TryParse(previous, out var old) && AppInfo.Normalize(old) < AppInfo.Version;
        if (!updated) return;

        string version = AppInfo.VersionText;
        Notify(Loc.T("IdleDash is bijgewerkt naar {0}", version), Loc.T("Klik hier om te zien wat er nieuw is."),
            () => Browser.Open(AppInfo.ReleasePageUrl(version)));
    }

    /// <summary>Opent het instellingenscherm (of haalt het naar voren als het al open is).</summary>
    public void ShowSettings()
    {
        if (_dashboard == null) return;

        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var dashboard = _dashboard;
        _settingsWindow = new SettingsWindow(Settings, () => dashboard.ResetLayout());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Andere taal gekozen: het instellingenvenster opnieuw openen, op dezelfde plek.</summary>
    private void ReopenSettings()
    {
        if (_settingsWindow == null) return;
        double offset = _settingsWindow.ScrollOffset;
        var position = (_settingsWindow.Left, _settingsWindow.Top);
        _settingsWindow.Close();
        ShowSettings();
        if (_settingsWindow == null) return;
        _settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        (_settingsWindow.Left, _settingsWindow.Top) = position;
        _settingsWindow.ScrollOffset = offset;
    }

    /// <summary>Melding rechtsonder in beeld (via het systeemvak-icoon). onClick = wat er gebeurt als je erop klikt.</summary>
    public static void Notify(string title, string message, Action? onClick = null) =>
        Instance._tray?.ShowNotification(title, message, onClick);

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
