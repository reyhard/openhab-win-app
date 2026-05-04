using System.Globalization;
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

    [Fact]
    public void ThrowsForNullSnapshot()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", "PcChargingState", "PcLockedState", "PcSessionState");

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(null!, mapping));

        Assert.Equal("snapshot", ex.ParamName);
    }

    [Fact]
    public void ThrowsForNullMapping()
    {
        var snapshot = new DeviceStateSnapshot(87, true, true, "locked");

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(snapshot, null!));

        Assert.Equal("mapping", ex.ParamName);
    }

    [Fact]
    public void OmitsUpdatesWhenSnapshotFieldsAreNull()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", "PcChargingState", "PcLockedState", "PcSessionState");
        var snapshot = new DeviceStateSnapshot(null, null, null, null);

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Empty(updates);
    }

    [Fact]
    public void FormatsBatteryPercentUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var mapping = new DeviceStateMapping("PcBatteryLevel", null, null, null);
            var snapshot = new DeviceStateSnapshot(87, null, null, null);

            var updates = DeviceStateMapper.Map(snapshot, mapping);

            Assert.Equal([new DeviceStateUpdate("PcBatteryLevel", "87")], updates);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
