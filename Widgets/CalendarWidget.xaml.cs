using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Je komende afspraken uit Google Agenda, gegroepeerd per dag.</summary>
public partial class CalendarWidget : WidgetBase
{
    private static readonly CultureInfo Dutch = new("nl-NL");

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _loading;
    private bool _hasData;

    public CalendarWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override async void OnStart()
    {
        GoogleService.StateChanged += OnGoogleStateChanged;
        _timer.Start();
        await RefreshAsync();
    }

    protected override void OnStop()
    {
        GoogleService.StateChanged -= OnGoogleStateChanged;
        _timer.Stop();
    }

    private async void OnGoogleStateChanged() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_loading) return;
        if (ShowProblemIfAny()) return;

        _loading = true;
        try
        {
            var events = await GoogleService.GetEventsAsync(_settings.CalendarDays);
            _hasData = true;
            Render(events);
        }
        catch (Exception ex)
        {
            GoogleService.HandleError(ex);
            if (!ShowProblemIfAny() && !_hasData)
                ShowMessage("Je agenda ophalen lukt nu niet. Controleer je internetverbinding.", showButton: false);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Toont een melding als de Google-koppeling niet werkt. Geeft true terug als dat zo is.</summary>
    private bool ShowProblemIfAny()
    {
        string? problem = GoogleService.DescribeProblem("je afspraken");
        if (problem == null) return false;
        _hasData = false;
        ShowMessage(problem, showButton: GoogleService.State != GoogleState.Connecting);
        return true;
    }

    private void ShowMessage(string text, bool showButton)
    {
        EventList.Children.Clear();
        MessageText.Text = text;
        MessageButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void Render(List<CalendarEvent> events)
    {
        var now = DateTime.Now;
        var upcoming = events.Where(e => e.End > now).ToList();
        if (upcoming.Count == 0)
        {
            ShowMessage(_settings.CalendarDays == 1
                ? "Geen afspraken meer vandaag."
                : $"Geen afspraken de komende {_settings.CalendarDays} dagen.", showButton: false);
            return;
        }

        MessagePanel.Visibility = Visibility.Collapsed;
        EventList.Children.Clear();

        DateTime? currentDay = null;
        foreach (var item in upcoming)
        {
            // Een afspraak die gisteren begon en nog loopt, hoort bij vandaag
            var day = item.Start.Date < now.Date ? now.Date : item.Start.Date;
            if (day != currentDay)
            {
                currentDay = day;
                EventList.Children.Add(CreateDayHeader(day, first: EventList.Children.Count == 0));
            }
            EventList.Children.Add(CreateEventRow(item, day));
        }
    }

    private TextBlock CreateDayHeader(DateTime day, bool first)
    {
        string text = day == DateTime.Today ? "Vandaag"
            : day == DateTime.Today.AddDays(1) ? "Morgen"
            : Capitalize(day.ToString("dddd d MMMM", Dutch));

        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, first ? 0 : 16, 0, 8),
        };
    }

    private Grid CreateEventRow(CalendarEvent item, DateTime day)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Stipje in de kleur van de agenda
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ParseColor(item.Color),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        string time = item.AllDay ? "Hele dag"
            : item.Start.Date < day ? "tot " + item.End.ToString("HH:mm")
            : item.Start.ToString("HH:mm");

        var timeText = new TextBlock
        {
            Text = time,
            FontSize = 15,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(timeText, 1);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        Grid.SetColumn(title, 2);

        row.Children.Add(dot);
        row.Children.Add(timeText);
        row.Children.Add(title);
        return row;
    }

    private Brush ParseColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // onbekende kleur: standaardkleur gebruiken
        }
        return (Brush)FindResource("BarBrush");
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Dutch) + text[1..];

    private void MessageButton_Click(object sender, RoutedEventArgs e) => App.Instance.ShowSettings();
}
