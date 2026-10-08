using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IdleDash.Core;

namespace IdleDash;

/// <summary>De zijbalk van de instellingen: onderdelen, zoeken, en taal wisselen zonder het venster te sluiten.</summary>
public partial class SettingsWindow
{
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x7F, 0x8A, 0x9C));
    private static readonly Brush Selected = Frozen(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));
    private readonly List<(string Key, string Name, string Icon, StackPanel Page)> _pages = new();
    private string _currentPage = "general";

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void SetupNavigation()
    {
        _pages.Clear();
        _pages.Add(("general", "Algemeen", "\uE713", PageGeneral));
        _pages.Add(("look", "Uiterlijk", "\uE790", PageLook));
        _pages.Add(("calendars", "Agenda's", "\uE787", PageCalendars));
        _pages.Add(("weather", "Weer en focus", "\uE706", PageWeather));
        _pages.Add(("home", "Smarthome", "\uE80F", PageHome));
        _pages.Add(("music", "Muziek", "\uE8D6", PageMusic));
        _pages.Add(("backup", "Back-up", "\uE81C", PageBackup));
        _pages.Add(("about", "Over IdleDash", "\uE946", PageAbout));
        BuildNav();
        ShowPage(_settings.LastSettingsPage ?? "general");
    }

    private void BuildNav()
    {
        NavList.Children.Clear();
        foreach (var page in _pages)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = page.Icon,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 15,
                Width = 30,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock { Text = Loc.T(page.Name), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Style = (Style)FindResource("MenuButton"), Tag = page.Key, Margin = new Thickness(0, 0, 0, 2) };
            string key = page.Key;
            button.Click += (_, _) => ShowPage(key);
            NavList.Children.Add(button);
        }
    }

    private void ShowPage(string key)
    {
        int index = _pages.FindIndex(p => p.Key == key);
        var page = _pages[index < 0 ? 0 : index];
        _currentPage = page.Key;
        foreach (var p in _pages) p.Page.Visibility = p.Key == page.Key ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = Loc.T(page.Name);
        foreach (var button in NavList.Children.OfType<Button>())
            button.Background = (string)button.Tag == page.Key ? Selected : Brushes.Transparent;
        Scroller.ScrollToTop();
        if (_settings.LastSettingsPage != page.Key)
        {
            _settings.LastSettingsPage = page.Key;
            _settings.Save();
        }
    }

    // ─────────────────────────── Zoeken ───────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SearchResults.Children.OfType<Button>().FirstOrDefault() is { } first)
        {
            first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        string query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchResults.Children.Clear();
        bool searching = query.Length >= 2;
        NavList.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchResults.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (!searching) return;

        var seen = new HashSet<string>();
        foreach (var page in _pages)
        {
            foreach (var (element, text) in Labels(page.Page))
            {
                if (!text.Contains(query, StringComparison.CurrentCultureIgnoreCase) || !seen.Add(page.Key + "|" + text)) continue;
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = Loc.T(page.Name), FontSize = 12, Foreground = Muted });
                var button = new Button { Content = content, Style = (Style)FindResource("MenuButton"), Margin = new Thickness(0, 0, 0, 2) };
                string key = page.Key;
                var target = element;
                button.Click += (_, _) =>
                {
                    ShowPage(key);
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        target.BringIntoView();
                        Flash(target);
                    }));
                };
                SearchResults.Children.Add(button);
                if (SearchResults.Children.Count >= 15) return;
            }
        }
        if (SearchResults.Children.Count == 0)
            SearchResults.Children.Add(new TextBlock { Text = Loc.T("Niets gevonden."), Margin = new Thickness(10, 6, 0, 0), Foreground = Muted });
    }

    /// <summary>Kopjes, labels, schakelaars en knoppen in een onderdeel: daarin wordt gezocht.</summary>
    private IEnumerable<(FrameworkElement Element, string Text)> Labels(DependencyObject root)
    {
        var section = (Style)FindResource("SectionTitle");
        var label = (Style)FindResource("FieldLabel");
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node) continue;
            switch (node)
            {
                case TextBlock text when (text.Style == section || text.Style == label) && text.Text.Length > 0:
                    yield return (text, text.Text);
                    break;
                case CheckBox box when box.Content is StackPanel { Children.Count: > 0 } content && content.Children[0] is TextBlock title:
                    yield return (box, title.Text);
                    continue;
                case Button button when button.Content is string caption && button.Visibility == Visibility.Visible:
                    yield return (button, caption);
                    continue;
            }
            foreach (var found in Labels(node)) yield return found;
        }
    }

    /// <summary>Het gevonden onderdeel even laten knipperen, zodat je ziet waar het staat.</summary>
    private static void Flash(UIElement element) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(450)) { RepeatBehavior = new RepeatBehavior(3) });

    // ─────────────────────────── Taal ───────────────────────────

    /// <summary>Andere taal gekozen: alles in dit venster opnieuw vertalen, zonder het te sluiten.</summary>
    public void RefreshLanguage()
    {
        _suppress++;
        try
        {
            Loc.Apply(this, recordNew: false);
            foreach (var box in new[] { MonitorBox, DelayBox, NightStartBox, NightEndBox, DimBox, FocusBox, BreakBox }) box.Items.Clear();
        }
        finally
        {
            _suppress--;
        }
        LoadValues();
        BuildLanguageSection();
        BuildThemeSection();
        BuildAppleSection();
        BuildIcsSection();
        BuildSmartHomeSection();
        BuildDoorbellSection();
        BuildBackupSection();
        BuildSpotifySection();
        UpdateGoogleSection();
        RefreshUpdateSection();
        BuildNav();
        SearchBox.Text = "";
        ShowPage(_currentPage);
    }
}
