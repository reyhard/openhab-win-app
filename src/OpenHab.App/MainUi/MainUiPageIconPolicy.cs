using OpenHab.Rendering.Icons;

namespace OpenHab.App.MainUi;

public static class MainUiPageIconPolicy
{
    public static Uri? BuildIconUri(Uri openHabBaseUri, string? icon)
    {
        ArgumentNullException.ThrowIfNull(openHabBaseUri);

        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        return OpenHabIconUriBuilder.Build(openHabBaseUri, icon.Trim(), iconState: null, format: "svg");
    }

    public static bool ShouldAttachOpenHabAuth(Uri iconUri, Uri openHabBaseUri)
    {
        ArgumentNullException.ThrowIfNull(iconUri);
        ArgumentNullException.ThrowIfNull(openHabBaseUri);

        return string.Equals(iconUri.Scheme, openHabBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(iconUri.Host, openHabBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            && iconUri.Port == openHabBaseUri.Port;
    }
}
