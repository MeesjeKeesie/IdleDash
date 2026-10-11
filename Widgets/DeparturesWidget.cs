using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Vertrekbord: de eerstvolgende treinen (of ook bus, tram en metro) van een station, met storingen. Internationaal via Transitous.</summary>
public sealed class DeparturesWidget : WidgetBase
{
    private readonly StackPanel _rows = new() { ClipToBounds = true };
    private readonly StackPanel _alertPanel = new();
    private readonly TextBlock _title = new() { FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _footer = new() { FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private List<Departure> _departures = new();
    private List<TransitAlert> _alerts = new();
    private string? _error;
    private DateTime _updated;

    public DeparturesWidget()
    {
        var root = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_alertPanel, Dock.Top);
        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_title);
        root.Children.Add(_alertPanel);
        root.Children.Add(_footer);
        root.Children.Add(_rows);
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Content = root;
        _timer.Tick += async (_, _) => await LoadAsync();
    }

    private StationChoice? Station => Config.Get<StationChoice?>("station", null);
    private int Count => Config.Get("count", 8);
    private bool TrainsOnly => Config.Get("trainsOnly", true);
    private bool ShowAlerts => Config.Get("alerts", true);

    protected override void OnStart()
    {
        Render();
        _ = LoadAsync();
        _timer.Start();
    }

    protected override void OnStop() => _timer.Stop();

    public override void OnSettingsChanged()
    {
        _departures.Clear();
        _alerts.Clear();
        _error = null;
        Render();
        _ = LoadAsync();
    }

    public override void Refresh() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (Station is not { } station)
        {
            Render();
            return;
        }
        try
        {
            var board = await TransitousApi.GetDeparturesAsync(station.Id, TrainsOnly ? 40 : Count + 6, Loc.Language);
            _departures = board.Departures
                .Where(d => !TrainsOnly || TransitousApi.TrainModes.Contains(d.Mode))
                .Where(d => (d.Expected ?? d.Planned) > DateTimeOffset.Now.AddMinutes(-1))
                .Take(Count)
                .ToList();
            _alerts = TransitousApi.AllAlerts(board.StopAlerts, _departures);
            _error = null;
            _updated = DateTime.Now;
        }
        catch
        {
            _error = Loc.T("Vertrektijden ophalen lukte niet");
        }
        Render();
    }

    private void Render()
    {
        _rows.Children.Clear();
        _alertPanel.Children.Clear();
        var station = Station;
        _title.Text = station?.Name ?? Loc.T("Vertrektijden");
        if (station == null)
        {
            _rows.Children.Add(Message(Loc.T("Kies een station via het tandwieltje.")));
            _footer.Text = "";
            return;
        }

        // Storingen en werkzaamheden: de ernstigste eerst, hooguit twee (de rest via "meer meldingen")
        if (ShowAlerts && _alerts.Count > 0)
        {
            var ordered = _alerts.OrderByDescending(a => a.IsSerious).ToList();
            foreach (var alert in ordered.Take(2)) _alertPanel.Children.Add(AlertRow(alert));
            if (ordered.Count > 2)
            {
                var more = Text(Loc.T("+{0} meer meldingen", ordered.Count - 2), 12, "TextMutedBrush");
                more.Margin = new Thickness(22, 0, 0, 4);
                more.ToolTip = string.Join("\n\n", ordered.Skip(2).Select(a => a.Header));
                _alertPanel.Children.Add(more);
            }
        }

        if (_departures.Count == 0) _rows.Children.Add(Message(_error ?? (_updated == default ? Loc.T("Ophalen…") : Loc.T("Geen vertrekken gevonden."))));
        foreach (var departure in _departures) _rows.Children.Add(Row(departure));
        string source = Loc.T("Bron: {0}", TransitousApi.Source);
        _footer.Text = _updated == default ? source : Loc.T("{0}, bijgewerkt {1}", source, Loc.Time(_updated));
        if (_error != null && _departures.Count > 0) _footer.Text = _error + ". " + _footer.Text;
    }

    private FrameworkElement AlertRow(TransitAlert alert)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var icon = Text("\uE7BA", 14, alert.IsSerious ? "BarWarnBrush" : "TextSecondaryBrush");
        icon.FontFamily = (FontFamily)FindResource("IconFont");
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var text = Text(alert.Header, 13.5, alert.IsSerious ? "TextPrimaryBrush" : "TextSecondaryBrush");
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxHeight = 38;
        row.Children.Add(text);
        if (alert.Description.Length > 0)
            row.ToolTip = new ToolTip { Content = new TextBlock { Text = alert.Description, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
        return row;
    }

    private FrameworkElement Message(string text)
    {
        var message = Text(text, 15, "TextSecondaryBrush");
        message.TextWrapping = TextWrapping.Wrap;
        message.Margin = new Thickness(0, 8, 0, 0);
        return message;
    }

    private FrameworkElement Row(Departure d)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        foreach (double width in new[] { 56.0, 36.0, 84.0 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = Text(Loc.Time(d.Planned.LocalDateTime), 16, d.Cancelled ? "TextMutedBrush" : "TextPrimaryBrush");
        if (d.Cancelled) time.TextDecorations = TextDecorations.Strikethrough;
        Add(grid, time, 0);
        if (!d.Cancelled && d.DelayMinutes > 0) Add(grid, Text("+" + d.DelayMinutes, 14, "BarWarnBrush"), 1);

        // Lijn als gekleurd label (in de kleur van de vervoerder, als die er een heeft)
        var badge = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
        var lineText = new TextBlock { Text = d.Line, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        if (TransitUi.Brush(d.Color) is { } color)
        {
            badge.Background = color;
            lineText.Foreground = TransitUi.TextOn(color);
        }
        else
        {
            badge.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
            lineText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        }
        badge.Child = lineText;
        badge.VerticalAlignment = VerticalAlignment.Center;
        Add(grid, badge, 2);

        // Bestemming, met een waarschuwingsteken als er een melding bij deze trein hoort
        var destinationRow = new DockPanel();
        if (ShowAlerts && d.Alerts.Count > 0)
        {
            var warning = Text("\uE7BA", 12, d.Alerts.Any(a => a.IsSerious) ? "BarWarnBrush" : "TextMutedBrush");
            warning.FontFamily = (FontFamily)FindResource("IconFont");
            warning.Margin = new Thickness(6, 0, 0, 0);
            warning.VerticalAlignment = VerticalAlignment.Center;
            warning.ToolTip = string.Join("\n\n", d.Alerts.Select(a => a.Header));
            DockPanel.SetDock(warning, Dock.Right);
            destinationRow.Children.Add(warning);
        }
        var destination = Text(d.Cancelled ? Loc.T("{0} (rijdt niet)", d.Destination) : d.Destination, 15, d.Cancelled ? "TextMutedBrush" : "TextPrimaryBrush");
        destination.TextTrimming = TextTrimming.CharacterEllipsis;
        destinationRow.Children.Add(destination);
        Add(grid, destinationRow, 3);

        if (d.Track != null)
        {
            string track = TransitousApi.TrainModes.Contains(d.Mode) ? Loc.T("spoor {0}", d.Track) : d.Track;
            var trackText = Text(track, 13, d.TrackChanged ? "BarWarnBrush" : "TextSecondaryBrush");
            trackText.Margin = new Thickness(8, 0, 0, 0);
            if (d.TrackChanged) trackText.ToolTip = Loc.T("Ander spoor dan gepland");
            Add(grid, trackText, 4);
        }
        return grid;
    }

    private static void Add(Grid grid, FrameworkElement element, int column)
    {
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Station")));
        var current = Ui.Hint(Station?.Name ?? Loc.T("Nog geen station gekozen."));
        panel.Children.Add(current);
        panel.Children.Add(TransitUi.StationSearch(stopsOnly: true, choice =>
        {
            Config.Set("station", choice);
            saved();
            current.Text = choice.Name;
        }));

        panel.Children.Add(Ui.Section(Loc.T("Weergave")));
        panel.Children.Add(Ui.Switch(Loc.T("Alleen treinen"), Loc.T("Zet uit om ook bus, tram, metro en veerboot te zien."), TrainsOnly, v => { Config.Set("trainsOnly", v); saved(); }));
        panel.Children.Add(Ui.Switch(Loc.T("Storingen en werkzaamheden tonen"), Loc.T("Meldingen van de vervoerder bovenaan het bord. Wijs een melding aan voor de hele uitleg."), ShowAlerts, v => { Config.Set("alerts", v); saved(); }));
        panel.Children.Add(Ui.Label(Loc.T("Aantal vertrekken")));
        panel.Children.Add(Ui.Combo(new[] { ("5", 5), ("8", 8), ("10", 10), ("12", 12) }, Count, v => { Config.Set("count", v); saved(); }));
        panel.Children.Add(Ui.Hint(Loc.T("Vertrektijden komen van Transitous, een open en internationale bron. Vertragingen en spoorwijzigingen zie je waar de vervoerder ze doorgeeft.")));
        return panel;
    }
}
