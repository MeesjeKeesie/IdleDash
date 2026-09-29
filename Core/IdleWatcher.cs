using System.Diagnostics;
using System.Text;
using System.Windows.Threading;

namespace IdleDash.Core;

/// <summary>
/// Kijkt elke seconde of er een echt venster op het bovenste scherm staat.
/// Leeg (langer dan ShowDelaySeconds) = idle, dan verschijnt het dashboard.
/// </summary>
public sealed class IdleWatcher
{
    // Onderdelen van Windows zelf die niet als "venster" tellen
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly Func<IntPtr> _getMonitor;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly Dictionary<uint, string> _processNames = new();
    private DateTime? _freeSince;

    public bool IsIdle { get; private set; }
    public event Action<bool>? IdleChanged;

    public IdleWatcher(Func<IntPtr> getMonitor, AppSettings settings)
    {
        _getMonitor = getMonitor;
        _settings = settings;
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    private void Check()
    {
        IntPtr monitor = _getMonitor();
        bool occupied = monitor == IntPtr.Zero || HasWindowOn(monitor);

        if (occupied)
        {
            _freeSince = null;
            SetIdle(false);
            return;
        }

        _freeSince ??= DateTime.Now;
        if ((DateTime.Now - _freeSince.Value).TotalSeconds >= _settings.ShowDelaySeconds)
            SetIdle(true);
    }

    private void SetIdle(bool idle)
    {
        if (IsIdle == idle) return;
        IsIdle = idle;
        IdleChanged?.Invoke(idle);
    }

    private bool HasWindowOn(IntPtr monitor)
    {
        if (_processNames.Count > 500) _processNames.Clear();

        bool found = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd)
                && NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL) == monitor
                && IsRealWindow(hwnd))
            {
                found = true;
                return false; // gevonden, stoppen met zoeken
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private bool IsRealWindow(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) return false;                      // geminimaliseerd
        if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;      // geen titel = geen echt venster

        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT)) != 0)
            return false;                                                     // overlays, meldingen, zwevende balkjes

        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
            return false;                                                     // verborgen (bv. ander virtueel bureaublad)

        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (IgnoredClasses.Contains(className.ToString())) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == _ownProcessId) return false;                         // IdleDash zelf

        if (_settings.IgnoredProcesses.Count > 0
            && _settings.IgnoredProcesses.Contains(GetProcessName(processId), StringComparer.OrdinalIgnoreCase))
            return false;

        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect.Width >= 40 && rect.Height >= 40;
    }

    private string GetProcessName(uint processId)
    {
        if (_processNames.TryGetValue(processId, out var name)) return name;
        try { name = Process.GetProcessById((int)processId).ProcessName; }
        catch { name = ""; }
        _processNames[processId] = name;
        return name;
    }
}
