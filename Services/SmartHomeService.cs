using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>
/// Alle smarthome-systemen samen. Haalt standen op voor de widget en houdt sensoren in de gaten
/// die een melding moeten geven (bv. deurbel of beweging).
/// </summary>
public static class SmartHomeService
{
    private static AppSettings _settings = new();
    private static List<ISmartProvider> _providers = new();
    private static (DateTime Time, List<SmartDevice> Devices, List<string> Problems)? _cache;
    private static Task<(List<SmartDevice>, List<string>)>? _loading;
    private static DispatcherTimer? _watchTimer;
    private static readonly Dictionary<string, (bool? On, string? Value)> LastSeen = new();

    /// <summary>Een melding-sensor ging af. Het dashboard toont dan een melding.</summary>
    public static event Action<SmartDevice>? Triggered;

    /// <summary>Koppelingen zijn veranderd (bv. in de instellingen).</summary>
    public static event Action? Reconfigured;

    public static bool HasAnySystem => _providers.Count > 0;

    public static void Configure(AppSettings settings)
    {
        _settings = settings;
        var providers = new List<ISmartProvider>();
        var home = settings.SmartHome;

        if (!string.IsNullOrWhiteSpace(home.HomeAssistantUrl) && Secrets.Unprotect(home.HomeAssistantToken) is { Length: > 0 } token)
        {
            try
            {
                providers.Add(new HomeAssistantProvider(home.HomeAssistantUrl, token));
            }
            catch
            {
                // ongeldig adres
            }
        }
        if (!string.IsNullOrWhiteSpace(home.HueBridge) && Secrets.Unprotect(home.HueUser) is { Length: > 0 } user)
            providers.Add(new HueProvider(home.HueBridge, user));
        if (home.ShellyDevices.Count > 0)
            providers.Add(new ShellyProvider(home.ShellyDevices));

        _providers = providers;
        _cache = null;
        LastSeen.Clear();
        UpdateWatcher();
        Reconfigured?.Invoke();
    }

    /// <summary>Alle apparaten van alle systemen. Twee seconden bewaard, zodat widget en meldingen één keer ophalen.</summary>
    public static async Task<(List<SmartDevice> Devices, List<string> Problems)> GetDevicesAsync(bool fresh = false)
    {
        if (!fresh && _cache is { } cache && DateTime.Now - cache.Time < TimeSpan.FromSeconds(2))
            return (cache.Devices, cache.Problems);
        _loading ??= LoadAsync();
        try
        {
            var (devices, problems) = await _loading;
            _cache = (DateTime.Now, devices, problems);
            return (devices, problems);
        }
        finally
        {
            _loading = null;
        }
    }

    private static async Task<(List<SmartDevice>, List<string>)> LoadAsync()
    {
        var devices = new List<SmartDevice>();
        var problems = new List<string>();
        foreach (var provider in _providers.ToList())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                devices.AddRange(await provider.GetDevicesAsync(timeout.Token));
            }
            catch (UnauthorizedAccessException)
            {
                problems.Add(Loc.T("{0}: koppeling geweigerd, koppel opnieuw in de instellingen", provider.Name));
            }
            catch
            {
                problems.Add(Loc.T("{0}: niet bereikbaar", provider.Name));
            }
        }
        devices.AddRange(SmartGroups.Build(_settings.SmartHome.Groups, devices));
        return (devices, problems);
    }

    /// <summary>Een actie uitvoeren. Geeft een foutmelding terug, of null als het gelukt is.</summary>
    public static async Task<string?> InvokeAsync(SmartDevice device, SmartAction action, int? value = null, string? code = null, int? value2 = null)
    {
        if (device.Kind == DeviceKind.Group) return await InvokeGroupAsync(device, action, value, value2);

        if (device.Sensitive && !_settings.SmartHome.AllowSensitive)
            return Loc.T("Sloten en alarm bedienen staat uit in de instellingen.");

        var provider = _providers.FirstOrDefault(p => device.Key.StartsWith(p.Prefix + ":"));
        if (provider == null) return Loc.T("Dit systeem is niet meer gekoppeld.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await provider.InvokeAsync(device, action, value, value2, code, timeout.Token);
            _cache = null;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Loc.T("{0}: koppeling geweigerd, koppel opnieuw in de instellingen", provider.Name);
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            return ex.Message;
        }
        catch
        {
            return Loc.T("{0}: niet bereikbaar", provider.Name);
        }
    }

    /// <summary>Een eigen groep bedienen: de opdracht gaat tegelijk naar alle lampen en schakelaars erin.</summary>
    private static async Task<string?> InvokeGroupAsync(SmartDevice group, SmartAction action, int? value, int? value2)
    {
        var settings = _settings.SmartHome.Groups.FirstOrDefault(g => SmartGroups.KeyPrefix + g.Id == group.Key);
        if (settings == null) return Loc.T("Deze groep bestaat niet meer.");
        var (devices, _) = await GetDevicesAsync();
        var members = devices.Where(d => settings.Devices.Contains(d.Key)).ToList();
        var plan = SmartGroups.Plan(group, members, action);
        if (plan.Count == 0) return Loc.T("Dit kan niet bij dit apparaat.");
        var results = await Task.WhenAll(plan.Select(step => InvokeAsync(step.Member, step.Action, value, null, value2)));
        _cache = null;
        return results.FirstOrDefault(r => r != null);
    }

    /// <summary>Groepen of keuzes veranderd: alles opnieuw ophalen en de widgets laten bijwerken.</summary>
    public static void Refresh()
    {
        _cache = null;
        Reconfigured?.Invoke();
    }

    // ─────────────────────────── Meldingen (deurbel, beweging) ───────────────────────────

    private static void UpdateWatcher()
    {
        bool needed = _providers.Count > 0 && _settings.SmartHome.NotifyDevices.Count > 0;
        if (needed && _watchTimer == null)
        {
            _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _watchTimer.Tick += async (_, _) => await WatchAsync();
            _watchTimer.Start();
        }
        else if (!needed && _watchTimer != null)
        {
            _watchTimer.Stop();
            _watchTimer = null;
        }
    }

    private static bool _watching;

    private static async Task WatchAsync()
    {
        if (_watching) return;
        _watching = true;
        try
        {
            var (devices, _) = await GetDevicesAsync();
            foreach (var device in devices.Where(d => _settings.SmartHome.NotifyDevices.Contains(d.Key)))
            {
                bool known = LastSeen.TryGetValue(device.Key, out var last);
                LastSeen[device.Key] = (device.IsOn, device.Value);
                if (!known) continue;   // eerste keer: alleen onthouden

                bool fired = device.Kind == DeviceKind.Event
                    ? device.Value != last.Value && !string.IsNullOrEmpty(device.Value) && device.Value is not ("unavailable" or "unknown")
                    : device.IsOn == true && last.On == false;
                if (fired) Triggered?.Invoke(device);
            }
        }
        catch
        {
            // volgende ronde opnieuw
        }
        finally
        {
            _watching = false;
        }
    }
}
