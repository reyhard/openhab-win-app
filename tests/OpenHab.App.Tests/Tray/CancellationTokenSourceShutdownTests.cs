using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class CancellationTokenSourceShutdownTests
{
    [Fact]
    public async Task CancelAndDisposeAsync_WhenCallbackThrowsReportsFailureAndCompletes()
    {
        var source = new CancellationTokenSource();
        using var registration = source.Token.Register(
            () => throw new InvalidOperationException("Cancellation callback failed."));
        Exception? reportedFailure = null;

        await CancellationTokenSourceShutdown.CancelAndDisposeAsync(
            source,
            failure => reportedFailure = failure);

        Assert.IsType<AggregateException>(reportedFailure);
        Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
    }
}
