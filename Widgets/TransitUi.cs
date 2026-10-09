using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Een gekozen station of plaats (voor het vertrekbord of het midden van de treinkaart).</summary>
public sealed class StationChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
}

/// <summary>Gedeelde onderdelen voor de treinwidgets.</summary>
internal static class TransitUi
{
    /// <summary>Zoekveld voor een station (of ook een plaats), met de resultaten als knoppen eronder.</summary>
    public static FrameworkElement StationSearch(bool stopsOnly, Action<StationChoice> chosen)
    {
        var panel = new StackPanel();
        var box = new TextBox();
        var results = new StackPanel();
        var status = Ui.Hint("");
        panel.Children.Add(Ui.Label(stopsOnly ? Loc.T("Station zoeken") : Loc.T("Plaats of station zoeken")));
        panel.Children.Add(box);

        async Task SearchAsync()
        {
            if (box.Text.Trim().Length < 2) return;
            results.Children.Clear();
            status.Text = Loc.T("Zoeken…");
            try
            {
                var found = await TransitousApi.SearchAsync(box.Text, stopsOnly, Loc.Language);
                status.Text = found.Count == 0 ? Loc.T("Niets gevonden.") : "";
                foreach (var stop in found)
                {
                    string label = stop.Name + (stop.Area != null && stop.Area != stop.Name ? ", " + stop.Area : "") + (stop.Country != null ? " (" + stop.Country + ")" : "");
                    var button = Ui.Button(label, () =>
                    {
                        chosen(new StationChoice { Id = stop.Id, Name = stop.Name, Lat = stop.Lat, Lon = stop.Lon });
                        results.Children.Clear();
                        status.Text = Loc.T("Gekozen: {0}", stop.Name);
                    });
                    button.HorizontalAlignment = HorizontalAlignment.Left;
                    button.Margin = new Thickness(0, 6, 0, 0);
                    results.Children.Add(button);
                }
            }
            catch
            {
                status.Text = Loc.T("Zoeken lukte niet. Controleer je internetverbinding.");
            }
        }

        box.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await SearchAsync();
        };
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Zoeken"), async () => await SearchAsync())));
        panel.Children.Add(status);
        panel.Children.Add(results);
        return panel;
    }

    /// <summary>Kleur van een lijn als penseel (of null als de vervoerder geen kleur opgeeft).</summary>
    public static SolidColorBrush? Brush(string? hex)
    {
        if (hex == null) return null;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Zwart of wit, afhankelijk van wat het best leesbaar is op deze achtergrond.</summary>
    public static Brush TextOn(SolidColorBrush background)
    {
        var c = background.Color;
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return luminance > 0.6 ? Brushes.Black : Brushes.White;
    }
}
