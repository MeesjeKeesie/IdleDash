using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash.Widgets;

public partial class ClockWidget : WidgetBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public ClockWidget()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateTime();
        UpdateTime();
    }

    protected override void OnStart()
    {
        UpdateTime();
        _timer.Start();
    }

    protected override void OnStop() => _timer.Stop();
    public override void Refresh() => UpdateTime();
    public override void OnSettingsChanged() => UpdateTime();

    private void UpdateTime()
    {
        var now = DateTime.Now;
        bool seconds = Config.Get("seconds", true);

        // 12-uurs notatie: "3:05" met een klein "PM" erachter
        TimeText.Inlines.Clear();
        if (Loc.Use12Hour)
        {
            TimeText.Inlines.Add(new Run(now.ToString("h:mm", CultureInfo.InvariantCulture)));
            TimeText.Inlines.Add(new Run(" " + now.ToString("tt", CultureInfo.GetCultureInfo("en-US"))) { FontSize = 44 });
        }
        else
        {
            TimeText.Inlines.Add(new Run(now.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }
        SecondsText.Text = seconds ? now.ToString("ss", CultureInfo.InvariantCulture) : "";
        SecondsText.Visibility = seconds ? Visibility.Visible : Visibility.Collapsed;

        DateText.Visibility = Config.Get("date", true) ? Visibility.Visible : Visibility.Collapsed;
        DateText.Text = Loc.Date(now);
    }

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Klok")));
        panel.Children.Add(Ui.Switch(Loc.T("Seconden tonen"), null, Config.Get("seconds", true), v => { Config.Set("seconds", v); saved(); }));
        panel.Children.Add(Ui.Switch(Loc.T("Datum tonen"), null, Config.Get("date", true), v => { Config.Set("date", v); saved(); }));
        panel.Children.Add(Ui.Hint(Loc.T("12- of 24-uurs notatie stel je in bij de instellingen van IdleDash, onder Taal en weergave."), 14));
        return panel;
    }
}
