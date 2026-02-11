using System.Runtime.InteropServices;

namespace InputMonitorMapper;

/// <summary>
/// Wraps Win32 APIs for enumerating monitors and constraining the mouse cursor.
/// </summary>
public static class MonitorMapper
{
    #region Win32 structures and delegates

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public System.Windows.Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    #endregion

    #region Win32 P/Invoke

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClipCursor(out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    /// <summary>Default to primary monitor.</summary>
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>Returns the monitor handle (HMONITOR) that contains the given window.</summary>
    public static IntPtr GetMonitorHandleFromWindow(IntPtr hwnd) =>
        MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    #endregion

    /// <summary>
    /// Represents a single physical monitor.
    /// </summary>
    public sealed class MonitorInfo
    {
        public IntPtr Handle { get; }
        public string DeviceName { get; }
        public RECT Bounds { get; }
        public RECT WorkArea { get; }
        public bool IsPrimary { get; }

        internal MonitorInfo(IntPtr handle, string deviceName, RECT bounds, RECT workArea, bool isPrimary)
        {
            Handle = handle;
            DeviceName = deviceName;
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
        }

        public string DisplayName => ToString();

        public override string ToString()
        {
            var primary = IsPrimary ? " (Primary)" : "";
            return $"Monitor {DeviceName}{primary} — {Bounds.Right - Bounds.Left}×{Bounds.Bottom - Bounds.Top}";
        }
    }

    private static readonly List<MonitorInfo> Monitors = new();
    private static readonly object Lock = new();
    private static bool _isClipped;

    /// <summary>
    /// Enumerates all display monitors.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        lock (Lock)
        {
            Monitors.Clear();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumProc, IntPtr.Zero);
            return Monitors.ToList();
        }
    }

    private static bool EnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            return true;

        const uint MONITORINFOF_PRIMARY = 1;
        var info = new MonitorInfo(
            hMonitor,
            string.IsNullOrEmpty(mi.szDevice) ? $"Display {Monitors.Count + 1}" : mi.szDevice,
            mi.rcMonitor,
            mi.rcWork,
            (mi.dwFlags & MONITORINFOF_PRIMARY) != 0);

        Monitors.Add(info);
        return true;
    }

    /// <summary>
    /// Locks the mouse cursor to the given monitor's bounds.
    /// </summary>
    public static bool ClipMouseToMonitor(MonitorInfo monitor)
    {
        if (monitor == null) return false;
        var r = monitor.Bounds;
        if (!ClipCursor(ref r)) return false;
        _isClipped = true;
        return true;
    }

    /// <summary>
    /// Locks the mouse cursor to the given rectangle (e.g. work area of a monitor).
    /// </summary>
    public static bool ClipMouseToRect(RECT rect)
    {
        if (!ClipCursor(ref rect)) return false;
        _isClipped = true;
        return true;
    }

    /// <summary>
    /// Releases the mouse cursor constraint.
    /// </summary>
    public static void ReleaseMouseClip()
    {
        ClipCursor(IntPtr.Zero);
        _isClipped = false;
    }

    /// <summary>
    /// Returns true if the cursor is currently clipped by this app.
    /// </summary>
    public static bool IsMouseClipped() => _isClipped;
}
