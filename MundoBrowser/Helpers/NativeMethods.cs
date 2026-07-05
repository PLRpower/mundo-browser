using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MundoBrowser.Helpers;

public static class NativeMethods
{
    public const string AppUserModelId = AppRuntime.AppUserModelId;

    public enum DWMWINDOWATTRIBUTE
    {
        DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35
    }

    public enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", PreserveSig = false)]
    public static extern void SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid guid, uint id) { fmtid = guid; pid = id; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptr;
        
        public static PropVariant FromString(string value)
        {
            var pv = new PropVariant();
            pv.vt = 31; // VT_LPWSTR
            pv.ptr = Marshal.StringToCoTaskMemUni(value);
            return pv;
        }
    }

    public static void SetWindowAppId(IntPtr hwnd, string appId)
    {
        try
        {
            Guid guid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"); // IID_IPropertyStore
            SHGetPropertyStoreForWindow(hwnd, ref guid, out var store);
            var key = new PropertyKey(new Guid("9F4C1853-C90B-4D97-A417-E78590E07DF9"), 5); // PKEY_AppUserModel_ID
            var pv = PropVariant.FromString(appId);
            try
            {
                store.SetValue(ref key, ref pv);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(pv.ptr);
            }
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref int pvAttribute, uint cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static bool IsCurrentProcessForeground()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    public static void SetWindowCorners(Window window, DWM_WINDOW_CORNER_PREFERENCE preference)
    {
        try
        {
            var hWnd = new WindowInteropHelper(window).Handle;
            SetWindowCorners(hWnd, preference);
        }
        catch { }
    }

    public static void SetWindowCorners(IntPtr hWnd, DWM_WINDOW_CORNER_PREFERENCE preference)
    {
        try
        {
            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(uint));
        }
        catch { }
    }

    public static void SuppressAccentBorder(Window window)
    {
        SetWindowFrameColors(window, showSubtleBorder: false);
    }

        public static void SetWindowFrameColors(Window window, bool showSubtleBorder)
    {
        try
        {
            var hWnd = new WindowInteropHelper(window).Handle;
            int noColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            int borderColor = showSubtleBorder ? 0x00303030 : noColor;

            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
            DwmSetWindowAttribute(hWnd, DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref noColor, sizeof(uint));
        }
        catch { }
    }

    public static void ApplyDarkMode(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            Action apply = () =>
            {
                int trueValue = 1;
                DwmSetWindowAttribute(helper.Handle, (DWMWINDOWATTRIBUTE)20, ref trueValue, sizeof(int));
            };

            if (helper.Handle == IntPtr.Zero)
            {
                window.SourceInitialized += (s, e) => apply();
            }
            else
            {
                apply();
            }
        }
        catch { }
    }

    public static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam, bool isFullScreen)
    {
        MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            MONITORINFO monitorInfo = new MONITORINFO();
            GetMonitorInfo(monitor, monitorInfo);
            
            // Use rcMonitor for true fullscreen (covers taskbar), rcWork for normal maximized
            RECT rcLimitArea = isFullScreen ? monitorInfo.rcMonitor : monitorInfo.rcWork;
            RECT rcMonitorArea = monitorInfo.rcMonitor;
            
            mmi.ptMaxPosition.x = rcLimitArea.left - rcMonitorArea.left;
            mmi.ptMaxPosition.y = rcLimitArea.top - rcMonitorArea.top;
            mmi.ptMaxSize.x = rcLimitArea.right - rcLimitArea.left;
            mmi.ptMaxSize.y = rcLimitArea.bottom - rcLimitArea.top;
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    public static RECT GetMonitorRect(IntPtr hwnd, bool useWorkArea)
    {
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            MONITORINFO monitorInfo = new MONITORINFO();
            if (GetMonitorInfo(monitor, monitorInfo))
            {
                return useWorkArea ? monitorInfo.rcWork : monitorInfo.rcMonitor;
            }
        }

        return new RECT
        {
            left = 0,
            top = 0,
            right = (int)SystemParameters.PrimaryScreenWidth,
            bottom = (int)SystemParameters.PrimaryScreenHeight
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor = new RECT();
        public RECT rcWork = new RECT();
        public int dwFlags = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public const int SW_RESTORE = 9;
    public const int ASW_ANY = -1;
    public const uint SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_IDLIST = 0x0000;
    public const uint SHCNF_FLUSH = 0x1000;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
}
