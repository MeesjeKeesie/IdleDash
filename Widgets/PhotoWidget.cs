using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Foto-slideshow uit één of meer mappen, met overgangen.</summary>
public sealed class PhotoWidget : WidgetBase
{
    private sealed class Layer : Grid
    {
        public readonly Image Back = new() { Stretch = Stretch.UniformToFill, Effect = new BlurEffect { Radius = 40 }, Opacity = 0.55 };
        public readonly Image Front = new() { Stretch = Stretch.Uniform };
        public readonly ScaleTransform Zoom = new(1, 1);

        public Layer()
        {
            Children.Add(Back);
            Children.Add(Front);
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = Zoom;
            Opacity = 0;
        }
    }

    private readonly Grid _stage = new();
    private readonly Layer _a = new(), _b = new();
    private readonly StackPanel _message = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
    private readonly DispatcherTimer _timer = new();
    private Layer _current;
    private List<string> _files = new();
    private int _index = -1;
    private DateTime _scannedAt;
    private bool _busy;

    public PhotoWidget()
    {
        _current = _a;
        _stage.Children.Add(_a);
        _stage.Children.Add(_b);
        _stage.Children.Add(_message);
        _stage.SizeChanged += (_, _) => _stage.Clip = new RectangleGeometry(new Rect(_stage.RenderSize), 14, 14);
        Content = _stage;
        _timer.Tick += async (_, _) => await ShowNextAsync();
    }

    private int Interval => Math.Max(5, Config.Get("interval", 30));

    protected override async void OnStart()
    {
        await RescanAsync(force: false);
        await ShowNextAsync();
        _timer.Interval = TimeSpan.FromSeconds(Interval);
        _timer.Start();
    }

    protected override void OnStop() => _timer.Stop();

    public override async void OnSettingsChanged()
    {
        _timer.Interval = TimeSpan.FromSeconds(Interval);
        await RescanAsync(force: true);
        if (IsRunning) await ShowNextAsync();
    }

    private async Task RescanAsync(bool force)
    {
        if (!force && _files.Count > 0 && DateTime.Now - _scannedAt < TimeSpan.FromMinutes(30)) return;
        var folders = Config.Get("folders", new List<string>());
        bool subfolders = Config.Get("subfolders", true);
        string order = Config.Get("order", "random");
        _files = await Task.Run(() => PhotoLibrary.Scan(folders, subfolders, order));
        _index = -1;
        _scannedAt = DateTime.Now;
    }

    private async Task ShowNextAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Config.Get("folders", new List<string>()).Count == 0)
            {
                ShowMessage(Loc.T("Kies een map met foto's."), withSettings: true);
                return;
            }
            if (_files.Count == 0)
            {
                ShowMessage(Loc.T("Geen foto's gevonden in de gekozen map."), withSettings: true);
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int maxSize = (int)Math.Clamp(Math.Max(ActualWidth, ActualHeight) * scale * 1.25, 640, 2560);
            bool heicFailed = false;
            for (int tries = 0; tries < Math.Min(20, _files.Count); tries++)
            {
                _index = (_index + 1) % _files.Count;
                string path = _files[_index];
                var image = await Task.Run(() => PhotoLoader.Load(path, maxSize));
                if (image != null)
                {
                    Show(image);
                    return;
                }
                if (PhotoLibrary.IsHeic(path)) heicFailed = true;
            }
            if (heicFailed) ShowHeicHint();
            else ShowMessage(Loc.T("Deze foto's kunnen niet geopend worden."), withSettings: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(BitmapSource image)
    {
        _message.Visibility = Visibility.Collapsed;
        var next = _current == _a ? _b : _a;
        var previous = _current;
        _current = next;

        bool fill = Config.Get("fit", "blur") == "fill";
        next.Front.Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform;
        next.Back.Visibility = fill ? Visibility.Collapsed : Visibility.Visible;
        next.Front.Source = image;
        next.Back.Source = image;
        Panel.SetZIndex(next, 1);
        Panel.SetZIndex(previous, 0);

        string transition = Config.Get("transition", "fade");
        next.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        next.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        next.Zoom.ScaleX = next.Zoom.ScaleY = 1;
        if (transition == "kenburns")
        {
            var zoom = new DoubleAnimation(1.0, 1.08, TimeSpan.FromSeconds(Interval + 2));
            next.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            next.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }

        if (transition == "none")
        {
            next.BeginAnimation(OpacityProperty, null);
            next.Opacity = 1;
            previous.BeginAnimation(OpacityProperty, null);
            previous.Opacity = 0;
            return;
        }
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.9));
        fade.Completed += (_, _) =>
        {
            if (_current != next) return;
            previous.BeginAnimation(OpacityProperty, null);
            previous.Opacity = 0;
        };
        next.BeginAnimation(OpacityProperty, fade);
    }

    private void ShowMessage(string text, bool withSettings)
    {
        _message.Children.Clear();
        var message = Text(text, 16, "TextSecondaryBrush");
        message.TextWrapping = TextWrapping.Wrap;
        _message.Children.Add(message);
        if (withSettings) _message.Children.Add(Pill(Loc.T("Map kiezen"), RequestSettings));
        _message.Visibility = Visibility.Visible;
    }

    private void ShowHeicHint()
    {
        _message.Children.Clear();
        var text = Text(Loc.T("Voor iPhone-foto's (HEIC) heeft Windows twee gratis onderdelen uit de Microsoft Store nodig."), 15, "TextSecondaryBrush");
        text.TextWrapping = TextWrapping.Wrap;
        _message.Children.Add(text);
        var row = new WrapPanel();
        row.Children.Add(Pill(Loc.T("HEIF-uitbreiding"), () => OpenStore("9PMMSR1CGPWG")));
        row.Children.Add(Pill(Loc.T("HEVC-uitbreiding"), () => OpenStore("9N4WGH0Z6VHQ")));
        row.Children.Add(Pill(Loc.T("Opnieuw proberen"), async () => { await RescanAsync(force: true); await ShowNextAsync(); }));
        _message.Children.Add(row);
        _message.Visibility = Visibility.Visible;
    }

    private Button Pill(string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 12, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        button.Style = (Style)FindResource("PillButton");
        button.Click += (_, _) => click();
        return button;
    }

    private static void OpenStore(string productId)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=" + productId) { UseShellExecute = true });
        }
        catch
        {
            Browser.Open("https://apps.microsoft.com/detail/" + productId);
        }
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        var folders = Config.Get("folders", new List<string>());
        var list = new StackPanel();

        void Build()
        {
            list.Children.Clear();
            if (folders.Count == 0) list.Children.Add(Ui.Hint(Loc.T("Nog geen map gekozen.")));
            foreach (string folder in folders.ToList())
                list.Children.Add(Ui.RemovableRow(Path.GetFileName(folder.TrimEnd('\\')), folder, () =>
                {
                    folders.Remove(folder);
                    Config.Set("folders", folders);
                    saved();
                    Build();
                }));
        }

        panel.Children.Add(Ui.Section(Loc.T("Foto's")));
        Build();
        panel.Children.Add(list);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Map toevoegen…"), () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Kies een map met foto's") };
            if (dialog.ShowDialog() != true || folders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase)) return;
            folders.Add(dialog.FolderName);
            Config.Set("folders", folders);
            saved();
            Build();
        })));
        panel.Children.Add(Ui.Hint(Loc.T("Tip: kies een map die met OneDrive of Google Drive synchroniseert, dan komen je telefoonfoto's er vanzelf bij.")));
        panel.Children.Add(Ui.Switch(Loc.T("Ook submappen"), null, Config.Get("subfolders", true), v => { Config.Set("subfolders", v); saved(); }));

        panel.Children.Add(Ui.Columns(
            (Loc.T("Wisselen elke"), Ui.Combo(new[]
            {
                (Loc.T("10 seconden"), 10), (Loc.T("30 seconden"), 30), (Loc.T("1 minuut"), 60), (Loc.T("5 minuten"), 300),
            }, Config.Get("interval", 30), v => { Config.Set("interval", v); saved(); }), "*"),
            (Loc.T("Volgorde"), Ui.Combo(new[]
            {
                (Loc.T("Willekeurig"), "random"), (Loc.T("Op naam"), "name"), (Loc.T("Op datum"), "date"),
            }, Config.Get("order", "random"), v => { Config.Set("order", v); saved(); }), "*")));

        panel.Children.Add(Ui.Columns(
            (Loc.T("Overgang"), Ui.Combo(new[]
            {
                (Loc.T("Vervagen"), "fade"), (Loc.T("Langzaam inzoomen"), "kenburns"), (Loc.T("Geen"), "none"),
            }, Config.Get("transition", "fade"), v => { Config.Set("transition", v); saved(); }), "*"),
            (Loc.T("Passend maken"), Ui.Combo(new[]
            {
                (Loc.T("Hele foto, wazige rand"), "blur"), (Loc.T("Vullend (bijsnijden)"), "fill"),
            }, Config.Get("fit", "blur"), v => { Config.Set("fit", v); saved(); }), "*")));
        return panel;
    }
}
