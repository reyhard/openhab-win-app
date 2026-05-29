namespace OpenHab.Sitemaps.Runtime;

public static class SitemapSwitchStateResolver
{
    public static string ResolveToggleCommand(string? displayState, string? rawItemState)
    {
        var isOn = TryResolveIsOn(rawItemState) ?? TryResolveIsOn(displayState) ?? false;
        return isOn ? "OFF" : "ON";
    }

    public static string ResolveEventDisplayState(string? currentDisplayState, string rawItemState)
    {
        if (IsLockDisplayState(currentDisplayState))
        {
            if (string.Equals(rawItemState, "ON", StringComparison.OrdinalIgnoreCase))
            {
                return "LOCKED";
            }

            if (string.Equals(rawItemState, "OFF", StringComparison.OrdinalIgnoreCase))
            {
                return "UNLOCKED";
            }
        }

        return rawItemState;
    }

    public static bool? TryResolveIsOn(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var normalized = state.Trim();
        if (string.Equals(normalized, "ON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "OFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "UNLOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static bool IsLockDisplayState(string? state) =>
        string.Equals(state, "LOCKED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "UNLOCKED", StringComparison.OrdinalIgnoreCase);
}
