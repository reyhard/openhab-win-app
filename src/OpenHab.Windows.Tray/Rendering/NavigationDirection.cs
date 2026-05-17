namespace OpenHab.Windows.Tray.Rendering;

/// <summary>
/// Direction of a sitemap page transition used to pick the slide axis.
/// </summary>
internal enum NavigationDirection
{
    /// <summary>Navigating deeper (child page). Content slides left.</summary>
    Forward,

    /// <summary>Navigating shallower (parent page). Content slides right.</summary>
    Back
}
