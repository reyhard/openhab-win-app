using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.DeviceInfo;

public sealed class DeviceInfoSyncService : IDisposable
{
    private readonly Func<DeviceInfoSyncSettings> getSettings;
    private readonly Func<IOpenHabClient?> getClient;
    private readonly IDeviceStateSnapshotSource snapshotSource;
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly CancellationTokenSource serviceCancellation = new();
    private Timer? timer;
    private int isDisposed;

    public DeviceInfoSyncService(
        Func<DeviceInfoSyncSettings> getSettings,
        Func<IOpenHabClient?> getClient,
        IDeviceStateSnapshotSource snapshotSource)
    {
        this.getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
        this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
    }

    public DeviceInfoSyncStatus CurrentStatus { get; private set; } = DeviceInfoSyncStatus.Initial;

    public void Start()
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        var settings = getSettings();
        timer?.Dispose();
        timer = new Timer(
            _ => _ = TriggerFromTimerAsync(),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(settings.SyncIntervalMinutes));
    }

    public void RefreshInterval()
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        if (timer is null)
        {
            return;
        }

        var settings = getSettings();
        timer.Change(TimeSpan.FromMinutes(settings.SyncIntervalMinutes), TimeSpan.FromMinutes(settings.SyncIntervalMinutes));
    }

    public async Task TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serviceCancellation.Token);
        var effectiveCancellation = linkedCancellation.Token;
        var enteredGate = false;

        try
        {
            await syncGate.WaitAsync(effectiveCancellation);
            enteredGate = true;

            if (Volatile.Read(ref isDisposed) != 0)
            {
                return;
            }

            var attempted = DateTimeOffset.UtcNow;
            var settings = getSettings().Normalized();
            if (!settings.IsEnabled)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = "Disabled",
                    LastError = null
                };
                return;
            }

            var client = getClient();
            if (client is null)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = null,
                    LastError = "No openHAB client available"
                };
                return;
            }

            var snapshot = await snapshotSource.CaptureAsync(effectiveCancellation);
            var updates = DeviceStateMapper.Map(snapshot, settings.ToMapping());
            if (updates.Count == 0)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = "No configured Items",
                    LastError = null
                };
                return;
            }

            var successfulUpdates = 0;
            var failedUpdates = 0;
            string? firstError = null;

            foreach (var update in updates)
            {
                try
                {
                    await client.SetItemStateAsync(update.ItemName, update.State, effectiveCancellation);
                    successfulUpdates++;
                }
                catch (OperationCanceledException) when (effectiveCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedUpdates++;
                    firstError ??= $"{ex.GetType().Name}: {ex.Message}";
                    DiagnosticLogger.Warn($"Device Info Sync item update failed ({update.ItemName}): {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (failedUpdates == 0)
            {
                CurrentStatus = new DeviceInfoSyncStatus(
                    LastAttemptedSync: attempted,
                    LastSuccessfulSync: DateTimeOffset.UtcNow,
                    LastResult: updates.Count == 1 ? "1 Item updated" : $"{updates.Count} Items updated",
                    LastError: null);
                return;
            }

            CurrentStatus = new DeviceInfoSyncStatus(
                LastAttemptedSync: attempted,
                LastSuccessfulSync: successfulUpdates > 0 ? DateTimeOffset.UtcNow : CurrentStatus.LastSuccessfulSync,
                LastResult: $"{updates.Count} Items attempted, {successfulUpdates} succeeded, {failedUpdates} failed",
                LastError: firstError);
        }
        catch (OperationCanceledException) when (serviceCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }
        catch (Exception ex)
        {
            CurrentStatus = CurrentStatus with
            {
                LastAttemptedSync = DateTimeOffset.UtcNow,
                LastResult = null,
                LastError = $"{ex.GetType().Name}: {ex.Message}"
            };
            DiagnosticLogger.Warn($"Device Info Sync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (enteredGate)
            {
                syncGate.Release();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        serviceCancellation.Cancel();
        timer?.Dispose();
        timer = null;
    }

    private async Task TriggerFromTimerAsync()
    {
        try
        {
            await TriggerSyncAsync(serviceCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
