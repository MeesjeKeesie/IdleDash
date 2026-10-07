using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>Een playlist op de widget (bewaard, zodat de widget ook zonder internet zijn tegels kan tonen).</summary>
public sealed class PlaylistChoice
{
    public string Uri { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImageUrl { get; set; }
    public int TrackCount { get; set; }
}

/// <summary>Playlists: klik op een tegel en de playlist speelt op Spotify (Premium).</summary>
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
        if (!SpotifyService.IsConnected)
        {
            ShowMessage(Loc.T("Koppel Spotify in de instellingen van IdleDash. Afspelen kan alleen met Spotify Premium."), withButton: true);
            return;
        }
        var choices = Choices;
        if (choices.Count == 0)
        {
            ShowMessage(Loc.T("Kies via het tandwieltje in de bewerkmodus welke playlists hier komen."), withButton: false);
            return;
        }
        _message.Visibility = Visibility.Collapsed;
        foreach (var choice in choices) _tiles.Children.Add(CreateTile(choice));
    }

    private void ShowMessage(string text, bool withButton)
    {
        _message.Children.Clear();
        var message = Text(text, 15, "TextSecondaryBrush");
        message.TextWrapping = TextWrapping.Wrap;
        _message.Children.Add(message);
        if (withButton)
        {
            var button = new Button
            {
                Content = Loc.T("Instellingen openen"),
                Style = (Style)FindResource("PillButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0),
            };
            button.Click += (_, _) => App.Instance.ShowSettings();
            _message.Children.Add(button);
        }
        _message.Visibility = Visibility.Visible;
    }

    private Button CreateTile(PlaylistChoice choice)
    {
        var cover = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center };
        cover.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
        var note = Text("\uE8D6", 26, "TextSecondaryBrush");
        note.FontFamily = (FontFamily)FindResource("IconFont");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Center;
        cover.Child = note;
        if (choice.ImageUrl != null && Uri.TryCreate(choice.ImageUrl, UriKind.Absolute, out var imageUri))
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
            Text = choice.Name,
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
            ToolTip = Loc.T("Afspelen op Spotify"),
        };
        button.Click += async (_, _) => await PlayAsync(choice);
        return button;
    }

    private async Task PlayAsync(PlaylistChoice choice)
    {
        if (_busy) return;
        _busy = true;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        _status.Text = Loc.T("Starten…");
        try
        {
            string? error = await SpotifyService.PlayAsync(choice.Uri, choice.TrackCount, Config.Get("shuffle", true));
            _status.Text = error ?? Loc.T("Speelt nu: {0}", choice.Name);
            if (error != null) _status.SetResourceReference(TextBlock.ForegroundProperty, "BarWarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    // ─────────────────────────── Instellingen ───────────────────────────

    public override FrameworkElement? CreateSettings(Action saved)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Playlists")));
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
        return panel;
    }
}
