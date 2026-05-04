using OpenHab.Core.DeviceState;

namespace OpenHab.Core.Tests;

public sealed class DeviceStateMapperTests
{
    [Fact]
    public void MapsBatteryChargingLockAndSessionState()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", "PcChargingState", "PcLockedState", "PcSessionState");
        var snapshot = new DeviceStateSnapshot(87, true, true, "locked");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcBatteryLevel", "87"),
            new DeviceStateUpdate("PcChargingState", "ON"),
            new DeviceStateUpdate("PcLockedState", "ON"),
            new DeviceStateUpdate("PcSessionState", "locked")
        ], updates);
    }

    [Fact]
    public void OmitsUnmappedTelemetryItems()
    {
        var mapping = new DeviceStateMapping(BatteryLevelItem: null, ChargingStateItem: null, LockedStateItem: "PcLockedState", SessionStateItem: null);
        var snapshot = new DeviceStateSnapshot(50, false, false, "active");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcLockedState", "OFF")], updates);
    }
}
