using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>
/// Smarthome: lampen, schakelaars, scènes, sensoren, sloten en meer uit Home Assistant, Philips Hue en Shelly.
/// Klik op een tegel om te schakelen; scroll op een lamp om te dimmen.
/// </summary>
public sealed class SmartHomeWidget : WidgetBase
{
    private readonly WrapPanel _tiles = new();
    private readonly StackPanel _message = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _problem = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _dimTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private List<SmartDevice> _devices = new();
    private List<string> _problems = new();
    private (SmartDevice Device, int Value)? _pendingDim;
    private bool _loading;
    private Popup? _popup;

    public SmartHomeWidget()
    {
        var root = new Grid { ClipToBounds = true };
        root.Children.Add(_tiles);
        root.Children.Add(_message);
        root.Children.Add(_problem);
        Content = root;
        _timer.Tick += async (_, _) => await LoadAsync();
        _dimTimer.Tick += async (_, _) => await ApplyDimAsync();
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1) Render();
        };
    }

    protected override async void OnStart()
    {
        SmartHomeService.Reconfigured += OnReconfigured;
        _timer.Start();
        await LoadAsync();
    }

    protected override void OnStop()
    {
        SmartHomeService.Reconfigured -= OnReconfigured;
        _timer.Stop();
    }

    private async void OnReconfigured() => await LoadAsync();
    public override async void OnSettingsChanged() => await LoadAsync();
    public override void Refresh() => Render();

    private List<string> Selected => Config.Get("devices", new List<string>());

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (!SmartHomeService.HasAnySystem)
            {
                ShowMessage(Loc.T("Koppel eerst Home Assistant, Philips Hue of Shelly in de instellingen van IdleDash."), appSettings: true);
                return;
            }
            var keys = Selected;
            if (keys.Count == 0)
            {
                ShowMessage(Loc.T("Kies welke apparaten je hier wilt zien."), appSettings: false);
                return;
            }
            var (all, problems) = await SmartHomeService.GetDevicesAsync();
            _devices = keys.Select(k => all.FirstOrDefault(d => d.Key == k)).OfType<SmartDevice>().ToList();
            _problems = problems;
            Render();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowMessage(string text, bool appSettings)
    {
        _tiles.Children.Clear();
        _problem.Text = "";
        _message.Children.Clear();
        var message = Text(text, 16, "TextSecondaryBrush");
        message.TextWrapping = TextWrapping.Wrap;
        _message.Children.Add(message);
        var button = new Button
        {
            Content = appSettings ? Loc.T("Instellingen openen") : Loc.T("Apparaten kiezen"),
            Style = (Style)FindResource("PillButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };
        button.Click += (_, _) =>
        {
            if (appSettings) App.Instance.ShowSettings();
            else RequestSettings();
        };
        _message.Children.Add(button);
        _message.Visibility = Visibility.Visible;
    }

    private void Render()
    {
        if (_popup?.IsOpen == true) return;   // niet ombouwen terwijl de kleurkiezer open is
        if (_devices.Count == 0 && Selected.Count > 0 && SmartHomeService.HasAnySystem)
        {
            ShowMessage(_problems.Count > 0 ? _problems[0] : Loc.T("De gekozen apparaten zijn niet gevonden."), appSettings: _problems.Count > 0);
            return;
        }
        if (_devices.Count == 0) return;

        _message.Visibility = Visibility.Collapsed;
        _tiles.Children.Clear();
        int columns = Math.Max(1, (int)(ActualWidth / 150));
        double width = Math.Floor(ActualWidth / columns) - 10;
        foreach (var device in _devices) _tiles.Children.Add(CreateTile(device, Math.Max(110, width)));
        _problem.Text = _problems.Count > 0 ? _problems[0] : "";
        _problem.Foreground = Res("TextMutedBrush");
    }

    private Border CreateTile(SmartDevice device, double width)
    {
        bool on = device.IsOn == true && device.Kind is DeviceKind.Light or DeviceKind.Switch or DeviceKind.Binary or DeviceKind.Cover or DeviceKind.Group;
        var accent = ((SolidColorBrush)Res("BarBrush")).Color;
        var tile = new Border
        {
            Width = width,
            Height = 92,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(14),
            Background = on ? TileBrush(device, accent) : Res("SubtleBrush"),
            Cursor = IsControllable(device) ? Cursors.Hand : Cursors.Arrow,
            ToolTip = device.Source,
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var icon = Text(Icon(device), 18);
        icon.FontFamily = (FontFamily)FindResource("IconFont");
        var name = Text(device.Name, 15);
        name.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(name, 1);
        var state = Text(StateText(device), 13, "TextSecondaryBrush");
        Grid.SetRow(state, 2);
        content.Children.Add(icon);
        if (device.Kind is DeviceKind.Light or DeviceKind.Group && (device.Brightness != null || device.Color != null))
        {
            // Klik op de tegel = aan/uit; dit knopje opent kleur, wit en helderheid
            var palette = new Button
            {
                Content = "\uE790",
                ToolTip = Loc.T("Kleur en helderheid"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -6, 0),
                Style = (Style)FindResource("SmallIconButton"),
            };
            palette.Click += (_, _) => OpenPicker(device, tile);
            content.Children.Add(palette);
        }
        content.Children.Add(name);
        content.Children.Add(state);
        tile.Child = content;

        tile.MouseLeftButtonUp += async (_, _) => await ActAsync(device);
        if (device.Kind == DeviceKind.Light || (device.Kind == DeviceKind.Group && device.Brightness != null))
        {
            tile.MouseWheel += (_, e) =>
            {
                int current = _pendingDim?.Device.Key == device.Key ? _pendingDim.Value.Value : device.Brightness ?? (device.IsOn == true ? 100 : 0);
                int value = Math.Clamp(current + (e.Delta > 0 ? 10 : -10), 1, 100);
                _pendingDim = (device, value);
                state.Text = $"{value}%";
                _dimTimer.Stop();
                _dimTimer.Start();
                e.Handled = true;
            };
        }
        return tile;
    }

    private async Task ApplyDimAsync()
    {
        _dimTimer.Stop();
        if (_pendingDim is not { } pending) return;
        _pendingDim = null;
        string? error = await SmartHomeService.InvokeAsync(pending.Device, SmartAction.Brightness, pending.Value);
        await AfterActionAsync(error);
    }

    /// <summary>Een brandende lamp kleurt de tegel in zijn eigen kleur; anders de accentkleur van het thema.</summary>
    private static Brush TileBrush(SmartDevice device, Color accent)
    {
        var lamp = LightPicker.LampColor(device.Color);
        var color = lamp ?? accent;
        return new SolidColorBrush(Color.FromArgb(lamp != null ? (byte)0x59 : (byte)0x4D, color.R, color.G, color.B));
    }

    /// <summary>De kiezer voor kleur, wit en helderheid openen, onder de tegel.</summary>
    private void OpenPicker(SmartDevice device, FrameworkElement anchor)
    {
        if (_popup != null) _popup.IsOpen = false;
        var picker = new LightPicker(device, async (action, value, value2) =>
        {
            var current = _devices.FirstOrDefault(d => d.Key == device.Key) ?? device;
            return await SmartHomeService.InvokeAsync(current, action, value, null, value2);
        });
        var popup = new Popup
        {
            Child = picker,
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        popup.Closed += async (_, _) =>
        {
            if (_popup == popup) _popup = null;
            await LoadAsync();   // de nieuwe stand ophalen en de tegels bijwerken
            Render();
        };
        _popup = popup;
        popup.IsOpen = true;
    }

    private static bool IsControllable(SmartDevice d) =>
        d.Kind is DeviceKind.Light or DeviceKind.Switch or DeviceKind.Scene or DeviceKind.Script or DeviceKind.Button
            or DeviceKind.Lock or DeviceKind.Alarm or DeviceKind.Cover or DeviceKind.Group;

    private async Task ActAsync(SmartDevice device)
    {
        if (!IsControllable(device)) return;
        SmartAction action = device.Kind switch
        {
            DeviceKind.Scene or DeviceKind.Script or DeviceKind.Button => SmartAction.Activate,
            DeviceKind.Lock => device.IsOn == true ? SmartAction.Unlock : SmartAction.Lock,
            DeviceKind.Alarm => device.IsOn == true ? SmartAction.Disarm : SmartAction.Arm,
            DeviceKind.Cover => device.IsOn == true ? SmartAction.Close : SmartAction.Open,
            _ => SmartAction.Toggle,
        };

        string? code = null;
        if (device.Sensitive)
        {
            // Iedereen bij je scherm kan klikken: sloten, alarm en garagedeuren altijd laten bevestigen
            string question = action switch
            {
                SmartAction.Unlock => Loc.T("{0} ontgrendelen?", device.Name),
                SmartAction.Lock => Loc.T("{0} op slot doen?", device.Name),
                SmartAction.Disarm => Loc.T("{0} uitschakelen?", device.Name),
                SmartAction.Arm => Loc.T("{0} inschakelen?", device.Name),
                SmartAction.Open => Loc.T("{0} openen?", device.Name),
                _ => Loc.T("{0} sluiten?", device.Name),
            };
            var (ok, entered) = Dialogs.Confirm(question, Loc.T("Dit gebeurt echt, ook als je niet thuis bent."), Loc.T("Ja, doen"), device.NeedsCode);
            if (!ok) return;
            code = entered;
        }

        string? error = await SmartHomeService.InvokeAsync(device, action, null, code);
        await AfterActionAsync(error);
    }

    private async Task AfterActionAsync(string? error)
    {
        if (error != null)
        {
            _problem.Text = error;
            _problem.Foreground = Res("BarWarnBrush");
            return;
        }
        await Task.Delay(400);   // even wachten tot het systeem de nieuwe stand kent
        var (all, problems) = await SmartHomeService.GetDevicesAsync(fresh: true);
        _devices = Selected.Select(k => all.FirstOrDefault(d => d.Key == k)).OfType<SmartDevice>().ToList();
        _problems = problems;
        Render();
    }

    private static string Icon(SmartDevice d) => d.Kind switch
    {
        DeviceKind.Light => "\uEA80",
        DeviceKind.Switch => "\uE7E8",
        DeviceKind.Scene => "\uE790",
        DeviceKind.Script or DeviceKind.Button => "\uE768",
        DeviceKind.Lock => d.IsOn == true ? "\uE72E" : "\uE785",
        DeviceKind.Alarm => "\uEA18",
        DeviceKind.Cover => "\uE80F",
        DeviceKind.Climate or DeviceKind.Sensor => "\uE9D9",
        DeviceKind.Group => "\uE902",
        _ => "\uEA8F",
    };

    private static string StateText(SmartDevice d) => d.Kind switch
    {
        DeviceKind.Light => d.IsOn == null ? "–" : d.IsOn == true ? (d.Brightness is int b && b > 0 ? $"{b}%" : Loc.T("Aan")) : Loc.T("Uit"),
        DeviceKind.Switch => d.IsOn == null ? "–" : (d.IsOn == true ? Loc.T("Aan") : Loc.T("Uit")) + (d.Value != null ? ", " + d.Value : ""),
        DeviceKind.Scene or DeviceKind.Script or DeviceKind.Button => Loc.T("Klik om te starten"),
        _ => d.Value ?? "–",
    };

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Apparaten")));
        panel.Children.Add(Ui.Hint(Loc.T("Vink aan wat je op deze widget wilt zien. Systemen koppel je in de instellingen van IdleDash.")));
        var search = new TextBox { Margin = new Thickness(0, 12, 0, 0), ToolTip = Loc.T("Zoeken") };
        var list = new StackPanel();
        var status = Ui.Hint(Loc.T("Apparaten ophalen…"));
        panel.Children.Add(search);
        panel.Children.Add(status);
        panel.Children.Add(list);

        List<SmartDevice> all = new();
        var selected = Selected;

        void Build()
        {
            list.Children.Clear();
            string filter = search.Text.Trim();
            foreach (var group in all.Where(d => filter.Length == 0 || d.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                                     .GroupBy(d => d.Source))
            {
                list.Children.Add(Ui.Label(group.Key));
                foreach (var device in group)
                {
                    var box = Ui.Switch(device.Name, KindName(device.Kind), selected.Contains(device.Key), on =>
                    {
                        if (on && !selected.Contains(device.Key)) selected.Add(device.Key);
                        if (!on) selected.Remove(device.Key);
                        Config.Set("devices", selected);
                        saved();
                    });
                    box.Margin = new Thickness(0, 8, 0, 0);
                    list.Children.Add(box);
                }
            }
        }

        search.TextChanged += (_, _) => Build();
        panel.Loaded += async (_, _) =>
        {
            var (devices, problems) = await SmartHomeService.GetDevicesAsync(fresh: true);
            all = devices;
            status.Text = devices.Count > 0 ? "" : problems.FirstOrDefault() ?? Loc.T("Nog geen smarthome-systeem gekoppeld.");
            Build();
        };
        return panel;
    }

    private static string KindName(DeviceKind kind) => kind switch
    {
        DeviceKind.Light => Loc.T("Lamp"),
        DeviceKind.Switch => Loc.T("Schakelaar"),
        DeviceKind.Scene => Loc.T("Scène"),
        DeviceKind.Script => Loc.T("Script"),
        DeviceKind.Button => Loc.T("Knop"),
        DeviceKind.Sensor => Loc.T("Sensor"),
        DeviceKind.Binary => Loc.T("Melder"),
        DeviceKind.Event => Loc.T("Gebeurtenis (bv. deurbel)"),
        DeviceKind.Lock => Loc.T("Slot"),
        DeviceKind.Alarm => Loc.T("Alarm"),
        DeviceKind.Cover => Loc.T("Rolluik of deur"),
        DeviceKind.Climate => Loc.T("Thermostaat"),
        DeviceKind.Group => Loc.T("Groep"),
        _ => "",
    };
}
