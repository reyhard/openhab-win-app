using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsBluetoothInfo(bool? IsBluetoothConnected, string? ConnectedDeviceNames);

internal static class WindowsBluetoothInfoReader
{
    public static async Task<WindowsBluetoothInfo> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            DeviceInformationCollection connectedDevices = await DeviceInformation.FindAllAsync(selector);
            cancellationToken.ThrowIfCancellationRequested();

            if (connectedDevices.Count == 0)
            {
                return new WindowsBluetoothInfo(false, null);
            }

            var names = connectedDevices
                .Select(static device => device.Name?.Trim())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new WindowsBluetoothInfo(
                true,
                names.Length == 0 ? "Connected" : string.Join(", ", names));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new WindowsBluetoothInfo(null, null);
        }
    }
}
