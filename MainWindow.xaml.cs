using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IdleDash.Core;
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

        foreach (var definition in WidgetCatalog.All)
            AddList.Children.Add(CreateMenuItem(definition));

        // Luisteren naar Windows: schermen aangesloten, losgekoppeld of resolutie veranderd
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Closed += (_, _) => Application.Current.Shutdown();
    }

    public void Start()
    {
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
        foreach (var host in _hosts) host.Widget.OnSettingsChanged();
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
        var host = new WidgetHost(config, widget, GridSize);
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
        row.Children.Add(new TextBlock { Text = definition.Name, VerticalAlignment = VerticalAlignment.Center });

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
    private void AddButton_Click(object sender, RoutedEventArgs e) => AddPopup.IsOpen = true;
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

    private void UpdateSky()
    {
        var sky = SkyPalette.At(DateTime.Now);
        SkyTop.Color = sky.Top;
        SkyBottom.Color = sky.Bottom;
        GlowStart.Color = Color.FromArgb(0x55, sky.Glow.R, sky.Glow.G, sky.Glow.B);
        GlowEnd.Color = Color.FromArgb(0x00, sky.Glow.R, sky.Glow.G, sky.Glow.B);
    }

    private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;
}
