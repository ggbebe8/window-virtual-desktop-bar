using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing; // 참조 추가 필요
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
    // 데이터 구조 개편: 데스크톱 그룹 -> [앱, 앱...]
    public class DesktopGroup : INotifyPropertyChanged
    {
        public int DesktopId { get; set; }
        public ObservableCollection<AppInfo> Apps { get; set; } = new ObservableCollection<AppInfo>();

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BackgroundBrush)); // 배경색도 바뀌었다고 알림
                }
            }
        }

        // 🔥 현재 데스크톱일 경우 연한 그레이 톤의 반투명 배경색 적용
        public System.Windows.Media.Brush BackgroundBrush => IsCurrent
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xCC, 0xCC, 0xCC))
            : System.Windows.Media.Brushes.Transparent;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class AppInfo : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        public ImageSource AppIcon { get; set; }

        private bool _isFocused;
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged(); // IsFocused가 바뀌었다고 UI에 알림
                    OnPropertyChanged(nameof(FocusBrush)); // FocusBrush 색상도 같이 바뀌었다고 알림
                }
            }
        }

        public System.Windows.Media.Brush FocusBrush => IsFocused ? System.Windows.Media.Brushes.SkyBlue : System.Windows.Media.Brushes.Transparent;

        // UI 갱신 이벤트를 위한 필수 코드
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public partial class MainWindow : Window
    {
        //Tray 정의
        private NotifyIcon _notifyIcon = new NotifyIcon();
        private bool _isExit;

        // --- [Win32 API Import] 시작 ---
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetWindowDesktopNumber(IntPtr window);
        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern void PinWindow(IntPtr hwnd);

        delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        // 🔥 아이콘 추출을 위한 주요 Win32 함수 및 메시지
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetDesktopCount(); // 전체 데스크톱 개수 가져오기

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow(); // 현재 최상단(포커스) 창 가져오기

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);


        // 🔥 실시간 포커스 변경 감지를 위한 Win32 API
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        const uint EVENT_SYSTEM_FOREGROUND = 3; // 포커스 변경 이벤트 번호
        const uint WINEVENT_OUTOFCONTEXT = 0;

        // 가비지 컬렉터(GC)가 훅을 날려버리지 않게 전역 변수로 유지해야 합니다.
        private WinEventDelegate _winEventDelegate;
        private IntPtr _hWinEventHook;

        const int WM_GETICON = 0x7F;
        const int ICON_SMALL = 0;
        const int GCLP_HICON = -14;
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] // 32비트용
        static extern uint GetClassLongPtr32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")] // 64비트용
        static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
        // --- [Win32 API Import] 끝 ---
        private const int WM_HOTKEY = 0x0312;

        // 🔥 작업표시줄을 이기기 위한 강력한 강제 최상단 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;     // 크기 변경 안 함
        private const uint SWP_NOMOVE = 0x0002;     // 위치 변경 안 함
        private const uint SWP_NOACTIVATE = 0x0010; // 🔥 [핵심] 포커스를 뺏지 않음 (사용자 타이핑 방해 금지)

        [DllImport("VirtualDesktopAccessor.dll")]
        public static extern int GetCurrentDesktopNumber(); // 현재 화면에 띄워진 데스크톱 번호 가져오기

        // UI에 바인딩할 그룹 리스트
        public ObservableCollection<DesktopGroup> Groups { get; set; } = new ObservableCollection<DesktopGroup>();

        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            DesktopGroups.ItemsSource = Groups; // XAML과 연결

            // 🔥 위치 잡기 로직은 생성자에서 바로 호출해 버립니다.
            SetWindowPosition();
        }

#region Tray
        ////////////////////////////////////////////////////////////////////
        private void InitNotifyIcon()
        {
            //아이콘 경로
            _notifyIcon.Icon = new System.Drawing.Icon("app.ico");
            _notifyIcon.Visible = false;

            //트레이 아이콘 더블클릭 이벤트 등록
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            //트레이 아이콘 우클릭 메뉴 등록
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("종료");
            exitMenuItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }


        //윈도우 X를 눌러 닫을 경우 닫기 취소 및 트레이 아이콘으로 이동
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit)
            {
                e.Cancel = true; // 닫기 취소
                HideMainWindow();
            }
            else
            {
                _notifyIcon.Dispose();
            }
            base.OnClosing(e);
        }

        //윈도우 숨기기
        private void HideMainWindow()
        {
            this.Hide();

            //윈도우 숨기면서 트레이 아이콘 표시
            _notifyIcon.Visible = true;

            //알림 메시지 표시
            _notifyIcon.ShowBalloonTip(1000, "앱이 트레이로 최소화되었습니다.", "아이콘을 더블클릭하면 다시 열립니다.", ToolTipIcon.Info);
        }

        //윈도우 표시
        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();

            //트레이 아이콘 제거
            _notifyIcon.Visible = false;
        }

        //우클릭 메뉴 -> 윈도우 종료
        private void ExitApplication()
        {
            _isExit = true;
            _notifyIcon.Visible = false;

            //Application이 WinForm과 겹쳐서 아래처럼 명시합니다.        
            System.Windows.Application.Current.Shutdown();

        }
        ////////////////////////////////////////////////////////////////////

#endregion Tray

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetWindowPosition(); // 위치 잡기
            PinWindow(new WindowInteropHelper(this).Handle); // 모든 데스크톱 고정

            // 단축키 등록 생략 (이전 답변 참고)

            RefreshData(); // 초기 데이터 갱신
        }

        // 윈도우 메시지를 가로채는 훅(Hook) 함수
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 들어온 메시지가 '글로벌 핫키'가 눌렸다는 신호라면
            if (msg == WM_HOTKEY)
            {
                // wParam에는 우리가 RegisterHotKey로 등록했던 ID(예: 9000)가 들어옵니다.
                if (wParam.ToInt32() == 9000)
                {
                    // 현재 창이 켜져 있으면 끄고, 꺼져 있으면 켭니다. (토글)
                    if (this.Visibility == Visibility.Visible)
                    {
                        this.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        RefreshData(); // 창을 다시 켤 때 최신 데스크톱 상태로 새로고침!
                        this.Visibility = Visibility.Visible;
                    }

                    // 우리가 이 키 입력을 처리했으므로 다른 프로그램으로 넘어가지 않게 막음
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // ✅ 모든 초기화 로직을 여기로 통합합니다.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr myHwnd = new WindowInteropHelper(this).Handle;
            PinWindow(myHwnd);

            HwndSource source = HwndSource.FromHwnd(myHwnd);
            source.AddHook(HwndHook);

            RegisterHotKey(myHwnd, 9000, 0x0001 | 0x0004, 0x56);

            // 🔥 [추가된 부분] 포커스 변경 실시간 감지 훅 등록
            _winEventDelegate = new WinEventDelegate(WinEventProc);
            _hWinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(200); // DWM이 창을 인식할 시간 부여
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    PinWindow(myHwnd);
                });
            });

            RefreshData();
        }

        // 🔥 윈도우 포커스가 바뀔 때마다 자동으로 실행되는 함수
        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                // 🔥 [핵심 1] 새 창이 데스크톱에 완전히 등록될 때까지 0.15초 대기
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(150);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        RefreshData();
                        this.Topmost = false;
                        this.Topmost = true;
                    });
                });
            }
        }

        // 🔥 위치 설정 (작업표시줄 바로 위 좌측)
        private void SetWindowPosition()
        {
            // 1. 윈도우 OS가 멋대로 창 위치를 자동 지정하는 것을 차단
            this.WindowStartupLocation = WindowStartupLocation.Manual;

            // 2. 좌측 여백은 고정이므로 미리 설정
            this.Left = SystemParameters.WorkArea.Left + 10;

            // 3. 앱 개수가 변해서 창 크기가 달라질 때마다 바닥으로 끌어내림
            this.SizeChanged += (s, e) =>
            {
                // 💡 Dispatcher.BeginInvoke: WPF가 데이터 바인딩과 UI 그리기를 모두 끝낼 때까지 
                // 찰나의 순간을 기다렸다가(Render 우선순위) 정확한 ActualHeight를 가져옴
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    double workingAreaBottom = SystemParameters.WorkArea.Bottom;

                    // 작업표시줄 맨 위에서 이 창의 실제 높이만큼 빼서 바닥에 딱 붙임
                    this.Top = workingAreaBottom;// - this.ActualHeight;

                }), System.Windows.Threading.DispatcherPriority.Render);
            };
        }

        private void RefreshData()
        {
            IntPtr myHwnd = new WindowInteropHelper(this).Handle;
            PinWindow(myHwnd);

            // 1. 전체 데스크톱 개수를 가져와서 빈 슬롯부터 미리 만들기
            int desktopCount = 0;
            try
            {
                desktopCount = GetDesktopCount();
            }
            catch
            {
                desktopCount = 3; // 만약 DLL 구버전이라 에러가 나면 임시로 3개로 고정
            }

            if (Groups.Count != desktopCount)
            {
                Groups.Clear();
                for (int i = 0; i < desktopCount; i++)
                {
                    Groups.Add(new DesktopGroup { DesktopId = i + 1 });
                }
            }

            // 2. 현재 작업 중인 창 핸들 가져오기
            IntPtr focusedHwnd = GetForegroundWindow();
            int currentDesktopIndex = -1;
            try { currentDesktopIndex = GetCurrentDesktopNumber(); } catch { }

            // 1. 현재 화면에 있는 '모든 유효한 창 핸들'만 싹 수집
            var currentWindows = new List<AppInfo_Internal>();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);

                    if (title.Length > 0 && title.ToString() != "VD Bar")
                    {
                        int deskNum = GetWindowDesktopNumber(hWnd);
                        if (deskNum >= 0 && deskNum < desktopCount)
                        {
                            currentWindows.Add(new AppInfo_Internal { DesktopId = deskNum + 1, Hwnd = hWnd });
                        }
                    }
                }
                return true;
            }, 0);

            // 🔥 2. 기존 UI 리스트와 새 리스트를 '동기화(Sync)' 합니다 (순서 유지의 비결)
            foreach (var group in Groups)
            {
                // 👉 [추가된 부분] 현재 화면에 띄워진 데스크톱 번호와 그룹 번호가 같으면 하이라이트 (API는 0부터 시작하므로 +1)
                group.IsCurrent = (group.DesktopId == currentDesktopIndex + 1);

                // 이 데스크톱(그룹)에 있어야 할 최신 창 목록
                var newAppsForThisDesk = currentWindows.Where(x => x.DesktopId == group.DesktopId).ToList();

                // [삭제] 기존 UI에는 있는데, 현재 윈도우 목록엔 없는 창 (꺼진 창)
                var toRemove = group.Apps.Where(a => !newAppsForThisDesk.Any(n => n.Hwnd == a.Hwnd)).ToList();
                foreach (var app in toRemove)
                {
                    group.Apps.Remove(app);
                }

                // [추가 및 갱신]
                foreach (var newApp in newAppsForThisDesk)
                {
                    // 이 창이 이미 기존 UI에 있는지 확인
                    var existingApp = group.Apps.FirstOrDefault(a => a.Hwnd == newApp.Hwnd);

                    if (existingApp == null)
                    {
                        // UI에 없다면 새로 켜진 창이므로 아이콘을 뽑아서 맨 뒤에 '추가'
                        ImageSource icon = ExtractIconFromHwnd(newApp.Hwnd);
                        if (icon != null)
                        {
                            group.Apps.Add(new AppInfo
                            {
                                Hwnd = newApp.Hwnd,
                                AppIcon = icon,
                                IsFocused = (newApp.Hwnd == focusedHwnd)
                            });
                        }
                    }
                    else
                    {
                        // 이미 있다면 순서는 가만히 냅두고 '포커스'만 갱신
                        existingApp.IsFocused = (existingApp.Hwnd == focusedHwnd);
                    }
                }
            }
        }

        // 임시 저장을 위한 내부 클래스
        class AppInfo_Internal { public int DesktopId; public IntPtr Hwnd; public ImageSource Icon; }

        // 🔥🔥🔥 [초핵심] Hwnd로부터 WPF ImageSource 아이콘 뽑아내기
        private ImageSource ExtractIconFromHwnd(IntPtr hWnd)
        {
            try
            {
                IntPtr hIcon = IntPtr.Zero;

                // 방법 1: SendMessage(WM_GETICON)로 창에 직접 요청 (가장 정확)
                hIcon = SendMessage(hWnd, WM_GETICON, ICON_SMALL, 0);

                // 방법 2: 방법1 실패 시 클래스 정보에서 가져오기 (GCLP_HICON)
                if (hIcon == IntPtr.Zero)
                {
                    if (IntPtr.Size == 8) // 64비트
                        hIcon = GetClassLongPtr64(hWnd, GCLP_HICON);
                    else // 32비트
                        hIcon = (IntPtr)GetClassLongPtr32(hWnd, GCLP_HICON);
                }

                if (hIcon != IntPtr.Zero)
                {
                    // HICON 핸들을 WPF의 BitmapSource로 변환
                    // System.Drawing 참조 필요
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze(); // 크로스 스레드 예방 및 성능 최적화
                    return bitmapSource;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"아이콘 추출 실패: {ex.Message}");
            }
            return null; // 실패 시 null
        }
        protected override void OnClosed(EventArgs e)
        {
            if (_hWinEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_hWinEventHook);
            }
            base.OnClosed(e);
        }
    }
}