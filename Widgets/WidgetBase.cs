using System.Windows;
using System.Windows.Controls;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>
/// Basis voor elke widget. Start() wordt aangeroepen als het dashboard verschijnt,
/// Stop() als het verdwijnt, zodat widgets niets doen terwijl je ze toch niet ziet.
/// </summary>
public class WidgetBase : UserControl
{
    private bool _running;

    protected bool IsRunning => _running;

    /// <summary>Positie, thema en opties van deze widget.</summary>
    public WidgetConfig Config { get; private set; } = new();

    /// <summary>De algemene instellingen van IdleDash.</summary>
    protected AppSettings Settings { get; private set; } = new();

    /// <summary>De widget vraagt om zijn instellingenvenster (bv. "kies een map").</summary>
    public event Action? SettingsRequested;

    /// <summary>Wordt door de houder aangeroepen zodra de widget op het dashboard komt.</summary>
    public void Attach(WidgetConfig config, AppSettings settings)
    {
        Config = config;
        Settings = settings;
        OnAttached();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        OnStart();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        OnStop();
    }

    protected virtual void OnAttached() { }
    protected virtual void OnStart() { }
    protected virtual void OnStop() { }

    /// <summary>Instellingen zijn aangepast (algemeen of van deze widget).</summary>
    public virtual void OnSettingsChanged() { }

    /// <summary>Taal, eenheden of thema veranderd: teksten en kleuren opnieuw opbouwen zonder opnieuw op te halen.</summary>
    public virtual void Refresh() { }

    /// <summary>De widget wordt van het dashboard gehaald.</summary>
    public virtual void OnRemoved() { }

    /// <summary>Extra instellingen voor deze widget in het tandwiel-venster, of null. saved() na elke wijziging aanroepen.</summary>
    public virtual FrameworkElement? CreateSettings(Action saved) => null;

    protected void RequestSettings() => SettingsRequested?.Invoke();

    // ── Kleine hulpjes voor widgets die in code gebouwd worden ──

    protected System.Windows.Media.Brush Res(string key) => (System.Windows.Media.Brush)FindResource(key);

    protected TextBlock Text(string text, double size, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);   // volgt het thema, ook als dat later wisselt
        return block;
    }
}
