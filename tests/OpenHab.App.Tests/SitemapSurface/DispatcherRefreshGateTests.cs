using OpenHab.App.Runtime;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class DispatcherRefreshGateTests
{
    [Fact]
    public void RequestRecordsPendingRefreshWhenEnqueueFails()
    {
        var gate = new DispatcherRefreshGate(_ => false);
        var refreshes = 0;

        gate.Request(() => refreshes++);

        Assert.Equal(0, refreshes);
        Assert.True(gate.HasPendingRefresh);
    }

    [Fact]
    public void DrainRunsOnePendingRefresh()
    {
        var queue = new Queue<Action>();
        var shouldEnqueue = false;
        var gate = new DispatcherRefreshGate(action =>
        {
            if (!shouldEnqueue)
            {
                return false;
            }

            queue.Enqueue(action);
            return true;
        });
        var refreshes = 0;

        gate.Request(() => refreshes++);
        shouldEnqueue = true;
        gate.Drain(() => refreshes++);
        while (queue.TryDequeue(out var action))
        {
            action();
        }

        Assert.Equal(1, refreshes);
        Assert.False(gate.HasPendingRefresh);
    }

    [Fact]
    public void SuccessfulRequestDoesNotClearExistingPendingRefresh()
    {
        var queue = new Queue<Action>();
        var shouldEnqueue = false;
        var gate = new DispatcherRefreshGate(action =>
        {
            if (!shouldEnqueue)
            {
                return false;
            }

            queue.Enqueue(action);
            return true;
        });

        gate.Request(() => { });
        shouldEnqueue = true;
        gate.Request(() => { });
        while (queue.TryDequeue(out var action))
        {
            action();
        }

        Assert.True(gate.HasPendingRefresh);
    }
}
