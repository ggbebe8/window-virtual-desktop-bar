using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VirtualDesktopBar;

public partial class MainWindow
{
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _maintenanceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IntPtr _barHwnd;
    private int _taskbarCreatedMsg;
    private bool _reportedNativeError;
    private static ImageSource? _defaultAppIcon;

    private static ImageSource DefaultAppIcon
    {
        get
        {
            if (_defaultAppIcon != null) return _defaultAppIcon;
            var icon = Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Application.Handle,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            icon.Freeze();
            return _defaultAppIcon = icon;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Param;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern bool DeregisterShellHookWindow(IntPtr hwnd);

    private static IntPtr ReadWindowStyle(IntPtr hwnd) => IntPtr.Size == 8
        ? GetWindowLongPtr(hwnd, GWL_EXSTYLE) : new IntPtr(GetWindowLong32(hwnd, GWL_EXSTYLE));

    private void InitializePresentation()
    {
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (!IsVisible || _isExit) return;
            RefreshSafely();
            SetWindowPosition();
            EnsureAboveTaskbar();
        };
        // Recover missed shell notifications without repeatedly changing Z order.
        _maintenanceTimer.Tick += (_, _) =>
        {
            if (!IsVisible || _isExit) return;
            RefreshSafely();
            SetWindowPosition();
            EnsureAboveTaskbar();
        };
        Loaded += (_, _) =>
        {
            PinBar();
            RefreshSafely();
            SetWindowPosition();
            ForceTopmost();
            _maintenanceTimer.Start();
        };
        SizeChanged += (_, _) => SetWindowPosition();
    }

    private void AddPlacementMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        var overlay = new System.Windows.Forms.ToolStripMenuItem("작업표시줄에 겹치기") { Checked = !UseBottomOffset };
        var above = new System.Windows.Forms.ToolStripMenuItem("작업표시줄 바로 위") { Checked = UseBottomOffset };
        void SelectPlacement(bool useAbove)
        {
            UseBottomOffset = useAbove;
            overlay.Checked = !useAbove;
            above.Checked = useAbove;
            SaveSettings();
            SetWindowPosition();
            ForceTopmost();
        }
        overlay.Click += (_, _) => SelectPlacement(false);
        above.Click += (_, _) => SelectPlacement(true);
        var placement = new System.Windows.Forms.ToolStripMenuItem("바 위치");
        placement.DropDownItems.Add(overlay);
        placement.DropDownItems.Add(above);
        menu.Items.Add(placement);
    }

    private void PinBar()
    {
        if (_barHwnd == IntPtr.Zero || _isExit) return;
        try { PinWindow(_barHwnd); }
        catch (Exception ex) { ReportNativeError(ex); }
    }

    private void RefreshSafely()
    {
        try { RefreshData(); }
        catch (Exception ex) { ReportNativeError(ex); }
    }

    private void ReportNativeError(Exception ex)
    {
        Trace.TraceError("Virtual desktop refresh failed: {0}", ex);
        if (_reportedNativeError) return;
        _reportedNativeError = true;
        _notifyIcon.ShowBalloonTip(5000, "VD Bar",
            "가상 데스크톱 정보를 읽지 못했습니다. VirtualDesktopAccessor.dll과 Windows 버전을 확인해 주세요.",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void EnsureAboveTaskbar()
    {
        if (_barHwnd == IntPtr.Zero || !IsVisible) return;
        if ((ReadWindowStyle(_barHwnd).ToInt64() & 0x8) == 0) // WS_EX_TOPMOST
        {
            ForceTopmost();
            return;
        }
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar)) return;
        // Only repair when Explorer actually placed its taskbar ahead of this bar.
        for (var window = GetWindow(_barHwnd, 3); window != IntPtr.Zero; window = GetWindow(window, 3))
        {
            if (window != taskbar) continue;
            ForceTopmost();
            break;
        }
    }

    private void PositionAtTaskbar()
    {
        if (_barHwnd == IntPtr.Zero || !IsVisible || ActualHeight <= 0) return;
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen == null) return;
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        bool hasTaskbar = SHAppBarMessage(5, ref data) != UIntPtr.Zero;
        var taskbar = hasTaskbar
            ? new System.Drawing.Rectangle(data.Rect.Left, data.Rect.Top,
                data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top)
            : System.Drawing.Rectangle.Empty;
        if (taskbar.Width <= 0 || taskbar.Height <= 0) hasTaskbar = false;

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int width = (int)Math.Ceiling(ActualWidth * transform.M11);
        int height = (int)Math.Ceiling(ActualHeight * transform.M22);
        int margin = (int)Math.Round(10 * transform.M11);
        var position = BarPlacement.Calculate(bounds, work, hasTaskbar ? taskbar : null,
            hasTaskbar ? data.Edge : 3, width, height, margin, UseBottomOffset);
        // Native screen coordinates avoid mixing WPF DIPs and pixels on scaled displays.
        if (GetWindowRect(_barHwnd, out var current) && current.Left == position.X && current.Top == position.Y) return;
        SetWindowPos(_barHwnd, IntPtr.Zero, position.X, position.Y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | 0x0004); // SWP_NOZORDER
    }
}
