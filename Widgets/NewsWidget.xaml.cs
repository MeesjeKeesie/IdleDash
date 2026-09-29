using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>De laatste nieuwskoppen van de NOS. Klik op een kop om het artikel in je browser te openen.</summary>
public partial class NewsWidget : WidgetBase
{
    private static readonly CultureInfo Dutch = new("nl-NL");

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(10) };
    private DateTime _lastUpdate = DateTime.MinValue;
    private string _loadedFeed = "";

    public NewsWidget(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override async void OnStart()
    {
        _timer.Start();
        if (DateTime.Now - _lastUpdate > TimeSpan.FromMinutes(10)) await RefreshAsync();
    }

    protected override void OnStop() => _timer.Stop();

    public override async void OnSettingsChanged()
    {
        if (_settings.NewsFeed == _loadedFeed) return;
        _lastUpdate = DateTime.MinValue;
        if (IsRunning) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        string feed = _settings.NewsFeed;
        var items = await NewsService.GetAsync(feed);
        if (items == null)
        {
            if (_lastUpdate == DateTime.MinValue)
            {
                HeadlineList.Children.Clear();
                MessageText.Text = "Nieuws ophalen lukt nu niet. Controleer je internetverbinding.";
                MessageText.Visibility = Visibility.Visible;
            }
            return;
        }

        _lastUpdate = DateTime.Now;
        _loadedFeed = feed;
        SourceText.Text = "NOS " + NewsService.FeedName(feed);
        MessageText.Visibility = Visibility.Collapsed;
        HeadlineList.Children.Clear();
        foreach (var item in items.Take(12))
            HeadlineList.Children.Add(CreateRow(item));
    }

    private Button CreateRow(NewsItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = DescribeTime(item.Published),
            FontSize = 14,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
        };

        // Maximaal twee regels, daarna "…"
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis,
            LineHeight = 23,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = 46,
        };
        Grid.SetColumn(title, 1);

        grid.Children.Add(time);
        grid.Children.Add(title);

        var button = new Button
        {
            Content = grid,
            Style = (Style)FindResource("RowButton"),
            ToolTip = "Openen op nos.nl",
            Margin = new Thickness(0, 0, 0, 2),
        };
        button.Click += (_, _) => OpenLink(item.Link);
        return button;
    }

    private static string DescribeTime(DateTimeOffset? published)
    {
        if (published == null) return "";
        var local = published.Value.LocalDateTime;
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("ddd", Dutch);
    }

    private static void OpenLink(string link)
    {
        if (!link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch
        {
            // geen standaardbrowser ingesteld
        }
    }
}
