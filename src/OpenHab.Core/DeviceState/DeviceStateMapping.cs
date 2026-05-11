namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateMapping(
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem,
    string? WifiConnectedItem,
    string? WifiNameItem,
    string? OpenHabConnectionItem,
    string? FocusStateItem);
