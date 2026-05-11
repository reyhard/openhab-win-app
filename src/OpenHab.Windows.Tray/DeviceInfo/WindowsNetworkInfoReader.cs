using Windows.Networking.Connectivity;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsNetworkInfo(bool? IsWifiConnected, string? WifiName);

internal sealed class WindowsNetworkInfoReader
{
    public WindowsNetworkInfo Read()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return new WindowsNetworkInfo(false, null);
            }

            var isWifi = profile.IsWlanConnectionProfile;
            if (!isWifi)
            {
                return new WindowsNetworkInfo(false, null);
            }

            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            return new WindowsNetworkInfo(true, string.IsNullOrWhiteSpace(ssid) ? null : ssid);
        }
        catch
        {
            return new WindowsNetworkInfo(null, null);
        }
    }
}
