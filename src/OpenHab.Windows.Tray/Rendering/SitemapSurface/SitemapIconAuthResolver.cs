using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapIconAuthResolver(AppSettingsController settingsController)
{
    public SitemapControlFactory.IconAuthContext Resolve(TransportKind transportKind)
    {
        if (transportKind == TransportKind.Local)
        {
            return new SitemapControlFactory.IconAuthContext(
                ApiToken: GetApiToken(TransportKind.Local),
                BasicUserName: null,
                BasicPassword: null,
                TransportKind: transportKind);
        }

        var cloudCredentials = GetCloudCredentials();
        return new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: cloudCredentials?.UserName,
            BasicPassword: cloudCredentials?.Password,
            TransportKind: transportKind);
    }

    private string? GetApiToken(TransportKind kind)
    {
        try { return settingsController.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private CloudCredentials? GetCloudCredentials()
    {
        try { return settingsController.GetCloudCredentialsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }
}
