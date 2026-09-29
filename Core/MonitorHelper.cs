using System.Runtime.InteropServices;

namespace IdleDash.Core;

public record MonitorInfo(IntPtr Handle, NativeMethods.RECT Bounds, string DeviceName, bool IsPrimary);

public static class MonitorHelper
{
    public static List<MonitorInfo> GetAll()
    {
        var monitors = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr handle, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (NativeMethods.GetMonitorInfo(handle, ref info))
                {
                    bool primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    monitors.Add(new MonitorInfo(handle, info.rcMonitor, info.szDevice, primary));
                }
                return true;
            }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>
    /// Geeft het scherm waar IdleDash op moet. Zonder instelling: het hoogst geplaatste scherm
    /// dat niet je hoofdscherm is (staan ze even hoog, dan het meest linkse).
    /// Het hoofdscherm wordt nooit automatisch gekozen, zodat je werk nooit wordt afgedekt.
    /// </summary>
    public static MonitorInfo? FindTarget(string? deviceName)
    {
        var monitors = GetAll();

        if (!string.IsNullOrWhiteSpace(deviceName))
            return monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        return monitors
            .Where(m => !m.IsPrimary)
            .OrderBy(m => m.Bounds.Top)
            .ThenBy(m => m.Bounds.Left)
            .FirstOrDefault();
    }
}
