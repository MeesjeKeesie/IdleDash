using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;
using Path = System.IO.Path;

namespace IdleDash.Widgets;

/// <summary>
/// Treinkaart: waar de treinen in je regio nu rijden. Posities zijn geschat uit dienstregeling en vertragingen
/// (Transitous, internationaal). Zonder kaartsleutel tekent IdleDash het spoornetwerk zelf; met een gratis
/// CARTO-sleutel komt er een donkere of lichte kaart onder.
/// </summary>
public sealed class TrainMapWidget : WidgetBase
{
    private static readonly HttpClient TileClient = CreateTileClient();
    private static readonly string TileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleDash", "tiles");

    private readonly Canvas _tiles = new() { Opacity = 0.9 };
    private readonly Canvas _rails = new();
    private readonly Canvas _stations = new();
    private readonly Canvas _trains = new();
    private readonly TextBlock _credit = new() { FontSize = 10.5, Padding = new Thickness(6, 2, 6, 2) };
    private readonly Border _creditBox = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6, 0, 0, 0) };
    private readonly TextBlock _message = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _fetch = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<TrainSegment> _segments = new();
    private (double X, double Y) _origin;
    private string? _error;
    private bool _loaded;

    public TrainMapWidget()
    {
        var map = new Grid { ClipToBounds = true };
        foreach (var layer in new[] { _tiles, _rails, _stations, _trains }) map.Children.Add(layer);
        _creditBox.Child = _credit;
        _creditBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));
        _credit.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
        map.Children.Add(_creditBox);
        map.Children.Add(_message);
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Content = map;
        SizeChanged += (_, _) => Layout();
        _fetch.Tick += async (_, _) => await LoadAsync();
        _tick.Tick += (_, _) => PlaceTrains();
    }

    private StationChoice? Center => Config.Get<StationChoice?>("center", null);
    private int Zoom => Config.Get("zoom", 10);
    private string MapStyle => Config.Get("style", "dark");
    private bool Names => Config.Get("names", false);
    private bool HasTiles => MapStyle != "none" && !string.IsNullOrWhiteSpace(Settings.CartoKey);

    protected override void OnStart()
    {
        Layout();
        _ = LoadAsync();
        _fetch.Start();
        _tick.Start();
    }

    protected override void OnStop()
    {
        _fetch.Stop();
        _tick.Stop();
    }

    public override void OnSettingsChanged()
    {
        _segments.Clear();
        _loaded = false;
        Layout();
        _ = LoadAsync();
    }

    public override void Refresh() => _ = LoadAsync();

    // ─────────────────────────── Kaart opbouwen ───────────────────────────

    private void Layout()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width < 10 || height < 10) return;
        if (Center is not { } center)
        {
            foreach (var layer in new[] { _tiles, _rails, _stations, _trains }) layer.Children.Clear();
            ShowMessage(Loc.T("Kies het midden van de kaart via het tandwieltje."));
            _creditBox.Visibility = Visibility.Collapsed;
            return;
        }
        var (cx, cy) = WebMercator.ToPixel(center.Lat, center.Lon, Zoom);
        _origin = (cx - width / 2, cy - height / 2);
        DrawTiles(width, height);
        DrawNetwork();
        PlaceTrains();
        UpdateStatus();
    }

    private Point ToScreen(double lat, double lon)
    {
        var (x, y) = WebMercator.ToPixel(lat, lon, Zoom);
        return new Point(x - _origin.X, y - _origin.Y);
    }

    private void DrawTiles(double width, double height)
    {
        _tiles.Children.Clear();
        if (!HasTiles) return;
        string name = MapStyle == "light" ? "light_all" : "dark_all";
        int zoom = Zoom, count = 1 << zoom;
        int x0 = (int)Math.Floor(_origin.X / WebMercator.TileSize), y0 = (int)Math.Floor(_origin.Y / WebMercator.TileSize);
        int x1 = (int)Math.Floor((_origin.X + width) / WebMercator.TileSize), y1 = (int)Math.Floor((_origin.Y + height) / WebMercator.TileSize);
        for (int ty = y0; ty <= y1; ty++)
        {
            if (ty < 0 || ty >= count) continue;
            for (int tx = x0; tx <= x1; tx++)
            {
                var image = new Image { Width = WebMercator.TileSize, Height = WebMercator.TileSize, Stretch = Stretch.Fill };
                Canvas.SetLeft(image, tx * WebMercator.TileSize - _origin.X);
                Canvas.SetTop(image, ty * WebMercator.TileSize - _origin.Y);
                _tiles.Children.Add(image);
                _ = LoadTileAsync(image, name, zoom, ((tx % count) + count) % count, ty, Settings.CartoKey!);
            }
        }
    }

    /// <summary>Een kaarttegel uit de eigen opslag, of eenmalig ophalen bij CARTO (30 dagen bewaard).</summary>
    private static async Task LoadTileAsync(Image target, string name, int z, int x, int y, string key)
    {
        string file = Path.Combine(TileDir, name, z.ToString(), x.ToString(), y + ".png");
        try
        {
            if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30))
            {
                byte[] data = await TileClient.GetByteArrayAsync($"https://basemaps.cartocdn.com/rastertiles/{name}/{z}/{x}/{y}.png?key={Uri.EscapeDataString(key.Trim())}");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, data);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            target.Source = bitmap;
        }
        catch
        {
            // deze tegel overslaan; het spoor en de treinen blijven gewoon zichtbaar
        }
    }

    /// <summary>Het spoor (uit de routes van de treinen) en de belangrijkste stations.</summary>
    private void DrawNetwork()
    {
        _rails.Children.Clear();
        _stations.Children.Clear();
        if (_segments.Count == 0) return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var segment in _segments)
            {
                context.BeginFigure(ToScreen(segment.Lats[0], segment.Lons[0]), false, false);
                for (int i = 1; i < segment.Lats.Length; i++) context.LineTo(ToScreen(segment.Lats[i], segment.Lons[i]), true, true);
            }
        }
        geometry.Freeze();
        var rails = new System.Windows.Shapes.Path { Data = geometry, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Opacity = HasTiles ? 0.6 : 0.45 };
        rails.SetResourceReference(Shape.StrokeProperty, "TextMutedBrush");
        _rails.Children.Add(rails);

        var placed = new List<Point>();
        var stations = _segments.SelectMany(s => new[] { s.From, s.To })
            .Where(s => s.Name.Length > 0)
            .GroupBy(s => s.Name)
            .Select(g => g.First())
            .OrderByDescending(s => s.Importance);
        int dots = 0;
        foreach (var station in stations)
        {
            var point = ToScreen(station.Lat, station.Lon);
            if (point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight || dots++ > 80) continue;
            var dot = new Ellipse { Width = 5, Height = 5 };
            dot.SetResourceReference(Shape.FillProperty, "TextSecondaryBrush");
            Canvas.SetLeft(dot, point.X - 2.5);
            Canvas.SetTop(dot, point.Y - 2.5);
            _stations.Children.Add(dot);
            if (placed.Count >= 8 || placed.Any(p => Math.Abs(p.X - point.X) < 90 && Math.Abs(p.Y - point.Y) < 18)) continue;
            placed.Add(point);
            var label = Text(station.Name, 11.5, "TextSecondaryBrush");
            Canvas.SetLeft(label, point.X + 6);
            Canvas.SetTop(label, point.Y - 8);
            _stations.Children.Add(label);
        }
    }

    /// <summary>Elke seconde de treinen op hun geschatte plek zetten.</summary>
    private void PlaceTrains()
    {
        _trains.Children.Clear();
        if (_segments.Count == 0) return;
        var now = DateTimeOffset.Now;
        var moving = new HashSet<string>();
        var positions = new List<(TrainSegment Segment, double Lat, double Lon)>();
        foreach (var segment in _segments)
        {
            if (TransitousApi.PositionAt(segment, now) is not { } position) continue;
            positions.Add((segment, position.Lat, position.Lon));
            if (segment.Name.Length > 0) moving.Add(segment.Name);
        }
        // Treinen die over hooguit twee minuten vertrekken, wachten op het station
        foreach (var segment in _segments.Where(s => s.Departure > now && s.Departure - now < TimeSpan.FromMinutes(2) && !moving.Contains(s.Name)))
            positions.Add((segment, segment.From.Lat, segment.From.Lon));

        foreach (var (segment, lat, lon) in positions)
        {
            var point = ToScreen(lat, lon);
            if (point.X < -10 || point.Y < -10 || point.X > ActualWidth + 10 || point.Y > ActualHeight + 10) continue;
            var dot = new Ellipse { Width = 11, Height = 11, StrokeThickness = 2, ToolTip = segment.Name.Length > 0 ? $"{segment.Name} → {segment.To.Name}" : segment.To.Name };
            if (TransitUi.Brush(segment.Color) is { } color) dot.Fill = color;
            else dot.SetResourceReference(Shape.FillProperty, "BarBrush");
            dot.SetResourceReference(Shape.StrokeProperty, "TextPrimaryBrush");
            Canvas.SetLeft(dot, point.X - 5.5);
            Canvas.SetTop(dot, point.Y - 5.5);
            _trains.Children.Add(dot);
            if (!Names || segment.Name.Length == 0) continue;
            var label = Text(segment.Name, 10.5, "TextPrimaryBrush");
            Canvas.SetLeft(label, point.X + 8);
            Canvas.SetTop(label, point.Y - 7);
            _trains.Children.Add(label);
        }
    }

    // ─────────────────────────── Gegevens ───────────────────────────

    private async Task LoadAsync()
    {
        if (Center == null || ActualWidth < 10)
        {
            UpdateStatus();
            return;
        }
        // Iets ruimer dan het zichtbare stuk, zodat treinen niet ineens verschijnen aan de rand
        double marginX = ActualWidth * 0.1, marginY = ActualHeight * 0.1;
        var (north, west) = WebMercator.ToLatLon(_origin.X - marginX, _origin.Y - marginY, Zoom);
        var (south, east) = WebMercator.ToLatLon(_origin.X + ActualWidth + marginX, _origin.Y + ActualHeight + marginY, Zoom);
        try
        {
            _segments = await TransitousApi.GetTrainsAsync(south, west, north, east, Zoom, DateTimeOffset.Now);
            _error = null;
        }
        catch
        {
            _error = Loc.T("Treinen ophalen lukte niet");
        }
        _loaded = true;
        DrawNetwork();
        PlaceTrains();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (Center == null) return;
        if (_error != null && _segments.Count == 0) ShowMessage(_error);
        else if (!_loaded) ShowMessage(Loc.T("Ophalen…"));
        else if (_segments.Count == 0) ShowMessage(Loc.T("Er rijden nu geen treinen in dit gebied."));
        else _message.Visibility = Visibility.Collapsed;
        _creditBox.Visibility = Visibility.Visible;
        _credit.Text = HasTiles ? "Transitous  © OpenStreetMap contributors, © CARTO" : "Transitous";
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
    }

    private static HttpClient CreateTileClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd(TransitousApi.UserAgent);
        return client;
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Midden van de kaart")));
        var current = Ui.Hint(Center?.Name ?? Loc.T("Nog geen plek gekozen."));
        panel.Children.Add(current);
        panel.Children.Add(TransitUi.StationSearch(stopsOnly: false, choice =>
        {
            Config.Set("center", choice);
            saved();
            current.Text = choice.Name;
        }));

        panel.Children.Add(Ui.Section(Loc.T("Kaart")));
        panel.Children.Add(Ui.Label(Loc.T("Hoe ver uitgezoomd")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Dichtbij (stad)"), 12), (Loc.T("Regio"), 10), (Loc.T("Provincie"), 9), (Loc.T("Groot gebied"), 8),
        }, Zoom, v => { Config.Set("zoom", v); saved(); }));
        panel.Children.Add(Ui.Switch(Loc.T("Treinnummers tonen"), null, Names, v => { Config.Set("names", v); saved(); }));
        panel.Children.Add(Ui.Label(Loc.T("Ondergrond")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Alleen het spoor"), "none"), (Loc.T("Donkere kaart"), "dark"), (Loc.T("Lichte kaart"), "light"),
        }, MapStyle, v => { Config.Set("style", v); saved(); }));

        panel.Children.Add(Ui.Label(Loc.T("Sleutel van CARTO (voor de donkere of lichte kaart)")));
        panel.Children.Add(Ui.Hint(Loc.T("Een sleutel is gratis voor niet-commercieel gebruik en heb je binnen een minuut. Zonder sleutel tekent IdleDash alleen het spoor en de stations.")));
        var key = new TextBox { Text = Settings.CartoKey ?? "" };
        key.LostFocus += (_, _) =>
        {
            string value = key.Text.Trim();
            if (value == (Settings.CartoKey ?? "")) return;
            Settings.CartoKey = value.Length > 0 ? value : null;
            saved();
        };
        panel.Children.Add(key);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Sleutel aanvragen"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://carto.com/basemaps/apikey/") { UseShellExecute = true }))));
        panel.Children.Add(Ui.Hint(Loc.T("Treinposities zijn geschat uit de dienstregeling en vertragingen (Transitous), geen GPS. Ze worden elke minuut ververst.")));
        return panel;
    }
}
