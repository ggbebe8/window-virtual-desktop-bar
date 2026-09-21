namespace VirtualDesktopBar;

// All access is on the UI thread, including completion of asynchronous icon reads.
internal sealed class IconRefreshSchedule
{
    private int _completedReads;
    private long _version;
    private long _readVersion;
    internal bool Pending { get; private set; }
    internal long NextReadAt { get; private set; }

    internal bool TryBegin(long now)
    {
        if (Pending || now < NextReadAt) return false;
        Pending = true;
        _readVersion = _version;
        return true;
    }

    internal void Invalidate(long now)
    {
        _version++;
        // Coalesce repeated redraw notifications without starving a pending refresh.
        NextReadAt = System.Math.Min(NextReadAt, now + 2000);
    }

    internal void Complete(long now, bool attempted)
    {
        Pending = false;
        if (attempted) _completedReads++;
        // Initial lookup plus three retries catches delayed browser app icons.
        // A notification arriving during a read must survive that read's completion.
        NextReadAt = !attempted || _completedReads < 4 || _version != _readVersion
            ? now + 2000 : long.MaxValue;
    }
}
