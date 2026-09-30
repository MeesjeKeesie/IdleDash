using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Nieuws en RSS: NOS, BBC of elke website met een feed. Klik op een kop om hem te openen.</summary>
public partial class NewsWidget : WidgetBase
{
    private static readonly HashSet<string> Read = new();   // aangeklikte koppen (deze sessie)

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(10) };
    private List<(FeedSource Source, List<FeedItem> Items)> _data = new();
    private DateTime _lastUpdate = DateTime.MinValue;
    private string _loadedFeeds = "";

    public NewsWidget(AppSettings settings)
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync(force: false);
    }

    protected override void OnAttached()
    {
        if (Config.Options.ContainsKey("feeds")) return;
        // Eerste keer (of widget uit 1.0/1.1): BBC voor Engels, anders de NOS-rubriek van vroeger
        var start = Loc.IsEnglish
            ? new FeedSource { Url = FeedService.Presets.First(p => p.Group == "BBC").Url, Name = "BBC News" }
            : FeedService.FromLegacyNos(Settings.NewsFeed);
        Config.Set("feeds", new List<FeedSource> { start });
    }

    private List<FeedSource> Feeds => Config.Get("feeds", new List<FeedSource>());

    protected override async void OnStart()
    {
        _timer.Start();
        if (DateTime.Now - _lastUpdate > TimeSpan.FromMinutes(10)) await RefreshAsync(force: false);
    }

    protected override void OnStop() => _timer.Stop();

    public override async void OnSettingsChanged()
    {
        // Alleen weergave-opties veranderd (of iets in de algemene instellingen)? Dan niets opnieuw downloaden.
        if (FeedsKey() == _loadedFeeds)
        {
            Render();
            return;
        }
        _lastUpdate = DateTime.MinValue;
        if (IsRunning) await RefreshAsync(force: false);   // nieuwe bronnen worden meteen opgehaald, bekende komen uit de cache
    }

    private string FeedsKey() => string.Join("|", Feeds.Select(f => f.Url));

    public override void Refresh() => Render();

    private async Task RefreshAsync(bool force)
    {
        var feeds = Feeds;
        var data = new List<(FeedSource, List<FeedItem>)>();
        foreach (var feed in feeds)
        {
            var items = await FeedService.GetAsync(feed, force ? TimeSpan.Zero : TimeSpan.FromMinutes(9));
            if (items != null) data.Add((feed, items));
        }
        if (data.Count == 0 && feeds.Count > 0 && _data.Count > 0) return;   // even geen internet: oude koppen laten staan
        _data = data;
        _lastUpdate = DateTime.Now;
        _loadedFeeds = FeedsKey();
        Render();
    }

    private void Render()
    {
        HeadlineList.Children.Clear();
        var feeds = Feeds;
        SourceText.Text = string.Join(", ", feeds.Select(f => f.Name));

        if (feeds.Count == 0)
        {
            ShowMessage(Loc.T("Kies een nieuwsbron via het tandwieltje in de bewerkmodus."));
            return;
        }
        if (_data.Count == 0)
        {
            ShowMessage(Loc.T("Nieuws ophalen lukt nu niet. Controleer je internetverbinding."));
            return;
        }
        MessageText.Visibility = Visibility.Collapsed;

        int max = Config.Get("max", 8);
        bool images = Config.Get("images", false);
        bool summary = Config.Get("summary", false);
        bool mixed = Config.Get("layout", "mixed") == "mixed" || _data.Count == 1;

        if (mixed)
        {
            foreach (var item in _data.SelectMany(d => d.Items).OrderByDescending(i => i.Published ?? DateTimeOffset.MinValue).Take(max))
                HeadlineList.Children.Add(CreateRow(item, images, summary, showSource: _data.Count > 1));
            return;
        }

        int perFeed = Math.Max(2, max / _data.Count);
        foreach (var (source, items) in _data)
        {
            var header = Text(source.Name, 13, "TextMutedBrush");
            header.Margin = new Thickness(8, HeadlineList.Children.Count == 0 ? 0 : 10, 8, 4);
            HeadlineList.Children.Add(header);
            foreach (var item in items.Take(perFeed)) HeadlineList.Children.Add(CreateRow(item, images, summary, showSource: false));
        }
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
    }

    private Button CreateRow(FeedItem item, bool images, bool summary, bool showSource)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Loc.Use12Hour ? 70 : 52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = Text(DescribeTime(item.Published), 14, "TextMutedBrush");
        time.Margin = new Thickness(0, 2, 0, 0);
        time.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;

        var texts = new StackPanel();
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
        texts.Children.Add(title);
        if (summary && item.Summary != null) texts.Children.Add(Text(item.Summary, 13, "TextSecondaryBrush"));
        if (showSource) texts.Children.Add(Text(item.Source, 12, "TextMutedBrush"));
        Grid.SetColumn(texts, 1);

        grid.Children.Add(time);
        grid.Children.Add(texts);

        if (images && item.ImageUrl != null && Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUri))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = imageUri;
                bitmap.DecodePixelWidth = 160;
                bitmap.EndInit();
                var picture = new Border
                {
                    Width = 72,
                    Height = 48,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(10, 2, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
                };
                Grid.SetColumn(picture, 2);
                grid.Children.Add(picture);
            }
            catch
            {
                // plaatje niet te laden: dan zonder
            }
        }

        var button = new Button
        {
            Content = grid,
            Style = (Style)FindResource("RowButton"),
            ToolTip = item.Link.Length > 0 ? Loc.T("Openen in je browser") : null,
            Margin = new Thickness(0, 0, 0, 2),
            Opacity = Read.Contains(item.Link) ? 0.5 : 1,
        };
        button.Click += (_, _) =>
        {
            Browser.Open(item.Link);
            Read.Add(item.Link);
            button.Opacity = 0.5;
        };
        return button;
    }

    private static string DescribeTime(DateTimeOffset? published)
    {
        if (published == null) return "";
        var local = published.Value.LocalDateTime;
        return local.Date == DateTime.Today ? Loc.Time(local) : local.ToString("ddd", Loc.Culture);
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        var feeds = Feeds;
        var list = new StackPanel();

        void Save()
        {
            Config.Set("feeds", feeds);
            saved();
        }

        void Build()
        {
            list.Children.Clear();
            if (feeds.Count == 0) list.Children.Add(Ui.Hint(Loc.T("Nog geen bron gekozen.")));
            foreach (var feed in feeds.ToList())
                list.Children.Add(Ui.RemovableRow(feed.Name, feed.Url, () => { feeds.Remove(feed); Save(); Build(); }));
        }

        panel.Children.Add(Ui.Section(Loc.T("Bronnen")));
        Build();
        panel.Children.Add(list);

        // Kant-en-klare bronnen (eerst die in je eigen taal)
        panel.Children.Add(Ui.Label(Loc.T("Toevoegen uit de lijst")));
        var presets = FeedService.Presets
            .OrderBy(p => p.Language == (Loc.IsEnglish ? "en" : "nl") ? 0 : 1)
            .Select(p => (p.Name, (FeedPreset?)p))
            .ToList();
        FeedPreset? chosen = presets[0].Item2;
        var presetBox = Ui.Combo(presets, chosen, p => chosen = p);
        panel.Children.Add(presetBox);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Toevoegen"), () =>
        {
            if (chosen == null || feeds.Any(f => f.Url == chosen.Url)) return;
            feeds.Add(new FeedSource { Url = chosen.Url, Name = chosen.Name });
            Save();
            Build();
        })));

        // Eigen website of feed
        panel.Children.Add(Ui.Label(Loc.T("Of een eigen website of feed-adres")));
        var address = new TextBox();
        var status = Ui.Hint(Loc.T("Bijvoorbeeld tweakers.net, een YouTube-kanaal of reddit.com/r/…"));
        panel.Children.Add(address);
        panel.Children.Add(status);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Zoeken en toevoegen"), async () =>
        {
            if (address.Text.Trim().Length < 3) return;
            status.Text = Loc.T("Zoeken…");
            var found = await FeedService.DiscoverAsync(address.Text);
            if (found == null)
            {
                status.Text = Loc.T("Geen feed gevonden op dit adres.");
                return;
            }
            if (!feeds.Any(f => f.Url == found.Value.Url))
            {
                feeds.Add(new FeedSource { Url = found.Value.Url, Name = found.Value.Title });
                Save();
                Build();
            }
            status.Text = Loc.T("Toegevoegd: {0}", found.Value.Title);
            address.Text = "";
        })));

        panel.Children.Add(Ui.Section(Loc.T("Weergave")));
        panel.Children.Add(Ui.Columns(
            (Loc.T("Bij meerdere bronnen"), Ui.Combo(new[]
            {
                (Loc.T("Alles door elkaar, nieuwste eerst"), "mixed"), (Loc.T("Per bron"), "grouped"),
            }, Config.Get("layout", "mixed"), v => { Config.Set("layout", v); saved(); }), "2*"),
            (Loc.T("Aantal koppen"), Ui.Combo(new[] { ("5", 5), ("8", 8), ("10", 10), ("15", 15) },
                Config.Get("max", 8), v => { Config.Set("max", v); saved(); }), "*")));
        panel.Children.Add(Ui.Switch(Loc.T("Plaatjes tonen"), null, Config.Get("images", false), v => { Config.Set("images", v); saved(); }));
        panel.Children.Add(Ui.Switch(Loc.T("Korte samenvatting tonen"), null, Config.Get("summary", false), v => { Config.Set("summary", v); saved(); }));
        return panel;
    }
}
