using OpenHab.App.DeviceInfo;
using OpenHab.App.Settings;
using OpenHab.App.Tests.Runtime;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.Tests.DeviceInfo;

public sealed class DeviceInfoSyncServiceTests
{
    [Fact]
    public async Task TriggerSyncAsyncDoesNothingWhenDisabled()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "OFF"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = false },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Empty(client.StatesSet);
        Assert.Equal("Disabled", service.CurrentStatus.LastResult);
    }

    [Fact]
    public async Task TriggerSyncAsyncSendsConfiguredMappedStates()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "ON"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Contains(("DeskBatteryLevel", "87"), client.StatesSet);
        Assert.Contains(("DeskChargingState", "ON"), client.StatesSet);
        Assert.Contains(("DeskLockedState", "OFF"), client.StatesSet);
        Assert.Contains(("DeskSessionState", "active"), client.StatesSet);
        Assert.Contains(("DeskWifiConnected", "ON"), client.StatesSet);
        Assert.Contains(("DeskWifiName", "HomeNet"), client.StatesSet);
        Assert.Contains(("DeskOpenHabConnection", "online"), client.StatesSet);
        Assert.Contains(("DeskFocusState", "ON"), client.StatesSet);
        Assert.Equal("8 Items updated", service.CurrentStatus.LastResult);
        Assert.NotNull(service.CurrentStatus.LastSuccessfulSync);
    }

    [Fact]
    public async Task TriggerSyncAsyncSkipsBlankItemMappings()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "OFF"));
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk") with
        {
            IsEnabled = true,
            WifiNameItem = null,
            FocusStateItem = null
        };
        var service = new DeviceInfoSyncService(() => settings, () => client, source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.DoesNotContain(client.StatesSet, update => update.ItemName == "DeskWifiName");
        Assert.DoesNotContain(client.StatesSet, update => update.ItemName == "DeskFocusState");
        Assert.Equal("6 Items updated", service.CurrentStatus.LastResult);
    }

    [Fact]
    public async Task TriggerSyncAsyncCapturesFailureWithoutThrowing()
    {
        var client = new FakeOpenHabClient
        {
            SetItemStateFailure = new InvalidOperationException("network down")
        };
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, null, null, null, null, null, null, null));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Equal("InvalidOperationException: network down", service.CurrentStatus.LastError);
        Assert.Null(service.CurrentStatus.LastSuccessfulSync);
        Assert.Equal("1 Items attempted, 0 succeeded, 1 failed", service.CurrentStatus.LastResult);
    }

    [Fact]
    public async Task TriggerSyncAsyncContinuesAfterPerItemFailureAndReportsPartialResult()
    {
        var client = new FakeOpenHabClient();
        client.SetItemStateFailuresByItem["DeskWifiName"] = new InvalidOperationException("wifi rejected");
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "ON"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Equal(7, client.StatesSet.Count);
        Assert.DoesNotContain(client.StatesSet, update => update.ItemName == "DeskWifiName");
        Assert.Equal("8 Items attempted, 7 succeeded, 1 failed", service.CurrentStatus.LastResult);
        Assert.Equal("InvalidOperationException: wifi rejected", service.CurrentStatus.LastError);
        Assert.NotNull(service.CurrentStatus.LastSuccessfulSync);
    }

    [Fact]
    public async Task TriggerSyncAsyncClearsStaleSuccessResultWhenFailureOccurs()
    {
        var successClient = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "ON"));
        FakeOpenHabClient currentClient = successClient;
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => currentClient,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);
        Assert.Equal("8 Items updated", service.CurrentStatus.LastResult);

        var failureClient = new FakeOpenHabClient
        {
            SetItemStateFailure = new InvalidOperationException("network down")
        };
        currentClient = failureClient;

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.NotEqual("8 Items updated", service.CurrentStatus.LastResult);
        Assert.Equal("8 Items attempted, 0 succeeded, 8 failed", service.CurrentStatus.LastResult);
        Assert.Equal("InvalidOperationException: network down", service.CurrentStatus.LastError);
    }

    [Fact]
    public async Task DisposePreventsFurtherUseWithoutThrowing()
    {
        var client = new FakeOpenHabClient();
        var source = new BlockingSnapshotSource();
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        service.Start();
        service.Dispose();

        service.RefreshInterval();
        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Empty(client.StatesSet);
    }

    [Fact]
    public async Task StartAndRefreshIntervalAfterDisposeDoNothingWithoutThrowing()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "ON"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        service.Dispose();

        service.Start();
        service.RefreshInterval();
        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Empty(client.StatesSet);
    }

    [Fact]
    public async Task DisposeCancelsInFlightSync()
    {
        var client = new FakeOpenHabClient();
        var source = new CancellableBlockingSnapshotSource();
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        var syncTask = service.TriggerSyncAsync(CancellationToken.None);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.Dispose();
        await source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await syncTask;

        Assert.Empty(client.StatesSet);
    }

    private sealed class FakeSnapshotSource(DeviceStateSnapshot snapshot) : IDeviceStateSnapshotSource
    {
        public Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(snapshot);
        }
    }

    private sealed class BlockingSnapshotSource : IDeviceStateSnapshotSource
    {
        public Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<DeviceStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }
    }

    private sealed class CancellableBlockingSnapshotSource : IDeviceStateSnapshotSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Unreachable");
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }
}
