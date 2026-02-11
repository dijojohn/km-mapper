using System.Runtime.InteropServices;

namespace InputMonitorMapper;

/// <summary>
/// Enumerates mice via Raw Input and routes each mouse's movement and clicks
/// to a specific monitor. Uses RIDEV_NOLEGACY so we receive exclusive raw input
/// and must inject cursor movement and button events ourselves.
/// </summary>
public static class RawInputHelper
{
    #region Constants

    private const int RIM_TYPEMOUSE = 0;
    private const int RIM_TYPEKEYBOARD = 1;
    private const int RID_INPUT = 0x10000003;
    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int RIDEV_NOLEGACY = 0x00000030;

    private const int RIDI_DEVICENAME = 0x20000007;

    private const ushort RI_KEY_BREAK = 0x0001; // key up

    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    private const ushort RI_MOUSE_WHEEL = 0x0400;

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    #endregion

    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern int GetRawInputData(IntPtr hRawInput, int uiCommand, IntPtr pData, ref int pcbSize, int cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        // union: mouse, keyboard, hid - we only need to know dwType
    }

    #endregion

    private static readonly Dictionary<IntPtr, MonitorMapper.MonitorInfo> DeviceToMonitor = new();
    private static readonly Dictionary<IntPtr, MonitorMapper.MonitorInfo> KeyboardToMonitor = new();
    private static readonly Dictionary<IntPtr, IntPtr> MonitorHandleToTargetWindow = new();
    private static readonly object Lock = new();
    private static IntPtr _targetHwnd;
    private static bool _registered;
    private static bool _keyboardRegistered;

    /// <summary>
    /// Info about a physical mouse device for display and assignment.
    /// </summary>
    public sealed class MouseDeviceInfo
    {
        public IntPtr Handle { get; }
        public string Name { get; }

        internal MouseDeviceInfo(IntPtr handle, string name)
        {
            Handle = handle;
            Name = name ?? $"Mouse {handle}";
        }

        public override string ToString() => string.IsNullOrEmpty(Name) ? $"Mouse {Handle}" : Name;
    }

    /// <summary>
    /// Info about a physical keyboard device for display and assignment.
    /// </summary>
    public sealed class KeyboardDeviceInfo
    {
        public IntPtr Handle { get; }
        public string Name { get; }

        internal KeyboardDeviceInfo(IntPtr handle, string name)
        {
            Handle = handle;
            Name = name ?? $"Keyboard {handle}";
        }

        public override string ToString() => string.IsNullOrEmpty(Name) ? $"Keyboard {Handle}" : Name;
    }

    /// <summary>
    /// Enumerate all mouse devices (Raw Input).
    /// </summary>
    public static IReadOnlyList<MouseDeviceInfo> GetMice()
    {
        uint count = 0;
        GetRawInputDeviceList(IntPtr.Zero, ref count, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());
        if (count == 0) return Array.Empty<MouseDeviceInfo>();

        var size = Marshal.SizeOf<RAWINPUTDEVICELIST>();
        var buffer = Marshal.AllocHGlobal((int)(count * size));
        try
        {
            uint actual = count;
            if (GetRawInputDeviceList(buffer, ref actual, (uint)size) != count)
                return Array.Empty<MouseDeviceInfo>();

            var list = new List<MouseDeviceInfo>();
            for (int i = 0; i < count; i++)
            {
                var ptr = IntPtr.Add(buffer, i * size);
                var dev = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(ptr);
                if (dev.dwType != RIM_TYPEMOUSE) continue;

                var name = GetDeviceName(dev.hDevice);
                list.Add(new MouseDeviceInfo(dev.hDevice, name));
            }
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Enumerate all keyboard devices (Raw Input).
    /// </summary>
    public static IReadOnlyList<KeyboardDeviceInfo> GetKeyboards()
    {
        uint count = 0;
        GetRawInputDeviceList(IntPtr.Zero, ref count, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());
        if (count == 0) return Array.Empty<KeyboardDeviceInfo>();

        var size = Marshal.SizeOf<RAWINPUTDEVICELIST>();
        var buffer = Marshal.AllocHGlobal((int)(count * size));
        try
        {
            uint actual = count;
            if (GetRawInputDeviceList(buffer, ref actual, (uint)size) != count)
                return Array.Empty<KeyboardDeviceInfo>();

            var list = new List<KeyboardDeviceInfo>();
            for (int i = 0; i < count; i++)
            {
                var ptr = IntPtr.Add(buffer, i * size);
                var dev = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(ptr);
                if (dev.dwType != RIM_TYPEKEYBOARD) continue;

                var name = GetDeviceName(dev.hDevice);
                list.Add(new KeyboardDeviceInfo(dev.hDevice, name));
            }
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    private static string GetDeviceName(IntPtr hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return "";
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref size) == 0)
                return "";
            return Marshal.PtrToStringUni(buf) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Assign a mouse device to a monitor. Call for each mouse you want to bind.
    /// </summary>
    public static void AssignMouseToMonitor(IntPtr deviceHandle, MonitorMapper.MonitorInfo? monitor)
    {
        lock (Lock)
        {
            if (monitor == null)
                DeviceToMonitor.Remove(deviceHandle);
            else
                DeviceToMonitor[deviceHandle] = monitor;
        }
    }

    /// <summary>
    /// Assign a keyboard device to a monitor. Keys from that keyboard are sent to the window on that monitor.
    /// </summary>
    public static void AssignKeyboardToMonitor(IntPtr deviceHandle, MonitorMapper.MonitorInfo? monitor)
    {
        lock (Lock)
        {
            if (monitor == null)
                KeyboardToMonitor.Remove(deviceHandle);
            else
                KeyboardToMonitor[deviceHandle] = monitor;
        }
    }

    /// <summary>
    /// Call when the foreground window changes so we know which window to send keys to for each monitor.
    /// </summary>
    public static void UpdateTargetWindowFromForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        var hMon = MonitorMapper.GetMonitorHandleFromWindow(hwnd);
        if (hMon == IntPtr.Zero) return;
        lock (Lock)
        {
            MonitorHandleToTargetWindow[hMon] = hwnd;
        }
    }

    /// <summary>
    /// Enable multi-keyboard mode: register keyboards with NOLEGACY so we receive keys and inject to the window on the assigned monitor.
    /// </summary>
    public static bool EnableMultiKeyboard(IntPtr hwnd)
    {
        var devices = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 1,
                usUsage = 6, // keyboard
                dwFlags = RIDEV_INPUTSINK | RIDEV_NOLEGACY,
                hwndTarget = hwnd
            }
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            return false;
        lock (Lock) _keyboardRegistered = true;
        return true;
    }

    public static void DisableMultiKeyboard()
    {
        var devices = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 1,
                usUsage = 6,
                dwFlags = 0,
                hwndTarget = IntPtr.Zero
            }
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        lock (Lock)
        {
            _keyboardRegistered = false;
            KeyboardToMonitor.Clear();
            MonitorHandleToTargetWindow.Clear();
        }
    }

    public static bool IsMultiKeyboardActive
    {
        get { lock (Lock) return _keyboardRegistered; }
    }

    /// <summary>
    /// Enable multi-mouse mode: register for raw input with NOLEGACY so we get
    /// exclusive mouse input and must inject movement/clicks. Target window receives WM_INPUT.
    /// </summary>
    public static bool EnableMultiMouse(IntPtr hwnd)
    {
        _targetHwnd = hwnd;
        var devices = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 1,
                usUsage = 2, // mouse
                dwFlags = RIDEV_INPUTSINK | RIDEV_NOLEGACY,
                hwndTarget = hwnd
            }
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            return false;
        lock (Lock) _registered = true;
        return true;
    }

    /// <summary>
    /// Disable multi-mouse: unregister (use RIDEV_REMOVE by setting hwndTarget to zero and re-register?).
    /// Actually to "unregister" we register with the same usage but without NOLEGACY and with hwndTarget = IntPtr.Zero.
    /// So we register again with dwFlags = 0, hwndTarget = IntPtr.Zero to stop receiving.
    /// </summary>
    public static void DisableMultiMouse()
    {
        var devices = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 1,
                usUsage = 2,
                dwFlags = 0,
                hwndTarget = IntPtr.Zero
            }
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        lock (Lock)
        {
            _registered = false;
            DeviceToMonitor.Clear();
        }
    }

    public static bool IsMultiMouseActive
    {
        get { lock (Lock) return _registered; }
    }

    /// <summary>
    /// Process WM_INPUT (lParam). Returns true if the message was handled.
    /// </summary>
    public static bool ProcessInput(IntPtr lParam)
    {
        int size = 0;
        int headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size <= 0) return false;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) <= 0)
                return false;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);

            if (header.dwType == RIM_TYPEKEYBOARD)
                return ProcessKeyboardInput(buffer, header);

            if (header.dwType != RIM_TYPEMOUSE) return false;

            MonitorMapper.MonitorInfo? monitor;
            lock (Lock)
            {
                DeviceToMonitor.TryGetValue(header.hDevice, out monitor);
            }

            int dataOffset = Marshal.SizeOf<RAWINPUTHEADER>();
            var mouse = Marshal.PtrToStructure<RAWMOUSE>(IntPtr.Add(buffer, dataOffset));

            // Assigned mice: clamp to monitor. Unassigned: apply movement normally (no clamp).
            if (mouse.lLastX != 0 || mouse.lLastY != 0)
            {
                if (!GetCursorPos(out var pt)) return true;
                int nx = pt.x + mouse.lLastX;
                int ny = pt.y + mouse.lLastY;
                if (monitor != null)
                {
                    var bounds = monitor.Bounds;
                    nx = Math.Clamp(nx, bounds.Left, bounds.Right - 1);
                    ny = Math.Clamp(ny, bounds.Top, bounds.Bottom - 1);
                }
                SetCursorPos(nx, ny);
            }

            // Buttons: re-inject so the window under cursor receives the click
            var flags = mouse.usButtonFlags;
            if (flags != 0)
            {
                var inputs = new List<INPUT>();
                if ((flags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
                    inputs.Add(CreateMouseInput(0, 0, MOUSEEVENTF_LEFTDOWN));
                if ((flags & RI_MOUSE_LEFT_BUTTON_UP) != 0)
                    inputs.Add(CreateMouseInput(0, 0, MOUSEEVENTF_LEFTUP));
                if ((flags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
                    inputs.Add(CreateMouseInput(0, 0, MOUSEEVENTF_RIGHTDOWN));
                if ((flags & RI_MOUSE_RIGHT_BUTTON_UP) != 0)
                    inputs.Add(CreateMouseInput(0, 0, MOUSEEVENTF_RIGHTUP));
                if ((flags & RI_MOUSE_WHEEL) != 0)
                    inputs.Add(CreateMouseWheelInput((uint)((short)mouse.usButtonData << 16)));

                if (inputs.Count > 0)
                    SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ProcessKeyboardInput(IntPtr buffer, RAWINPUTHEADER header)
    {
        MonitorMapper.MonitorInfo? monitor;
        lock (Lock)
        {
            KeyboardToMonitor.TryGetValue(header.hDevice, out monitor);
        }

        int dataOffset = Marshal.SizeOf<RAWINPUTHEADER>();
        var kb = Marshal.PtrToStructure<RAWKEYBOARD>(IntPtr.Add(buffer, dataOffset));

        IntPtr targetHwnd = IntPtr.Zero;
        if (monitor != null)
        {
            lock (Lock)
            {
                MonitorHandleToTargetWindow.TryGetValue(monitor.Handle, out targetHwnd);
            }
        }
        if (targetHwnd == IntPtr.Zero)
            targetHwnd = GetForegroundWindow();

        if (targetHwnd != IntPtr.Zero)
            SetForegroundWindow(targetHwnd);

        uint flags = (kb.Flags & RI_KEY_BREAK) != 0 ? KEYEVENTF_KEYUP : 0;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = kb.VKey,
                wScan = kb.MakeCode,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        return true;
    }

    private static INPUT CreateMouseInput(int dx, int dy, uint dwFlags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = 0,
                dwFlags = dwFlags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    private static INPUT CreateMouseWheelInput(uint mouseData)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = mouseData,
                dwFlags = MOUSEEVENTF_WHEEL,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }
}
