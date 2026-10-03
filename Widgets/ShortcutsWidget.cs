using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IdleDash.Core;

namespace IdleDash.Widgets;

/// <summary>Een snelkoppeling op de widget: een app, bestand, map of website.</summary>
public sealed class ShortcutItem
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public bool IsWeb { get; set; }
}

/// <summary>Snelkoppelingen naar apps en websites. Sleep er een app, snelkoppeling of link op om hem toe te voegen.</summary>
public sealed class ShortcutsWidget : WidgetBase
{
    private readonly WrapPanel _tiles = new();
    private readonly TextBlock _empty = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    public ShortcutsWidget()
    {
        var root = new Grid { ClipToBounds = true, Background = Brushes.Transparent, AllowDrop = true };
        root.Children.Add(_tiles);
        root.Children.Add(_empty);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        root.DragOver += OnDragOver;
        root.Drop += OnDrop;
        Content = root;
    }

    private List<ShortcutItem> Items => Config.Get("items", new List<ShortcutItem>());

    protected override void OnStart() => Render();
    public override void OnSettingsChanged() => Render();
    public override void Refresh() => Render();

    private void Render()
    {
        _tiles.Children.Clear();
        var items = Items;
        _empty.Text = Loc.T("Sleep apps, snelkoppelingen of een link uit je browser hierheen, of voeg ze toe via het tandwieltje.");
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in items) _tiles.Children.Add(CreateTile(item));
    }

    private Button CreateTile(ShortcutItem item)
    {
        var icon = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        var glyph = Text(item.IsWeb ? "\uE774" : "\uE71D", 24, "TextSecondaryBrush");
        glyph.FontFamily = (FontFamily)FindResource("IconFont");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var image = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        icon.Children.Add(glyph);
        icon.Children.Add(image);
        _ = LoadIconAsync(item, image, glyph);

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            Margin = new Thickness(0, 6, 0, 0),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var content = new StackPanel();
        content.Children.Add(icon);
        content.Children.Add(name);
        var button = new Button
        {
            Content = content,
            Width = 92,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = item.Target,
            Style = (Style)FindResource("RowButton"),
        };
        button.Click += (_, _) => Launch(item);
        return button;
    }

    private static async Task LoadIconAsync(ShortcutItem item, Image image, UIElement fallback)
    {
        ImageSource? source = item.IsWeb ? await ShellIcons.ForWebsiteAsync(item.Target) : ShellIcons.ForFile(item.Target);
        if (source == null) return;
        image.Source = source;
        fallback.Visibility = Visibility.Collapsed;
    }

    private static void Launch(ShortcutItem item)
    {
        try
        {
            if (item.IsWeb)
            {
                Browser.Open(item.Target);
                return;
            }
            var info = new ProcessStartInfo(item.Target) { UseShellExecute = true };
            string? folder = Path.GetDirectoryName(item.Target);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) info.WorkingDirectory = folder;
            Process.Start(info);
        }
        catch
        {
            App.Notify(Loc.T("Openen lukte niet"), Loc.T("{0} kon niet worden geopend.", item.Name));
        }
    }

    // ─────────────────────────── Slepen ───────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || UrlFrom(e.Data) != null ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var items = Items;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (string file in files)
                if (FromPath(file) is { } item) items.Add(item);
        }
        else if (UrlFrom(e.Data) is string url)
        {
            items.Add(new ShortcutItem { Name = NameFromUrl(url), Target = url, IsWeb = true });
        }
        Config.Set("items", items);
        Settings.Save();
        Render();
        e.Handled = true;
    }

    /// <summary>Een link die uit de adresbalk van een browser is gesleept.</summary>
    private static string? UrlFrom(IDataObject data)
    {
        foreach (string format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
        {
            try
            {
                if (!data.GetDataPresent(format)) continue;
                string? text = data.GetData(format) switch
                {
                    string s => s,
                    MemoryStream m => (format.EndsWith('W') ? Encoding.Unicode : Encoding.ASCII).GetString(m.ToArray()).TrimEnd('\0'),
                    _ => null,
                };
                string? first = text?.Split('\n')[0].Trim();
                if (first != null && Uri.TryCreate(first, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri.ToString();
            }
            catch
            {
                // volgend formaat proberen
            }
        }
        return null;
    }

    public static ShortcutItem? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? url = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];
                if (url != null)
                    return new ShortcutItem { Name = Path.GetFileNameWithoutExtension(path), Target = url, IsWeb = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) };
            }
            catch
            {
                // dan als gewoon bestand toevoegen
            }
        }
        string name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
        return new ShortcutItem { Name = name, Target = path };
    }

    public static string NameFromUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host) : url;

    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        return url.Contains("://") ? url : "https://" + url;
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        var items = Items;
        var list = new StackPanel();

        void Save()
        {
            Config.Set("items", items);
            saved();
        }

        void Build()
        {
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(Ui.Hint(Loc.T("Nog geen snelkoppelingen.")));
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int index = i;
                var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                texts.Children.Add(Ui.Hint(item.Target, 0));
                row.Children.Add(texts);
                if (index > 0)
                {
                    var up = Ui.Button(Loc.T("Omhoog"), () =>
                    {
                        (items[index - 1], items[index]) = (items[index], items[index - 1]);
                        Save();
                        Build();
                    });
                    up.Margin = new Thickness(10, 0, 0, 0);
                    Grid.SetColumn(up, 1);
                    row.Children.Add(up);
                }
                var remove = Ui.Button(Loc.T("Verwijderen"), () => { items.Remove(item); Save(); Build(); });
                remove.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
        }

        panel.Children.Add(Ui.Section(Loc.T("Snelkoppelingen")));
        Build();
        panel.Children.Add(list);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("App of bestand toevoegen…"), () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Kies een app of snelkoppeling"),
                Filter = Loc.T("Programma's en snelkoppelingen") + "|*.exe;*.lnk;*.url;*.bat;*.cmd|" + Loc.T("Alle bestanden") + "|*.*",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                DereferenceLinks = false,   // de snelkoppeling zelf bewaren
            };
            if (dialog.ShowDialog() != true || FromPath(dialog.FileName) is not { } item) return;
            items.Add(item);
            Save();
            Build();
        })));

        panel.Children.Add(Ui.Label(Loc.T("Website toevoegen")));
        var name = new TextBox();
        var address = new TextBox();
        panel.Children.Add(Ui.Columns((Loc.T("Naam"), name, "*"), (Loc.T("Adres"), address, "2*")));
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Website toevoegen"), () =>
        {
            string url = NormalizeUrl(address.Text);
            if (address.Text.Trim().Length < 3 || !Uri.TryCreate(url, UriKind.Absolute, out _)) return;
            items.Add(new ShortcutItem { Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : NameFromUrl(url), Target = url, IsWeb = true });
            name.Text = address.Text = "";
            Save();
            Build();
        })));
        panel.Children.Add(Ui.Hint(Loc.T("Tip: sleep apps, snelkoppelingen of een link uit je browser rechtstreeks op de widget. Een app uit de Microsoft Store sleep je eerst vanuit Start naar je bureaublad."), 14));
        return panel;
    }
}
