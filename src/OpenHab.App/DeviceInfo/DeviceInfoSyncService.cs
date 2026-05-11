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
        var settings = getSettings();
        timer?.Dispose();
        timer = new Timer(
            _ => _ = TriggerSyncAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(settings.SyncIntervalMinutes));
    }

    public void RefreshInterval()
    {
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

        await syncGate.WaitAsync(cancellationToken);
        try
        {
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

            var snapshot = await snapshotSource.CaptureAsync(cancellationToken);
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

            foreach (var update in updates)
            {
                await client.SetItemStateAsync(update.ItemName, update.State, cancellationToken);
            }

            CurrentStatus = new DeviceInfoSyncStatus(
                LastAttemptedSync: attempted,
                LastSuccessfulSync: DateTimeOffset.UtcNow,
                LastResult: updates.Count == 1 ? "1 Item updated" : $"{updates.Count} Items updated",
                LastError: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CurrentStatus = CurrentStatus with
            {
                LastAttemptedSync = DateTimeOffset.UtcNow,
                LastError = $"{ex.GetType().Name}: {ex.Message}"
            };
            DiagnosticLogger.Warn($"Device Info Sync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            syncGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        timer?.Dispose();
        syncGate.Dispose();
    }
}
