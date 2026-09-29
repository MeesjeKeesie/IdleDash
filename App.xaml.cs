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
        ShowWelcomeOnce();
        _ = NotifyAboutUpdateAsync();

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
        Notify("IdleDash draait",
            hasScreen
                ? "Het dashboard verschijnt zodra je bovenste scherm leeg is. Klik op het IdleDash-icoon bij de klok voor de instellingen."
                : "Klik op het IdleDash-icoon bij de klok en kies op welk scherm het dashboard moet komen.");
    }

    /// <summary>Eén keer per nieuwe versie een melding als er een update op GitHub staat.</summary>
    private async Task NotifyAboutUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(1));   // eerst rustig opstarten
        var (succeeded, update) = await UpdateService.CheckAsync();
        if (!succeeded || update == null || update.Version == Settings.LastNotifiedVersion) return;

        Settings.LastNotifiedVersion = update.Version;
        Settings.Save();
        Notify("Nieuwe versie van IdleDash", $"Versie {update.Version} staat klaar. Je downloadt hem via Instellingen > Over IdleDash.");
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

    /// <summary>Melding rechtsonder in beeld (via het systeemvak-icoon).</summary>
    public static void Notify(string title, string message) => Instance._tray?.ShowNotification(title, message);

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
