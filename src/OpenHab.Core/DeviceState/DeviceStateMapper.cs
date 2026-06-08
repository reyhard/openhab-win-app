using System.Globalization;

namespace OpenHab.Core.DeviceState;

public static class DeviceStateMapper
{
    public static IReadOnlyList<DeviceStateUpdate> Map(DeviceStateSnapshot snapshot, DeviceStateMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapping);

        var updates = new List<DeviceStateUpdate>();

        AddIfPresent(updates, mapping.BatteryLevelItem, FormatBatteryLevel(snapshot.BatteryLevelPercent));
        AddBoolState(updates, mapping.ChargingStateItem, snapshot.IsCharging);
        AddBoolState(updates, mapping.LockedStateItem, snapshot.IsLocked);
        AddIfPresent(updates, mapping.SessionStateItem, snapshot.SessionState);
        AddBoolState(updates, mapping.WifiConnectedItem, snapshot.IsWifiConnected);
        AddConnectionName(updates, mapping.WifiNameItem, snapshot.IsWifiConnected, snapshot.WifiName);
        AddBoolState(updates, mapping.BluetoothConnectedItem, snapshot.IsBluetoothConnected);
        AddConnectionName(updates, mapping.BluetoothDeviceNamesItem, snapshot.IsBluetoothConnected, snapshot.BluetoothDeviceNames);
        AddIfPresent(updates, mapping.OpenHabConnectionItem, snapshot.OpenHabConnectionState);
        AddIfPresent(updates, mapping.FocusStateItem, snapshot.FocusState);

        return updates;
    }

    private static string? FormatBatteryLevel(int? batteryLevelPercent)
    {
        return batteryLevelPercent?.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddBoolState(List<DeviceStateUpdate> updates, string? itemName, bool? value)
    {
        AddIfPresent(updates, itemName, value is null ? null : ToSwitchState(value.Value));
    }

    private static void AddConnectionName(List<DeviceStateUpdate> updates, string? itemName, bool? isConnected, string? name)
    {
        if (isConnected is false)
        {
            AddIfPresent(updates, itemName, "UNDEF");
            return;
        }

        AddIfPresent(updates, itemName, name);
    }

    private static void AddIfPresent(List<DeviceStateUpdate> updates, string? itemName, string? state)
    {
        if (itemName is not null && state is not null)
        {
            updates.Add(new DeviceStateUpdate(itemName, state));
        }
    }

    private static string ToSwitchState(bool value)
    {
        return value ? "ON" : "OFF";
    }
}
