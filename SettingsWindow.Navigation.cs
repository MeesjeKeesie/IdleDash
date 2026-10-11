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
        BuildHotkeySection();
        Closed += (_, _) => SetRecording(false);
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

    // ─────────────────────────── Sneltoets ───────────────────────────

    private readonly StackPanel _hotkeyPanel = new();
    private bool _recording;
    private string? _hotkeyHint;

    private void BuildHotkeySection()
    {
        if (_hotkeyPanel.Parent == null) PageGeneral.Children.Add(_hotkeyPanel);
        _hotkeyPanel.Children.Clear();
        _hotkeyPanel.Children.Add(Ui.Section(Loc.T("Sneltoets")));
        _hotkeyPanel.Children.Add(Ui.Hint(Loc.T("Roep het dashboard op met een toetscombinatie, ook als er vensters op dat scherm staan. Druk nog een keer of op Esc om het weer te verbergen.")));
        string current = HotkeyText.Display(_settings.Hotkey);
        _hotkeyPanel.Children.Add(new TextBlock
        {
            Text = _recording ? Loc.T("Druk nu de toetsen die je wilt gebruiken… (Esc om te stoppen)") : current.Length > 0 ? current : Loc.T("Uit"),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        _hotkeyPanel.Children.Add(Ui.Row(
            Ui.Button(_recording ? Loc.T("Stoppen") : Loc.T("Wijzigen"), () => SetRecording(!_recording), accent: !_recording),
            Ui.Button(Loc.T("Uitzetten"), () => SaveHotkey("none"))));
        _hotkeyPanel.Children.Add(Ui.Hint(_hotkeyHint ?? Loc.T("Gebruik twee van Ctrl, Alt en Shift (of de Windows-toets) samen met een letter, cijfer of F-toets.")));
        if (!_recording && MainWindow.HotkeyProblem is string problem) _hotkeyPanel.Children.Add(Ui.Hint(problem));
    }

    private void SetRecording(bool on)
    {
        if (on == _recording) return;
        _recording = on;
        _hotkeyHint = null;
        var dashboard = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (on)
        {
            dashboard?.SuspendHotkey();   // anders vangt de oude sneltoets de toetsen weg
            PreviewKeyDown += RecordKey;
        }
        else
        {
            PreviewKeyDown -= RecordKey;
            dashboard?.ApplyHotkey();
        }
        BuildHotkeySection();
    }

    private void RecordKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            SetRecording(false);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var held = Keyboard.Modifiers;
        uint modifiers = (held.HasFlag(ModifierKeys.Control) ? HotkeyText.Ctrl : 0) | (held.HasFlag(ModifierKeys.Alt) ? HotkeyText.Alt : 0)
                       | (held.HasFlag(ModifierKeys.Shift) ? HotkeyText.Shift : 0) | (held.HasFlag(ModifierKeys.Windows) ? HotkeyText.Win : 0);
        uint code = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyText.IsAllowed(modifiers, code))
        {
            _hotkeyHint = Loc.T("Die combinatie kan niet. Gebruik twee van Ctrl, Alt en Shift (of de Windows-toets) samen met een letter, cijfer of F-toets.");
            BuildHotkeySection();
            return;
        }
        SaveHotkey(HotkeyText.Format(modifiers, code));
    }

    private void SaveHotkey(string value)
    {
        if (_recording)
        {
            PreviewKeyDown -= RecordKey;
            _recording = false;
        }
        _hotkeyHint = null;
        _settings.Hotkey = value;
        _settings.Save();
        Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()?.ApplyHotkey();
        BuildHotkeySection();
    }

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
        BuildHotkeySection();
        BuildNav();
        SearchBox.Text = "";
        ShowPage(_currentPage);
    }
}
