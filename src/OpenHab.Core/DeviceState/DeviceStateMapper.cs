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

        if (mapping.WifiConnectedItem is not null && snapshot.IsWifiConnected is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.WifiConnectedItem, snapshot.IsWifiConnected.Value ? "ON" : "OFF"));
        }

        if (mapping.WifiNameItem is not null)
        {
            if (snapshot.IsWifiConnected is false)
            {
                updates.Add(new DeviceStateUpdate(mapping.WifiNameItem, "UNDEF"));
            }
            else if (snapshot.WifiName is not null)
            {
                updates.Add(new DeviceStateUpdate(mapping.WifiNameItem, snapshot.WifiName));
            }
        }

        if (mapping.BluetoothConnectedItem is not null && snapshot.IsBluetoothConnected is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.BluetoothConnectedItem, snapshot.IsBluetoothConnected.Value ? "ON" : "OFF"));
        }

        if (mapping.BluetoothDeviceNamesItem is not null)
        {
            if (snapshot.IsBluetoothConnected is false)
            {
                updates.Add(new DeviceStateUpdate(mapping.BluetoothDeviceNamesItem, "UNDEF"));
            }
            else if (snapshot.BluetoothDeviceNames is not null)
            {
                updates.Add(new DeviceStateUpdate(mapping.BluetoothDeviceNamesItem, snapshot.BluetoothDeviceNames));
            }
        }

        if (mapping.OpenHabConnectionItem is not null && snapshot.OpenHabConnectionState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.OpenHabConnectionItem, snapshot.OpenHabConnectionState));
        }

        if (mapping.FocusStateItem is not null && snapshot.FocusState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.FocusStateItem, snapshot.FocusState));
        }

        return updates;
    }
}
