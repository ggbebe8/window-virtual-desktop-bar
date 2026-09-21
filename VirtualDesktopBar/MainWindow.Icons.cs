using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualDesktopBar;

public partial class MainWindow
{
    private readonly SemaphoreSlim _iconReaderGate = new(1);
    private readonly CancellationTokenSource _iconCancellation = new();

    private async void UpdateAppIcon(AppInfo app)
    {
        if (_isExit || !IsVisible || !app.IconSchedule.TryBegin(Environment.TickCount64)) return;
        bool attempted = false;
        try
        {
            await _iconReaderGate.WaitAsync(_iconCancellation.Token);
            try
            {
                if (_isExit || !IsVisible || !Groups.Any(g => g.Apps.Contains(app))) return;
                attempted = true;
                var result = await Task.Run(() => WindowIconReader.Read(app.Hwnd), _iconCancellation.Token);
                if (_isExit || !Groups.Any(g => g.Apps.Contains(app))) return;
                if (result != null && result.Fingerprint != app.IconFingerprint)
                {
                    app.AppIcon = result.Image;
                    app.IconFingerprint = result.Fingerprint;
                }
            }
            finally { _iconReaderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Trace.TraceWarning("Window icon lookup failed: {0}", ex.Message); }
        finally
        {
            app.IconSchedule.Complete(Environment.TickCount64, attempted);
        }
    }
}
