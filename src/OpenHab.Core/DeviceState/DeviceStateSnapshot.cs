namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateSnapshot(
    int? BatteryLevelPercent,
    bool? IsCharging,
    bool? IsLocked,
    string? SessionState,
    bool? IsWifiConnected,
    string? WifiName,
    string? OpenHabConnectionState,
    string? FocusState);
