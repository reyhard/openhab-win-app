using OpenHab.Core.DeviceState;

namespace OpenHab.App.DeviceInfo;

public interface IDeviceStateSnapshotSource
{
    Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
