using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Dagelijks feitje: "Vandaag in de geschiedenis" (Wikipedia) en een weetje. Elke dag iets anders.</summary>
public sealed class FactWidget : WidgetBase
{
    private readonly StackPanel _panel = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private DateTime _shownDate;
    private HistoryEvent? _event;
    private string? _historyError;
    private bool _loading;

    public FactWidget()
    {
        Content = _panel;
        _timer.Tick += async (_, _) =>
        {
            if (DateTime.Today != _shownDate) await LoadAsync();
        };
    }

    /// <summary>"both" (allebei), "history" of "fact".</summary>
    private string Mode => Config.Get("mode", "both");

    protected override void OnStart()
    {
        Render();
        _ = LoadAsync();
        _timer.Start();
    }

    protected override void OnStop() => _timer.Stop();
    public override void OnSettingsChanged() => _ = LoadAsync();
    public override void Refresh() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        _shownDate = DateTime.Today;
        if (Mode != "fact")
        {
            _loading = true;
            try
            {
                _event = OnThisDay.Pick(await OnThisDay.GetAsync(DateTime.Today, Loc.Language == "nl"), DateTime.Today);
                _historyError = _event == null ? Loc.T("Geen gebeurtenis gevonden voor vandaag.") : null;
            }
            catch
            {
                _event = null;
                _historyError = Loc.T("Wikipedia is nu niet bereikbaar.");
            }
            _loading = false;
        }
        Render();
    }

    private void Render()
    {
        _panel.Children.Clear();
        if (Mode != "fact")
        {
            _panel.Children.Add(Heading(_event != null ? Loc.T("Vandaag in {0}", _event.Year) : Loc.T("Vandaag in de geschiedenis"), 0));
            _panel.Children.Add(Body(_event?.Text ?? _historyError ?? (_loading ? Loc.T("Ophalen…") : "")));
        }
        if (Mode != "history")
        {
            var fact = Facts.ForDay(DateTime.Today);
            _panel.Children.Add(Heading(Loc.T("Wist je dat?"), Mode == "both" ? 18 : 0));
            _panel.Children.Add(Body(Loc.Language == "nl" ? fact.Nl : fact.En));
        }
        if (Mode != "fact" && _event != null)
        {
            var credit = Text(Loc.T("Bron: {0}", "Wikipedia"), 11.5, "TextMutedBrush");
            credit.Margin = new Thickness(0, 12, 0, 0);
            _panel.Children.Add(credit);
        }
    }

    private TextBlock Heading(string text, double top)
    {
        var heading = Text(text, 14, "TextSecondaryBrush");
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, top, 0, 4);
        return heading;
    }

    private TextBlock Body(string text)
    {
        var body = Text(text, 17, "TextPrimaryBrush");
        body.TextWrapping = TextWrapping.Wrap;
        body.LineHeight = 24;
        return body;
    }

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Label(Loc.T("Wat wil je zien?")));
        panel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Allebei"), "both"), (Loc.T("Vandaag in de geschiedenis"), "history"), (Loc.T("Alleen een weetje"), "fact"),
        }, Mode, v => { Config.Set("mode", v); saved(); }));
        panel.Children.Add(Ui.Hint(Loc.T("Vandaag in de geschiedenis komt van Wikipedia, in de taal van IdleDash. De weetjes zitten in IdleDash zelf en werken ook zonder internet.")));
        return panel;
    }
}
