using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Je komende afspraken uit Google Agenda, Apple iCloud en agenda-links, gegroepeerd per dag.</summary>
public partial class CalendarWidget : WidgetBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private List<CalendarEvent> _events = new();
    private List<string> _problems = new();
    private bool _loading;
    private bool _hasData;
    private string _loadedKey = "";

    public CalendarWidget(AppSettings settings)
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private int Days => Config.Get("days", Settings.CalendarDays);
    private List<string> Hidden => Config.Get("hidden", new List<string>());

    protected override async void OnStart()
    {
        GoogleService.StateChanged += OnSourcesChanged;
        _timer.Start();
        await RefreshAsync();
    }

    protected override void OnStop()
    {
        GoogleService.StateChanged -= OnSourcesChanged;
        _timer.Stop();
    }

    private async void OnSourcesChanged() => await RefreshAsync();
    public override async void OnSettingsChanged()
    {
        if (SourcesKey() == _loadedKey && _hasData)
        {
            Render();
            return;
        }
        await RefreshAsync();
    }

    /// <summary>Alles waarvan de afspraken afhangen: keuzes van deze widget en de gekoppelde agenda's.</summary>
    private string SourcesKey() => string.Join("|",
        Days, string.Join(",", Hidden), Config.Get("showAdd", true), GoogleService.State,
        Settings.Apple?.Email, Settings.Apple?.Calendars.Count, string.Join(",", Settings.IcsFeeds.Select(f => f.Url)));
    public override void Refresh() => Render();

    private bool HasAnySource =>
        GoogleService.State is GoogleState.Connected or GoogleState.Connecting
        || Settings.Apple is { Calendars.Count: > 0 }
        || Settings.IcsFeeds.Count > 0;

    private async Task RefreshAsync()
    {
        if (_loading) return;
        if (!HasAnySource)
        {
            string text = GoogleService.DescribeProblem(Loc.T("je afspraken")) is string google && GoogleService.State == GoogleState.Expired
                ? google
                : Loc.T("Koppel een agenda in de instellingen: Google, Apple iCloud of een agenda-link.");
            ShowMessage(text, showButton: true);
            return;
        }
        if (GoogleService.State == GoogleState.Connecting && Settings.Apple == null && Settings.IcsFeeds.Count == 0)
        {
            ShowMessage(Loc.T("Verbinden met Google…"), showButton: false);
            return;
        }

        _loading = true;
        try
        {
            _loadedKey = SourcesKey();
            var (events, problems) = await CalendarHub.GetEventsAsync(Days, Hidden);
            _events = events;
            _problems = problems;
            _hasData = true;
            Render();
            var calendars = await CalendarHub.GetCalendarsAsync();
            AddButton.Visibility = Config.Get("showAdd", true) && calendars.Any(c => c.Writable) ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            if (!_hasData) ShowMessage(Loc.T("Je agenda ophalen lukt nu niet. Controleer je internetverbinding."), showButton: false);
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowMessage(string text, bool showButton)
    {
        EventList.Children.Clear();
        AddButton.Visibility = Visibility.Collapsed;
        ProblemText.Text = "";
        MessageText.Text = text;
        MessageButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void Render()
    {
        if (!_hasData) return;
        var now = DateTime.Now;
        var upcoming = _events.Where(e => e.End > now).ToList();
        ProblemText.Text = _problems.Count > 0 ? Loc.T("Niet gelukt: {0}", string.Join(", ", _problems)) : "";

        if (upcoming.Count == 0)
        {
            ShowMessage(Days == 1 ? Loc.T("Geen afspraken meer vandaag.") : Loc.T("Geen afspraken de komende {0} dagen.", Days), showButton: false);
            ProblemText.Text = _problems.Count > 0 ? Loc.T("Niet gelukt: {0}", string.Join(", ", _problems)) : "";
            return;
        }

        MessagePanel.Visibility = Visibility.Collapsed;
        EventList.Children.Clear();
        DateTime? currentDay = null;
        foreach (var item in upcoming)
        {
            var day = item.Start.Date < now.Date ? now.Date : item.Start.Date;   // gisteren begonnen en loopt nog: vandaag
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
        string text = day == DateTime.Today ? Loc.T("Vandaag")
            : day == DateTime.Today.AddDays(1) ? Loc.T("Morgen")
            : Loc.Date(day);
        var header = Text(text, 14, "TextSecondaryBrush");
        header.Margin = new Thickness(0, first ? 0 : 16, 40, 8);
        return header;
    }

    private Grid CreateEventRow(CalendarEvent item, DateTime day)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Loc.Use12Hour ? 96 : 76) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ParseColor(item.Color),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        string time = item.AllDay ? Loc.T("Hele dag")
            : item.Start.Date < day ? Loc.T("tot {0}", Loc.Time(item.End))
            : Loc.Time(item.Start);
        var timeText = Text(time, 15, "TextSecondaryBrush");
        timeText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(timeText, 1);

        var title = Text(item.Title, 16);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 2);

        row.Children.Add(dot);
        row.Children.Add(timeText);
        row.Children.Add(title);
        return row;
    }

    private Brush ParseColor(string? hex) =>
        ThemeColors.Parse(hex) is { } c ? new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)) : Res("BarBrush");

    private void MessageButton_Click(object sender, RoutedEventArgs e) => App.Instance.ShowSettings();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var calendars = (await CalendarHub.GetCalendarsAsync()).Where(c => c.Writable).ToList();
        if (calendars.Count == 0) return;
        if (new AddEventWindow(calendars).ShowDialog() == true) await RefreshAsync();
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Agenda")));
        panel.Children.Add(Ui.Label(Loc.T("Afspraken tonen voor")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Alleen vandaag"), 1), (Loc.T("3 dagen"), 3), (Loc.T("7 dagen"), 7), (Loc.T("14 dagen"), 14),
        }, Days, v => { Config.Set("days", v); saved(); }));
        panel.Children.Add(Ui.Switch(Loc.T("Knop om afspraken toe te voegen"), null, Config.Get("showAdd", true), v => { Config.Set("showAdd", v); saved(); }));

        panel.Children.Add(Ui.Label(Loc.T("Welke agenda's")));
        var list = new StackPanel();
        var status = Ui.Hint(Loc.T("Agenda's ophalen…"));
        panel.Children.Add(status);
        panel.Children.Add(list);
        panel.Children.Add(Ui.Hint(Loc.T("Google, Apple iCloud en agenda-links koppel je in de instellingen van IdleDash.")));

        panel.Loaded += async (_, _) =>
        {
            var calendars = await CalendarHub.GetCalendarsAsync();
            status.Text = calendars.Count == 0 ? Loc.T("Nog geen agenda gekoppeld.") : "";
            var hidden = Hidden;
            foreach (var calendar in calendars)
            {
                var box = Ui.Switch(calendar.Name, calendar.Source, !hidden.Contains(calendar.Key), on =>
                {
                    if (on) hidden.Remove(calendar.Key);
                    else if (!hidden.Contains(calendar.Key)) hidden.Add(calendar.Key);
                    Config.Set("hidden", hidden);
                    saved();
                });
                box.Margin = new Thickness(0, 8, 0, 0);
                list.Children.Add(box);
            }
        };
        return panel;
    }
}
