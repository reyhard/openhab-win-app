using Windows.Networking.Connectivity;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsNetworkInfo(bool? IsWifiConnected, string? WifiName);

internal static class WindowsNetworkInfoReader
{
    public static WindowsNetworkInfo Read()
    {
        try
        {
            var profiles = NetworkInformation.GetConnectionProfiles();
            if (profiles is null || profiles.Count == 0)
            {
                return new WindowsNetworkInfo(false, null);
            }

            ConnectionProfile? wifiProfile = null;
            foreach (var profile in profiles)
            {
                if (!profile.IsWlanConnectionProfile)
                {
                    continue;
                }

                if (profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
                {
                    continue;
                }

                wifiProfile = profile;
                break;
            }

            if (wifiProfile is null)
            {
                return new WindowsNetworkInfo(false, null);
            }

            var ssid = wifiProfile.WlanConnectionProfileDetails?.GetConnectedSsid();
            var wifiName = string.IsNullOrWhiteSpace(ssid) ? wifiProfile.ProfileName : ssid;
            return new WindowsNetworkInfo(true, string.IsNullOrWhiteSpace(wifiName) ? null : wifiName);
        }
        catch
        {
            return new WindowsNetworkInfo(null, null);
        }
    }
}
