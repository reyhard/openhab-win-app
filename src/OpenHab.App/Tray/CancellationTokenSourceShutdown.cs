namespace OpenHab.App.Tray;

public static class CancellationTokenSourceShutdown
{
    public static async Task CancelAndDisposeAsync(
        CancellationTokenSource source,
        Action<Exception> onCancellationFailure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onCancellationFailure);

        try
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onCancellationFailure(ex);
        }
        finally
        {
            source.Dispose();
        }
    }
}
