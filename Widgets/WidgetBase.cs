using System.Windows.Controls;

namespace IdleDash.Widgets;

/// <summary>
/// Basis voor elke widget. Start() wordt aangeroepen als het dashboard verschijnt,
/// Stop() als het verdwijnt, zodat widgets niets doen terwijl je ze toch niet ziet.
/// </summary>
public class WidgetBase : UserControl
{
    private bool _running;

    protected bool IsRunning => _running;

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

    protected virtual void OnStart() { }
    protected virtual void OnStop() { }

    /// <summary>Er is iets aangepast in de instellingen (bv. een andere plaats voor het weer).</summary>
    public virtual void OnSettingsChanged() { }

    /// <summary>De widget wordt van het dashboard gehaald.</summary>
    public virtual void OnRemoved() { }
}
