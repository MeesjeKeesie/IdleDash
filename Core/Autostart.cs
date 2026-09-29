using Microsoft.Win32;

namespace IdleDash.Core;

/// <summary>Automatisch opstarten met Windows, via het register (alleen voor jouw account, geen beheerder nodig).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IdleDash";

    /// <summary>Het programma dat Windows start bij het inloggen, of null als autostart uit staat.</summary>
    public static string? RegisteredCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    public static bool IsEnabled => RegisteredCommand != null;

    /// <summary>Zet autostart aan voor precies deze IdleDash.exe.</summary>
    public static void Enable()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
