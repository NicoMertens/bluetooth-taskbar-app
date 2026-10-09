using System.Windows.Interop;
using Nc2.BluetoothTaskbarApp.Bluetooth;
using Nc2.BluetoothTaskbarApp.Interop;

namespace Nc2.BluetoothTaskbarApp.Tray;

/// <summary>
/// A notification-area icon driven straight through Shell_NotifyIcon — no
/// WinForms dependency, and the glyph is re-rendered when state or DPI changes.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WsExToolWindow = 0x00000080;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private readonly int _iconId = 1;

    private IntPtr _currentIcon = IntPtr.Zero;
    private bool _added;
    private int _connectedCount;
    private BatteryLevel _battery;
    private string _tooltip = "Bluetooth";
    private bool _disposed;

    /// <summary>Left click or keyboard selection on the icon.</summary>
    public event Action? Activated;

    /// <summary>Right click; the argument is the screen position to show a menu at.</summary>
    public event Action<NativeMethods.POINT>? ContextMenuRequested;

    public TrayIcon()
    {
        var parameters = new HwndSourceParameters("bluetooth-taskbar-app.Tray")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            ExtendedWindowStyle = WsExToolWindow,
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        // Explorer restarts drop every tray icon; this message tells us to re-add.
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        Add();
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>
    /// Sets badge count, battery tint and tooltip together, so a state change
    /// costs one redraw rather than several.
    /// </summary>
    public void Update(int connectedCount, BatteryLevel battery, string tooltip)
    {
        if (_connectedCount == connectedCount && _battery == battery && _tooltip == tooltip && _added)
            return;

        _connectedCount = connectedCount;
        _battery = battery;
        _tooltip = tooltip;
        UpdateIcon();
    }

    private void Add()
    {
        IntPtr icon = IconRenderer.CreateTrayIcon(_connectedCount, _battery);

        NativeMethods.NOTIFYICONDATA data = CreateData(icon);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            if (icon != IntPtr.Zero)
                NativeMethods.DestroyIcon(icon);
            return;
        }

        ReplaceIconHandle(icon);
        _added = true;

        // Opt into the v4 callback contract (NIN_SELECT / WM_CONTEXTMENU).
        var version = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = _iconId,
            uVersion = NativeMethods.NOTIFYICON_VERSION_4,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref version);
    }

    private void UpdateIcon()
    {
        if (!_added)
        {
            Add();
            return;
        }

        IntPtr icon = IconRenderer.CreateTrayIcon(_connectedCount, _battery);
        NativeMethods.NOTIFYICONDATA data = CreateData(icon);

        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
            ReplaceIconHandle(icon);
        else if (icon != IntPtr.Zero)
            NativeMethods.DestroyIcon(icon);
    }

    private NativeMethods.NOTIFYICONDATA CreateData(IntPtr icon) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = _iconId,
        uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
        uCallbackMessage = NativeMethods.WM_TRAYICON,
        hIcon = icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>The shell copies the icon, so the previous handle can go once swapped in.</summary>
    private void ReplaceIconHandle(IntPtr icon)
    {
        if (_currentIcon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_currentIcon);

        _currentIcon = icon;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _added = false;
            Add();
            handled = true;
            return IntPtr.Zero;
        }

        // Switching Windows between light and dark repaints the taskbar, so the
        // glyph has to be redrawn in the opposite colour.
        if (msg == NativeMethods.WM_SETTINGCHANGE
            && lParam != IntPtr.Zero
            && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            UpdateIcon();
            return IntPtr.Zero;
        }

        if (msg != NativeMethods.WM_TRAYICON)
            return IntPtr.Zero;

        // v4 packing: lParam low word is the event, wParam carries the anchor point.
        int notification = (int)((long)lParam & 0xFFFF);

        switch (notification)
        {
            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
            case NativeMethods.WM_LBUTTONUP:
                Activated?.Invoke();
                handled = true;
                break;

            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
                var point = new NativeMethods.POINT
                {
                    X = (short)((long)wParam & 0xFFFF),
                    Y = (short)(((long)wParam >> 16) & 0xFFFF),
                };
                ContextMenuRequested?.Invoke(point);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_added)
        {
            var data = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _source.Handle,
                uID = _iconId,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_currentIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_currentIcon);
            _currentIcon = IntPtr.Zero;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
