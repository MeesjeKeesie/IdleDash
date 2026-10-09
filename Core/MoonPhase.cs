namespace IdleDash.Core;

/// <summary>Stand van de maan, berekend (geen internet nodig). Fase 0 = nieuwe maan, 0,5 = volle maan.</summary>
public static class MoonPhase
{
    private const double Synodic = 29.530588853;                                            // dagen van nieuwe maan tot nieuwe maan
    private static readonly DateTime KnownNewMoon = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    public static (double Phase, double Illumination) Compute(DateTime utc)
    {
        double days = (utc - KnownNewMoon).TotalDays;
        double phase = (days % Synodic + Synodic) % Synodic / Synodic;
        return (phase, (1 - Math.Cos(2 * Math.PI * phase)) / 2);
    }

    /// <summary>De naam van de fase, in het Nederlands (vertaald via Loc).</summary>
    public static string Name(double phase) => phase switch
    {
        < 0.0339 or > 0.9661 => "Nieuwe maan",
        < 0.216 => "Wassende sikkel",
        < 0.284 => "Eerste kwartier",
        < 0.466 => "Wassende maan",
        < 0.534 => "Volle maan",
        < 0.716 => "Afnemende maan",
        < 0.784 => "Laatste kwartier",
        _ => "Afnemende sikkel",
    };
}
