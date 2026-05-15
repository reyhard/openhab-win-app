using System.Text.RegularExpressions;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.Settings;

public sealed record DeviceInfoSyncSettings(
    bool IsEnabled,
    string DeviceIdentifier,
    int SyncIntervalMinutes,
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem,
    string? WifiConnectedItem,
    string? WifiNameItem,
    string? BluetoothConnectedItem,
    string? BluetoothDeviceNamesItem,
    string? OpenHabConnectionItem,
    string? FocusStateItem)
{
    public const int MinSyncIntervalMinutes = 1;
    public const int MaxSyncIntervalMinutes = 240;
    public const int DefaultSyncIntervalMinutes = 15;

    private static readonly Regex InvalidIdentifierCharacters = new(
        "[^A-Za-z0-9_]",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public bool HasAnyMapping =>
        BatteryLevelItem is not null ||
        ChargingStateItem is not null ||
        LockedStateItem is not null ||
        SessionStateItem is not null ||
        WifiConnectedItem is not null ||
        WifiNameItem is not null ||
        BluetoothConnectedItem is not null ||
        BluetoothDeviceNamesItem is not null ||
        OpenHabConnectionItem is not null ||
        FocusStateItem is not null;

    public static DeviceInfoSyncSettings Default { get; } = CreateDefault(Environment.MachineName);

    public static DeviceInfoSyncSettings CreateDefault(string rawIdentifier)
    {
        var identifier = SanitizeDeviceIdentifier(rawIdentifier);
        return new DeviceInfoSyncSettings(
            IsEnabled: false,
            DeviceIdentifier: identifier,
            SyncIntervalMinutes: DefaultSyncIntervalMinutes,
            BatteryLevelItem: identifier + "BatteryLevel",
            ChargingStateItem: identifier + "ChargingState",
            LockedStateItem: identifier + "LockedState",
            SessionStateItem: identifier + "SessionState",
            WifiConnectedItem: identifier + "WifiConnected",
            WifiNameItem: identifier + "WifiName",
            BluetoothConnectedItem: identifier + "BluetoothConnected",
            BluetoothDeviceNamesItem: identifier + "BluetoothDeviceNames",
            OpenHabConnectionItem: identifier + "OpenHabConnection",
            FocusStateItem: identifier + "FocusState");
    }

    public DeviceStateMapping ToMapping()
    {
        return new DeviceStateMapping(
            NormalizeItemName(BatteryLevelItem),
            NormalizeItemName(ChargingStateItem),
            NormalizeItemName(LockedStateItem),
            NormalizeItemName(SessionStateItem),
            NormalizeItemName(WifiConnectedItem),
            NormalizeItemName(WifiNameItem),
            NormalizeItemName(BluetoothConnectedItem),
            NormalizeItemName(BluetoothDeviceNamesItem),
            NormalizeItemName(OpenHabConnectionItem),
            NormalizeItemName(FocusStateItem));
    }

    public DeviceInfoSyncSettings Normalized()
    {
        var identifier = SanitizeDeviceIdentifier(DeviceIdentifier);
        var interval = SyncIntervalMinutes;
        if (interval < MinSyncIntervalMinutes || interval > MaxSyncIntervalMinutes)
        {
            interval = DefaultSyncIntervalMinutes;
        }

        return this with
        {
            DeviceIdentifier = identifier,
            SyncIntervalMinutes = interval,
            BatteryLevelItem = NormalizeItemName(BatteryLevelItem),
            ChargingStateItem = NormalizeItemName(ChargingStateItem),
            LockedStateItem = NormalizeItemName(LockedStateItem),
            SessionStateItem = NormalizeItemName(SessionStateItem),
            WifiConnectedItem = NormalizeItemName(WifiConnectedItem),
            WifiNameItem = NormalizeItemName(WifiNameItem),
            BluetoothConnectedItem = NormalizeItemName(BluetoothConnectedItem),
            BluetoothDeviceNamesItem = NormalizeItemName(BluetoothDeviceNamesItem),
            OpenHabConnectionItem = NormalizeItemName(OpenHabConnectionItem),
            FocusStateItem = NormalizeItemName(FocusStateItem)
        };
    }

    public static string SanitizeDeviceIdentifier(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "WindowsDevice" : value.Trim();
        var sanitized = InvalidIdentifierCharacters.Replace(trimmed, string.Empty);
        return string.IsNullOrWhiteSpace(sanitized) ? "WindowsDevice" : sanitized;
    }

    private static string? NormalizeItemName(string? itemName)
    {
        return string.IsNullOrWhiteSpace(itemName) ? null : itemName.Trim();
    }
}
