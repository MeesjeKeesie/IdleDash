using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>Aftelklok: één of meer aftellingen, zoals een vakantie of verjaardag.</summary>
public sealed class CountdownWidget : WidgetBase
{
    private readonly StackPanel _list = new();
    private readonly DispatcherTimer _timer = new();
    private List<CountdownItem> _items = new();
    private string _mode = "days";

    public CountdownWidget()
    {
        var root = new Grid { ClipToBounds = true };
        root.Children.Add(_list);
        Content = root;
        _timer.Tick += (_, _) => Render();
    }

    protected override void OnAttached() => Load();

    private void Load()
    {
        _mode = Config.Get("mode", "days");
        _items = Config.Get("items", new List<CountdownItem>());
        if (!Config.Options.ContainsKey("items"))
        {
            // Eerste keer: een voorbeeld, zodat je meteen ziet hoe het werkt
            _items = new List<CountdownItem>
            {
                new() { Name = Loc.T("Kerst"), Date = $"{DateTime.Today.Year}-12-25", Emoji = "🎄", Yearly = true },
            };
            Config.Set("items", _items);
        }
    }

    protected override void OnStart()
    {
        Render();
        _timer.Interval = _mode == "dhms" ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(20);
        _timer.Start();
    }

    protected override void OnStop() => _timer.Stop();

    public override void OnSettingsChanged()
    {
        Load();
        if (!IsRunning) return;
        _timer.Stop();
        OnStart();
    }

    public override void Refresh() => Render();

    private void Render()
    {
        _list.Children.Clear();
        var now = DateTime.Now;
        var upcoming = _items
            .Select(item => (Item: item, Next: Countdown.Next(item, now)))
            .Where(x => x.Next != null)
            .OrderBy(x => x.Next)
            .ToList();

        if (upcoming.Count == 0)
        {
            var empty = Text(Loc.T("Geen aftellingen. Voeg er een toe via het tandwieltje in de bewerkmodus."), 16, "TextSecondaryBrush");
            empty.TextWrapping = TextWrapping.Wrap;
            _list.Children.Add(empty);
            return;
        }

        if (upcoming.Count == 1)
        {
            // Eén aftelling: groot
            var (item, next) = upcoming[0];
            _list.Children.Add(Text($"{item.Emoji} {item.Name}".Trim(), 20, "TextSecondaryBrush"));
            var value = Text(Countdown.Format(next!.Value, Countdown.HasTime(item), now, _mode), 58);
            value.FontWeight = FontWeights.Light;
            value.Margin = new Thickness(0, 4, 0, 0);
            _list.Children.Add(value);
            _list.Children.Add(Text(Loc.Date(next.Value) + (Countdown.HasTime(item) ? ", " + Loc.Time(next.Value) : ""), 14, "TextMutedBrush"));
            return;
        }

        foreach (var (item, next) in upcoming)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var emoji = Text(item.Emoji, 20);
            emoji.FontFamily = (System.Windows.Media.FontFamily)FindResource("SymbolFont");
            var name = Text(item.Name, 17);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            var value = Text(Countdown.Format(next!.Value, Countdown.HasTime(item), now, _mode), 17, "TextSecondaryBrush");
            value.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(value, 2);

            row.Children.Add(emoji);
            row.Children.Add(name);
            row.Children.Add(value);
            _list.Children.Add(row);
        }
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        var rows = new StackPanel();

        void Save()
        {
            Config.Set("items", _items);
            saved();
        }

        void Build()
        {
            rows.Children.Clear();
            foreach (var item in _items.ToList())
            {
                var card = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
                var date = Ui.Field(DisplayDate(item.Date), _ => { });
                date.LostFocus += (_, _) =>
                {
                    if (TryParseDate(date.Text, out var d))
                    {
                        item.Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        Save();
                    }
                    date.Text = DisplayDate(item.Date);   // ongeldig: terug naar de vorige datum
                };
                card.Children.Add(Ui.Columns(
                    (Loc.T("Naam"), Ui.Field(item.Name, v => { item.Name = v.Trim(); Save(); }), "2*"),
                    (Loc.T("Datum"), date, "1.3*"),
                    (Loc.T("Tijd"), Ui.Field(item.Time ?? "", v => { item.Time = string.IsNullOrWhiteSpace(v) ? null : v.Trim(); Save(); }), "0.8*"),
                    (Loc.T("Emoji"), Ui.Field(item.Emoji, v => { item.Emoji = v.Trim(); Save(); }), "0.7*")));
                var yearly = Ui.Switch(Loc.T("Elk jaar (bv. een verjaardag)"), null, item.Yearly, v => { item.Yearly = v; Save(); });
                var remove = Ui.Button(Loc.T("Verwijderen"), () => { _items.Remove(item); Save(); Build(); });
                remove.Margin = new Thickness(0, 10, 0, 0);
                card.Children.Add(yearly);
                card.Children.Add(remove);
                rows.Children.Add(card);
            }
        }

        panel.Children.Add(Ui.Section(Loc.T("Aftellingen")));
        panel.Children.Add(Ui.Hint(Loc.T("Tijd mag leeg blijven: dan telt hij af tot die dag.")));
        Build();
        panel.Children.Add(rows);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Aftelling toevoegen"), () =>
        {
            _items.Add(new CountdownItem { Name = Loc.T("Nieuw"), Date = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            Save();
            Build();
        })));

        panel.Children.Add(Ui.Label(Loc.T("Weergave")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Alleen dagen"), "days"),
            (Loc.T("Dagen, uren en minuten"), "dhm"),
            (Loc.T("Met seconden"), "dhms"),
        }, _mode, v => { Config.Set("mode", v); saved(); }));
        return panel;
    }

    private static string DisplayDate(string iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d", Loc.Culture)
            : iso;

    private static bool TryParseDate(string text, out DateTime date) =>
        DateTime.TryParse(text.Trim(), Loc.Culture, DateTimeStyles.None, out date)
        || DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
