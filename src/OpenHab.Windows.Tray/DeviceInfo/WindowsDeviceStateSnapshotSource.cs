using System.Diagnostics.CodeAnalysis;
using OpenHab.App.DeviceInfo;
using OpenHab.App.Runtime;
using OpenHab.Core.DeviceState;

namespace OpenHab.Windows.Tray.DeviceInfo;

[ExcludeFromCodeCoverage(Justification = "Composition of Windows device-state readers.")]
internal sealed class WindowsDeviceStateSnapshotSource(
    SitemapRuntimeController runtimeController,
    WindowsFocusInfoReader focusReader,
    WindowsSessionInfoReader sessionReader) : IDeviceStateSnapshotSource
{
    public async Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var battery = WindowsBatteryInfoReader.Read();
        var network = WindowsNetworkInfoReader.Read();
        var bluetooth = await WindowsBluetoothInfoReader.ReadAsync(cancellationToken);

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
