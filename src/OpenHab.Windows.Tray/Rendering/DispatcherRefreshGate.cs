namespace OpenHab.Windows.Tray.Rendering;

public sealed class DispatcherRefreshGate(Func<Action, bool> tryEnqueue)
{
    private int pendingRefresh;

    public bool HasPendingRefresh => Interlocked.CompareExchange(ref pendingRefresh, 0, 0) == 1;

    public void Request(Action refresh)
    {
        if (!tryEnqueue(refresh))
        {
            Interlocked.Exchange(ref pendingRefresh, 1);
        }
    }

    public void Drain(Action refresh)
    {
        if (Interlocked.Exchange(ref pendingRefresh, 0) == 0)
        {
            return;
        }

        Request(refresh);
    }
}
