using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using IdleDash.Core;
using GTask = Google.Apis.Tasks.v1.Data.Task;
using GTaskList = Google.Apis.Tasks.v1.Data.TaskList;

namespace IdleDash.Services;

public record TaskItem(string Id, string Title, DateTime? Due, bool IsSubtask);
public record TaskListInfo(string Id, string Title);

public enum GoogleState { NoKey, NotConnected, Connecting, Connected, Expired }

/// <summary>
/// Koppeling met je Google-account: afspraken uit Google Agenda en taken uit Google Taken.
///
/// Welke sleutel wordt gebruikt:
/// 1. Een eigen sleutelbestand in %AppData%\IdleDash (via "Sleutelbestand kiezen" in de instellingen), anders
/// 2. de ingebouwde sleutel van de officiële download. Die wordt op GitHub tijdens het bouwen
///    ingevuld vanuit de repository-secrets en staat dus nooit in de broncode.
/// Je login zelf staat altijd in %AppData%\IdleDash\google-login.
/// </summary>
public static class GoogleService
{
    private static readonly string[] Scopes =
    {
        CalendarService.Scope.CalendarReadonly,   // agenda's lezen
        CalendarService.Scope.CalendarEvents,     // afspraken toevoegen (nieuw in 1.2.0)
        TasksService.Scope.Tasks,                 // taken lezen, afvinken en toevoegen
    };

    private static UserCredential? _credential;
    private static CancellationTokenSource? _connectCancel;
    private static int _attempt;
    private static CalendarService? _calendar;
    private static TasksService? _tasks;

    public static GoogleState State { get; private set; } = GoogleState.NotConnected;
    public static string? AccountEmail { get; private set; }

    /// <summary>Gaat af als de koppeling verandert (gekoppeld, verlopen, ontkoppeld...).</summary>
    public static event Action? StateChanged;

    public static string KeyPath => Path.Combine(AppSettings.Folder, "google-sleutel.json");
    private static string LoginFolder => Path.Combine(AppSettings.Folder, "google-login");

    private static readonly ClientSecrets? BuiltInSecrets = ReadBuiltInSecrets();

    /// <summary>Deze versie heeft een ingebouwde sleutel (officiële download van GitHub).</summary>
    public static bool HasBuiltInKey => BuiltInSecrets != null;

    /// <summary>Je hebt zelf een sleutelbestand gekozen.</summary>
    public static bool HasOwnKey => File.Exists(KeyPath);

    public static bool HasKey => HasOwnKey || HasBuiltInKey;

    public static bool IsConnected => State == GoogleState.Connected;

    // ─────────────────────────── Koppelen ───────────────────────────

    /// <summary>
    /// Kopieert het sleutelbestand dat je bij Google hebt gedownload naar de IdleDash-map.
    /// Geeft een Nederlandse foutmelding als het niet het juiste soort bestand is.
    /// </summary>
    public static void ImportKey(string sourcePath)
    {
        using (var doc = JsonDocument.Parse(File.ReadAllText(sourcePath)))
        {
            if (doc.RootElement.TryGetProperty("web", out _))
                throw new InvalidDataException("Dit is een sleutel voor een webapplicatie. Maak in Google Cloud een client van het type Desktop-app.");
            if (!doc.RootElement.TryGetProperty("installed", out _))
                throw new InvalidDataException("Dit lijkt geen sleutelbestand van Google te zijn.");
        }

        Directory.CreateDirectory(AppSettings.Folder);
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(KeyPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, KeyPath, overwrite: true);

        // Nieuwe sleutel = opnieuw inloggen
        ClearLogin();
        SetState(GoogleState.NotConnected);
    }

    /// <summary>Eigen sleutelbestand weggooien en terug naar de ingebouwde sleutel.</summary>
    public static void RemoveOwnKey()
    {
        try
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
        }
        catch
        {
            // bestand even op slot: niet erg
        }
        ClearLogin();
        SetState(HasKey ? GoogleState.NotConnected : GoogleState.NoKey);
    }

    /// <summary>Leest de sleutel die tijdens het bouwen op GitHub in het programma is gezet (zie IdleDash.csproj).</summary>
    private static ClientSecrets? ReadBuiltInSecrets()
    {
        string? id = null, secret = null;
        foreach (var attribute in typeof(GoogleService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == "GoogleClientId") id = attribute.Value;
            else if (attribute.Key == "GoogleClientSecret") secret = attribute.Value;
        }
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)
            ? null
            : new ClientSecrets { ClientId = id.Trim(), ClientSecret = secret.Trim() };
    }

    /// <summary>Bij het opstarten: stil opnieuw verbinden als je eerder al hebt ingelogd (geen browser).</summary>
    public static async Task RestoreAsync()
    {
        if (!HasKey)
        {
            SetState(GoogleState.NoKey);
            return;
        }
        if (!Directory.Exists(LoginFolder) || Directory.GetFiles(LoginFolder).Length == 0)
        {
            SetState(GoogleState.NotConnected);
            return;
        }
        SetState(GoogleState.Connecting);
        await ConnectCoreAsync(CancellationToken.None);
    }

    /// <summary>Opent de browser zodat je kunt inloggen bij Google. Nog een keer klikken = opnieuw proberen.</summary>
    public static async Task ConnectAsync()
    {
        if (!HasKey)
        {
            SetState(GoogleState.NoKey);
            return;
        }

        // Een eerdere poging afbreken (bv. als je het browservenster per ongeluk had gesloten)
        _connectCancel?.Cancel();
        var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _connectCancel = cancel;

        SetState(GoogleState.Connecting);
        try
        {
            await ConnectCoreAsync(cancel.Token);
        }
        finally
        {
            if (_connectCancel == cancel) _connectCancel = null;
            cancel.Dispose();
        }
    }

    private static async Task ConnectCoreAsync(CancellationToken cancel)
    {
        int attempt = ++_attempt;
        CalendarService calendarService;
        try
        {
            ClientSecrets secrets;
            if (HasOwnKey)
            {
                using var stream = File.OpenRead(KeyPath);
                secrets = GoogleClientSecrets.FromStream(stream).Secrets;
            }
            else
            {
                secrets = BuiltInSecrets ?? throw new InvalidOperationException("Geen Google-sleutel.");
            }

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets, Scopes, "user", cancel, new FileDataStore(LoginFolder, true));

            var initializer = new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "IdleDash",
            };
            calendarService = new CalendarService(initializer);
            _calendar = calendarService;
            _tasks = new TasksService(initializer);
        }
        catch
        {
            if (attempt != _attempt) return;   // er loopt al een nieuwere poging
            // Browser gesloten, te lang gewacht of toegang geweigerd
            _credential = null;
            _calendar = null;
            _tasks = null;
            SetState(GoogleState.NotConnected);
            return;
        }

        try
        {
            // Meteen testen, en meteen je e-mailadres ophalen voor in de instellingen
            var calendars = await calendarService.CalendarList.List().ExecuteAsync(cancel);
            if (attempt != _attempt) return;
            AccountEmail = calendars.Items?.FirstOrDefault(c => c.Primary == true)?.Id;
            SetState(GoogleState.Connected);
        }
        catch (Exception ex) when (IsAuthProblem(ex) && attempt == _attempt)
        {
            ClearLogin();
            SetState(GoogleState.Expired);
        }
        catch
        {
            if (attempt != _attempt) return;
            // Bijvoorbeeld nog geen internet vlak na het opstarten: de widgets proberen het later opnieuw
            SetState(GoogleState.Connected);
        }
    }

    public static async Task DisconnectAsync()
    {
        try
        {
            if (_credential != null) await _credential.RevokeTokenAsync(CancellationToken.None);
        }
        catch
        {
            // Intrekken bij Google mislukt (bv. geen internet): lokaal wissen is genoeg
        }
        ClearLogin();
        SetState(HasKey ? GoogleState.NotConnected : GoogleState.NoKey);
    }

    /// <summary>
    /// Voor widgets: kijk of een fout betekent dat de login verlopen is. Zo ja, dan wordt
    /// de status "verlopen" en vragen de widgets je om opnieuw te koppelen.
    /// </summary>
    public static void HandleError(Exception ex)
    {
        if (IsAuthProblem(ex) && State == GoogleState.Connected)
        {
            ClearLogin();
            SetState(GoogleState.Expired);
        }
    }

    /// <summary>Tekst voor een widget als de koppeling (nog) niet werkt, of null als alles in orde is.</summary>
    public static string? DescribeProblem(string what) => State switch
    {
        GoogleState.NoKey or GoogleState.NotConnected => Loc.T("Koppel je Google-account in de instellingen om hier {0} te zien.", what),
        GoogleState.Expired => Loc.T("Je Google-koppeling is verlopen. Koppel opnieuw in de instellingen."),
        GoogleState.Connecting => Loc.T("Verbinden met Google…"),
        _ => null,
    };

    private static bool IsAuthProblem(Exception ex) =>
        ex is TokenResponseException
        || (ex is Google.GoogleApiException api && api.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized);

    private static void ClearLogin()
    {
        try
        {
            if (Directory.Exists(LoginFolder)) Directory.Delete(LoginFolder, recursive: true);
        }
        catch
        {
            // map even op slot: niet erg
        }
        _credential = null;
        _calendar = null;
        _tasks = null;
        AccountEmail = null;
    }

    private static void SetState(GoogleState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    // ─────────────────────────── Agenda ───────────────────────────

    /// <summary>Heeft de koppeling toestemming om afspraken toe te voegen? (Koppelingen van voor 1.2.0 nog niet.)</summary>
    public static bool CanWriteCalendar =>
        _credential?.Token?.Scope?.Contains("calendar.events", StringComparison.Ordinal) == true;

    /// <summary>Je agenda's in Google Agenda (alle die je hebt aangevinkt).</summary>
    public static async Task<List<CalendarInfo>> GetCalendarsAsync()
    {
        var calendar = _calendar ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        var list = await calendar.CalendarList.List().ExecuteAsync();
        return (list.Items ?? new List<CalendarListEntry>())
            .Where(c => c.Selected == true || c.Primary == true)
            .Select(c => new CalendarInfo(
                "google:" + c.Id,
                string.IsNullOrWhiteSpace(c.SummaryOverride) ? c.Summary ?? c.Id : c.SummaryOverride,
                "Google",
                c.BackgroundColor,
                CanWriteCalendar && c.AccessRole is "owner" or "writer"))
            .ToList();
    }

    public static async Task<List<CalendarEvent>> GetEventsAsync(IEnumerable<CalendarInfo> calendars, DateTime from, DateTime until)
    {
        var calendar = _calendar ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        var result = new List<CalendarEvent>();

        foreach (var info in calendars)
        {
            var request = calendar.Events.List(info.Key["google:".Length..]);
            request.TimeMinDateTimeOffset = new DateTimeOffset(from);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(until);
            request.SingleEvents = true;   // herhalende afspraken als losse afspraken
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.MaxResults = 100;
            var events = await request.ExecuteAsync();

            foreach (var item in events.Items ?? new List<Event>())
            {
                if (item.Status == "cancelled") continue;
                DateTime? start = ParseEventTime(item.Start);
                if (start == null) continue;
                DateTime end = ParseEventTime(item.End) ?? start.Value;
                bool allDay = !string.IsNullOrEmpty(item.Start?.Date);
                string title = string.IsNullOrWhiteSpace(item.Summary) ? Loc.T("(zonder titel)") : item.Summary.Trim();
                result.Add(new CalendarEvent(title, start.Value, end, allDay, info.Color, info.Key));
            }
        }
        return result;
    }

    public static async Task AddEventAsync(string calendarId, string title, DateTime start, DateTime end, bool allDay)
    {
        var calendar = _calendar ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        var item = new Event
        {
            Summary = title,
            Start = allDay
                ? new EventDateTime { Date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                : new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start) },
            End = allDay
                ? new EventDateTime { Date = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                : new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end) },
        };
        await calendar.Events.Insert(item, calendarId).ExecuteAsync();
    }

    private static DateTime? ParseEventTime(EventDateTime? time)
    {
        if (time == null) return null;

        if (!string.IsNullOrEmpty(time.DateTimeRaw)
            && DateTimeOffset.TryParse(time.DateTimeRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
            return moment.LocalDateTime;

        if (!string.IsNullOrEmpty(time.Date)
            && DateTime.TryParseExact(time.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day;

        return null;
    }

    // ─────────────────────────── Taken ───────────────────────────

    public static async Task<List<TaskListInfo>> GetTaskListsAsync()
    {
        var tasks = _tasks ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        var lists = await tasks.Tasklists.List().ExecuteAsync();
        return (lists.Items ?? new List<GTaskList>())
            .Select(list => new TaskListInfo(list.Id, list.Title ?? Loc.T("Takenlijst")))
            .ToList();
    }

    /// <summary>Open taken uit een lijst (null = je eerste lijst), hoofdtaken met hun subtaken eronder.</summary>
    public static async Task<(string ListId, List<TaskItem> Items)> GetTasksAsync(string? listId)
    {
        var tasks = _tasks ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));

        if (string.IsNullOrEmpty(listId))
        {
            var lists = await tasks.Tasklists.List().ExecuteAsync();
            listId = lists.Items?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(listId)) return ("", new List<TaskItem>());
        }

        var request = tasks.Tasks.List(listId);
        request.ShowCompleted = false;
        request.ShowHidden = false;
        request.MaxResults = 100;
        var response = await request.ExecuteAsync();
        var all = response.Items ?? new List<GTask>();

        var items = new List<TaskItem>();
        foreach (var parent in all.Where(t => string.IsNullOrEmpty(t.Parent)).OrderBy(t => t.Position, StringComparer.Ordinal))
        {
            items.Add(ToItem(parent, isSubtask: false));
            foreach (var child in all.Where(t => t.Parent == parent.Id).OrderBy(t => t.Position, StringComparer.Ordinal))
                items.Add(ToItem(child, isSubtask: true));
        }
        return (listId, items);
    }

    public static async Task CompleteTaskAsync(string listId, string taskId)
    {
        var tasks = _tasks ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        await tasks.Tasks.Patch(new GTask { Status = "completed" }, listId, taskId).ExecuteAsync();
    }

    public static async Task AddTaskAsync(string listId, string title)
    {
        var tasks = _tasks ?? throw new InvalidOperationException(Loc.T("Niet gekoppeld met Google."));
        await tasks.Tasks.Insert(new GTask { Title = title }, listId).ExecuteAsync();
    }

    private static TaskItem ToItem(GTask task, bool isSubtask)
    {
        DateTime? due = null;
        if (!string.IsNullOrEmpty(task.Due)
            && DateTimeOffset.TryParse(task.Due, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            due = date.UtcDateTime.Date;   // Google bewaart alleen de datum (als middernacht UTC)

        string title = string.IsNullOrWhiteSpace(task.Title) ? Loc.T("(zonder titel)") : task.Title.Trim();
        return new TaskItem(task.Id, title, due, isSubtask);
    }
}
