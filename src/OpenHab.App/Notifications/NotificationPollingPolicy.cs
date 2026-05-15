using System.Security.Cryptography;
using System.Text;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.App.Notifications;

public sealed record NotificationPollingConfig(
    EndpointMode EndpointMode,
    Uri CloudEndpoint,
    int PollIntervalSeconds,
    string? CloudCredentialsFingerprint);

public static class NotificationPollingPolicy
{
    public static NotificationPollingConfig BuildConfig(AppSettings settings, CloudCredentials? cloudCredentials)
    {
        return new NotificationPollingConfig(
            settings.EndpointMode,
            settings.CloudEndpoint,
            settings.NotificationPollIntervalSeconds,
            BuildCloudCredentialsFingerprint(cloudCredentials));
    }

    public static bool ShouldReconfigure(NotificationPollingConfig? activeConfig, NotificationPollingConfig nextConfig)
    {
        return !Equals(activeConfig, nextConfig);
    }

    private static string? BuildCloudCredentialsFingerprint(CloudCredentials? cloudCredentials)
    {
        if (cloudCredentials is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cloudCredentials.UserName)
            || string.IsNullOrWhiteSpace(cloudCredentials.Password))
        {
            return null;
        }

        var fingerprintMaterial = $"{cloudCredentials.UserName.Trim()}\0{cloudCredentials.Password}";
        var fingerprintBytes = Encoding.UTF8.GetBytes(fingerprintMaterial);
        return Convert.ToHexString(SHA256.HashData(fingerprintBytes));
    }
}
