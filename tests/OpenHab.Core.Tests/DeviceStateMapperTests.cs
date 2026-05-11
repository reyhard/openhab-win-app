using System.Globalization;
using OpenHab.Core.DeviceState;

namespace OpenHab.Core.Tests;

public sealed class DeviceStateMapperTests
{
    [Fact]
    public void MapsAllConfiguredDeviceInfoSignals()
    {
        var mapping = new DeviceStateMapping(
            "PcBatteryLevel",
            "PcChargingState",
            "PcLockedState",
            "PcSessionState",
            "PcWifiConnected",
            "PcWifiName",
            "PcOpenHabConnection",
            "PcFocusState");
        var snapshot = new DeviceStateSnapshot(
            BatteryLevelPercent: 87,
            IsCharging: true,
            IsLocked: true,
            SessionState: "locked",
            IsWifiConnected: true,
            WifiName: "HomeNet",
            OpenHabConnectionState: "online",
            FocusState: "ON");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcBatteryLevel", "87"),
            new DeviceStateUpdate("PcChargingState", "ON"),
            new DeviceStateUpdate("PcLockedState", "ON"),
            new DeviceStateUpdate("PcSessionState", "locked"),
            new DeviceStateUpdate("PcWifiConnected", "ON"),
            new DeviceStateUpdate("PcWifiName", "HomeNet"),
            new DeviceStateUpdate("PcOpenHabConnection", "online"),
            new DeviceStateUpdate("PcFocusState", "ON")
        ], updates);
    }

    [Fact]
    public void OmitsUnmappedDeviceInfoItems()
    {
        var mapping = new DeviceStateMapping(
            BatteryLevelItem: null,
            ChargingStateItem: null,
            LockedStateItem: "PcLockedState",
            SessionStateItem: null,
            WifiConnectedItem: null,
            WifiNameItem: null,
            OpenHabConnectionItem: null,
            FocusStateItem: null);
        var snapshot = new DeviceStateSnapshot(50, false, false, "active", false, null, "offline", "OFF");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcLockedState", "OFF")], updates);
    }

    [Fact]
    public void OmitsUpdatesWhenSnapshotFieldsAreNull()
    {
        var mapping = new DeviceStateMapping(
            "PcBatteryLevel",
            "PcChargingState",
            "PcLockedState",
            "PcSessionState",
            "PcWifiConnected",
            "PcWifiName",
            "PcOpenHabConnection",
            "PcFocusState");
        var snapshot = new DeviceStateSnapshot(null, null, null, null, null, null, null, null);

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Empty(updates);
    }

    [Fact]
    public void MapsWifiDisconnectedNameAsUndefWhenWifiNameItemIsConfigured()
    {
        var mapping = new DeviceStateMapping(
            null,
            null,
            null,
            null,
            WifiConnectedItem: "PcWifiConnected",
            WifiNameItem: "PcWifiName",
            OpenHabConnectionItem: null,
            FocusStateItem: null);
        var snapshot = new DeviceStateSnapshot(
            null,
            null,
            null,
            null,
            IsWifiConnected: false,
            WifiName: null,
            OpenHabConnectionState: null,
            FocusState: null);

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcWifiConnected", "OFF"),
            new DeviceStateUpdate("PcWifiName", "UNDEF")
        ], updates);
    }

    [Fact]
    public void MapsFocusUnsupportedAsStringState()
    {
        var mapping = new DeviceStateMapping(null, null, null, null, null, null, null, "PcFocusState");
        var snapshot = new DeviceStateSnapshot(null, null, null, null, null, null, null, "UNSUPPORTED");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcFocusState", "UNSUPPORTED")], updates);
    }

    [Fact]
    public void ThrowsForNullSnapshot()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", null, null, null, null, null, null, null);

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(null!, mapping));

        Assert.Equal("snapshot", ex.ParamName);
    }

    [Fact]
    public void ThrowsForNullMapping()
    {
        var snapshot = new DeviceStateSnapshot(87, true, true, "locked", true, "HomeNet", "online", "ON");

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(snapshot, null!));

        Assert.Equal("mapping", ex.ParamName);
    }

    [Fact]
    public void FormatsBatteryPercentUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var mapping = new DeviceStateMapping("PcBatteryLevel", null, null, null, null, null, null, null);
            var snapshot = new DeviceStateSnapshot(87, null, null, null, null, null, null, null);

            var updates = DeviceStateMapper.Map(snapshot, mapping);

            Assert.Equal([new DeviceStateUpdate("PcBatteryLevel", "87")], updates);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
