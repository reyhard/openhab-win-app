using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapValueMatcherTests
{
    [Theory]
    [InlineData(" 5000 ", "5000", true)]
    [InlineData("5000", "5000.0", true)]
    [InlineData("ON", "on", true)]
    [InlineData("5000", "5000.2", false)]
    [InlineData("An", "ON", false)]
    [InlineData(null, "ON", false)]
    [InlineData("ON", " ", false)]
    [InlineData("", "ON", false)]
    public void Matches_TrimsAndComparesNumerically(string? left, string? right, bool expected)
    {
        Assert.Equal(expected, SitemapValueMatcher.Matches(left, right));
    }
}
