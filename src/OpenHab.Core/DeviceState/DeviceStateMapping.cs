namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateMapping(
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem,
    string? WifiConnectedItem,
    string? WifiNameItem,
    string? BluetoothConnectedItem,
    string? BluetoothDeviceNamesItem,
    string? OpenHabConnectionItem,
    string? FocusStateItem);
