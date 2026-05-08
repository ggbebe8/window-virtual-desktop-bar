using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VirtualDesktopBar
{
    public class DesktopGroup : INotifyPropertyChanged
    {
        public int DesktopId { get; set; }
        public ObservableCollection<AppInfo> Apps { get; set; } = new ObservableCollection<AppInfo>();

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); } }
        }

        private bool _isLast;
        public bool IsLast
        {
            get => _isLast;
            set { if (_isLast != value) { _isLast = value; OnPropertyChanged(); } }
        }

        public System.Windows.Media.Brush BackgroundBrush => IsCurrent
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xCC, 0xCC, 0xCC))
            : System.Windows.Media.Brushes.Transparent;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppInfo : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        public ImageSource AppIcon { get; set; }

        private bool _isFocused;
        public bool IsFocused
        {
            get => _isFocused;
            set { if (_isFocused != value) { _isFocused = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusBrush)); } }
        }

        public System.Windows.Media.Brush FocusBrush => IsFocused ? System.Windows.Media.Brushes.SkyBlue : System.Windows.Media.Brushes.Transparent;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon = new System.Windows.Forms.NotifyIcon();
        private bool _isExit;

        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetWindowDesktopNumber(IntPtr window);
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern void PinWindow(IntPtr hwnd);
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetDesktopCount();
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern void GoToDesktopNumber(int desktopNumber);
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetCurrentDesktopNumber();

        delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        static extern uint GetClassLongPtr32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SW_RESTORE = 9;
        private const int WM_HOTKEY = 0x0312;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const int WM_GETICON = 0x7F;
        private const int ICON_SMALL = 0;
        private const int GCLP_HICON = -14;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventDelegate;
        private IntPtr _hWinEventHook;

        public ObservableCollection<DesktopGroup> Groups { get; set; } = new ObservableCollection<DesktopGroup>();

        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            DesktopGroups.ItemsSource = Groups;
            SetWindowPosition();
        }

        private void InitNotifyIcon()
        {
            _notifyIcon.Icon = new System.Drawing.Icon("app.ico");
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var toggleMenuItem = new System.Windows.Forms.ToolStripMenuItem("바 표시/숨기기");
            toggleMenuItem.Click += (s, e) => {
                if (this.Visibility == Visibility.Visible) this.Hide();
                else { RefreshData(); this.Show(); ForceTopmost(); }
            };
            contextMenu.Items.Add(toggleMenuItem);

            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("종료");
            exitMenuItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit) { e.Cancel = true; HideMainWindow(); }
            base.OnClosing(e);
        }

        private void HideMainWindow()
        {
            this.Hide();
            _notifyIcon.ShowBalloonTip(1000, "VirtualDesktopBar", "앱이 트레이로 최소화되었습니다.", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            PinWindow(new WindowInteropHelper(this).Handle); // 다시 열 때 고정 확인
            ForceTopmost();
        }

        private void ExitApplication()
        {
            _isExit = true;
            if (_hWinEventHook != IntPtr.Zero) UnhookWinEvent(_hWinEventHook);
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Process.GetCurrentProcess().Kill(); // 가장 확실한 프로세스 종료
        }

        private void AppIcon_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AppInfo app)
            {
                int targetDesktopIndex = -1;
                foreach (var group in Groups) { if (group.Apps.Contains(app)) { targetDesktopIndex = group.DesktopId - 1; break; } }
                if (targetDesktopIndex != -1) { try { GoToDesktopNumber(targetDesktopIndex); } catch { } }
                ShowWindow(app.Hwnd, SW_RESTORE);
                SetForegroundWindow(app.Hwnd);
                e.Handled = true;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetWindowPosition();
            IntPtr myHwnd = new WindowInteropHelper(this).Handle;
            PinWindow(myHwnd);
            RefreshData();
            ForceTopmost();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 9000)
            {
                if (this.Visibility == Visibility.Visible) this.Hide();
                else { RefreshData(); this.Show(); ForceTopmost(); }
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr myHwnd = new WindowInteropHelper(this).Handle;
            PinWindow(myHwnd);
            HwndSource.FromHwnd(myHwnd).AddHook(HwndHook);
            RegisterHotKey(myHwnd, 9000, 0x0001 | 0x0004, 0x56);
            _winEventDelegate = new WinEventDelegate(WinEventProc);
            _hWinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            RefreshData();
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(150);
                    Dispatcher.Invoke(() => {
                        PinWindow(new WindowInteropHelper(this).Handle); // 데스크톱 전환 대비 수시로 고정
                        RefreshData();
                        ForceTopmost();
                    });
                });
            }
        }

        private void ForceTopmost()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            this.Topmost = false; this.Topmost = true;
        }

        private void SetWindowPosition()
        {
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = SystemParameters.WorkArea.Left + 10;
            this.SizeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => this.Top = SystemParameters.PrimaryScreenHeight - this.ActualHeight), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void RefreshData()
        {
            int desktopCount = 3;
            try { desktopCount = GetDesktopCount(); } catch { }
            if (Groups.Count != desktopCount) { Groups.Clear(); for (int i = 0; i < desktopCount; i++) Groups.Add(new DesktopGroup { DesktopId = i + 1 }); }
            IntPtr focusedHwnd = GetForegroundWindow();
            int currentDesktopIndex = -1;
            try { currentDesktopIndex = GetCurrentDesktopNumber(); } catch { }
            for (int i = 0; i < Groups.Count; i++) { Groups[i].IsLast = (i == Groups.Count - 1); Groups[i].IsCurrent = (Groups[i].DesktopId == currentDesktopIndex + 1); }
            var currentWindows = new List<AppInfo_Internal>();
            EnumWindows((hWnd, lParam) => {
                if (IsWindowVisible(hWnd)) {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    if (title.Length > 0 && title.ToString() != "VD Bar") {
                        int deskNum = GetWindowDesktopNumber(hWnd);
                        if (deskNum >= 0 && deskNum < desktopCount) currentWindows.Add(new AppInfo_Internal { DesktopId = deskNum + 1, Hwnd = hWnd });
                    }
                }
                return true;
            }, 0);
            foreach (var group in Groups) {
                var newAppsForThisDesk = currentWindows.Where(x => x.DesktopId == group.DesktopId).ToList();
                var toRemove = group.Apps.Where(a => !newAppsForThisDesk.Any(n => n.Hwnd == a.Hwnd)).ToList();
                foreach (var app in toRemove) group.Apps.Remove(app);
                foreach (var newApp in newAppsForThisDesk) {
                    var existingApp = group.Apps.FirstOrDefault(a => a.Hwnd == newApp.Hwnd);
                    if (existingApp == null) {
                        ImageSource icon = ExtractIconFromHwnd(newApp.Hwnd);
                        if (icon != null) group.Apps.Add(new AppInfo { Hwnd = newApp.Hwnd, AppIcon = icon, IsFocused = (newApp.Hwnd == focusedHwnd) });
                    }
                    else { existingApp.IsFocused = (existingApp.Hwnd == focusedHwnd); }
                }
            }
        }

        class AppInfo_Internal { public int DesktopId; public IntPtr Hwnd; }

        private ImageSource ExtractIconFromHwnd(IntPtr hWnd)
        {
            try {
                IntPtr hIcon = SendMessage(hWnd, 0x7F, 0, 0);
                if (hIcon == IntPtr.Zero) hIcon = (IntPtr.Size == 8) ? GetClassLongPtr64(hWnd, -14) : (IntPtr)GetClassLongPtr32(hWnd, -14);
                if (hIcon != IntPtr.Zero) {
                    BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze(); return bs;
                }
            } catch { }
            return null;
        }

        protected override void OnClosed(EventArgs e) { if (_hWinEventHook != IntPtr.Zero) UnhookWinEvent(_hWinEventHook); _notifyIcon.Dispose(); base.OnClosed(e); }
    }
}
