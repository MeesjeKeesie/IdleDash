using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IdleDash.Core;

/// <summary>
/// Icoontje in het systeemvak (rechtsonder bij de klok). Klik = instellingen, rechtsklik = menu.
/// Toont ook meldingen, bijvoorbeeld als je focustimer klaar is.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8001;   // WM_APP + 1: ons eigen berichtnummer
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int NIN_BALLOONUSERCLICK = 0x0405;   // er is op een melding geklikt
    private const uint CmdSettings = 1;
    private const uint CmdPause = 2;
    private const uint CmdExit = 3;
    private const uint CmdUpdate = 4;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _added;
    private Action? _notificationClick;

    /// <summary>Voor het vinkje bij "Dashboard pauzeren".</summary>
    public bool Paused { get; set; }

    /// <summary>Tekst voor een extra menu-item bovenaan (bv. "Bijwerken naar versie 1.2.0"), of null.</summary>
    public string? UpdateMenuText { get; set; }

    public event Action? UpdateRequested;
    public event Action? SettingsRequested;
    public event Action? PauseToggled;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        // Onzichtbaar venstertje dat de klikken op het icoon ontvangt
        _window = new HwndSource(new HwndSourceParameters("IdleDashTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);

        // Als Verkenner herstart, verdwijnen alle icoontjes; dit bericht vertelt ons dat we hem opnieuw moeten plaatsen
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _icon = LoadTrayIcon(out _ownsIcon);
        Add();
    }

    /// <summary>Melding rechtsonder. onClick wordt uitgevoerd als je erop klikt.</summary>
    public void ShowNotification(string title, string message, Action? onClick = null)
    {
        if (!_added) return;
        _notificationClick = onClick;
        var data = CreateData(NativeMethods.NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = message.Length > 255 ? message[..255] : message;
        data.dwInfoFlags = NativeMethods.NIIF_NONE;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        data.szTip = "IdleDash";
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WM_LBUTTONUP) SettingsRequested?.Invoke();
            else if (mouse == WM_RBUTTONUP) ShowMenu();
            else if (mouse == NIN_BALLOONUSERCLICK)
            {
                var action = _notificationClick;
                _notificationClick = null;
                action?.Invoke();
            }
            handled = true;
        }
        else if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            Add();
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (UpdateMenuText != null)
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(CmdUpdate), UpdateMenuText);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        }
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(CmdSettings), Loc.T("Instellingen"));
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (Paused ? NativeMethods.MF_CHECKED : 0u),
            new UIntPtr(CmdPause), Loc.T("Dashboard pauzeren"));
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(CmdExit), Loc.T("IdleDash afsluiten"));
        NativeMethods.SetMenuDefaultItem(menu, CmdSettings, 0);

        NativeMethods.GetCursorPos(out var point);
        NativeMethods.SetForegroundWindow(_window.Handle);   // anders sluit het menu niet als je ernaast klikt
        int command = NativeMethods.TrackPopupMenuEx(menu,
            NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
            point.X, point.Y, _window.Handle, IntPtr.Zero);
        NativeMethods.PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        switch ((uint)command)
        {
            case CmdSettings: SettingsRequested?.Invoke(); break;
            case CmdPause: PauseToggled?.Invoke(); break;
            case CmdExit: ExitRequested?.Invoke(); break;
            case CmdUpdate: UpdateRequested?.Invoke(); break;
        }
    }

    /// <summary>Haalt het IdleDash-icoon uit het programma zelf, in het formaat dat het systeemvak wil.</summary>
    private static IntPtr LoadTrayIcon(out bool owned)
    {
        owned = false;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/IdleDash.ico"));
            if (resource != null)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                string path = Path.Combine(AppSettings.Folder, "tray.ico");
                using (var file = File.Create(path))
                    resource.Stream.CopyTo(file);

                int size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
                IntPtr icon = NativeMethods.LoadImage(IntPtr.Zero, path, NativeMethods.IMAGE_ICON, size, size, NativeMethods.LR_LOADFROMFILE);
                if (icon != IntPtr.Zero)
                {
                    owned = true;
                    return icon;
                }
            }
        }
        catch
        {
            // lukt het niet, dan het standaard Windows-icoon
        }
        return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }
        if (_ownsIcon && _icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _window.Dispose();
    }
}
