using System.Globalization;

namespace IdleDash.Core;

/// <summary>Rekent uit of het nu "nacht" is volgens je instellingen (ook als dat over middernacht heen loopt).</summary>
public static class NightMode
{
    public static bool IsNight(AppSettings settings, DateTime now)
    {
        if (!settings.NightModeEnabled) return false;
        if (!TryParse(settings.NightStart, out var start) || !TryParse(settings.NightEnd, out var end)) return false;

        var time = now.TimeOfDay;
        return start <= end
            ? time >= start && time < end      // bv. 01:00 tot 06:00
            : time >= start || time < end;     // bv. 23:00 tot 07:00, over middernacht heen
    }

    public static bool TryParse(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
}
