using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>
/// Focustimer (pomodoro): werken, pauze, werken... Loopt door als het dashboard even verdwijnt,
/// en geeft een geluidje plus een melding rechtsonder als de tijd om is.
/// </summary>
public partial class FocusTimerWidget : WidgetBase
{
    private const double Center = 120;
    private const double Radius = 117;   // net binnen de rand, zodat de dikke lijn precies over de ring valt

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _isBreak;
    private bool _running;
    private bool _started;
    private DateTime _endTime;
    private TimeSpan _remaining;

    public FocusTimerWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += (_, _) => Tick();
        ResetPhase();
    }

    private TimeSpan PhaseLength =>
        TimeSpan.FromMinutes(Math.Max(1, _isBreak ? _settings.BreakMinutes : _settings.FocusMinutes));

    // Bewust niets bij stoppen: de timer moet doorlopen als je even iets anders doet
    protected override void OnStart() => UpdateView();

    public override void OnSettingsChanged()
    {
        // Nieuwe duur meteen tonen, tenzij je al bezig bent
        if (!_started) ResetPhase();
    }

    public override void OnRemoved() => _timer.Stop();

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _remaining = _endTime - DateTime.Now;
            _running = false;
            _timer.Stop();
        }
        else
        {
            _endTime = DateTime.Now + _remaining;
            _running = true;
            _started = true;
            _timer.Start();
        }
        UpdateView();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _running = false;
        _timer.Stop();
        ResetPhase();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        _running = false;
        _timer.Stop();
        SwitchPhase();
    }

    private void Tick()
    {
        if (!_running) return;
        if (DateTime.Now < _endTime)
        {
            UpdateView();
            return;
        }

        // Tijd is om
        _running = false;
        _timer.Stop();
        bool focusDone = !_isBreak;
        SwitchPhase();

        NativeMethods.MessageBeep(NativeMethods.MB_ICONASTERISK);
        if (focusDone)
            App.Notify("Focus-sessie klaar", $"Tijd voor {_settings.BreakMinutes} minuten pauze.");
        else
            App.Notify("Pauze voorbij", "Klaar voor de volgende focus-sessie?");
    }

    private void SwitchPhase()
    {
        _isBreak = !_isBreak;
        ResetPhase();
    }

    private void ResetPhase()
    {
        _started = false;
        _remaining = PhaseLength;
        UpdateView();
    }

    private void UpdateView()
    {
        var left = _running ? _endTime - DateTime.Now : _remaining;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;

        // Afronden naar boven, zodat je "25:00" ziet bij de start en "00:01" in de laatste seconde
        int totalSeconds = (int)Math.Ceiling(left.TotalSeconds);
        TimeText.Text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";

        ModeText.Text = _isBreak ? "Pauze" : "Focus";
        StartButton.Content = _running ? "\uE769" : "\uE768";
        StartButton.ToolTip = _running ? "Pauzeren" : (_started ? "Verder" : "Starten");
        SkipButton.ToolTip = _isBreak ? "Naar focus" : "Naar pauze";

        var color = (Brush)FindResource(_isBreak ? "BreakBrush" : "BarBrush");
        ProgressArc.Stroke = color;
        StartButton.Background = color;

        double fraction = left.TotalSeconds / PhaseLength.TotalSeconds;
        ProgressArc.Data = CreateArc(fraction);
    }

    /// <summary>Boog vanaf de bovenkant, met de klok mee. fraction 1 = hele cirkel, 0 = niets.</summary>
    private static Geometry CreateArc(double fraction)
    {
        if (fraction <= 0.0005) return Geometry.Empty;
        if (fraction >= 0.9995) return new EllipseGeometry(new Point(Center, Center), Radius, Radius);

        double angle = fraction * 2 * Math.PI;
        var start = new Point(Center, Center - Radius);
        var end = new Point(Center + Radius * Math.Sin(angle), Center - Radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(Radius, Radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
