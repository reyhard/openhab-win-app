namespace OpenHab.App.Settings;

public static class SettingsPageTransitionPlanner
{
    public static bool TryResolveDirection(
        SettingsPageKind currentPage,
        SettingsPageKind destination,
        out SettingsPageTransitionDirection direction)
    {
        direction = SettingsPageTransitionDirection.Forward;
        if (currentPage == destination)
        {
            return false;
        }

        direction = destination == SettingsPageKind.Root
            ? SettingsPageTransitionDirection.Back
            : SettingsPageTransitionDirection.Forward;
        return true;
    }
}
