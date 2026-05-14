using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class BackgroundResourceReleaseControllerTests
{
    [Fact]
    public async Task ApplyShellState_WhenHiddenSchedulesReleaseAfterDelay()
    {
        var delay = new ControllableDelay();
        var releaseCount = 0;
        var controller = new BackgroundResourceReleaseController(
            delay.DelayAsync,
            () => releaseCount++);

        controller.ApplyShellState(
            TrayShellState.Initial with { VisibleSurface = TrayShellSurface.None },
            TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), delay.RequestedDelay);
        Assert.True(controller.IsReleasePending);

        delay.Complete();
        await delay.WaitForReleaseAsync(() => releaseCount);

        Assert.Equal(1, releaseCount);
        Assert.False(controller.IsReleasePending);
    }

    [Fact]
    public async Task ApplyShellState_WhenSurfaceBecomesVisibleCancelsPendingRelease()
    {
        var delay = new ControllableDelay();
        var releaseCount = 0;
        var controller = new BackgroundResourceReleaseController(
            delay.DelayAsync,
            () => releaseCount++);

        controller.ApplyShellState(
            TrayShellState.Initial with { VisibleSurface = TrayShellSurface.None },
            TimeSpan.FromMinutes(5));
        controller.ApplyShellState(
            TrayShellState.Initial with { VisibleSurface = TrayShellSurface.MainWindow },
            TimeSpan.FromMinutes(5));

        Assert.True(delay.IsCanceled);
        Assert.False(controller.IsReleasePending);

        delay.Complete();
        await Task.Yield();

        Assert.Equal(0, releaseCount);
    }

    [Fact]
    public void ApplyShellState_WhenAlreadyPendingDoesNotRestartDelay()
    {
        var delay = new ControllableDelay();
        var controller = new BackgroundResourceReleaseController(delay.DelayAsync, () => { });
        var hidden = TrayShellState.Initial with { VisibleSurface = TrayShellSurface.None };

        controller.ApplyShellState(hidden, TimeSpan.FromMinutes(5));
        controller.ApplyShellState(hidden, TimeSpan.FromMinutes(10));

        Assert.Equal(1, delay.RequestCount);
        Assert.Equal(TimeSpan.FromMinutes(5), delay.RequestedDelay);
    }

    private sealed class ControllableDelay
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken cancellationToken;

        public TimeSpan RequestedDelay { get; private set; }

        public int RequestCount { get; private set; }

        public bool IsCanceled => cancellationToken.IsCancellationRequested;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestedDelay = delay;
            this.cancellationToken = cancellationToken;
            return completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete()
        {
            completion.TrySetResult();
        }

        public async Task WaitForReleaseAsync(Func<int> getReleaseCount)
        {
            for (var i = 0; i < 20 && getReleaseCount() == 0; i++)
            {
                await Task.Delay(10);
            }
        }
    }
}
