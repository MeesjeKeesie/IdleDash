using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Buienradar: gaat het de komende twee uur regenen? Ververst elke 5 minuten.</summary>
public partial class RainWidget : WidgetBase
{
    private const double MaxBarHeight = 100;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private DateTime _lastUpdate = DateTime.MinValue;
    private double _latitude;
    private double _longitude;

    public RainWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override async void OnStart()
    {
        _timer.Start();
        if (DateTime.Now - _lastUpdate > TimeSpan.FromMinutes(5)) await RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    public override async void OnSettingsChanged()
    {
        // Andere plaats gekozen: meteen opnieuw ophalen
        if (_settings.WeatherLatitude == _latitude && _settings.WeatherLongitude == _longitude) return;
        _lastUpdate = DateTime.MinValue;
        if (IsRunning) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _latitude = _settings.WeatherLatitude;
        _longitude = _settings.WeatherLongitude;

        var points = await RainService.GetAsync(_latitude, _longitude);
        if (points == null || points.Count == 0)
        {
            if (_lastUpdate == DateTime.MinValue)
            {
                SummaryText.Text = "Geen regendata";
                DetailText.Text = "Buienradar werkt alleen voor Nederland en België.";
                Bars.Children.Clear();
                StartTimeText.Text = MidTimeText.Text = EndTimeText.Text = "";
            }
            return;
        }

        _lastUpdate = DateTime.Now;
        SummaryText.Text = RainService.Summarize(points);
        DetailText.Text = $"{_settings.WeatherPlace}, bron: Buienradar";
        DrawBars(points);
        StartTimeText.Text = "Nu";
        MidTimeText.Text = points[points.Count / 2].Time;
        EndTimeText.Text = points[points.Count - 1].Time;
    }

    private void DrawBars(List<RainPoint> points)
    {
        Bars.Children.Clear();
        Bars.Columns = points.Count;
        var brush = (Brush)FindResource("BarBrush");

        foreach (var point in points)
        {
            // Logaritmische schaal: motregen blijft zichtbaar, een wolkbreuk past er nog op
            double fraction = point.MmPerHour < RainService.RainThreshold
                ? 0
                : Math.Min(1, Math.Log(1 + point.MmPerHour) / Math.Log(11));

            Bars.Children.Add(new Border
            {
                Height = Math.Max(3, fraction * MaxBarHeight),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                CornerRadius = new CornerRadius(2),
                Background = brush,
                Opacity = fraction == 0 ? 0.18 : 0.9,
            });
        }
    }
}
