using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public static class SitemapSwitchStateResolver
{
    public static string ResolveToggleCommand(string? displayState, string? rawItemState)
    {
        var isOn = TryResolveIsOn(rawItemState) ?? TryResolveIsOn(displayState) ?? false;
        return isOn ? "OFF" : "ON";
    }

    /// <summary>
    /// Resolves the state text to show for an incoming raw item state. SSE events carry only the
    /// raw value, not the sitemap widget pattern that produced the fetched label, so the pattern
    /// captured from the REST sitemap metadata is applied when it is available and supported.
    /// Mapping commands are left raw so the renderer can substitute the mapping label, formatted
    /// lock states stay LOCKED/UNLOCKED, and unsupported patterns fall back to the raw value.
    /// </summary>
    public static string ResolveEventDisplayState(
        string? currentDisplayState,
        string? statePattern,
        string rawItemState,
        IReadOnlyList<SitemapMapping>? mappings = null)
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

        if (MatchesMappingCommand(rawItemState, mappings))
        {
            return rawItemState;
        }

        return OpenHabStatePatternFormatter.TryFormat(statePattern, rawItemState) ?? rawItemState;
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

    private static bool MatchesMappingCommand(string rawItemState, IReadOnlyList<SitemapMapping>? mappings)
    {
        if (mappings is null || mappings.Count == 0 || string.IsNullOrWhiteSpace(rawItemState))
        {
            return false;
        }

        foreach (var mapping in mappings)
        {
            if (SitemapValueMatcher.Matches(mapping.Command, rawItemState))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLockDisplayState(string? state) =>
        string.Equals(state, "LOCKED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "UNLOCKED", StringComparison.OrdinalIgnoreCase);
}
