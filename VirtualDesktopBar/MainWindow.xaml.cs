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
        public bool IsCurrent { get => _isCurrent; set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundBrush)); } } }
        private bool _isLast;
        public bool IsLast { get => _isLast; set { if (_isLast != value) { _isLast = value; OnPropertyChanged(); } } }
        public System.Windows.Media.Brush BackgroundBrush => IsCurrent ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xCC, 0xCC, 0xCC)) : System.Windows.Media.Brushes.Transparent;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppInfo : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        private ImageSource _appIcon;
        public ImageSource AppIcon { get => _appIcon; set { _appIcon = value; OnPropertyChanged(); } }
        private bool _isFocused;
        public bool IsFocused { get => _isFocused; set { if (_isFocused != value) { _isFocused = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocusBrush)); } } }
        public System.Windows.Media.Brush FocusBrush => IsFocused ? System.Windows.Media.Brushes.SkyBlue : System.Windows.Media.Brushes.Transparent;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon = new System.Windows.Forms.NotifyIcon();
        private bool _isExit;
        private int _shellHookMsg;

        [DllImport("VirtualDesktopAccessor.dll")] public static extern int GetWindowDesktopNumber(IntPtr window);
        [DllImport("VirtualDesktopAccessor.dll")] public static extern void PinWindow(IntPtr hwnd);
        [DllImport("VirtualDesktopAccessor.dll")] public static extern int GetDesktopCount();
        [DllImport("VirtualDesktopAccessor.dll")] public static extern void GoToDesktopNumber(int desktopNumber);
        [DllImport("VirtualDesktopAccessor.dll")] public static extern int GetCurrentDesktopNumber();
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);
        delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, int wParam, int lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int RegisterWindowMessage(string lpString);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] static extern uint GetClassLongPtr32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")] static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, SW_RESTORE = 9, WM_HOTKEY = 0x0312, WM_GETICON = 0x7F;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventDelegate;
        private IntPtr _hWinEventHook;

        public ObservableCollection<DesktopGroup> Groups { get; set; } = new ObservableCollection<DesktopGroup>();

        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            DesktopGroups.ItemsSource = Groups;
        }

        private void InitNotifyIcon()
        {
            _notifyIcon.Icon = new Icon("app.ico");
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("위치 리셋 (Shift+Ctrl+V)", null, (s, e) => SetWindowPosition()));
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("바 표시/숨기기 (Alt+V)", null, (s, e) => ToggleUI()));
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("종료", null, (s, e) => ExitApplication()));
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ToggleUI() { if (this.Visibility == Visibility.Visible) HideMainWindow(); else ShowMainWindow(); }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_isExit) { e.Cancel = true; HideMainWindow(); } base.OnClosing(e); }
        private void HideMainWindow() { this.Hide(); }
        private void ShowMainWindow() { RefreshData(true); this.Show(); this.WindowState = WindowState.Normal; this.Activate(); SetWindowPosition(); }
        private void ExitApplication() { _isExit = true; if (_hWinEventHook != IntPtr.Zero) UnhookWinEvent(_hWinEventHook); _notifyIcon.Dispose(); Process.GetCurrentProcess().Kill(); }

        private void AppIcon_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AppInfo app)
            {
                int targetDesk = -1;
                foreach (var g in Groups) { if (g.Apps.Contains(app)) { targetDesk = g.DesktopId - 1; break; } }
                if (targetDesk != -1) try { GoToDesktopNumber(targetDesk); } catch { }
                ShowWindow(app.Hwnd, SW_RESTORE); SetForegroundWindow(app.Hwnd); ForceTopmost(); e.Handled = true;
            }
        }

        // 🔥 데스크톱 번호 클릭 시 해당 데스크톱으로 이동 및 포커스 복구
        private async void DesktopId_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DesktopGroup group)
            {
                try 
                { 
                    GoToDesktopNumber(group.DesktopId - 1); 
                    
                    // 데스크톱 전환 애니메이션 대기 후 포커스 복구
                    await System.Threading.Tasks.Task.Delay(300);
                    FocusTopWindowOnDesktop(group.DesktopId);
                } 
                catch { }
                e.Handled = true;
            }
        }

        private void FocusTopWindowOnDesktop(int desktopId)
        {
            IntPtr topHwnd = IntPtr.Zero;
            // EnumWindows는 Z-Order(위에서 아래) 순서로 창을 탐색합니다.
            EnumWindows((hWnd, lParam) => {
                if (IsWindowVisible(hWnd)) {
                    // 해당 데스크톱에 속한 창인지 확인
                    if (GetWindowDesktopNumber(hWnd) + 1 == desktopId) {
                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);
                        // 우리 앱(바)은 제외하고 가장 위에 있는 유효한 창 찾기
                        if (title.Length > 0 && title.ToString() != "VD Bar") {
                            topHwnd = hWnd;
                            return false; // 첫 번째(최상단) 창을 찾았으므로 탐색 중단
                        }
                    }
                }
                return true;
            }, 0);

            if (topHwnd != IntPtr.Zero) {
                ShowWindow(topHwnd, SW_RESTORE);
                SetForegroundWindow(topHwnd);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 9000) { ToggleUI(); handled = true; }
                else if (id == 9001) { SetWindowPosition(); handled = true; }
            }
            else if (msg == _shellHookMsg) { DelayedRefresh(800); handled = true; } // 지연 시간 약간 증가
            return IntPtr.Zero;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr myHwnd = new WindowInteropHelper(this).Handle;
            IntPtr exStyle = GetWindowLongPtr(myHwnd, GWL_EXSTYLE);
            if (IntPtr.Size == 8) SetWindowLongPtr64(myHwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE));
            else SetWindowLong32(myHwnd, GWL_EXSTYLE, (int)exStyle.ToInt64() | WS_EX_NOACTIVATE);

            HwndSource.FromHwnd(myHwnd).AddHook(HwndHook);
            _shellHookMsg = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(myHwnd);
            RegisterHotKey(myHwnd, 9000, 0x0001 | 0x0004, 0x56); // Alt+V
            RegisterHotKey(myHwnd, 9001, 0x0002 | 0x0004, 0x56); // Shift+Ctrl+V

            _winEventDelegate = new WinEventDelegate(WinEventProc);
            _hWinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_DESKTOPSWITCH, IntPtr.Zero, _winEventDelegate, 0, 0, 0);

            RefreshData(true);
            SetWindowPosition();
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (this.Visibility != Visibility.Visible) return;
            DelayedRefresh(eventType == EVENT_SYSTEM_DESKTOPSWITCH ? 100 : 300);
        }

        private async void DelayedRefresh(int delayMs)
        {
            await System.Threading.Tasks.Task.Delay(delayMs);
            Dispatcher.Invoke(() => { PinWindow(new WindowInteropHelper(this).Handle); RefreshData(true); ForceTopmost(); });
        }

        private void ForceTopmost()
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            if (hWnd != IntPtr.Zero && this.Visibility == Visibility.Visible)
            {
                SetWindowPos(hWnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                this.Topmost = false; this.Topmost = true;
            }
        }

        private void SetWindowPosition()
        {
            Dispatcher.Invoke(() => {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                var source = PresentationSource.FromVisual(this);
                double dpiY = (source?.CompositionTarget != null) ? source.CompositionTarget.TransformToDevice.M22 : 1.0;
                this.Left = (screen.WorkingArea.Left / dpiY) + 10;
                this.Top = (screen.Bounds.Bottom / dpiY) - this.ActualHeight;
                PinWindow(new WindowInteropHelper(this).Handle); ForceTopmost();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void RefreshData(bool forceIconUpdate = false)
        {
            if (this.Visibility != Visibility.Visible) return;
            int desktopCount = 3; try { desktopCount = GetDesktopCount(); } catch { }
            if (Groups.Count != desktopCount) { Groups.Clear(); for (int i = 0; i < desktopCount; i++) Groups.Add(new DesktopGroup { DesktopId = i + 1 }); }
            IntPtr focusedHwnd = GetForegroundWindow();
            int currentDesk = -1; try { currentDesk = GetCurrentDesktopNumber(); } catch { }
            for (int i = 0; i < Groups.Count; i++) { Groups[i].IsLast = (i == Groups.Count - 1); Groups[i].IsCurrent = (Groups[i].DesktopId == currentDesk + 1); }

            var currentWindows = new List<AppInfo_Internal>();
            EnumWindows((hWnd, lParam) => {
                if (IsWindowVisible(hWnd)) {
                    StringBuilder title = new StringBuilder(256); GetWindowText(hWnd, title, title.Capacity);
                    if (title.Length > 0 && title.ToString() != "VD Bar") {
                        int deskNum = GetWindowDesktopNumber(hWnd);
                        if (deskNum >= 0 && deskNum < desktopCount) currentWindows.Add(new AppInfo_Internal { DesktopId = deskNum + 1, Hwnd = hWnd });
                    }
                }
                return true;
            }, 0);

            foreach (var group in Groups) {
                var newApps = currentWindows.Where(x => x.DesktopId == group.DesktopId).ToList();
                var toRemove = group.Apps.Where(a => !newApps.Any(n => n.Hwnd == a.Hwnd)).ToList();
                foreach (var a in toRemove) group.Apps.Remove(a);
                foreach (var n in newApps) {
                    var existing = group.Apps.FirstOrDefault(a => a.Hwnd == n.Hwnd);
                    if (existing == null) {
                        ImageSource icon = ExtractIconFromHwnd(n.Hwnd);
                        if (icon != null) group.Apps.Add(new AppInfo { Hwnd = n.Hwnd, AppIcon = icon, IsFocused = (n.Hwnd == focusedHwnd) });
                    }
                    else {
                        existing.IsFocused = (existing.Hwnd == focusedHwnd);
                        if (forceIconUpdate) { var newIcon = ExtractIconFromHwnd(n.Hwnd); if (newIcon != null) existing.AppIcon = newIcon; }
                    }
                }
            }
        }

        class AppInfo_Internal { public int DesktopId; public IntPtr Hwnd; }

        private ImageSource ExtractIconFromHwnd(IntPtr hWnd)
        {
            try {
                IntPtr hIcon = IntPtr.Zero; IntPtr res;
                // 1. WM_GETICON Small
                if (SendMessageTimeout(hWnd, WM_GETICON, 0, 0, 0x0002, 100, out res) != IntPtr.Zero && res != IntPtr.Zero) hIcon = res;
                // 2. WM_GETICON Big
                if (hIcon == IntPtr.Zero && SendMessageTimeout(hWnd, WM_GETICON, 1, 0, 0x0002, 100, out res) != IntPtr.Zero && res != IntPtr.Zero) hIcon = res;
                // 3. Class Small Icon (-34)
                if (hIcon == IntPtr.Zero) hIcon = (IntPtr.Size == 8) ? GetClassLongPtr64(hWnd, -34) : (IntPtr)GetClassLongPtr32(hWnd, -34);
                // 4. Class Icon (-14)
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
