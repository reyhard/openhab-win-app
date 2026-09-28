using System.Globalization;

namespace OpenHab.Sitemaps.Runtime;

public static class SitemapValueMatcher
{
    /// <summary>
    /// Shared state/mapping comparison used by the event resolver, the row mapper, and selection
    /// highlighting. Values are trimmed, compared case-insensitively, and then numerically when
    /// both sides parse as numbers so whitespace or formatting differences do not produce
    /// inconsistent display and active-mapping results.
    /// </summary>
    public static bool Matches(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var l = left.Trim();
        var r = right.Trim();
        if (string.Equals(l, r, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftIsNumber = double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);
        return leftIsNumber && rightIsNumber && Math.Abs(leftNumber - rightNumber) < 0.0001;
    }
}
