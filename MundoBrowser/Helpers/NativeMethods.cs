using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MundoBrowser.Helpers;

public static class NativeMethods
{
    public enum DWMWINDOWATTRIBUTE
    {
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
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
            store.SetValue(ref key, ref pv);
            store.Commit();
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

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

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
