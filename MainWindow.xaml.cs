using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Forms;

namespace InputMonitorMapper;

public partial class MainWindow : Window
{
    private const int WM_INPUT = 0x00FF;

    private MonitorMapper.MonitorInfo? _selectedMonitor;
    private bool _isLocked;
    private NotifyIcon? _trayIcon;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        LoadMonitors();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupTrayIcon();
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
        _hwndSource = source;
        RefreshMiceButton_Click(this, new RoutedEventArgs());
        RefreshKeyboardsButton_Click(this, new RoutedEventArgs());
        _foregroundUpdateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _foregroundUpdateTimer.Tick += (_, _) =>
        {
            if (RawInputHelper.IsMultiKeyboardActive)
                RawInputHelper.UpdateTargetWindowFromForeground();
        };
    }

    private System.Windows.Threading.DispatcherTimer? _foregroundUpdateTimer;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT && (RawInputHelper.IsMultiMouseActive || RawInputHelper.IsMultiKeyboardActive))
        {
            if (RawInputHelper.ProcessInput(lParam))
                handled = true;
        }
        return IntPtr.Zero;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Keyboard & Mouse → Monitor",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };

        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("Show window");
        showItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        menu.Items.Add(showItem);
        var unlockItem = new ToolStripMenuItem("Unlock mouse");
        unlockItem.Click += (_, _) => Dispatcher.Invoke(UnlockFromTray);
        menu.Items.Add(unlockItem);
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            MonitorMapper.ReleaseMouseClip();
            if (RawInputHelper.IsMultiMouseActive) RawInputHelper.DisableMultiMouse();
            if (RawInputHelper.IsMultiKeyboardActive) RawInputHelper.DisableMultiKeyboard();
            System.Windows.Application.Current.Shutdown();
        });
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = menu;
    }

    private void UnlockFromTray()
    {
        MonitorMapper.ReleaseMouseClip();
        _isLocked = false;
        UnlockButton.IsEnabled = false;
        LockButton.IsEnabled = _selectedMonitor != null;
        StatusText.Text = "Mouse unlocked from tray.";
    }

    private void LoadMonitors()
    {
        var monitors = MonitorMapper.GetMonitors();
        var items = monitors.Select(m => new MonitorItem(m)).ToList();
        MonitorList.ItemsSource = items;
        if (items.Count > 0)
            MonitorList.SelectedIndex = 0;
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMonitor = (MonitorList.SelectedItem as MonitorItem)?.Info;
        LockButton.IsEnabled = _selectedMonitor != null && !_isLocked;
        if (_selectedMonitor != null && !_isLocked)
            StatusText.Text = $"Selected: {_selectedMonitor}. Click Lock to confine the mouse to this monitor.";
    }

    private void MonitorList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedMonitor != null && !_isLocked)
            LockButton_Click(sender, e);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor == null) return;
        if (MonitorMapper.ClipMouseToMonitor(_selectedMonitor))
        {
            _isLocked = true;
            LockButton.IsEnabled = false;
            UnlockButton.IsEnabled = true;
            StatusText.Text = $"Mouse locked to {_selectedMonitor}. Click Unlock to release.";
        }
        else
        {
            StatusText.Text = "Failed to lock mouse to monitor.";
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        MonitorMapper.ReleaseMouseClip();
        _isLocked = false;
        UnlockButton.IsEnabled = false;
        LockButton.IsEnabled = _selectedMonitor != null;
        StatusText.Text = "Mouse unlocked. Select a monitor and click Lock to confine again.";
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        MonitorMapper.ReleaseMouseClip();
        if (RawInputHelper.IsMultiMouseActive)
            RawInputHelper.DisableMultiMouse();
        if (RawInputHelper.IsMultiKeyboardActive)
            RawInputHelper.DisableMultiKeyboard();
        _foregroundUpdateTimer?.Stop();
        _trayIcon?.Dispose();
    }

    #region Multi-mouse

    private readonly ObservableCollection<MouseAssignmentItem> _mouseAssignments = new();

    private void RefreshMiceButton_Click(object sender, RoutedEventArgs e)
    {
        var mice = RawInputHelper.GetMice();
        var monitors = MonitorMapper.GetMonitors().ToList();
        _mouseAssignments.Clear();
        foreach (var mouse in mice)
        {
            var item = new MouseAssignmentItem(mouse, monitors);
            item.PropertyChanged += (_, _) =>
            {
                UpdateMultiMouseButtons();
                if (RawInputHelper.IsMultiMouseActive)
                {
                    foreach (var a in _mouseAssignments)
                        RawInputHelper.AssignMouseToMonitor(a.Mouse.Handle, a.SelectedMonitor);
                }
            };
            _mouseAssignments.Add(item);
        }
        MouseAssignmentsList.ItemsSource = _mouseAssignments;
        UpdateMultiMouseButtons();
        MultiMouseStatusText.Text = mice.Count == 0
            ? "No mice found. Connect mice and click Refresh."
            : $"Found {mice.Count} mouse/mice. Assign each to a monitor.";
    }

    private void UpdateMultiMouseButtons()
    {
        var hasAssignment = _mouseAssignments.Any(a => a.SelectedMonitor != null);
        EnableMultiMouseButton.IsEnabled = hasAssignment && !RawInputHelper.IsMultiMouseActive;
        DisableMultiMouseButton.IsEnabled = RawInputHelper.IsMultiMouseActive;
    }

    private void EnableMultiMouseButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _mouseAssignments)
        {
            if (item.SelectedMonitor != null)
                RawInputHelper.AssignMouseToMonitor(item.Mouse.Handle, item.SelectedMonitor);
        }
        if (_hwndSource == null) return;
        MonitorMapper.ReleaseMouseClip();
        _isLocked = false;
        UnlockButton.IsEnabled = false;
        LockButton.IsEnabled = _selectedMonitor != null;
        if (RawInputHelper.EnableMultiMouse(_hwndSource.Handle))
        {
            EnableMultiMouseButton.IsEnabled = false;
            DisableMultiMouseButton.IsEnabled = true;
            MultiMouseStatusText.Text = "Multi-mouse active: each mouse moves the cursor only within its assigned monitor.";
        }
        else
        {
            MultiMouseStatusText.Text = "Failed to enable multi-mouse.";
        }
    }

    private void DisableMultiMouseButton_Click(object sender, RoutedEventArgs e)
    {
        RawInputHelper.DisableMultiMouse();
        DisableMultiMouseButton.IsEnabled = false;
        EnableMultiMouseButton.IsEnabled = _mouseAssignments.Any(a => a.SelectedMonitor != null);
        MultiMouseStatusText.Text = "Multi-mouse disabled. Mice behave normally again.";
    }

    #endregion

    #region Multi-keyboard

    private readonly ObservableCollection<KeyboardAssignmentItem> _keyboardAssignments = new();

    private void RefreshKeyboardsButton_Click(object sender, RoutedEventArgs e)
    {
        var keyboards = RawInputHelper.GetKeyboards();
        var monitors = MonitorMapper.GetMonitors().ToList();
        _keyboardAssignments.Clear();
        foreach (var kb in keyboards)
        {
            var item = new KeyboardAssignmentItem(kb, monitors);
            item.PropertyChanged += (_, _) =>
            {
                UpdateMultiKeyboardButtons();
                if (RawInputHelper.IsMultiKeyboardActive)
                {
                    foreach (var a in _keyboardAssignments)
                        RawInputHelper.AssignKeyboardToMonitor(a.Keyboard.Handle, a.SelectedMonitor);
                }
            };
            _keyboardAssignments.Add(item);
        }
        KeyboardAssignmentsList.ItemsSource = _keyboardAssignments;
        UpdateMultiKeyboardButtons();
        MultiKeyboardStatusText.Text = keyboards.Count == 0
            ? "No keyboards found. Connect keyboards and click Refresh."
            : $"Found {keyboards.Count} keyboard(s). Assign each to a monitor.";
    }

    private void UpdateMultiKeyboardButtons()
    {
        var hasAssignment = _keyboardAssignments.Any(a => a.SelectedMonitor != null);
        EnableMultiKeyboardButton.IsEnabled = hasAssignment && !RawInputHelper.IsMultiKeyboardActive;
        DisableMultiKeyboardButton.IsEnabled = RawInputHelper.IsMultiKeyboardActive;
    }

    private void EnableMultiKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _keyboardAssignments)
        {
            if (item.SelectedMonitor != null)
                RawInputHelper.AssignKeyboardToMonitor(item.Keyboard.Handle, item.SelectedMonitor);
        }
        if (_hwndSource == null) return;
        RawInputHelper.UpdateTargetWindowFromForeground();
        if (RawInputHelper.EnableMultiKeyboard(_hwndSource.Handle))
        {
            _foregroundUpdateTimer?.Start();
            EnableMultiKeyboardButton.IsEnabled = false;
            DisableMultiKeyboardButton.IsEnabled = true;
            MultiKeyboardStatusText.Text = "Multi-keyboard active: each keyboard types into the last focused window on its monitor.";
        }
        else
        {
            MultiKeyboardStatusText.Text = "Failed to enable multi-keyboard.";
        }
    }

    private void DisableMultiKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        _foregroundUpdateTimer?.Stop();
        RawInputHelper.DisableMultiKeyboard();
        DisableMultiKeyboardButton.IsEnabled = false;
        EnableMultiKeyboardButton.IsEnabled = _keyboardAssignments.Any(a => a.SelectedMonitor != null);
        MultiKeyboardStatusText.Text = "Multi-keyboard disabled. Keyboards behave normally again.";
    }

    #endregion

    private sealed class MonitorItem
    {
        public MonitorMapper.MonitorInfo Info { get; }
        public string DisplayName => Info.ToString();

        public MonitorItem(MonitorMapper.MonitorInfo info) => Info = info;
    }

    public sealed class MouseAssignmentItem : INotifyPropertyChanged
    {
        public RawInputHelper.MouseDeviceInfo Mouse { get; }
        public List<MonitorMapper.MonitorInfo> Monitors { get; }

        private MonitorMapper.MonitorInfo? _selectedMonitor;
        public MonitorMapper.MonitorInfo? SelectedMonitor
        {
            get => _selectedMonitor;
            set
            {
                if (_selectedMonitor == value) return;
                _selectedMonitor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
            }
        }

        public MouseAssignmentItem(RawInputHelper.MouseDeviceInfo mouse, List<MonitorMapper.MonitorInfo> monitors)
        {
            Mouse = mouse;
            Monitors = monitors;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class KeyboardAssignmentItem : INotifyPropertyChanged
    {
        public RawInputHelper.KeyboardDeviceInfo Keyboard { get; }
        public List<MonitorMapper.MonitorInfo> Monitors { get; }

        private MonitorMapper.MonitorInfo? _selectedMonitor;
        public MonitorMapper.MonitorInfo? SelectedMonitor
        {
            get => _selectedMonitor;
            set
            {
                if (_selectedMonitor == value) return;
                _selectedMonitor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
            }
        }

        public KeyboardAssignmentItem(RawInputHelper.KeyboardDeviceInfo keyboard, List<MonitorMapper.MonitorInfo> monitors)
        {
            Keyboard = keyboard;
            Monitors = monitors;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
