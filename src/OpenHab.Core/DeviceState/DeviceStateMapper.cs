using System.Globalization;

namespace OpenHab.Core.DeviceState;

public static class DeviceStateMapper
{
    public static IReadOnlyList<DeviceStateUpdate> Map(DeviceStateSnapshot snapshot, DeviceStateMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapping);

        var updates = new List<DeviceStateUpdate>();

        if (mapping.BatteryLevelItem is not null && snapshot.BatteryLevelPercent is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.BatteryLevelItem, snapshot.BatteryLevelPercent.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (mapping.ChargingStateItem is not null && snapshot.IsCharging is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.ChargingStateItem, snapshot.IsCharging.Value ? "ON" : "OFF"));
        }

        if (mapping.LockedStateItem is not null && snapshot.IsLocked is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.LockedStateItem, snapshot.IsLocked.Value ? "ON" : "OFF"));
        }

        if (mapping.SessionStateItem is not null && snapshot.SessionState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.SessionStateItem, snapshot.SessionState));
        }

        return updates;
    }
}
