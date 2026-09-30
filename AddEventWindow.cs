using System.Windows;
using System.Windows.Controls;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash;

/// <summary>Venster om een afspraak toe te voegen aan een Google- of iCloud-agenda.</summary>
public sealed class AddEventWindow : Window
{
    public AddEventWindow(List<CalendarInfo> calendars)
    {
        Ui.StyleWindow(this, 480, 560);
        Title = Loc.T("Afspraak toevoegen");
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 28) };
        panel.Children.Add(Ui.Title(Loc.T("Afspraak toevoegen")));

        var title = new TextBox();
        panel.Children.Add(Ui.Label(Loc.T("Wat")));
        panel.Children.Add(title);

        string calendarKey = calendars[0].Key;
        panel.Children.Add(Ui.Label(Loc.T("Agenda")));
        panel.Children.Add(Ui.Combo(calendars.Select(c => ($"{c.Name} ({c.Source})", c.Key)), calendarKey, k => calendarKey = k));
        if (GoogleService.IsConnected && !GoogleService.CanWriteCalendar)
            panel.Children.Add(Ui.Hint(Loc.T("Je Google-agenda's staan hier niet tussen: koppel Google opnieuw in de instellingen om afspraken te kunnen toevoegen.")));

        // Datum: vandaag, morgen en de komende twee maanden
        var date = DateTime.Today;
        var dates = Enumerable.Range(0, 62).Select(i =>
        {
            var d = DateTime.Today.AddDays(i);
            string text = i == 0 ? Loc.T("Vandaag") : i == 1 ? Loc.T("Morgen") : Loc.Date(d, "ddd d MMMM");
            return (text, d);
        });
        var dateBox = Ui.Combo(dates, date, d => date = d);

        // Tijden per kwartier
        var times = Enumerable.Range(0, 96).Select(i => (Loc.Time(DateTime.Today.AddMinutes(i * 15)), i * 15)).ToList();
        int start = Math.Min(95 * 15, (DateTime.Now.Hour + 1) * 60);
        int end = Math.Min(95 * 15, start + 60);
        var startBox = Ui.Combo(times, start, v => start = v);
        var endBox = Ui.Combo(times, end, v => end = v);

        panel.Children.Add(Ui.Columns((Loc.T("Datum"), dateBox, "1.6*"), (Loc.T("Van"), startBox, "*"), (Loc.T("Tot"), endBox, "*")));

        bool allDay = false;
        panel.Children.Add(Ui.Switch(Loc.T("Hele dag"), null, false, v =>
        {
            allDay = v;
            startBox.IsEnabled = endBox.IsEnabled = !v;
        }));

        var status = Ui.Hint("");
        var add = Ui.Button(Loc.T("Toevoegen"), async () =>
        {
            string text = title.Text.Trim();
            if (text.Length == 0)
            {
                status.Text = Loc.T("Geef de afspraak een naam.");
                return;
            }
            var from = allDay ? date : date.AddMinutes(start);
            var until = allDay ? date.AddDays(1) : date.AddMinutes(end <= start ? start + 60 : end);
            status.Text = Loc.T("Toevoegen…");
            IsEnabled = false;
            try
            {
                await CalendarHub.AddEventAsync(calendarKey, text, from, until, allDay);
                DialogResult = true;
            }
            catch (UnauthorizedAccessException)
            {
                status.Text = Loc.T("Inloggen bij de agenda lukt niet. Controleer de koppeling in de instellingen.");
            }
            catch (Exception ex)
            {
                GoogleService.HandleError(ex);
                status.Text = Loc.T("Toevoegen lukte niet: {0}", ex.Message);
            }
            finally
            {
                IsEnabled = true;
            }
        }, accent: true);
        var row = Ui.Row(add, Ui.Button(Loc.T("Annuleren"), Close));
        row.Margin = new Thickness(0, 24, 0, 0);
        panel.Children.Add(row);
        panel.Children.Add(status);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Loaded += (_, _) => title.Focus();
    }
}
