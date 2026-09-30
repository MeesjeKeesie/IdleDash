using System.Globalization;
using System.Text;
using IdleDash.Core;

namespace IdleDash.Services;

public record IcsEvent(string Uid, string Summary, DateTime Start, DateTime End, bool AllDay);

/// <summary>
/// Leest agenda's in iCalendar-formaat (.ics, zoals Apple, Outlook en Google die delen),
/// inclusief herhalende afspraken (dagelijks, wekelijks, maandelijks, jaarlijks), uitzonderingen en verplaatste afspraken.
/// Alle tijden worden omgezet naar de tijdzone van deze pc.
/// </summary>
public static class IcsParser
{
    private sealed class Raw
    {
        public string Uid = "";
        public string Summary = "";
        public DateTime Start;                 // wandkloktijd in Zone (of datum bij hele dag)
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        public bool AllDay;
        public DateTime? End;
        public TimeZoneInfo? EndZone;
        public TimeSpan? Duration;
        public string? Rule;
        public readonly List<DateTime> ExDatesUtc = new();
        public readonly List<DateTime> ExDates = new();   // hele dagen
        public DateTime? RecurrenceIdUtc;
        public DateTime? RecurrenceIdDate;
        public bool Cancelled;
    }

    /// <summary>Alle afspraken die (deels) tussen from en to vallen, in lokale tijd.</summary>
    public static List<IcsEvent> Expand(string ics, DateTime from, DateTime to)
    {
        var raws = ParseRaw(ics);
        var result = new List<IcsEvent>();

        // Verplaatste of losse aangepaste herhalingen (RECURRENCE-ID) vervangen het origineel
        var overridden = new HashSet<string>(raws
            .Where(r => r.RecurrenceIdUtc != null || r.RecurrenceIdDate != null)
            .Select(r => Key(r.Uid, r.RecurrenceIdUtc, r.RecurrenceIdDate)));

        foreach (var raw in raws)
        {
            if (raw.Cancelled) continue;
            var length = LengthOf(raw);

            if (raw.Rule == null || raw.RecurrenceIdUtc != null || raw.RecurrenceIdDate != null)
            {
                Add(result, raw, raw.Start, length, from, to);
                continue;
            }

            foreach (var start in Occurrences(raw, to))
            {
                bool excluded = raw.AllDay
                    ? raw.ExDates.Contains(start.Date) || overridden.Contains(Key(raw.Uid, null, start.Date))
                    : raw.ExDatesUtc.Contains(ToUtc(start, raw.Zone)) || overridden.Contains(Key(raw.Uid, ToUtc(start, raw.Zone), null));
                if (!excluded) Add(result, raw, start, length, from, to);
            }
        }
        return result.OrderBy(e => e.Start).ToList();
    }

    private static string Key(string uid, DateTime? utc, DateTime? date) =>
        uid + "|" + (utc?.ToString("o") ?? date?.ToString("yyyy-MM-dd") ?? "");

    private static TimeSpan LengthOf(Raw raw)
    {
        if (raw.Duration is TimeSpan duration) return duration;
        if (raw.End is DateTime end)
        {
            if (raw.AllDay) return end.Date - raw.Start.Date;
            return ToUtc(end, raw.EndZone ?? raw.Zone) - ToUtc(raw.Start, raw.Zone);
        }
        return raw.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
    }

    private static void Add(List<IcsEvent> result, Raw raw, DateTime start, TimeSpan length, DateTime from, DateTime to)
    {
        DateTime localStart, localEnd;
        if (raw.AllDay)
        {
            localStart = start.Date;
            localEnd = start.Date + (length <= TimeSpan.Zero ? TimeSpan.FromDays(1) : length);
        }
        else
        {
            var utc = ToUtc(start, raw.Zone);
            localStart = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
            localEnd = TimeZoneInfo.ConvertTimeFromUtc(utc + length, TimeZoneInfo.Local);
        }
        bool overlaps = raw.AllDay || length > TimeSpan.Zero ? localEnd > from && localStart < to : localStart >= from && localStart < to;
        if (overlaps)
            result.Add(new IcsEvent(raw.Uid, raw.Summary, localStart, localEnd, raw.AllDay));
    }

    // ─────────────────────────── Herhalingen ───────────────────────────

    private static IEnumerable<DateTime> Occurrences(Raw raw, DateTime to)
    {
        var parts = raw.Rule!.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        string freq = parts.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        int interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out int i) && i > 0 ? i : 1;
        int? count = int.TryParse(parts.GetValueOrDefault("COUNT"), out int c) && c > 0 ? c : null;
        DateTime? untilUtc = null;
        DateTime? untilDate = null;
        if (parts.TryGetValue("UNTIL", out var untilText) && TryParseValue(untilText, null, out var untilWall, out var untilZone, out bool untilAllDay))
        {
            if (untilAllDay || raw.AllDay) untilDate = untilWall.Date;
            else untilUtc = ToUtc(untilWall, untilZone ?? raw.Zone);
        }
        var byDay = parts.TryGetValue("BYDAY", out var bd) ? bd.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ParseByDay).Where(x => x != null).Select(x => x!.Value).ToList() : new();
        var byMonthDay = parts.TryGetValue("BYMONTHDAY", out var bmd) ? bmd.Split(',').Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v != 0).ToList() : new();
        var byMonth = parts.TryGetValue("BYMONTH", out var bm) ? bm.Split(',').Select(x => int.TryParse(x, out int v) ? v : 0).Where(v => v is >= 1 and <= 12).ToList() : new();

        // Grens in de tijdzone van de afspraak, met een dag marge
        DateTime limit = raw.AllDay ? to.Date.AddDays(1) : TimeZoneInfo.ConvertTime(to, TimeZoneInfo.Local, raw.Zone).AddDays(1);
        var start = raw.Start;
        int produced = 0;

        foreach (var candidate in Candidates(freq, interval, start, byDay, byMonthDay, byMonth))
        {
            if (candidate < start) continue;
            if (untilDate != null && candidate.Date > untilDate) yield break;
            if (untilUtc != null && ToUtc(candidate, raw.Zone) > untilUtc) yield break;
            if (candidate > limit) yield break;
            yield return candidate;
            if (count != null && ++produced >= count) yield break;
        }
    }

    private static IEnumerable<DateTime> Candidates(string freq, int interval, DateTime start,
        List<(int Ordinal, DayOfWeek Day)> byDay, List<int> byMonthDay, List<int> byMonth)
    {
        var time = start.TimeOfDay;
        const int maxSteps = 5000;

        switch (freq)
        {
            case "DAILY":
                for (int n = 0; n < maxSteps * 5; n++)
                {
                    var day = start.Date.AddDays((long)n * interval);
                    if (byDay.Count > 0 && !byDay.Any(b => b.Day == day.DayOfWeek)) continue;
                    if (byMonth.Count > 0 && !byMonth.Contains(day.Month)) continue;
                    yield return day + time;
                }
                break;

            case "WEEKLY":
            {
                var days = byDay.Count > 0 ? byDay.Select(b => b.Day).Distinct().ToList() : new List<DayOfWeek> { start.DayOfWeek };
                days.Sort((a, b) => MondayIndex(a).CompareTo(MondayIndex(b)));
                var weekStart = start.Date.AddDays(-MondayIndex(start.DayOfWeek));
                for (int n = 0; n < maxSteps; n++)
                {
                    var week = weekStart.AddDays((long)n * interval * 7);
                    foreach (var d in days) yield return week.AddDays(MondayIndex(d)) + time;
                }
                break;
            }

            case "MONTHLY":
                for (int n = 0; n < maxSteps; n++)
                {
                    var month = new DateTime(start.Year, start.Month, 1).AddMonths(n * interval);
                    foreach (var day in DaysInMonth(month, byDay, byMonthDay, start.Day)) yield return day + time;
                }
                break;

            case "YEARLY":
                for (int n = 0; n < maxSteps / 10; n++)
                {
                    int year = start.Year + n * interval;
                    if (year > 9998) yield break;
                    var months = byMonth.Count > 0 ? byMonth.OrderBy(m => m).ToList() : new List<int> { start.Month };
                    foreach (int m in months)
                        foreach (var day in DaysInMonth(new DateTime(year, m, 1), byDay, byMonthDay, start.Day))
                            yield return day + time;
                }
                break;
        }
    }

    private static IEnumerable<DateTime> DaysInMonth(DateTime month, List<(int Ordinal, DayOfWeek Day)> byDay, List<int> byMonthDay, int defaultDay)
    {
        int length = DateTime.DaysInMonth(month.Year, month.Month);
        var days = new List<DateTime>();
        if (byDay.Count > 0)
        {
            foreach (var (ordinal, weekday) in byDay)
            {
                var matches = Enumerable.Range(1, length).Select(d => new DateTime(month.Year, month.Month, d)).Where(d => d.DayOfWeek == weekday).ToList();
                if (ordinal == 0) days.AddRange(matches);
                else if (ordinal > 0 && ordinal <= matches.Count) days.Add(matches[ordinal - 1]);
                else if (ordinal < 0 && -ordinal <= matches.Count) days.Add(matches[matches.Count + ordinal]);
            }
        }
        else
        {
            foreach (int d in byMonthDay.Count > 0 ? byMonthDay : new List<int> { defaultDay })
            {
                int day = d > 0 ? d : length + d + 1;
                if (day >= 1 && day <= length) days.Add(new DateTime(month.Year, month.Month, day));
            }
        }
        return days.Distinct().OrderBy(d => d);
    }

    private static int MondayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    private static (int Ordinal, DayOfWeek Day)? ParseByDay(string text)
    {
        text = text.Trim().ToUpperInvariant();
        if (text.Length < 2) return null;
        DayOfWeek? day = text[^2..] switch
        {
            "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null,
        };
        if (day == null) return null;
        int ordinal = text.Length > 2 && int.TryParse(text[..^2], out int o) ? o : 0;
        return (ordinal, day.Value);
    }

    // ─────────────────────────── Tekst lezen ───────────────────────────

    private static List<Raw> ParseRaw(string ics)
    {
        var raws = new List<Raw>();
        var stack = new Stack<string>();
        Raw? current = null;

        foreach (string line in Unfold(ics))
        {
            var (name, parameters, value) = ParseLine(line);
            if (name.Length == 0) continue;

            if (name == "BEGIN")
            {
                stack.Push(value.Trim().ToUpperInvariant());
                if (stack.Peek() == "VEVENT" && stack.Count >= 1) current = new Raw();
                continue;
            }
            if (name == "END")
            {
                string ended = stack.Count > 0 ? stack.Pop() : "";
                if (ended == "VEVENT" && current != null)
                {
                    if (current.Start != default) raws.Add(current);
                    current = null;
                }
                continue;
            }
            if (current == null || stack.Count == 0 || stack.Peek() != "VEVENT") continue;   // bv. binnen een herinnering (VALARM)

            switch (name)
            {
                case "UID": current.Uid = value.Trim(); break;
                case "SUMMARY": current.Summary = Unescape(value).Trim(); break;
                case "STATUS": current.Cancelled = value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                case "RRULE": current.Rule = value.Trim(); break;
                case "DURATION": current.Duration = ParseDuration(value.Trim()); break;
                case "DTSTART":
                    if (TryParseValue(value, parameters, out var start, out var zone, out bool allDay))
                    {
                        current.Start = start;
                        current.Zone = zone ?? TimeZoneInfo.Local;
                        current.AllDay = allDay;
                    }
                    break;
                case "DTEND":
                    if (TryParseValue(value, parameters, out var end, out var endZone, out _))
                    {
                        current.End = end;
                        current.EndZone = endZone;
                    }
                    break;
                case "EXDATE":
                    foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!TryParseValue(part, parameters, out var ex, out var exZone, out bool exAllDay)) continue;
                        if (exAllDay) current.ExDates.Add(ex.Date);
                        else current.ExDatesUtc.Add(ToUtc(ex, exZone ?? current.Zone));
                    }
                    break;
                case "RECURRENCE-ID":
                    if (TryParseValue(value, parameters, out var rid, out var ridZone, out bool ridAllDay))
                    {
                        if (ridAllDay) current.RecurrenceIdDate = rid.Date;
                        else current.RecurrenceIdUtc = ToUtc(rid, ridZone ?? current.Zone);
                    }
                    break;
            }
        }
        foreach (var raw in raws.Where(r => r.Summary.Length == 0)) raw.Summary = Loc.T("(zonder titel)");
        return raws;
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (rawLine.Length > 0 && (rawLine[0] == ' ' || rawLine[0] == '\t'))
            {
                current.Append(rawLine, 1, rawLine.Length - 1);   // vervolg van de vorige regel
                continue;
            }
            if (current.Length > 0) yield return current.ToString();
            current.Clear().Append(rawLine);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static (string Name, Dictionary<string, string> Parameters, string Value) ParseLine(string line)
    {
        int colon = -1;
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) { colon = i; break; }
        }
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (colon < 0) return ("", parameters, "");

        var head = line[..colon].Split(';');
        foreach (string p in head.Skip(1))
        {
            int eq = p.IndexOf('=');
            if (eq > 0) parameters[p[..eq].Trim()] = p[(eq + 1)..].Trim().Trim('"');
        }
        return (head[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    private static bool TryParseValue(string value, Dictionary<string, string>? parameters, out DateTime wall, out TimeZoneInfo? zone, out bool allDay)
    {
        value = value.Trim();
        zone = null;
        allDay = (parameters != null && parameters.TryGetValue("VALUE", out var type) && type.Equals("DATE", StringComparison.OrdinalIgnoreCase))
                 || value.Length == 8;
        if (allDay)
            return DateTime.TryParseExact(value.Length >= 8 ? value[..8] : value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out wall);

        bool utc = value.EndsWith('Z') || value.EndsWith('z');
        string core = value.TrimEnd('Z', 'z');
        if (!DateTime.TryParseExact(core, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out wall))
            return false;
        zone = utc ? TimeZoneInfo.Utc
            : parameters != null && parameters.TryGetValue("TZID", out var tzid) ? FindZone(tzid)
            : TimeZoneInfo.Local;
        return true;
    }

    private static TimeSpan? ParseDuration(string text)
    {
        // P1D, PT1H30M, P1W, -PT15M
        var match = System.Text.RegularExpressions.Regex.Match(text,
            @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        int G(int i) => match.Groups[i].Success ? int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
        return match.Groups[1].Value == "-" ? -span : span;
    }

    private static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                char next = text[++i];
                sb.Append(next is 'n' or 'N' ? ' ' : next);
            }
            else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>Tijdzone uit een agenda: "Europe/Amsterdam" (Google, Apple) of "W. Europe Standard Time" (Outlook).</summary>
    public static TimeZoneInfo FindZone(string? tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid)) return TimeZoneInfo.Local;
        string id = tzid.Trim().Trim('"');
        foreach (string candidate in ZoneIds(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch
            {
                // volgende proberen
            }
        }
        return TimeZoneInfo.Local;

        static IEnumerable<string> ZoneIds(string id)
        {
            yield return id;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows)) yield return windows;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) yield return iana;
            var parts = id.Split('/');   // bv. "/mozilla.org/20050126_1/Europe/Amsterdam"
            if (parts.Length >= 2) yield return parts[^2] + "/" + parts[^1];
        }
    }

    private static DateTime ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);   // valt in het "gat" van de zomertijd
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    // ─────────────────────────── Nieuwe afspraak als .ics ───────────────────────────

    public static string Build(string uid, string title, DateTime start, DateTime end, bool allDay)
    {
        string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
        string Utc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local)
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//IdleDash//IdleDash//NL\r\nBEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(uid).Append("\r\n");
        sb.Append("DTSTAMP:").Append(DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
        if (allDay)
        {
            sb.Append("DTSTART;VALUE=DATE:").Append(start.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("DTEND;VALUE=DATE:").Append(end.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        else
        {
            sb.Append("DTSTART:").Append(Utc(start)).Append("\r\n");
            sb.Append("DTEND:").Append(Utc(end)).Append("\r\n");
        }
        sb.Append("SUMMARY:").Append(Escape(title)).Append("\r\n");
        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return sb.ToString();
    }
}
