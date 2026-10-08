using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash.Widgets;

/// <summary>
/// "Nu aan het afspelen": leest wat Windows zelf weet over de muziek die speelt
/// (Spotify, YouTube in je browser, enz.) en laat je bedienen met vorige/afspelen/volgende.
/// </summary>
public partial class NowPlayingWidget : WidgetBase
{
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Brush _placeholderBackground;

    // Handlers één keer aanmaken, zodat aan- en afmelden zeker hetzelfde object gebruikt
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onSessionChanged;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onMediaChanged;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlaybackChanged;
    private readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimelineChanged;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TimeSpan _position;
    private TimeSpan _duration;
    private DateTimeOffset _positionUpdated;
    private bool _playing;
    private int _mediaVersion;
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _shuffleOn;
    private bool _shuffleViaSpotify;
    private DateTime _spotifyShuffleChecked;

    public NowPlayingWidget()
    {
        InitializeComponent();
        _placeholderBackground = Cover.Background;
        _progressTimer.Tick += (_, _) => UpdateProgress();

        // Windows meldt veranderingen op een achtergrondthread; via de Dispatcher terug naar het scherm
        _onSessionChanged = (_, _) => Dispatcher.InvokeAsync(FollowPlayingSession);
        _sessionTimer.Tick += (_, _) => FollowPlayingSession();
        _onMediaChanged = (_, _) => Dispatcher.InvokeAsync(() => { _ = UpdateMediaAsync(); });
        _onPlaybackChanged = (_, _) => Dispatcher.InvokeAsync(UpdatePlayback);
        _onTimelineChanged = (_, _) => Dispatcher.InvokeAsync(UpdateTimeline);

        ShowNothing();
    }

    protected override async void OnStart()
    {
        _progressTimer.Start();
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (!IsRunning) return;   // intussen alweer verborgen
            _manager.CurrentSessionChanged += _onSessionChanged;
            AttachSession(BestSession());
            _sessionTimer.Start();
        }
        catch
        {
            ShowNothing();
        }
    }

    protected override void OnStop()
    {
        _progressTimer.Stop();
        _sessionTimer.Stop();
        if (_manager != null) _manager.CurrentSessionChanged -= _onSessionChanged;
        AttachSession(null);
    }

    /// <summary>De app die echt speelt. Speelt er niets, dan blijft het widget bij wat er stond (of de keuze van Windows).</summary>
    private GlobalSystemMediaTransportControlsSession? BestSession()
    {
        if (_manager == null) return null;
        try
        {
            var current = _manager.GetCurrentSession();
            if (IsPlaying(current)) return current;
            var sessions = _manager.GetSessions()?.ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();
            if (sessions.FirstOrDefault(IsPlaying) is { } playing) return playing;
            return sessions.FirstOrDefault(s => _session != null && s.SourceAppUserModelId == _session.SourceAppUserModelId) ?? current;
        }
        catch
        {
            return _manager.GetCurrentSession();
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession? session)
    {
        try
        {
            return session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private void FollowPlayingSession()
    {
        var best = BestSession();
        if ((best == null) != (_session == null) || best?.SourceAppUserModelId != _session?.SourceAppUserModelId) AttachSession(best);
    }

    private void SetShuffleVisual(bool on)
    {
        _shuffleOn = on;
        ShuffleButton.SetResourceReference(ForegroundProperty, on ? "BarBrush" : "TextMutedBrush");
        ShuffleButton.ToolTip = on ? Loc.T("Shuffle staat aan") : Loc.T("Shuffle staat uit");
    }

    private async Task RefreshSpotifyShuffleAsync()
    {
        _spotifyShuffleChecked = DateTime.Now;
        if (await SpotifyService.GetShuffleAsync() is bool on && _shuffleViaSpotify) SetShuffleVisual(on);
    }

    private async void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        bool target = !_shuffleOn;
        SetShuffleVisual(target);   // meteen laten zien; terugzetten als het niet lukt
        bool ok;
        try
        {
            ok = _shuffleViaSpotify
                ? await SpotifyService.SetShuffleAsync(target) == null
                : _session != null && await _session.TryChangeShuffleActiveAsync(target);
        }
        catch
        {
            ok = false;
        }
        if (!ok) SetShuffleVisual(!target);
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= _onMediaChanged;
            _session.PlaybackInfoChanged -= _onPlaybackChanged;
            _session.TimelinePropertiesChanged -= _onTimelineChanged;
        }

        _session = session;
        if (_session == null)
        {
            ShuffleButton.Visibility = Visibility.Collapsed;
            ShowNothing();
            return;
        }

        _session.MediaPropertiesChanged += _onMediaChanged;
        _session.PlaybackInfoChanged += _onPlaybackChanged;
        _session.TimelinePropertiesChanged += _onTimelineChanged;

        _ = UpdateMediaAsync();
        UpdatePlayback();
        UpdateTimeline();
    }

    private async Task UpdateMediaAsync()
    {
        var session = _session;
        if (session == null) return;
        int version = ++_mediaVersion;

        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (version != _mediaVersion || session != _session) return;   // intussen alweer iets anders

            TitleText.Text = string.IsNullOrWhiteSpace(media.Title) ? Loc.T("Onbekend nummer") : media.Title;
            ArtistText.Text = !string.IsNullOrWhiteSpace(media.Artist) ? media.Artist : media.AlbumTitle ?? "";

            var cover = await LoadCoverAsync(media.Thumbnail);
            if (version == _mediaVersion) SetCover(cover);
        }
        catch
        {
            // Sommige apps geven (even) geen gegevens: gewoon overslaan
        }
    }

    private void UpdatePlayback()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var info = session.GetPlaybackInfo();
            _playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlayPauseButton.Content = _playing ? "\uE769" : "\uE768";
            PlayPauseButton.ToolTip = _playing ? Loc.T("Pauzeren") : Loc.T("Afspelen");
            PlayPauseButton.IsEnabled = info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPlayEnabled || info.Controls.IsPauseEnabled;
            PrevButton.IsEnabled = info.Controls.IsPreviousEnabled;
            NextButton.IsEnabled = info.Controls.IsNextEnabled;

            // Shuffle: via Windows als de app dat ondersteunt, anders via de Spotify-koppeling
            bool viaWindows = info.Controls.IsShuffleEnabled;
            _shuffleViaSpotify = !viaWindows && SpotifyService.IsConnected
                && (session.SourceAppUserModelId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ?? false);
            ShuffleButton.Visibility = viaWindows || _shuffleViaSpotify ? Visibility.Visible : Visibility.Collapsed;
            if (viaWindows) SetShuffleVisual(info.IsShuffleActive == true);
            else if (_shuffleViaSpotify && DateTime.Now - _spotifyShuffleChecked > TimeSpan.FromSeconds(15)) _ = RefreshSpotifyShuffleAsync();
        }
        catch
        {
            // app is net gesloten
        }
    }

    private void UpdateTimeline()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var timeline = session.GetTimelineProperties();
            _position = timeline.Position;
            _duration = timeline.EndTime - timeline.StartTime;
            _positionUpdated = timeline.LastUpdatedTime;
        }
        catch
        {
            _duration = TimeSpan.Zero;
        }
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_session == null || _duration <= TimeSpan.Zero)
        {
            TrackProgress.Visibility = Visibility.Hidden;
            PositionText.Text = "";
            DurationText.Text = "";
            return;
        }

        // Apps melden de positie niet elke seconde: zelf doortellen zolang er iets speelt
        var position = _position;
        if (_playing)
        {
            var elapsed = DateTimeOffset.Now - _positionUpdated;
            if (elapsed > TimeSpan.Zero && elapsed < _duration) position += elapsed;
        }
        if (position > _duration) position = _duration;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;

        TrackProgress.Visibility = Visibility.Visible;
        TrackProgress.Value = position.TotalSeconds / _duration.TotalSeconds * 100;
        PositionText.Text = Format(position);
        DurationText.Text = Format(_duration);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void ShowNothing()
    {
        TitleText.Text = Loc.T("Er speelt niets");
        ArtistText.Text = Loc.T("Start muziek in Spotify of je browser");
        SetCover(null);
        _duration = TimeSpan.Zero;
        _playing = false;
        UpdateProgress();
        PlayPauseButton.Content = "\uE768";
        PlayPauseButton.IsEnabled = false;
        PrevButton.IsEnabled = false;
        NextButton.IsEnabled = false;
    }

    private void SetCover(ImageSource? image)
    {
        if (image == null)
        {
            Cover.Background = _placeholderBackground;
            CoverPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            Cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private static async Task<ImageSource?> LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 20_000_000) return null;

            // Plaatje uit Windows overnemen in een gewone .NET-stream
            var bytes = new byte[(int)stream.Size];
            using (var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 300;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session != null) await _session.TrySkipNextAsync(); } catch { }
    }
}
