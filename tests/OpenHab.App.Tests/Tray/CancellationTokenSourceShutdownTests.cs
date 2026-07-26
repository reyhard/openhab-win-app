using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class CancellationTokenSourceShutdownTests
{
    [Fact]
    public async Task CancelAsync_WhenCallbackThrowsReportsFailureAndLeavesDisposalToOwner()
    {
        using var source = new CancellationTokenSource();
        using var registration = source.Token.Register(
            () => throw new InvalidOperationException("Cancellation callback failed."));
        Exception? reportedFailure = null;

        await CancellationTokenSourceShutdown.CancelAsync(
            source,
            failure => reportedFailure = failure);

        Assert.IsType<AggregateException>(reportedFailure);
        Assert.True(source.IsCancellationRequested);
        _ = source.Token;
    }
}
