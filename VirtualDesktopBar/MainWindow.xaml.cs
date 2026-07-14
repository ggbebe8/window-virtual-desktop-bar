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

        private string _desktopName;
        public string DesktopName { get => _desktopName; set { if (_desktopName != value) { _desktopName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } } }
        public string DisplayName => MainWindow.ShowDesktopNames ? (string.IsNullOrEmpty(DesktopName) ? $"데스크톱 {DesktopId}" : DesktopName) : DesktopId.ToString();
        public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));

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
        [DllImport("VirtualDesktopAccessor.dll")] public static extern int GetDesktopName(int desktopNumber, byte[] name, int length);
        [DllImport("VirtualDesktopAccessor.dll")] public static extern void SetDesktopName(int desktopNumber, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);
        delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, int wParam, int lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
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

        public static bool ShowDesktopNames { get; set; } = false;
        public static bool UseBottomOffset { get; set; } = false;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, SW_RESTORE = 9, SW_SHOW = 5, WM_HOTKEY = 0x0312, WM_GETICON = 0x7F;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventDelegate;
        private IntPtr _hWinEventHook;

        public ObservableCollection<DesktopGroup> Groups { get; set; } = new ObservableCollection<DesktopGroup>();

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            InitNotifyIcon();
            DesktopGroups.ItemsSource = Groups;
        }

        private void SaveSettings()
        {
            try 
            { 
                string content = $"ShowDesktopNames={ShowDesktopNames}\nUseBottomOffset={UseBottomOffset}";
                System.IO.File.WriteAllText("settings.cfg", content); 
            } catch { }
        }

        private void LoadSettings()
        {
            try 
            { 
                if (System.IO.File.Exists("settings.cfg")) 
                {
                    var lines = System.IO.File.ReadAllLines("settings.cfg");
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            if (parts[0] == "ShowDesktopNames") ShowDesktopNames = bool.Parse(parts[1]);
                            else if (parts[0] == "UseBottomOffset") UseBottomOffset = bool.Parse(parts[1]);
                        }
                    }
                }
            } catch { }
        }

        private void InitNotifyIcon()
        {
            _notifyIcon.Icon = new Icon("app.ico");
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("위치 리셋", null, (s, e) => SetWindowPosition()));
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("바 표시/숨기기 (Win+Alt+V)", null, (s, e) => ToggleUI()));
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("이름/번호 전환", null, (s, e) => {
                ShowDesktopNames = !ShowDesktopNames;
                SaveSettings();
                foreach (var g in Groups) g.RefreshDisplayName();
            }));
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("바 위치 전환 (하단/여백)", null, (s, e) => {
                UseBottomOffset = !UseBottomOffset;
                SaveSettings();
                SetWindowPosition();
            }));
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
                
                if (IsIconic(app.Hwnd)) ShowWindow(app.Hwnd, SW_RESTORE);
                else ShowWindow(app.Hwnd, SW_SHOW);

                SetForegroundWindow(app.Hwnd); ForceTopmost(); e.Handled = true;
            }
        }

        // 🔥 데스크톱 번호 클릭 시 해당 데스크톱으로 이동
        private void DesktopId_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DesktopGroup group)
            {
                try 
                { 
                    GoToDesktopNumber(group.DesktopId - 1); 
                } 
                catch { }
                e.Handled = true;
            }
        }

        private void DesktopId_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DesktopGroup group)
            {
                string newName = ShowInputDialog(group.DesktopName);
                if (newName != null)
                {
                    SetDesktopName(group.DesktopId - 1, newName);
                    RefreshData(true);
                }
                e.Handled = true;
            }
        }

        private string ShowInputDialog(string defaultText)
        {
            Window dialog = new Window { Width = 300, Height = 130, Title = "이름 변경", WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ResizeMode = ResizeMode.NoResize };
            var tb = new System.Windows.Controls.TextBox { Text = defaultText, Margin = new Thickness(10) };
            var btn = new System.Windows.Controls.Button { Content = "확인", Margin = new Thickness(10, 0, 10, 10), Width = 80, IsDefault = true };
            btn.Click += (s, e) => dialog.DialogResult = true;
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(tb); panel.Children.Add(btn);
            dialog.Content = panel;
            if (dialog.ShowDialog() == true) return tb.Text;
            return null;
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
            RegisterHotKey(myHwnd, 9000, 0x0008 | 0x0001, 0x56); // Win+Alt+V

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
                
                double topPos = (screen.Bounds.Bottom / dpiY) - this.ActualHeight;
                if (UseBottomOffset) topPos -= 50;
                this.Top = topPos;

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
            for (int i = 0; i < Groups.Count; i++) 
            { 
                Groups[i].IsLast = (i == Groups.Count - 1); 
                Groups[i].IsCurrent = (Groups[i].DesktopId == currentDesk + 1);

                byte[] buffer = new byte[1024];
                int res = GetDesktopName(Groups[i].DesktopId - 1, buffer, buffer.Length);
                Groups[i].DesktopName = (res >= 0) ? Encoding.UTF8.GetString(buffer).Split('\0')[0] : "";
            }

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

            // 🔥 Z-Order 역순(실행 순서와 유사)으로 뒤집어 작업표시줄과 비슷한 느낌을 줍니다.
            currentWindows.Reverse();

            foreach (var group in Groups) {
                var newApps = currentWindows.Where(x => x.DesktopId == group.DesktopId).ToList();
                
                // 1. 없어진 창 제거
                var toRemove = group.Apps.Where(a => !newApps.Any(n => n.Hwnd == a.Hwnd)).ToList();
                foreach (var a in toRemove) group.Apps.Remove(a);
                
                // 2. 새로운 창 추가 및 기존 창 위치 유지
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

                // 🔥 3. 정렬 상태를 리스트 순서에 맞게 동기화 (기존 아이콘 위치는 최대한 고정)
                // 이 부분은 위 루프에서 순서대로 추가되므로 자연스럽게 유지됩니다.
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
