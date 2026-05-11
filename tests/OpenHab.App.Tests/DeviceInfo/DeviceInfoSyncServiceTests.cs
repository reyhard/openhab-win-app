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
    }

    private sealed class FakeSnapshotSource(DeviceStateSnapshot snapshot) : IDeviceStateSnapshotSource
    {
        public Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(snapshot);
        }
    }
}
