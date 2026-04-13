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
