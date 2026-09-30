using System.Net.Http;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>Eén agenda uit Google, iCloud of een ICS-link. Key: "google:…", "apple:…" of "ics:…".</summary>
public record CalendarInfo(string Key, string Name, string Source, string? Color, bool Writable);

public record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Color, string CalendarKey);

/// <summary>Voegt alle agenda-bronnen samen: Google, Apple iCloud en losse agenda-links.</summary>
public static class CalendarHub
{
    private static AppSettings _settings = new();
    private static readonly Dictionary<string, (DateTime Time, string Text)> IcsCache = new();

    public static void Configure(AppSettings settings) => _settings = settings;

    public static async Task<List<CalendarInfo>> GetCalendarsAsync()
    {
        var list = new List<CalendarInfo>();
        if (GoogleService.IsConnected)
        {
            try
            {
                list.AddRange(await GoogleService.GetCalendarsAsync());
            }
            catch (Exception ex)
            {
                GoogleService.HandleError(ex);
            }
        }
        if (_settings.Apple is { } apple)
            list.AddRange(apple.Calendars.Select(c => new CalendarInfo("apple:" + c.Url, c.Name, "iCloud", c.Color, true)));
        list.AddRange(_settings.IcsFeeds.Select(f => new CalendarInfo("ics:" + f.Id, f.Name, Loc.T("Agenda-link"), f.Color, false)));
        return list;
    }

    /// <summary>Afspraken van alle zichtbare agenda's. Problems: bronnen die niet lukten.</summary>
    public static async Task<(List<CalendarEvent> Events, List<string> Problems)> GetEventsAsync(int days, ICollection<string> hidden)
    {
        var from = DateTime.Now;
        var until = DateTime.Today.AddDays(Math.Max(1, days));
        var events = new List<CalendarEvent>();
        var problems = new List<string>();
        var calendars = (await GetCalendarsAsync()).Where(c => !hidden.Contains(c.Key)).ToList();

        // Google
        var google = calendars.Where(c => c.Key.StartsWith("google:")).ToList();
        if (google.Count > 0)
        {
            try
            {
                events.AddRange(await GoogleService.GetEventsAsync(google, from, until));
            }
            catch (Exception ex)
            {
                GoogleService.HandleError(ex);
                problems.Add("Google");
            }
        }

        // Apple iCloud (CalDAV)
        if (_settings.Apple is { } apple)
        {
            string password = Secrets.Unprotect(apple.Password);
            foreach (var calendar in calendars.Where(c => c.Key.StartsWith("apple:")))
            {
                try
                {
                    var texts = await CalDav.QueryAsync(calendar.Key[6..], apple.Email, password,
                        from.ToUniversalTime().AddDays(-1), until.ToUniversalTime().AddDays(1));
                    foreach (string text in texts)
                        events.AddRange(IcsParser.Expand(text, from, until)
                            .Select(e => new CalendarEvent(e.Summary, e.Start, e.End, e.AllDay, calendar.Color, calendar.Key)));
                }
                catch (UnauthorizedAccessException)
                {
                    problems.Add(Loc.T("iCloud: Apple ID of app-specifiek wachtwoord klopt niet"));
                    break;
                }
                catch
                {
                    problems.Add("iCloud: " + calendar.Name);
                }
            }
        }

        // Losse agenda-links
        foreach (var calendar in calendars.Where(c => c.Key.StartsWith("ics:")))
        {
            var feed = _settings.IcsFeeds.FirstOrDefault(f => "ics:" + f.Id == calendar.Key);
            if (feed == null) continue;
            string? text = await DownloadIcsAsync(feed.Url);
            if (text == null)
            {
                problems.Add(feed.Name);
                continue;
            }
            try
            {
                events.AddRange(IcsParser.Expand(text, from, until)
                    .Select(e => new CalendarEvent(e.Summary, e.Start, e.End, e.AllDay, feed.Color, calendar.Key)));
            }
            catch
            {
                problems.Add(feed.Name);
            }
        }

        var sorted = events
            .Where(e => e.End > from)
            .OrderBy(e => e.Start.Date).ThenBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start)
            .ToList();
        return (sorted, problems);
    }

    /// <summary>Een agenda-link ophalen (webcal:// werkt ook). 15 minuten bewaren om de server te sparen.</summary>
    private static async Task<string?> DownloadIcsAsync(string url)
    {
        string address = url.Trim();
        if (address.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) address = "https://" + address[9..];
        if (IcsCache.TryGetValue(address, out var cached) && DateTime.Now - cached.Time < TimeSpan.FromMinutes(15)) return cached.Text;
        try
        {
            string text = await Web.Client.GetStringAsync(address);
            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) return null;
            IcsCache[address] = (DateTime.Now, text);
            return text;
        }
        catch (HttpRequestException)
        {
            return cached.Text;
        }
        catch
        {
            return cached.Text;
        }
    }

    /// <summary>Een nieuwe afspraak toevoegen aan een Google- of iCloud-agenda.</summary>
    public static async Task AddEventAsync(string calendarKey, string title, DateTime start, DateTime end, bool allDay)
    {
        if (calendarKey.StartsWith("google:"))
        {
            await GoogleService.AddEventAsync(calendarKey[7..], title, start, end, allDay);
        }
        else if (calendarKey.StartsWith("apple:") && _settings.Apple is { } apple)
        {
            string uid = Guid.NewGuid().ToString("N") + "@idledash";
            await CalDav.PutAsync(calendarKey[6..], apple.Email, Secrets.Unprotect(apple.Password), uid,
                IcsParser.Build(uid, title, start, end, allDay));
        }
        else
        {
            throw new InvalidOperationException(Loc.T("Aan deze agenda kun je geen afspraken toevoegen."));
        }
    }
}
