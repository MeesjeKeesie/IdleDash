using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

public partial class WeatherWidget : WidgetBase
{
    private static readonly CultureInfo Dutch = new("nl-NL");
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshEvery };
    private DateTime _lastUpdate = DateTime.MinValue;
    private double _latitude;
    private double _longitude;

    public WeatherWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override async void OnStart()
    {
        _timer.Start();
        if (DateTime.Now - _lastUpdate > RefreshEvery) await RefreshAsync();
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
        var data = await WeatherService.GetAsync(_latitude, _longitude);
        if (data == null)
        {
            // Oude gegevens laten staan als we die al hebben
            if (_lastUpdate == DateTime.MinValue)
                DescText.Text = "Geen weerdata. Controleer je internetverbinding.";
            return;
        }

        _lastUpdate = DateTime.Now;
        var (text, icon) = WeatherService.Describe(data.Code, data.IsDay);

        IconText.Text = icon;
        TempText.Text = Degrees(data.Temperature);
        DescText.Text = $"{text}, voelt als {Degrees(data.FeelsLike)}";
        DetailText.Text = $"Wind {Math.Round(data.Wind):0} km/u, luchtvochtigheid {data.Humidity}%";

        ForecastGrid.Children.Clear();
        foreach (var day in data.Days.Skip(1).Take(3))
            ForecastGrid.Children.Add(CreateDay(day));
    }

    private UIElement CreateDay(DayForecast day)
    {
        var panel = new StackPanel();

        string name = day.Date.ToString("dddd", Dutch);
        panel.Children.Add(new TextBlock
        {
            Text = char.ToUpper(name[0], Dutch) + name[1..],
            FontSize = 15,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });

        panel.Children.Add(new TextBlock
        {
            Text = WeatherService.Describe(day.Code).Icon,
            FontFamily = (FontFamily)FindResource("SymbolFont"),
            FontSize = 26,
            Margin = new Thickness(0, 6, 0, 6),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });

        var temps = new TextBlock { FontSize = 16 };
        temps.Inlines.Add(new Run(Degrees(day.Max)));
        temps.Inlines.Add(new Run("  " + Degrees(day.Min)) { Foreground = (Brush)FindResource("TextMutedBrush") });
        panel.Children.Add(temps);

        return panel;
    }

    // (int) voorkomt "-0°"
    private static string Degrees(double value) => $"{(int)Math.Round(value)}°";
}
