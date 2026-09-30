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
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshEvery };
    private DateTime _lastUpdate = DateTime.MinValue;
    private double _latitude;
    private double _longitude;
    private WeatherData? _data;

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
                DescText.Text = Loc.T("Geen weerdata. Controleer je internetverbinding.");
            return;
        }

        _lastUpdate = DateTime.Now;
        _data = data;
        Render();
    }

    public override void Refresh() => Render();

    private void Render()
    {
        if (_data is not { } data) return;
        var (text, icon) = WeatherService.Describe(data.Code, data.IsDay);

        IconText.Text = icon;
        TempText.Text = Loc.Degrees(data.Temperature);
        DescText.Text = Loc.T("{0}, voelt als {1}", text, Loc.Degrees(data.FeelsLike));
        DetailText.Text = Loc.T("Wind {0}, luchtvochtigheid {1}%", Loc.Wind(data.Wind), data.Humidity);

        ForecastGrid.Children.Clear();
        foreach (var day in data.Days.Skip(1).Take(3))
            ForecastGrid.Children.Add(CreateDay(day));
    }

    private UIElement CreateDay(DayForecast day)
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = Loc.Date(day.Date, "dddd"),
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
        temps.Inlines.Add(new Run(Loc.Degrees(day.Max)));
        temps.Inlines.Add(new Run("  " + Loc.Degrees(day.Min)) { Foreground = (Brush)FindResource("TextMutedBrush") });
        panel.Children.Add(temps);

        return panel;
    }

}
