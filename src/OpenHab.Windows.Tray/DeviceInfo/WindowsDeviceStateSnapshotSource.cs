using OpenHab.App.DeviceInfo;
using OpenHab.App.Runtime;
using OpenHab.Core.DeviceState;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed class WindowsDeviceStateSnapshotSource(
    SitemapRuntimeController runtimeController,
    WindowsBatteryInfoReader batteryReader,
    WindowsNetworkInfoReader networkReader,
    WindowsBluetoothInfoReader bluetoothReader,
    WindowsFocusInfoReader focusReader,
    WindowsSessionInfoReader sessionReader) : IDeviceStateSnapshotSource
{
    public async Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var battery = batteryReader.Read();
        var network = networkReader.Read();
        var bluetooth = await bluetoothReader.ReadAsync(cancellationToken);

        var snapshot = new DeviceStateSnapshot(
            BatteryLevelPercent: battery.BatteryLevelPercent,
            IsCharging: battery.IsCharging,
            IsLocked: sessionReader.IsLocked,
            SessionState: sessionReader.SessionState,
            IsWifiConnected: network.IsWifiConnected,
            WifiName: network.WifiName,
            IsBluetoothConnected: bluetooth.IsBluetoothConnected,
            BluetoothDeviceNames: bluetooth.ConnectedDeviceNames,
            OpenHabConnectionState: runtimeController.Current.ConnectionState.ToString().ToLowerInvariant(),
            FocusState: focusReader.ReadState());

        return snapshot;
    }
}
