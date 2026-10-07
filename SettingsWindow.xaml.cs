using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash;

/// <summary>
/// Instellingen. Elke wijziging wordt meteen opgeslagen en doorgegeven aan het dashboard.
/// De onderdelen taal, thema, Apple, agenda-links en smarthome staan in SettingsWindow.Sections.cs.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _resetLayout;
    private int _suppress;          // > 0: we vullen zelf de velden, dus niet opslaan
    private bool _confirmReset;

    public SettingsWindow(AppSettings settings, Action resetLayout)
    {
        InitializeComponent();
        _settings = settings;
        _resetLayout = resetLayout;

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/IdleDash.ico"));
        }
        catch
        {
            // zonder icoon werkt het ook
        }

        Ui.KeepOnScreen(this);   // titelbalk altijd in beeld, ook op een klein scherm
        Loc.Apply(this);
        LoadValues();
        BuildLanguageSection();
        BuildThemeSection();
        BuildAppleSection();
        BuildIcsSection();
        BuildSmartHomeSection();
        BuildDoorbellSection();
        BuildBackupSection();
        BuildSpotifySection();
        SpotifyService.StateChanged += BuildSpotifySection;
        Closed += (_, _) => SpotifyService.StateChanged -= BuildSpotifySection;
        NtfyService.StatusChanged += UpdateDoorbellStatus;
        Closed += (_, _) => NtfyService.StatusChanged -= UpdateDoorbellStatus;

        UpdateGoogleSection();
        GoogleService.StateChanged += UpdateGoogleSection;
        Closed += (_, _) => GoogleService.StateChanged -= UpdateGoogleSection;

        RefreshUpdateSection();
        UpdateManager.StateChanged += RefreshUpdateSection;
        Closed += (_, _) => UpdateManager.StateChanged -= RefreshUpdateSection;
        if (UpdateManager.State == UpdateState.Idle) _ = UpdateManager.CheckAsync(userInitiated: false);
    }

    /// <summary>Hoe ver je naar beneden had gescrold (om na een taalwissel op dezelfde plek terug te komen).</summary>
    public double ScrollOffset
    {
        get => Scroller.VerticalOffset;
        set => Loaded += (_, _) => Scroller.ScrollToVerticalOffset(value);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => Ui.DarkTitleBar(this);

    // ─────────────────────────── Velden vullen ───────────────────────────

    private void LoadValues()
    {
        _suppress++;
        try
        {
            AddOption(MonitorBox, Loc.T("Automatisch (je bovenste extra scherm)"), null, _settings.MonitorDeviceName == null);
            var monitors = MonitorHelper.GetAll();
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            foreach (var monitor in monitors.OrderBy(m => m.Bounds.Top).ThenBy(m => m.Bounds.Left))
                AddOption(MonitorBox, DescribeMonitor(monitor, primary), monitor.DeviceName,
                    string.Equals(monitor.DeviceName, _settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            EnsureSelection(MonitorBox);

            foreach (double seconds in new[] { 1.0, 2, 3, 5, 10, 30 })
                AddOption(DelayBox, seconds == 1 ? Loc.T("1 seconde") : Loc.T("{0} seconden", seconds), seconds,
                    Math.Abs(_settings.ShowDelaySeconds - seconds) < 0.01);
            EnsureSelection(DelayBox, fallbackIndex: 1);
            PixelShiftSwitch.IsChecked = _settings.PixelShift;

            NightSwitch.IsChecked = _settings.NightModeEnabled;
            for (int minutes = 0; minutes < 24 * 60; minutes += 30)
            {
                string time = $"{minutes / 60:00}:{minutes % 60:00}";
                string label = Loc.Time(DateTime.Today.AddMinutes(minutes));
                AddOption(NightStartBox, label, time, time == _settings.NightStart);
                AddOption(NightEndBox, label, time, time == _settings.NightEnd);
            }
            EnsureSelection(NightStartBox, fallbackIndex: 46);
            EnsureSelection(NightEndBox, fallbackIndex: 14);
            foreach (var (text, percent) in new[] { (Loc.T("Een beetje"), 50), (Loc.T("Flink"), 70), (Loc.T("Heel donker"), 85), (Loc.T("Helemaal zwart"), 100) })
                AddOption(DimBox, text, percent, _settings.NightDimPercent == percent);
            EnsureSelection(DimBox, fallbackIndex: 1);
            NightFields.IsEnabled = _settings.NightModeEnabled;

            PlaceBox.Text = _settings.WeatherPlace;
            PlaceStatus.Text = Loc.T("Nu ingesteld: {0}", _settings.WeatherPlace);

            foreach (int minutes in new[] { 15, 20, 25, 30, 45, 50, 60, 90 })
                AddOption(FocusBox, Loc.T("{0} minuten", minutes), minutes, _settings.FocusMinutes == minutes);
            EnsureSelection(FocusBox, fallbackIndex: 2);
            foreach (int minutes in new[] { 3, 5, 10, 15, 20 })
                AddOption(BreakBox, Loc.T("{0} minuten", minutes), minutes, _settings.BreakMinutes == minutes);
            EnsureSelection(BreakBox, fallbackIndex: 1);

            AutoUpdateSwitch.IsChecked = _settings.AutoUpdate;
            AutostartSwitch.IsChecked = Autostart.IsEnabled;
            UpdateAutostartHint();
            IgnoredBox.Text = string.Join(", ", _settings.IgnoredProcesses);
        }
        finally
        {
            _suppress--;
        }
    }

    private static void AddOption(ComboBox box, string text, object? value, bool selected)
    {
        var item = new ComboBoxItem { Content = text, Tag = value };
        box.Items.Add(item);
        if (selected) box.SelectedItem = item;
    }

    private static void EnsureSelection(ComboBox box, int fallbackIndex = 0)
    {
        if (box.SelectedIndex < 0 && box.Items.Count > 0)
            box.SelectedIndex = Math.Min(fallbackIndex, box.Items.Count - 1);
    }

    private static T GetTag<T>(ComboBox box, T fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;

    private static string DescribeMonitor(MonitorInfo monitor, MonitorInfo? primary)
    {
        string number = monitor.DeviceName.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase);
        var b = monitor.Bounds;
        string where = monitor.IsPrimary ? Loc.T("hoofdscherm")
            : primary == null ? ""
            : b.Bottom <= primary.Bounds.Top ? Loc.T("boven")
            : b.Top >= primary.Bounds.Bottom ? Loc.T("onder")
            : b.Right <= primary.Bounds.Left ? Loc.T("links")
            : b.Left >= primary.Bounds.Right ? Loc.T("rechts")
            : "";
        string text = Loc.T("Scherm {0}", number) + $" ({b.Width} × {b.Height}";
        return where.Length > 0 ? $"{text}, {where})" : text + ")";
    }

    // ─────────────────────────── Opslaan ───────────────────────────

    /// <summary>Eén handler voor alle gewone velden: alles uitlezen, opslaan en het dashboard laten bijwerken.</summary>
    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress > 0) return;

        _settings.MonitorDeviceName = GetTag<string?>(MonitorBox, null);
        _settings.ShowDelaySeconds = GetTag(DelayBox, 2.0);
        _settings.PixelShift = PixelShiftSwitch.IsChecked == true;
        _settings.NightModeEnabled = NightSwitch.IsChecked == true;
        _settings.NightStart = GetTag(NightStartBox, "23:00");
        _settings.NightEnd = GetTag(NightEndBox, "07:00");
        _settings.NightDimPercent = GetTag(DimBox, 70);
        NightFields.IsEnabled = _settings.NightModeEnabled;
        _settings.FocusMinutes = GetTag(FocusBox, 25);
        _settings.BreakMinutes = GetTag(BreakBox, 5);
        _settings.IgnoredProcesses = IgnoredBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool autoUpdateTurnedOn = AutoUpdateSwitch.IsChecked == true && !_settings.AutoUpdate;
        _settings.AutoUpdate = AutoUpdateSwitch.IsChecked == true;
        _settings.NotifyChanged();
        if (autoUpdateTurnedOn && UpdateManager.State == UpdateState.Available)
            _ = UpdateManager.CheckAsync(userInitiated: false);
    }

    private async void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_confirmReset)
        {
            _confirmReset = true;
            ResetLayoutButton.Content = Loc.T("Weet je het zeker? Klik nog een keer");
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (_confirmReset)
            {
                _confirmReset = false;
                ResetLayoutButton.Content = Loc.T("Standaardindeling herstellen");
            }
            return;
        }
        _confirmReset = false;
        _resetLayout();
        ResetLayoutButton.Content = Loc.T("Indeling hersteld");
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (!_confirmReset) ResetLayoutButton.Content = Loc.T("Standaardindeling herstellen");
    }

    // ─────────────────────────── Plaats zoeken ───────────────────────────

    private async void PlaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchPlaceAsync();
    }

    private async void SearchPlaceButton_Click(object sender, RoutedEventArgs e) => await SearchPlaceAsync();

    private async Task SearchPlaceAsync()
    {
        string query = PlaceBox.Text.Trim();
        if (query.Length < 2) return;
        PlaceStatus.Text = Loc.T("Zoeken…");
        PlaceResults.Children.Clear();
        var places = await WeatherService.SearchPlacesAsync(query);
        if (places == null)
        {
            PlaceStatus.Text = Loc.T("Zoeken lukt nu niet. Controleer je internetverbinding.");
            return;
        }
        if (places.Count == 0)
        {
            PlaceStatus.Text = Loc.T("Niets gevonden voor \"{0}\".", query);
            return;
        }
        PlaceStatus.Text = Loc.T("Kies de juiste plaats:");
        foreach (var place in places)
        {
            var button = new Button { Content = place.Description, Style = (Style)FindResource("MenuButton") };
            button.Click += (_, _) => ChoosePlace(place);
            PlaceResults.Children.Add(button);
        }
    }

    private void ChoosePlace(Place place)
    {
        _settings.WeatherPlace = place.Name;
        _settings.WeatherLatitude = Math.Round(place.Latitude, 4);
        _settings.WeatherLongitude = Math.Round(place.Longitude, 4);
        _settings.NotifyChanged();
        PlaceBox.Text = place.Name;
        PlaceResults.Children.Clear();
        PlaceStatus.Text = Loc.T("Nu ingesteld: {0}", place.Description);
    }

    // ─────────────────────────── Google ───────────────────────────

    private void UpdateGoogleSection()
    {
        var state = GoogleService.State;
        bool hasKey = GoogleService.HasKey, builtIn = GoogleService.HasBuiltInKey, ownKey = GoogleService.HasOwnKey;
        bool connected = state == GoogleState.Connected;
        bool needsNewPermission = connected && !GoogleService.CanWriteCalendar;

        GoogleStatusText.Text = state switch
        {
            GoogleState.Connected => GoogleService.AccountEmail is string email ? Loc.T("Gekoppeld met {0}", email) : Loc.T("Gekoppeld"),
            GoogleState.Connecting => Loc.T("Wachten tot je inlogt in de browser…"),
            GoogleState.Expired => Loc.T("De koppeling is verlopen. Koppel opnieuw."),
            _ when !hasKey => Loc.T("Nog niet ingesteld"),
            _ => Loc.T("Nog niet gekoppeld"),
        };
        GoogleHintText.Text = state switch
        {
            GoogleState.Connected when needsNewPermission =>
                Loc.T("Nieuw: afspraken toevoegen vanaf het dashboard. Koppel opnieuw om daar toestemming voor te geven."),
            GoogleState.Connected => Loc.T("Je afspraken en taken staan op het dashboard."),
            GoogleState.Connecting => Loc.T("Er is een browservenster geopend. Log daar in en geef IdleDash toegang. Zegt Google dat de app niet geverifieerd is, klik dan op Geavanceerd en daarna op Ga naar IdleDash."),
            _ when !hasKey => Loc.T("Maak eerst een sleutelbestand aan in Google Cloud (stappen in ONTWIKKELEN.md) en kies het hier."),
            _ => Loc.T("Klik op Koppelen en log in met je Google-account."),
        };

        ConnectButton.Visibility = hasKey && !connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = state == GoogleState.Connecting ? Loc.T("Browser opnieuw openen") : Loc.T("Koppelen met Google");
        ReconnectButton.Visibility = needsNewPermission ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ChooseKeyButton.Content = builtIn ? Loc.T("Eigen sleutel (geavanceerd)…") : ownKey ? Loc.T("Ander sleutelbestand…") : Loc.T("Sleutelbestand kiezen…");
        ChooseKeyButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        RemoveKeyButton.Visibility = ownKey && builtIn && !connected ? Visibility.Visible : Visibility.Collapsed;
        GoogleCloudButton.Visibility = builtIn ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChooseKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Kies het sleutelbestand dat je bij Google hebt gedownload"),
            Filter = Loc.T("Google-sleutel") + " (*.json)|*.json",
        };
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) dialog.InitialDirectory = downloads;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            GoogleService.ImportKey(dialog.FileName);
        }
        catch (Exception ex)
        {
            GoogleHintText.Text = ex is InvalidDataException
                ? ex.Message
                : Loc.T("Dit bestand kon niet gelezen worden. Kies het .json-bestand dat je bij Google hebt gedownload.");
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await GoogleService.ConnectAsync();

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await GoogleService.DisconnectAsync();
        await GoogleService.ConnectAsync();
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e) => await GoogleService.DisconnectAsync();
    private void RemoveKeyButton_Click(object sender, RoutedEventArgs e) => GoogleService.RemoveOwnKey();
    private void GoogleCloudButton_Click(object sender, RoutedEventArgs e) => Browser.Open("https://console.cloud.google.com/auth/overview");

    // ─────────────────────────── Algemeen ───────────────────────────

    private void AutostartSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress > 0) return;
        try
        {
            if (AutostartSwitch.IsChecked == true) Autostart.Enable();
            else Autostart.Disable();
            UpdateAutostartHint();
        }
        catch (Exception ex)
        {
            AutostartHint.Text = Loc.T("Dat lukte niet: {0}", ex.Message);
        }
    }

    private void UpdateAutostartHint()
    {
        string? command = Autostart.RegisteredCommand;
        AutostartHint.Text = command == null ? Loc.T("IdleDash start vanzelf als je inlogt op Windows.") : Loc.T("Start: {0}", command.Trim('"'));
    }

    // ─────────────────────────── Over IdleDash ───────────────────────────

    private void RefreshUpdateSection()
    {
        var state = UpdateManager.State;
        string latest = UpdateManager.Latest?.Version ?? "";
        bool installed = AppInfo.IsInstalledCopy;

        VersionText.Text = AppInfo.IsDevBuild ? Loc.T("Zelf gebouwde versie") : Loc.T("Versie {0}", AppInfo.VersionText);
        UpdateText.Text = state switch
        {
            _ when AppInfo.IsDevBuild => Loc.T("Een zelf gebouwde versie zoekt niet naar updates."),
            UpdateState.Checking => Loc.T("Zoeken naar updates…"),
            UpdateState.UpToDate => Loc.T("Je hebt de nieuwste versie."),
            UpdateState.Downloading => Loc.T("Versie {0} wordt gedownload…", latest),
            UpdateState.Ready => Loc.T("Versie {0} staat klaar. IdleDash sluit even af en start daarna vanzelf opnieuw.", latest),
            UpdateState.Available when !installed => Loc.T("Versie {0} is beschikbaar. Je gebruikt de losse versie, dus je downloadt hem zelf.", latest),
            UpdateState.Available => Loc.T("Versie {0} is beschikbaar.", latest),
            UpdateState.Failed => Loc.T("Zoeken naar updates lukte niet. Controleer je internetverbinding."),
            _ => "",
        };

        InstallUpdateButton.Content = Loc.T("Bijwerken naar {0} en herstarten", latest);
        InstallUpdateButton.Visibility = state == UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.Visibility = state == UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.Visibility = AppInfo.IsDevBuild || state is UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready
            ? Visibility.Collapsed
            : Visibility.Visible;
        WhatsNewButton.Visibility = AppInfo.IsDevBuild ? Visibility.Collapsed : Visibility.Visible;
        AutoUpdateSwitch.Visibility = installed && !AppInfo.IsDevBuild ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstallUpdateButton_Click(object sender, RoutedEventArgs e) => UpdateManager.InstallNow();
    private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e) => Browser.Open(UpdateManager.Latest?.PageUrl);
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) => await UpdateManager.CheckAsync(userInitiated: true);
    private void WhatsNewButton_Click(object sender, RoutedEventArgs e) => Browser.Open(AppInfo.ReleasePageUrl(AppInfo.VersionText));
    private void GitHubButton_Click(object sender, RoutedEventArgs e) => Browser.Open(AppInfo.RepoUrl);
}
