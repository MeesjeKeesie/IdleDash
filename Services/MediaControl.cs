using Windows.Media.Control;

namespace IdleDash.Services;

/// <summary>Muziek en video's pauzeren die ergens op de pc spelen (Spotify, browser en andere apps).</summary>
public static class MediaControl
{
    public static async Task PauseAllAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var session in manager.GetSessions())
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await session.TryPauseAsync();
            }
        }
        catch
        {
            // geen mediabediening beschikbaar: dan niets pauzeren
        }
    }
}
