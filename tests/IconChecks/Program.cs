using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using VirtualDesktopBar;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
    }

    [STAThread]
    private static void Main()
    {
        CheckRefreshSchedule();
        Check(WindowIconReader.ParseResource(null) == null, "Missing resource");
        Check(WindowIconReader.ParseResource("\"C:\\icons, apps\\site.ico\",0") == ("C:\\icons, apps\\site.ico", 0), "Quoted path with comma");
        Check(WindowIconReader.ParseResource("%SystemRoot%\\System32\\shell32.dll,-42") ==
            (Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\shell32.dll"), -42), "Environment and negative resource ID");
        Check(WindowIconReader.Read(IntPtr.Zero) == null, "Destroyed or missing window");
        Check(WindowIconReader.ReadResource("Z:\\missing-vdbar-test.ico,0") == null, "Missing icon falls back");

        using var source = new HwndSource(new HwndSourceParameters("VD Bar icon regression checks")
            { Width = 1, Height = 1, WindowStyle = 0 }); // Hidden test window, never activated.
        bool ready = false;
        var requested = new List<int>();
        source.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg != 0x7F) return IntPtr.Zero;
            handled = true;
            requested.Add(w.ToInt32());
            return ready && w.ToInt32() == 2 ? System.Drawing.SystemIcons.Warning.Handle : IntPtr.Zero;
        });
        var initial = WindowIconReader.Read(source.Handle);
        Check(requested.Contains(2), "SMALL2 is queried");
        ready = true;
        var later = WindowIconReader.Read(source.Handle);
        Check(later != null && later.Fingerprint == IconFingerprint(System.Drawing.SystemIcons.Warning.Handle), "Late SMALL2 icon is resolved");
        Check(initial == null || initial.Fingerprint != later!.Fingerprint, "A later read replaces initial fallback");
        Check(later!.Image.IsFrozen, "Image can cross worker/UI threads");
        Check(WindowIconReader.Read(source.Handle)?.Fingerprint == later.Fingerprint, "Unchanged icon has stable fingerprint");

        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "web, app.ico");
        using (var stream = System.IO.File.Create(iconPath)) System.Drawing.SystemIcons.Information.Save(stream);
        var iid = typeof(WindowIconReader.IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(WindowIconReader.SHGetPropertyStoreForWindow(source.Handle, ref iid, out var store));
        var key = new WindowIconReader.PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 3 };
        var value = new WindowIconReader.PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni($"\"{iconPath}\",0") };
        try
        {
            Marshal.ThrowExceptionForHR(store!.SetValue(ref key, ref value));
            var shellIcon = WindowIconReader.Read(source.Handle);
            var expected = WindowIconReader.ReadResource($"\"{iconPath}\",0");
            Check(shellIcon != null && expected != null && shellIcon.Fingerprint == WindowIconReader.Fingerprint(expected),
                "Web-app shell resource takes precedence over browser window icon");
            Check(shellIcon!.Fingerprint != later.Fingerprint, "Shell-specific and generic window icons differ");
            var hwnd = source.Handle;
            var read = System.Threading.Tasks.Task.Run(() => WindowIconReader.Read(hwnd));
            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = read.ContinueWith(_ => source.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Check(read.GetAwaiter().GetResult()?.Fingerprint == shellIcon.Fingerprint,
                "Shell icon lookup also works on a background MTA thread");
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
            if (store != null) Marshal.ReleaseComObject(store);
            System.IO.File.Delete(iconPath);
        }
        Console.WriteLine($"Passed {_checks} icon checks.");
    }

    private static string IconFingerprint(IntPtr handle) => WindowIconReader.Fingerprint(
        Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()));

    private static void CheckRefreshSchedule()
    {
        var schedule = new IconRefreshSchedule();
        for (int i = 0; i < 4; i++)
        {
            long now = i * 2000;
            Check(schedule.TryBegin(now), "Initial icon lookup and delayed retries remain enabled");
            Check(!schedule.TryBegin(now), "Overlapping reads are prevented");
            schedule.Complete(now, attempted: true);
            Check(!schedule.TryBegin(now + 1000), "Retries do not spin between timer ticks");
        }
        Check(!schedule.TryBegin(3_600_000), "Stable icons are not polled even after an hour");
        schedule.Invalidate(3_600_000);
        schedule.Invalidate(3_601_000);
        Check(!schedule.TryBegin(3_601_999), "Redraw burst is coalesced");
        Check(schedule.TryBegin(3_602_000), "Repeated events do not postpone refresh indefinitely");
        schedule.Invalidate(3_602_001);
        schedule.Complete(3_602_100, attempted: true);
        Check(schedule.TryBegin(3_604_100), "Notification during lookup survives completion");
        schedule.Complete(3_604_100, attempted: true);
        Check(!schedule.TryBegin(7_200_000), "Event-triggered update returns to idle");

        var hidden = new IconRefreshSchedule();
        for (int i = 0; i < 5; i++)
        {
            hidden.TryBegin(i * 2000);
            hidden.Complete(i * 2000, attempted: false);
        }
        Check(hidden.TryBegin(10000), "Skipped reads while hidden do not exhaust initial attempts");
    }
}
