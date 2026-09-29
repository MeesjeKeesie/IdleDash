using IdleDash.Core;

namespace IdleDash.Widgets;

public record WidgetDefinition(
    string Type,
    string Name,
    string Icon,
    double Width,
    double Height,
    Func<AppSettings, WidgetBase> Create);

/// <summary>Alle widgets die je kunt toevoegen. Een nieuwe widget = één regel erbij.</summary>
public static class WidgetCatalog
{
    public static IReadOnlyList<WidgetDefinition> All { get; } = new List<WidgetDefinition>
    {
        new("clock",    "Klok en datum",       "\uE823", 600, 312, _ => new ClockWidget()),
        new("weather",  "Weer",                "\uE706", 432, 312, s => new WeatherWidget(s)),
        new("rain",     "Buienradar",          "\uE753", 432, 240, s => new RainWidget(s)),
        new("calendar", "Agenda",              "\uE787", 384, 432, s => new CalendarWidget(s)),
        new("tasks",    "Taken",               "\uE73A", 384, 384, s => new TasksWidget(s)),
        new("music",    "Nu aan het afspelen", "\uE8D6", 480, 192, _ => new NowPlayingWidget()),
        new("stats",    "PC-stats",            "\uE9D9", 384, 264, _ => new SystemStatsWidget()),
        new("news",     "NOS-nieuws",          "\uE8A5", 480, 384, s => new NewsWidget(s)),
        new("focus",    "Focustimer",          "\uE916", 312, 384, s => new FocusTimerWidget(s)),
    };

    public static WidgetDefinition? Find(string type) =>
        All.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));
}
