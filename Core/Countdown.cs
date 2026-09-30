using System.Globalization;

namespace IdleDash.Core;

/// <summary>Eén aftelling, bv. een vakantie of verjaardag.</summary>
public class CountdownItem
{
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";     // jjjj-mm-dd
    public string? Time { get; set; }          // uu:mm, of leeg voor de hele dag
    public string Emoji { get; set; } = "";
    public bool Yearly { get; set; }
}

/// <summary>Rekenwerk voor de aftelklok (zonder scherm, zodat het te testen is).</summary>
public static class Countdown
{
    /// <summary>Volgende moment van deze aftelling. Null als hij voorbij is (en niet jaarlijks) of ongeldig.</summary>
    public static DateTime? Next(CountdownItem item, DateTime now)
    {
        if (!DateTime.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        bool hasTime = TryTime(item.Time, out var time);

        DateTime At(int year)
        {
            int day = Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month));   // 29 feb in een gewoon jaar = 28 feb
            return new DateTime(year, date.Month, day) + time;
        }
        // Een aftelling zonder tijd loopt tot het einde van die dag ("Vandaag!")
        DateTime End(DateTime start) => hasTime ? start : start.Date.AddDays(1);

        if (!item.Yearly)
        {
            var target = date.Date + time;
            return End(target) > now ? target : null;
        }
        for (int year = Math.Max(now.Year - 1, 1); year <= now.Year + 1; year++)
        {
            var target = At(year);
            if (End(target) > now && target.Year >= date.Year) return target;
        }
        return null;
    }

    public static bool HasTime(CountdownItem item) => TryTime(item.Time, out _);

    /// <summary>Hoe lang nog, als tekst. mode: days (alleen dagen), dhm (dagen, uren, minuten) of dhms (ook seconden).</summary>
    public static string Format(DateTime target, bool hasTime, DateTime now, string mode)
    {
        if (mode == "days")
        {
            int days = (target.Date - now.Date).Days;
            return days <= 0 ? Loc.T("Vandaag!") : days == 1 ? Loc.T("Morgen") : Loc.T("{0} dagen", days);
        }

        var left = (hasTime ? target : target.Date) - now;
        if (left <= TimeSpan.Zero) return hasTime ? Loc.T("Nu!") : Loc.T("Vandaag!");

        string d = Loc.T("d"), u = Loc.T("u"), m = Loc.T("m"), s = Loc.T("s");
        string text = left.Days > 0
            ? $"{left.Days}{d} {left.Hours}{u} {left.Minutes}{m}"
            : $"{left.Hours}{u} {left.Minutes}{m}";
        return mode == "dhms" ? $"{text} {left.Seconds}{s}" : text;
    }

    private static bool TryTime(string? text, out TimeSpan time)
    {
        if (TimeSpan.TryParseExact(text?.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1))
            return true;
        time = TimeSpan.Zero;
        return false;
    }
}
