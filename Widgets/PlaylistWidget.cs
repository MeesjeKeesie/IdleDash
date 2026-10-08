using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Een Spotify-playlist op de widget (bewaard, zodat de tegels ook zonder internet te zien zijn).</summary>
public sealed class PlaylistChoice
{
    public string Uri { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
    public int TrackCount { get; set; }
}

/// <summary>
/// Playlists: Spotify (Premium) speelt met één klik; links van Apple Music, YouTube Music en andere diensten
/// openen de playlist in de app of de browser.
/// </summary>
public sealed class PlaylistWidget : WidgetBase
{
    private readonly WrapPanel _tiles = new();
    private readonly StackPanel _message = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 4, 0, 0) };
    private bool _busy;

    public PlaylistWidget()
    {
        var root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_tiles);
        root.Children.Add(_message);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Content = root;
    }

    private List<PlaylistChoice> Choices => Config.Get("playlists", new List<PlaylistChoice>());
    private List<MusicLink> Links => Config.Get("links", new List<MusicLink>());

    protected override void OnStart()
    {
        SpotifyService.StateChanged += Render;
        Render();
    }

    protected override void OnStop() => SpotifyService.StateChanged -= Render;
    public override void OnSettingsChanged() => Render();
    public override void Refresh() => Render();

    private void Render()
    {
        _tiles.Children.Clear();
        var choices = Choices;
        var links = Links;
        var spotify = SpotifyService.IsConnected ? choices : new List<PlaylistChoice>();
        if (spotify.Count == 0 && links.Count == 0)
        {
            ShowMessage(choices.Count > 0
                ? Loc.T("Koppel Spotify opnieuw in de instellingen om je Spotify-playlists te zien.")
                : Loc.T("Kies playlists via het tandwieltje: van Spotify (Premium) of een link van Apple Music, YouTube Music en andere diensten."));
            return;
        }
        _message.Visibility = Visibility.Collapsed;
        if (choices.Count > 0 && !SpotifyService.IsConnected)
            _status.Text = Loc.T("Koppel Spotify opnieuw in de instellingen om je Spotify-playlists te zien.");
        foreach (var choice in spotify)
            _tiles.Children.Add(CreateTile(choice.Name, choice.ImageUrl, Loc.T("Afspelen op Spotify"), async () => await PlaySpotifyAsync(choice)));
        foreach (var link in links)
            _tiles.Children.Add(CreateTile(link.Name, link.ImageUrl, Loc.T("Openen in {0}", link.Service), () => OpenLink(link)));
    }

    private void ShowMessage(string text)
    {
        _message.Children.Clear();
        var message = Text(text, 15, "TextSecondaryBrush");
        message.TextWrapping = TextWrapping.Wrap;
        _message.Children.Add(message);
        var button = new Button
        {
            Content = Loc.T("Playlists kiezen"),
            Style = (Style)FindResource("PillButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
        };
        button.Click += (_, _) => RequestSettings();
        _message.Children.Add(button);
        _message.Visibility = Visibility.Visible;
    }

    private Button CreateTile(string title, string? imageUrl, string tooltip, Action click)
    {
        var cover = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center };
        cover.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
        var note = Text("\uE8D6", 26, "TextSecondaryBrush");
        note.FontFamily = (FontFamily)FindResource("IconFont");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Center;
        cover.Child = note;
        if (imageUrl != null && Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = imageUri;
                bitmap.DecodePixelWidth = 144;
                bitmap.EndInit();
                cover.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                cover.Child = null;
            }
            catch
            {
                // geen hoesje: dan het muzieknoot-teken
            }
        }

        var name = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            Margin = new Thickness(0, 6, 0, 0),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var content = new StackPanel();
        content.Children.Add(cover);
        content.Children.Add(name);
        var button = new Button
        {
            Content = content,
            Width = 96,
            Margin = new Thickness(0, 0, 6, 6),
            Style = (Style)FindResource("RowButton"),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private async Task PlaySpotifyAsync(PlaylistChoice choice)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(Loc.T("Starten…"), error: false);
        try
        {
            string? error = await SpotifyService.PlayAsync(choice.Uri, choice.TrackCount, Config.Get("shuffle", true));
            SetStatus(error ?? Loc.T("Speelt nu: {0}", choice.Name), error != null);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenLink(MusicLink link)
    {
        try
        {
            MusicLinks.Open(link);
            SetStatus(Loc.T("Geopend in {0}: {1}", link.Service, link.Name), error: false);
        }
        catch
        {
            SetStatus(Loc.T("Openen lukte niet"), error: true);
        }
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, error ? "BarWarnBrush" : "TextMutedBrush");
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();

        // Spotify
        panel.Children.Add(Ui.Section("Spotify"));
        panel.Children.Add(Ui.Switch(Loc.T("Shuffle (willekeurige volgorde)"), null, Config.Get("shuffle", true), v => { Config.Set("shuffle", v); saved(); }));
        panel.Children.Add(Ui.Label(Loc.T("Welke playlists")));
        var status = Ui.Hint(SpotifyService.IsConnected ? Loc.T("Playlists ophalen…") : Loc.T("Koppel eerst Spotify in de instellingen van IdleDash."));
        var list = new StackPanel();
        panel.Children.Add(status);
        panel.Children.Add(list);
        panel.Loaded += async (_, _) =>
        {
            if (!SpotifyService.IsConnected) return;
            var (playlists, error) = await SpotifyService.GetPlaylistsAsync();
            status.Text = error ?? (playlists.Count == 0 ? Loc.T("Geen playlists gevonden.") : "");
            var choices = Choices;
            foreach (var playlist in playlists)
            {
                var box = Ui.Switch(playlist.Name, playlist.Owner, choices.Any(c => c.Uri == playlist.Uri), on =>
                {
                    choices.RemoveAll(c => c.Uri == playlist.Uri);
                    if (on) choices.Add(new PlaylistChoice { Uri = playlist.Uri, Name = playlist.Name, ImageUrl = playlist.ImageUrl, TrackCount = playlist.TrackCount });
                    Config.Set("playlists", choices);
                    saved();
                });
                box.Margin = new Thickness(0, 8, 0, 0);
                list.Children.Add(box);
            }
        };

        // Andere diensten (als link)
        panel.Children.Add(Ui.Section(Loc.T("Andere diensten")));
        panel.Children.Add(Ui.Hint(Loc.T("Plak de link van een playlist uit Apple Music, YouTube Music, Deezer, Tidal of SoundCloud. IdleDash opent de playlist in de app of je browser. YouTube Music begint dan meestal meteen; bij Apple Music druk je zelf op play.")));
        var links = Links;
        var linkList = new StackPanel();
        void BuildLinks()
        {
            linkList.Children.Clear();
            foreach (var link in links.ToList())
                linkList.Children.Add(Ui.RemovableRow(link.Name, link.Service, () => { links.Remove(link); Config.Set("links", links); saved(); BuildLinks(); }));
        }
        BuildLinks();
        panel.Children.Add(linkList);
        panel.Children.Add(Ui.Label(Loc.T("Link naar een playlist")));
        var address = new TextBox();
        var linkStatus = Ui.Hint("");
        panel.Children.Add(address);
        panel.Children.Add(Ui.Row(Ui.Button(Loc.T("Toevoegen"), async () =>
        {
            linkStatus.Text = Loc.T("Opzoeken…");
            if (await MusicLinks.LookupAsync(address.Text) is not { } link)
            {
                linkStatus.Text = Loc.T("Dit is geen geldige link.");
                return;
            }
            links.Add(link);
            Config.Set("links", links);
            saved();
            BuildLinks();
            address.Text = "";
            linkStatus.Text = Loc.T("Toegevoegd: {0}", link.Name);
        })));
        panel.Children.Add(linkStatus);
        return panel;
    }
}
