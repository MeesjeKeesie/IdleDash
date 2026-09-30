using System.Windows;
using System.Windows.Controls;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>Instellingen van één widget: het thema, plus wat de widget zelf aanbiedt.</summary>
public sealed class WidgetSettingsWindow : Window
{
    public WidgetSettingsWindow(WidgetHost host, AppSettings settings, string widgetName)
    {
        Ui.StyleWindow(this, 540, 680);
        Title = Loc.T("Instellingen: {0}", widgetName);

        var panel = new StackPanel { Margin = new Thickness(32, 24, 32, 32) };
        panel.Children.Add(Ui.Title(widgetName));

        // Thema van alleen deze widget
        panel.Children.Add(Ui.Section(Loc.T("Thema")));
        var themes = new List<(string, string?)> { (Loc.T("Zelfde als het dashboard"), null) };
        themes.AddRange(ThemePresets.All.Select(t => (Loc.T(t.Name), (string?)t.Name)));
        themes.AddRange(settings.CustomThemes.Select(t => (t.Name, (string?)t.Name)));
        panel.Children.Add(Ui.Combo(themes, host.Config.ThemeName, name =>
        {
            host.Config.ThemeName = name;
            host.ApplyTheme(name == null ? null : ThemeManager.Resolve(settings, name));
            settings.Save();
        }));
        panel.Children.Add(Ui.Hint(Loc.T("Eigen thema's maak je in de instellingen van IdleDash, onder Thema.")));

        // Wat de widget zelf te bieden heeft
        var extra = host.Widget.CreateSettings(() =>
        {
            settings.Save();
            host.Widget.OnSettingsChanged();
        });
        if (extra != null) panel.Children.Add(extra);

        var done = Ui.Button(Loc.T("Klaar"), Close, accent: true);
        done.Margin = new Thickness(0, 28, 0, 0);
        panel.Children.Add(done);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }
}
