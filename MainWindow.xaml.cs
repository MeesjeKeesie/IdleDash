using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;
using IdleDash.Widgets;

namespace IdleDash;

public partial class MainWindow : Window
{
    /// <summary>Widgets klikken vast op dit raster (in pixels).</summary>
    public const double GridSize = 24;

    private readonly AppSettings _settings;
    private readonly IdleWatcher _watcher;
    private readonly IntPtr _hwnd;
    private readonly List<WidgetHost> _hosts = new();
    private readonly DispatcherTimer _toolbarTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _minuteTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _wakeTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly Random _random = new();

    private MonitorInfo? _target;
    private readonly Brush _skyBrush;
    private readonly PhotoLayer _backgroundA = new(70), _backgroundB = new(70);
    private PhotoLayer _backgroundFront;
    private readonly DispatcherTimer _slideTimer = new();
    private List<string> _slides = new();
    private int _slideIndex = -1;
    private DateTime _slidesScanned;
    private bool _slideBusy;
    private int _backgroundVersion;
    private string _backgroundKey = "";
    private bool _editMode;
    private bool _toolbarVisible;
    private bool _widgetsCreated;
    private int _minutes;

    // Nachtmodus
    private double _dimTarget = -1;
    private bool _awake;
    private Point? _lastMouse;

    /// <summary>Gepauzeerd via het systeemvak: het dashboard verschijnt dan niet vanzelf.</summary>
    public bool Paused { get; private set; }

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _skyBrush = Background;
        BackgroundLayer.Children.Add(_backgroundA);
        BackgroundLayer.Children.Add(_backgroundB);
        _backgroundFront = _backgroundA;
        _slideTimer.Tick += async (_, _) => await NextSlideAsync();
        Loc.Apply(this);
        _settings = settings;
        _settings.Changed += OnSettingsChanged;

        // Venster alvast aanmaken (nog onzichtbaar), zodat we het naar het bovenste scherm kunnen zetten
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        HideFromAltTab();

        _watcher = new IdleWatcher(() => _target?.Handle ?? IntPtr.Zero, _settings);
        _watcher.IdleChanged += OnIdleChanged;

        _toolbarTimer.Tick += (_, _) =>
        {
            _toolbarTimer.Stop();
            if (!_editMode && !AddPopup.IsOpen) SetToolbarVisible(false);
        };
        _minuteTimer.Tick += (_, _) => OnMinute();
        _wakeTimer.Tick += (_, _) =>
        {
            _wakeTimer.Stop();
            _awake = false;
            UpdateNightMode();
        };

        FillAddList("");

        // Luisteren naar Windows: schermen aangesloten, losgekoppeld of resolutie veranderd
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        // Deurbel, beweging en andere smarthome-meldingen
        SmartHomeService.Triggered += device => ShowToast(device.Name, device.Value ?? Loc.T("Melding"));

        Closed += (_, _) => Application.Current.Shutdown();
    }

    public void Start()
    {
        ApplyBackground();
        FindTargetMonitor();
        UpdateSky();
        _minuteTimer.Start();
        _watcher.Start();
    }

    // ─────────────────────────── Bovenste scherm zoeken en vullen ───────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (msg == WM_DISPLAYCHANGE)
            Dispatcher.InvokeAsync(FindTargetMonitor, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    private void FindTargetMonitor()
    {
        _target = MonitorHelper.FindTarget(_settings.MonitorDeviceName);
        PlaceOnTarget();
    }

    private void PlaceOnTarget()
    {
        if (_target == null) return;
        var b = _target.Bounds;
        const uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;

        // Eerst verplaatsen (dan neemt Windows de schaal van dat scherm over), daarna precies passend maken
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, b.Left, b.Top, 400, 300, flags);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height, flags);
    }

    private void HideFromAltTab()
    {
        long exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
    }

    // ─────────────────────────── Tonen, verbergen en pauzeren ───────────────────────────

    private void OnIdleChanged(bool idle)
    {
        if (idle)
        {
            if (!Paused) ShowDashboard();
        }
        else
        {
            HideDashboard();
        }
    }

    private void ShowDashboard()
    {
        if (_target == null || IsVisible) return;
        PlaceOnTarget();
        Show();                 // ShowActivated=False: je toetsenbordfocus blijft waar hij was
        PlaceOnTarget();

        _awake = false;
        UpdateNightMode(animate: false);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
        foreach (var host in _hosts) host.Widget.Start();
    }

    private void HideDashboard()
    {
        if (!IsVisible) return;
        if (_editMode) SetEditMode(false);
        AddPopup.IsOpen = false;
        foreach (var host in _hosts) host.Widget.Stop();
        Hide();
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        if (paused) HideDashboard();
        else if (_watcher.IsIdle) ShowDashboard();
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    private void OnSettingsChanged()
    {
        // Ander scherm gekozen? Dan meteen verhuizen
        var fresh = MonitorHelper.FindTarget(_settings.MonitorDeviceName);
        if (fresh != _target) FindTargetMonitor();

        if (!_settings.PixelShift)
        {
            ShiftTransform.X = 0;
            ShiftTransform.Y = 0;
        }
        UpdateNightMode();

        bool languageChanged = Loc.Configure(_settings);
        ThemeManager.ApplyGlobal(_settings.Theme);
        ApplyBackground();
        if (languageChanged) ApplyLanguage();

        foreach (var host in _hosts)
        {
            if (host.Config.ThemeName != null) host.ApplyTheme(ThemeManager.Resolve(_settings, host.Config.ThemeName));
            host.Widget.OnSettingsChanged();
            host.Widget.Refresh();
        }
    }

    /// <summary>Andere taal of eenheden: alle teksten opnieuw.</summary>
    private void ApplyLanguage()
    {
        Loc.Apply(this, recordNew: false);
        FillAddList(AddSearchBox.Text);
        foreach (var host in _hosts) Loc.Apply(host, recordNew: false);
    }

    // ─────────────────────────── Achtergrond ───────────────────────────

    /// <summary>Achtergrond volgens het thema: lucht, effen kleur, kleurverloop, eigen foto of een wisselende fotomap.</summary>
    private void ApplyBackground()
    {
        var theme = _settings.Theme;
        string key = string.Join("|", theme.Background, theme.Color1, theme.Color2, theme.Photo, theme.PhotoDim, theme.PhotoFit,
            theme.PhotoAlign, theme.PhotoFolder, theme.PhotoSubfolders, theme.PhotoInterval, theme.PhotoOrder);
        if (key == _backgroundKey) return;
        _backgroundKey = key;
        _backgroundVersion++;
        _slideTimer.Stop();

        Glow.Visibility = Visibility.Collapsed;
        BackgroundTint.Opacity = Math.Clamp(theme.PhotoDim, 0, 90) / 100.0;
        var color1 = ThemeManager.ToColor(theme.Color1, Color.FromRgb(0x15, 0x23, 0x3A));
        var color2 = ThemeManager.ToColor(theme.Color2, color1);
        switch (theme.Background)
        {
            case "solid":
                ClearPhotos();
                Background = new SolidColorBrush(color1);
                break;
            case "gradient":
                ClearPhotos();
                Background = new LinearGradientBrush(color1, color2, 90);
                break;
            case "photo" when System.IO.File.Exists(theme.Photo):
                Background = Brushes.Black;
                ShowSinglePhoto(theme.Photo!, _backgroundVersion);
                break;
            case "folder" when System.IO.Directory.Exists(theme.PhotoFolder):
                Background = Brushes.Black;
                _slides.Clear();
                _ = NextSlideAsync();
                _slideTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, theme.PhotoInterval));
                _slideTimer.Start();
                break;
            default:
                FallBackToSky();
                break;
        }
    }

    private void ClearPhotos()
    {
        BackgroundLayer.Visibility = Visibility.Collapsed;
        BackgroundTint.Opacity = 0;
        _backgroundA.Clear();
        _backgroundB.Clear();
    }

    private void FallBackToSky()
    {
        ClearPhotos();
        Background = _skyBrush;
        Glow.Visibility = Visibility.Visible;
        UpdateSky();
    }

    private async void ShowSinglePhoto(string path, int version)
    {
        if (!await ShowBackgroundPhotoAsync(path, version, fade: false) && version == _backgroundVersion) FallBackToSky();
    }

    /// <summary>Volgende foto uit de map. Wisselt niet als het dashboard verborgen is (dan kijkt toch niemand).</summary>
    private async Task NextSlideAsync()
    {
        var theme = _settings.Theme;
        if (_slideBusy || theme.Background != "folder" || theme.PhotoFolder == null) return;
        if (!IsVisible && _backgroundFront.HasImage) return;
        _slideBusy = true;
        int version = _backgroundVersion;
        try
        {
            if (_slides.Count == 0 || DateTime.Now - _slidesScanned > TimeSpan.FromMinutes(30))
            {
                _slides = await Task.Run(() => PhotoLibrary.Scan(new[] { theme.PhotoFolder }, theme.PhotoSubfolders, theme.PhotoOrder));
                _slidesScanned = DateTime.Now;
                _slideIndex = -1;
                if (version != _backgroundVersion) return;
            }
            for (int tries = 0; tries < Math.Min(10, _slides.Count); tries++)
            {
                _slideIndex = (_slideIndex + 1) % _slides.Count;
                if (await ShowBackgroundPhotoAsync(_slides[_slideIndex], version, fade: _backgroundFront.HasImage)) return;
            }
            if (!_backgroundFront.HasImage && version == _backgroundVersion) FallBackToSky();   // geen enkele foto te openen
        }
        finally
        {
            _slideBusy = false;
        }
    }

    /// <summary>Foto laden (zo groot als het scherm nodig heeft) en tonen, met een zachte overgang als fade aan staat.</summary>
    private async Task<bool> ShowBackgroundPhotoAsync(string path, int version, bool fade)
    {
        var theme = _settings.Theme;
        double width = _target?.Bounds.Width ?? 2560, height = _target?.Bounds.Height ?? 1440;
        string fit = theme.PhotoFit, align = theme.PhotoAlign;
        var image = await Task.Run(() => PhotoLoader.LoadForScreen(path, width, height, fit));
        if (version != _backgroundVersion) return true;   // intussen een ander thema gekozen
        if (image == null) return false;

        var next = _backgroundFront == _backgroundA ? _backgroundB : _backgroundA;
        var previous = _backgroundFront;
        _backgroundFront = next;
        next.Show(image, fit, align);
        BackgroundLayer.Visibility = Visibility.Visible;
        BackgroundTint.Opacity = Math.Clamp(theme.PhotoDim, 0, 90) / 100.0;
        Panel.SetZIndex(next, 1);
        Panel.SetZIndex(previous, 0);

        if (!fade || !IsVisible)
        {
            next.BeginAnimation(OpacityProperty, null);
            next.Opacity = 1;
            previous.BeginAnimation(OpacityProperty, null);
            previous.Opacity = 0;
            previous.Clear();
            return true;
        }
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5));
        animation.Completed += (_, _) =>
        {
            if (_backgroundFront != next) return;
            previous.BeginAnimation(OpacityProperty, null);
            previous.Opacity = 0;
            previous.Clear();
        };
        next.BeginAnimation(OpacityProperty, animation);
        return true;
    }

    /// <summary>Melding bovenin het dashboard (bv. "Voordeur: Beweging"). Verdwijnt na 12 seconden.</summary>
    private void ShowToast(string title, string text)
    {
        App.Notify(title, text);
        ToastTitle.Text = title;
        ToastText.Text = text;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(12))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(13))));
        Toast.BeginAnimation(OpacityProperty, animation);
        if (IsVisible)
        {
            _awake = true;   // nachtmodus even uit, zodat je de melding ziet
            _wakeTimer.Stop();
            _wakeTimer.Start();
            UpdateNightMode();
        }
    }

    // ─────────────────────────── Nachtmodus ───────────────────────────

    private void UpdateNightMode(bool animate = true)
    {
        bool dim = !_editMode && !_awake && NightMode.IsNight(_settings, DateTime.Now);
        double target = dim ? Math.Clamp(_settings.NightDimPercent, 10, 100) / 100.0 : 0;
        if (animate && Math.Abs(target - _dimTarget) < 0.001) return;
        _dimTarget = target;

        // Langzaam donker worden, snel weer helder
        var duration = !animate ? TimeSpan.Zero : TimeSpan.FromSeconds(dim ? 4 : 0.4);
        DimOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(target, duration));
    }

    /// <summary>Muis bewogen tijdens de nachtmodus: een minuutje helder.</summary>
    private void WakeFromNightMode()
    {
        if (!NightMode.IsNight(_settings, DateTime.Now)) return;
        _awake = true;
        _wakeTimer.Stop();
        _wakeTimer.Start();
        UpdateNightMode();
    }

    // ─────────────────────────── Widgets ───────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_widgetsCreated) return;
        _widgetsCreated = true;

        if (_settings.Widgets.Count == 0)
            _settings.Widgets = CreateDefaultLayout(WidgetCanvas.ActualWidth, WidgetCanvas.ActualHeight);

        foreach (var config in _settings.Widgets.ToList())
            AddHost(config);

        _settings.Save();
    }

    /// <summary>Alle widgets weg en de standaardindeling terug (knop in de instellingen).</summary>
    public void ResetLayout()
    {
        foreach (var host in _hosts.ToList())
        {
            host.Widget.Stop();
            host.Widget.OnRemoved();
            WidgetCanvas.Children.Remove(host);
        }
        _hosts.Clear();

        if (!_widgetsCreated)
        {
            // Dashboard is nog nooit getoond: bij de eerste keer wordt de standaardindeling vanzelf gemaakt
            _settings.Widgets.Clear();
            _settings.Save();
            return;
        }

        _settings.Widgets = CreateDefaultLayout(WidgetCanvas.ActualWidth, WidgetCanvas.ActualHeight);
        foreach (var config in _settings.Widgets.ToList())
            AddHost(config);
        _settings.Save();
    }

    /// <summary>
    /// Standaardindeling: kolommen van links naar rechts vullen, in deze volgorde.
    /// Wat niet meer past, kun je later zelf toevoegen met de plusknop.
    /// </summary>
    private static List<WidgetConfig> CreateDefaultLayout(double width, double height)
    {
        string[] order = { "clock", "weather", "calendar", "tasks", "music", "stats", "rain", "news", "focus" };
        const double margin = GridSize * 3;
        const double gap = GridSize * 2;

        var result = new List<WidgetConfig>();
        double x = margin, y = margin, columnWidth = 0;

        foreach (string type in order)
        {
            var definition = WidgetCatalog.Find(type);
            if (definition == null) continue;

            // Past niet meer onder de vorige? Dan een nieuwe kolom
            if (y > margin && y + definition.Height > height - margin)
            {
                x += columnWidth + gap;
                y = margin;
                columnWidth = 0;
            }
            if (x + definition.Width > width - margin) continue;

            result.Add(new WidgetConfig
            {
                Type = definition.Type,
                X = x,
                Y = y,
                Width = definition.Width,
                Height = definition.Height,
            });
            y += definition.Height + gap;
            columnWidth = Math.Max(columnWidth, definition.Width);
        }
        return result;
    }

    private void AddHost(WidgetConfig config)
    {
        var definition = WidgetCatalog.Find(config.Type);
        if (definition == null)
        {
            _settings.Widgets.Remove(config);   // onbekend type (bv. oude versie), weggooien
            return;
        }

        // Zorgen dat de widget binnen het scherm valt
        config.Width = Math.Min(config.Width, Math.Max(144, WidgetCanvas.ActualWidth));
        config.Height = Math.Min(config.Height, Math.Max(144, WidgetCanvas.ActualHeight));
        config.X = Math.Clamp(config.X, 0, Math.Max(0, WidgetCanvas.ActualWidth - config.Width));
        config.Y = Math.Clamp(config.Y, 0, Math.Max(0, WidgetCanvas.ActualHeight - config.Height));

        var widget = definition.Create(_settings);
        var host = new WidgetHost(config, widget, GridSize, _settings);
        host.SettingsRequested += (_, _) => OpenWidgetSettings(host);
        Loc.Apply(host);
        host.LayoutChanged += (_, _) => _settings.Save();
        host.RemoveRequested += (_, _) => RemoveHost(host);
        host.SetEditMode(_editMode);

        WidgetCanvas.Children.Add(host);
        _hosts.Add(host);

        if (IsVisible) widget.Start();
    }

    private void AddWidget(WidgetDefinition definition)
    {
        var config = new WidgetConfig
        {
            Type = definition.Type,
            Width = definition.Width,
            Height = definition.Height,
            X = Snap((WidgetCanvas.ActualWidth - definition.Width) / 2),
            Y = Snap((WidgetCanvas.ActualHeight - definition.Height) / 2),
        };
        _settings.Widgets.Add(config);
        AddHost(config);
        _settings.Save();
    }

    private void RemoveHost(WidgetHost host)
    {
        host.Widget.Stop();
        host.Widget.OnRemoved();
        WidgetCanvas.Children.Remove(host);
        _hosts.Remove(host);
        _settings.Widgets.Remove(host.Config);
        _settings.Save();
    }

    private Button CreateMenuItem(WidgetDefinition definition)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = definition.Icon,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 15,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        row.Children.Add(new TextBlock { Text = Loc.T(definition.Name), VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = row, Style = (Style)FindResource("MenuButton") };
        button.Click += (_, _) =>
        {
            AddPopup.IsOpen = false;
            AddWidget(definition);
        };
        return button;
    }

    // ─────────────────────────── Bewerkmodus en knoppen ───────────────────────────

    private void SetEditMode(bool on)
    {
        _editMode = on;
        foreach (var host in _hosts) host.SetEditMode(on);

        var shown = on ? Visibility.Visible : Visibility.Collapsed;
        GridOverlay.Visibility = shown;
        AddButton.Visibility = shown;
        DoneButton.Visibility = shown;
        EditButton.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

        if (on)
        {
            ShiftTransform.X = 0;   // tijdens bewerken precies op het raster
            ShiftTransform.Y = 0;
            SetToolbarVisible(true);
        }
        else
        {
            AddPopup.IsOpen = false;
            _settings.Save();
            _toolbarTimer.Stop();
            _toolbarTimer.Start();
        }
        UpdateNightMode();   // tijdens bewerken nooit gedimd
    }

    private void SetToolbarVisible(bool visible)
    {
        if (_toolbarVisible == visible) return;
        _toolbarVisible = visible;
        Toolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 150 : 500)));
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        // Alleen echte bewegingen tellen (WPF meldt soms "beweging" als er iets onder de muis verandert)
        var position = e.GetPosition(this);
        if (_lastMouse is Point last && Math.Abs(position.X - last.X) < 3 && Math.Abs(position.Y - last.Y) < 3) return;
        _lastMouse = position;

        SetToolbarVisible(true);
        _toolbarTimer.Stop();
        _toolbarTimer.Start();
        WakeFromNightMode();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _editMode) SetEditMode(false);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => SetEditMode(true);
    private void DoneButton_Click(object sender, RoutedEventArgs e) => SetEditMode(false);
    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddSearchBox.Text = "";
        AddPopup.IsOpen = true;
        // Pas als je zelf op + klikt, krijgt het dashboard je toetsenbord (om te kunnen zoeken)
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Activate();
            AddSearchBox.Focus();
            Keyboard.Focus(AddSearchBox);
        });
    }

    /// <summary>De lijst met widgets om toe te voegen, gefilterd op wat je typt.</summary>
    private void FillAddList(string filter)
    {
        filter = filter.Trim();
        AddList.Children.Clear();
        foreach (var definition in WidgetCatalog.All.Where(d => filter.Length == 0
                     || Loc.T(d.Name).Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                     || d.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            AddList.Children.Add(CreateMenuItem(definition));
        if (AddList.Children.Count == 0)
        {
            AddList.Children.Add(new TextBlock
            {
                Text = Loc.T("Geen widget gevonden."),
                Margin = new Thickness(12, 4, 12, 10),
                Foreground = (Brush)FindResource("TextMutedBrush"),
            });
        }
    }

    private void AddSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddSearchHint.Visibility = AddSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        FillAddList(AddSearchBox.Text);
    }

    private void AddSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AddPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && AddList.Children.OfType<Button>().FirstOrDefault() is { } first)
        {
            first.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));   // eerste treffer toevoegen
            e.Handled = true;
        }
    }

    /// <summary>Melding bovenin het dashboard en in Windows, bv. van de deurbel.</summary>
    public void ShowAlert(string title, string text) => ShowToast(title, text);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Instance.ShowSettings();

    // ─────────────────────────── Sfeer: lucht en inbrand-bescherming ───────────────────────────

    private void OnMinute()
    {
        UpdateSky();
        UpdateNightMode();

        // Voor de zekerheid: klopt het scherm nog? (bv. na slaapstand)
        var fresh = MonitorHelper.FindTarget(_settings.MonitorDeviceName);
        if (fresh != _target) FindTargetMonitor();

        // Elke 3 minuten alles een paar pixels verschuiven, zodat niets inbrandt op het scherm
        _minutes++;
        if (_settings.PixelShift && !_editMode && _minutes % 3 == 0)
        {
            ShiftTransform.X = _random.Next(-4, 5);
            ShiftTransform.Y = _random.Next(-4, 5);
        }
    }

    private void OpenWidgetSettings(WidgetHost host)
    {
        string name = Loc.T(WidgetCatalog.Find(host.Config.Type)?.Name ?? host.Config.Type);
        new WidgetSettingsWindow(host, _settings, name).Show();
    }

    private void UpdateSky()
    {
        if (Background != _skyBrush) return;
        var sky = SkyPalette.At(DateTime.Now);
        SkyTop.Color = sky.Top;
        SkyBottom.Color = sky.Bottom;
        GlowStart.Color = Color.FromArgb(0x55, sky.Glow.R, sky.Glow.G, sky.Glow.B);
        GlowEnd.Color = Color.FromArgb(0x00, sky.Glow.R, sky.Glow.G, sky.Glow.B);
    }

    private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;
}
