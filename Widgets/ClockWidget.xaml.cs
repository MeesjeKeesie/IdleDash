using System.Globalization;
using System.Windows.Threading;

namespace IdleDash.Widgets;

public partial class ClockWidget : WidgetBase
{
    private static readonly CultureInfo Dutch = new("nl-NL");
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

    private void UpdateTime()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm", Dutch);
        SecondsText.Text = now.ToString("ss", Dutch);

        string date = now.ToString("dddd d MMMM", Dutch);          // "maandag 28 september"
        DateText.Text = char.ToUpper(date[0], Dutch) + date[1..];   // "Maandag 28 september"
    }
}
