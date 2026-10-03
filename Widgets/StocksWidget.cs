using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Aandelen, indexen en crypto: koers, verandering vandaag en een grafiekje van de dag.</summary>
public sealed class StocksWidget : WidgetBase
{
    private static readonly Brush Up = Frozen(0x4C, 0xC9, 0x85);
    private static readonly Brush Down = Frozen(0xF0, 0x6E, 0x6E);

    private readonly StackPanel _rows = new();
    private readonly TextBlock _footer = new() { FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, StockQuote?> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _updated;
    private bool _loading;

    public StocksWidget()
    {
        var root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_rows);
        Grid.SetRow(_footer, 1);
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        root.Children.Add(_footer);
        Content = root;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private List<StockSymbol> Symbols => Config.Get("symbols", new List<StockSymbol>());

    protected override void OnAttached()
    {
        if (Config.Options.ContainsKey("symbols")) return;
        Config.Set("symbols", Loc.IsEnglish
            ? new List<StockSymbol> { new() { Symbol = "^GSPC", Name = "S&P 500" }, new() { Symbol = "BTC-USD", Name = "Bitcoin" } }
            : new List<StockSymbol> { new() { Symbol = "^AEX", Name = "AEX" }, new() { Symbol = "BTC-EUR", Name = "Bitcoin" } });
    }

    protected override async void OnStart()
    {
        _timer.Start();
        if (DateTime.Now - _updated > TimeSpan.FromMinutes(4)) await RefreshAsync();
        else Render();
    }

    protected override void OnStop() => _timer.Stop();

    public override async void OnSettingsChanged()
    {
        Render();
        await RefreshAsync();
    }

    public override void Refresh() => Render();

    private async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            foreach (var item in Symbols)
                _quotes[item.Symbol] = await StockService.GetQuoteAsync(item.Symbol, TimeSpan.FromMinutes(4));
            _updated = DateTime.Now;
            Render();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Render()
    {
        _rows.Children.Clear();
        var symbols = Symbols;
        if (symbols.Count == 0)
        {
            var empty = Text(Loc.T("Kies aandelen via het tandwieltje in de bewerkmodus."), 15, "TextSecondaryBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            _rows.Children.Add(empty);
            _footer.Text = "";
            return;
        }
        bool chart = Config.Get("chart", true);
        int missing = 0;
        foreach (var item in symbols)
        {
            _quotes.TryGetValue(item.Symbol, out var quote);
            if (quote == null) missing++;
            _rows.Children.Add(CreateRow(item, quote, chart));
        }
        _footer.Text = missing == symbols.Count && _updated != default
            ? Loc.T("Koersen ophalen lukt nu niet. IdleDash probeert het straks opnieuw.")
            : Loc.T("Via {0}, ongeveer 15 minuten vertraagd", StockService.SourceName);
    }

    private Grid CreateRow(StockSymbol item, StockQuote? quote, bool chart)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = chart ? new GridLength(72) : new GridLength(0) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(item.Name.Length > 0 ? item.Name : quote?.Name ?? item.Symbol, 16));
        names.Children.Add(Text(item.Symbol, 12, "TextMutedBrush"));
        row.Children.Add(names);

        double? change = quote?.ChangePercent;
        var color = change is < 0 ? Down : Up;
        if (chart && quote is { Points.Count: > 1 })
        {
            var line = Sparkline(quote.Points, quote.PreviousClose, color);
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
        }

        var values = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var price = Text(quote == null ? "–" : StockService.FormatPrice(quote.Price, quote.Currency), 16);
        price.HorizontalAlignment = HorizontalAlignment.Right;
        price.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        values.Children.Add(price);
        if (change != null)
        {
            values.Children.Add(new TextBlock
            {
                Text = StockService.FormatChange(change.Value),
                FontSize = 12.5,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }
        Grid.SetColumn(values, 2);
        row.Children.Add(values);
        return row;
    }

    /// <summary>Verloop van vandaag, met een stippellijn op de slotkoers van gisteren.</summary>
    private static FrameworkElement Sparkline(List<double> points, double? baseline, Brush stroke)
    {
        const double width = 64, height = 26;
        double min = points.Min(), max = points.Max();
        if (baseline is double b)
        {
            min = Math.Min(min, b);
            max = Math.Max(max, b);
        }
        double range = Math.Max(max - min, 1e-9);
        var canvas = new Canvas { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (baseline is double close)
        {
            double y = height - (close - min) / range * height;
            var dashed = new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } };
            dashed.SetResourceReference(Shape.StrokeProperty, "TextMutedBrush");
            canvas.Children.Add(dashed);
        }
        var line = new Polyline { Stroke = stroke, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        for (int i = 0; i < points.Count; i++)
            line.Points.Add(new Point(i * width / (points.Count - 1), height - (points[i] - min) / range * height));
        canvas.Children.Add(line);
        return canvas;
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        var symbols = Symbols;
        var list = new StackPanel();

        void Save()
        {
            Config.Set("symbols", symbols);
            saved();
        }

        void Build()
        {
            list.Children.Clear();
            if (symbols.Count == 0) list.Children.Add(Ui.Hint(Loc.T("Nog geen aandelen gekozen.")));
            foreach (var item in symbols.ToList())
                list.Children.Add(Ui.RemovableRow(item.Name.Length > 0 ? item.Name : item.Symbol, item.Symbol, () => { symbols.Remove(item); Save(); Build(); }));
        }

        void Add(string symbol, string name)
        {
            symbol = symbol.Trim().ToUpperInvariant();
            if (symbol.Length == 0 || symbols.Any(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))) return;
            symbols.Add(new StockSymbol { Symbol = symbol, Name = name.Trim() });
            Save();
            Build();
        }

        panel.Children.Add(Ui.Section(Loc.T("Aandelen")));
        Build();
        panel.Children.Add(list);

        panel.Children.Add(Ui.Label(Loc.T("Zoek een aandeel, index of munt")));
        var search = new TextBox();
        var status = Ui.Hint(Loc.T("Bijvoorbeeld ASML, Apple, AEX of Bitcoin. Een code zoals ASML.AS kun je ook direct toevoegen."));
        var results = new StackPanel();

        async void DoSearch()
        {
            string query = search.Text.Trim();
            if (query.Length < 2) return;
            status.Text = Loc.T("Zoeken…");
            results.Children.Clear();
            var found = await StockService.SearchAsync(query);
            status.Text = found.Count == 0 ? Loc.T("Niets gevonden. Weet je de code zeker, voeg hem dan direct toe.") : "";
            foreach (var result in found)
            {
                string label = result.Exchange != null ? $"{result.Name} ({result.Symbol}, {result.Exchange})" : $"{result.Name} ({result.Symbol})";
                var button = Ui.Styled(new Button { Content = label }, "MenuButton");
                button.Click += (_, _) =>
                {
                    Add(result.Symbol, result.Name);
                    results.Children.Clear();
                    search.Text = "";
                };
                results.Children.Add(button);
            }
        }

        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) DoSearch();
        };
        panel.Children.Add(search);
        panel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Zoeken"), DoSearch),
            Ui.Button(Loc.T("Code direct toevoegen"), () =>
            {
                Add(search.Text, "");
                search.Text = "";
            })));
        panel.Children.Add(status);
        panel.Children.Add(results);

        panel.Children.Add(Ui.Switch(Loc.T("Grafiekje van vandaag tonen"), null, Config.Get("chart", true), v => { Config.Set("chart", v); saved(); }));
        panel.Children.Add(Ui.Hint(Loc.T("Koersen komen van Yahoo Finance: gratis en zonder account, ongeveer 15 minuten vertraagd."), 14));
        return panel;
    }
}
