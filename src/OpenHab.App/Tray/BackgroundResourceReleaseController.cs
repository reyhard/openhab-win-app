namespace OpenHab.App.Tray;

public sealed partial class BackgroundResourceReleaseController : IDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Action releaseResources;
    private readonly object syncRoot = new();

    private CancellationTokenSource? pendingReleaseCts;
    private bool disposed;

    public BackgroundResourceReleaseController(
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Action? releaseResources = null)
    {
        this.delayAsync = delayAsync ?? Task.Delay;
        this.releaseResources = releaseResources ?? (() => { });
    }

    public bool IsReleasePending
    {
        get
        {
            lock (syncRoot)
            {
                return pendingReleaseCts is not null;
            }
        }
    }

    public void ApplyShellState(TrayShellState state, TimeSpan releaseDelay)
    {
        if (state.VisibleSurface is not TrayShellSurface.None || state.ShouldExitProcess)
        {
            CancelPendingRelease();
            return;
        }

        ScheduleRelease(releaseDelay);
    }

    public void CancelPendingRelease()
    {
        CancellationTokenSource? ctsToCancel = null;

        lock (syncRoot)
        {
            if (pendingReleaseCts is null)
            {
                return;
            }

            ctsToCancel = pendingReleaseCts;
            pendingReleaseCts = null;
        }

        ctsToCancel.Cancel();
        ctsToCancel.Dispose();
    }

    public void Dispose()
    {
        disposed = true;
        CancelPendingRelease();
    }

    private void ScheduleRelease(TimeSpan releaseDelay)
    {
        if (releaseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseDelay), "Release delay must not be negative.");
        }

        CancellationTokenSource cts;
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pendingReleaseCts is not null)
            {
                return;
            }

            cts = new CancellationTokenSource();
            pendingReleaseCts = cts;
            _ = RunReleaseAsync(releaseDelay, cts);
        }
    }

    private async Task RunReleaseAsync(TimeSpan releaseDelay, CancellationTokenSource cts)
    {
        try
        {
            await delayAsync(releaseDelay, cts.Token).ConfigureAwait(false);
            cts.Token.ThrowIfCancellationRequested();
            releaseResources();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancellation is the normal path when a pending release is superseded.
        }
        finally
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(pendingReleaseCts, cts))
                {
                    pendingReleaseCts = null;
                }
            }

            cts.Dispose();
        }
    }
}
