using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash;

/// <summary>Instellingen. Elke wijziging wordt meteen opgeslagen en doorgegeven aan het dashboard.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _resetLayout;
    private int _suppress;          // > 0: we vullen zelf de velden, dus niet opslaan
    private bool _confirmReset;
    private string? _updateUrl;

    public SettingsWindow(AppSettings settings, Action resetLayout)
    {
        InitializeComponent();
        _settings = settings;
        _resetLayout = resetLayout;

        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/IdleDash.ico"));
        }
        catch
        {
            // zonder icoon werkt het ook
        }

        LoadValues();
        UpdateGoogleSection();
        GoogleService.StateChanged += UpdateGoogleSection;
        Closed += (_, _) => GoogleService.StateChanged -= UpdateGoogleSection;

        VersionText.Text = AppInfo.IsDevBuild ? "Zelf gebouwde versie" : $"Versie {AppInfo.VersionText}";
        _ = CheckForUpdateAsync();
    }

    /// <summary>Donkere titelbalk, passend bij de rest.</summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    // ─────────────────────────── Velden vullen ───────────────────────────

    private void LoadValues()
    {
        _suppress++;
        try
        {
            // Scherm
            AddOption(MonitorBox, "Automatisch (je bovenste extra scherm)", null, _settings.MonitorDeviceName == null);
            var monitors = MonitorHelper.GetAll();
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            foreach (var monitor in monitors.OrderBy(m => m.Bounds.Top).ThenBy(m => m.Bounds.Left))
            {
                AddOption(MonitorBox, DescribeMonitor(monitor, primary), monitor.DeviceName,
                    string.Equals(monitor.DeviceName, _settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            }
            EnsureSelection(MonitorBox);

            foreach (double seconds in new[] { 1.0, 2, 3, 5, 10, 30 })
                AddOption(DelayBox, seconds == 1 ? "1 seconde" : $"{seconds} seconden", seconds,
                    Math.Abs(_settings.ShowDelaySeconds - seconds) < 0.01);
            EnsureSelection(DelayBox, fallbackIndex: 1);

            PixelShiftSwitch.IsChecked = _settings.PixelShift;

            // Nachtmodus
            NightSwitch.IsChecked = _settings.NightModeEnabled;
            for (int minutes = 0; minutes < 24 * 60; minutes += 30)
            {
                string time = $"{minutes / 60:00}:{minutes % 60:00}";
                AddOption(NightStartBox, time, time, time == _settings.NightStart);
                AddOption(NightEndBox, time, time, time == _settings.NightEnd);
            }
            EnsureSelection(NightStartBox, fallbackIndex: 46);   // 23:00
            EnsureSelection(NightEndBox, fallbackIndex: 14);     // 07:00

            foreach (var (text, percent) in new[] { ("Een beetje", 50), ("Flink", 70), ("Heel donker", 85), ("Helemaal zwart", 100) })
                AddOption(DimBox, text, percent, _settings.NightDimPercent == percent);
            EnsureSelection(DimBox, fallbackIndex: 1);
            NightFields.IsEnabled = _settings.NightModeEnabled;

            // Weer
            PlaceBox.Text = _settings.WeatherPlace;
            PlaceStatus.Text = $"Nu ingesteld: {_settings.WeatherPlace}";

            // Focustimer
            foreach (int minutes in new[] { 15, 20, 25, 30, 45, 50, 60, 90 })
                AddOption(FocusBox, $"{minutes} minuten", minutes, _settings.FocusMinutes == minutes);
            EnsureSelection(FocusBox, fallbackIndex: 2);
            foreach (int minutes in new[] { 3, 5, 10, 15, 20 })
                AddOption(BreakBox, $"{minutes} minuten", minutes, _settings.BreakMinutes == minutes);
            EnsureSelection(BreakBox, fallbackIndex: 1);

            // Nieuws
            foreach (var (id, name) in NewsService.Feeds)
                AddOption(NewsBox, name, id, id == _settings.NewsFeed);
            EnsureSelection(NewsBox);

            // Agenda
            foreach (var (text, days) in new[] { ("Alleen vandaag", 1), ("3 dagen", 3), ("7 dagen", 7), ("14 dagen", 14) })
                AddOption(CalendarDaysBox, text, days, _settings.CalendarDays == days);
            EnsureSelection(CalendarDaysBox, fallbackIndex: 2);

            // Algemeen
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
        string where = monitor.IsPrimary ? "hoofdscherm"
            : primary == null ? ""
            : b.Bottom <= primary.Bounds.Top ? "boven"
            : b.Top >= primary.Bounds.Bottom ? "onder"
            : b.Right <= primary.Bounds.Left ? "links"
            : b.Left >= primary.Bounds.Right ? "rechts"
            : "";
        string text = $"Scherm {number} ({b.Width} × {b.Height}";
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
        _settings.NewsFeed = GetTag(NewsBox, "nosnieuwsalgemeen");
        _settings.CalendarDays = GetTag(CalendarDaysBox, 7);
        if (TaskListBox.SelectedItem is ComboBoxItem { Tag: string listId })
            _settings.GoogleTaskListId = listId;

        _settings.IgnoredProcesses = IgnoredBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.NotifyChanged();
    }

    private async void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        // Eerst vragen om een tweede klik, zodat je je indeling niet per ongeluk kwijtraakt
        if (!_confirmReset)
        {
            _confirmReset = true;
            ResetLayoutButton.Content = "Weet je het zeker? Klik nog een keer";
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (_confirmReset)
            {
                _confirmReset = false;
                ResetLayoutButton.Content = "Standaardindeling herstellen";
            }
            return;
        }

        _confirmReset = false;
        _resetLayout();
        ResetLayoutButton.Content = "Indeling hersteld";
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (!_confirmReset) ResetLayoutButton.Content = "Standaardindeling herstellen";
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

        PlaceStatus.Text = "Zoeken…";
        PlaceResults.Children.Clear();
        var places = await WeatherService.SearchPlacesAsync(query);

        if (places == null)
        {
            PlaceStatus.Text = "Zoeken lukt nu niet. Controleer je internetverbinding.";
            return;
        }
        if (places.Count == 0)
        {
            PlaceStatus.Text = $"Niets gevonden voor \"{query}\".";
            return;
        }

        PlaceStatus.Text = "Kies de juiste plaats:";
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
        PlaceStatus.Text = $"Nu ingesteld: {place.Description}";
    }

    // ─────────────────────────── Google ───────────────────────────

    private async void UpdateGoogleSection()
    {
        var state = GoogleService.State;
        bool hasKey = GoogleService.HasKey;
        bool builtIn = GoogleService.HasBuiltInKey;
        bool ownKey = GoogleService.HasOwnKey;
        bool connected = state == GoogleState.Connected;

        GoogleStatusText.Text = state switch
        {
            GoogleState.Connected => GoogleService.AccountEmail is string email ? $"Gekoppeld met {email}" : "Gekoppeld",
            GoogleState.Connecting => "Wachten tot je inlogt in de browser…",
            GoogleState.Expired => "De koppeling is verlopen. Koppel opnieuw.",
            _ when !hasKey => "Nog niet ingesteld",
            _ => "Nog niet gekoppeld",
        };

        GoogleHintText.Text = state switch
        {
            GoogleState.Connected when ownKey && builtIn => "Je afspraken en taken staan op het dashboard. Je gebruikt je eigen sleutel.",
            GoogleState.Connected => "Je afspraken en taken staan op het dashboard.",
            GoogleState.Connecting => "Er is een browservenster geopend. Log daar in en geef IdleDash toegang. "
                + "Zegt Google dat de app niet geverifieerd is, klik dan op Geavanceerd en daarna op Ga naar IdleDash.",
            _ when !hasKey => "Maak eerst een sleutelbestand aan in Google Cloud (stappen in ONTWIKKELEN.md) en kies het hier.",
            _ => "Klik op Koppelen en log in met je Google-account.",
        };

        ConnectButton.Visibility = hasKey && !connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = state == GoogleState.Connecting ? "Browser opnieuw openen" : "Koppelen met Google";
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

        // Eigen sleutel: voor de zelf gebouwde versie nodig, in de download alleen voor gevorderden
        ChooseKeyButton.Content = builtIn ? "Eigen sleutel (geavanceerd)…" : ownKey ? "Ander sleutelbestand…" : "Sleutelbestand kiezen…";
        ChooseKeyButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        RemoveKeyButton.Visibility = ownKey && builtIn && !connected ? Visibility.Visible : Visibility.Collapsed;
        GoogleCloudButton.Visibility = builtIn ? Visibility.Collapsed : Visibility.Visible;

        GoogleOptions.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (connected) await LoadTaskListsAsync();
    }

    private async Task LoadTaskListsAsync()
    {
        List<TaskListInfo> lists;
        try
        {
            lists = await GoogleService.GetTaskListsAsync();
        }
        catch (Exception ex)
        {
            GoogleService.HandleError(ex);
            return;
        }

        _suppress++;
        try
        {
            TaskListBox.Items.Clear();
            foreach (var list in lists)
                AddOption(TaskListBox, list.Title, list.Id, list.Id == _settings.GoogleTaskListId);
            EnsureSelection(TaskListBox);
        }
        finally
        {
            _suppress--;
        }
    }

    private void ChooseKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Kies het sleutelbestand dat je bij Google hebt gedownload",
            Filter = "Google-sleutel (*.json)|*.json",
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
                : "Dit bestand kon niet gelezen worden. Kies het .json-bestand dat je bij Google hebt gedownload.";
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await GoogleService.ConnectAsync();

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e) => await GoogleService.DisconnectAsync();

    private void RemoveKeyButton_Click(object sender, RoutedEventArgs e) => GoogleService.RemoveOwnKey();

    private void GoogleCloudButton_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://console.cloud.google.com/auth/overview");

    // ─────────────────────────── Over IdleDash ───────────────────────────

    private async Task CheckForUpdateAsync()
    {
        if (AppInfo.IsDevBuild) return;

        UpdateText.Text = "Zoeken naar updates…";
        var (succeeded, update) = await UpdateService.CheckAsync();
        if (!succeeded)
        {
            UpdateText.Text = "";
            return;
        }
        if (update == null)
        {
            UpdateText.Text = "Je hebt de nieuwste versie.";
            return;
        }

        _updateUrl = update.Url;
        UpdateText.Text = $"Versie {update.Version} is beschikbaar.";
        DownloadUpdateButton.Visibility = Visibility.Visible;
    }

    private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateUrl != null) OpenUrl(_updateUrl);
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepoUrl);

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
            AutostartHint.Text = "Dat lukte niet: " + ex.Message;
        }
    }

    private void UpdateAutostartHint()
    {
        string? command = Autostart.RegisteredCommand;
        AutostartHint.Text = command == null
            ? "IdleDash start vanzelf als je inlogt op Windows."
            : "Start: " + command.Trim('"');
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // geen standaardbrowser
        }
    }
}
