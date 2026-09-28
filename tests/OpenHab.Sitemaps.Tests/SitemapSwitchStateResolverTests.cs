using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class SitemapSwitchStateResolverTests
{
    [Theory]
    [InlineData("%.0f ml", "4860.2", "4860 ml")]
    [InlineData("%.0f ml", "4870.4", "4870 ml")]
    [InlineData("%.0f", "4870.5", "4871")]
    [InlineData("%.1f", "21.46", "21.5")]
    [InlineData("%d", "42", "42")]
    [InlineData("%s", "UNKNOWN", "UNKNOWN")]
    [InlineData("%.0f %%", "87", "87 %")]
    public void ResolveEventDisplayState_AppliesServerPatternToRawState(
        string pattern,
        string rawItemState,
        string expected)
    {
        var display = SitemapSwitchStateResolver.ResolveEventDisplayState("4870 ml", pattern, rawItemState);

        Assert.Equal(expected, display);
    }

    [Fact]
    public void ResolveEventDisplayState_DoesNotCorruptRepeatedNumericDisplayText()
    {
        var display = SitemapSwitchStateResolver.ResolveEventDisplayState(
            "Room 12: 12 °C",
            "%.0f °C",
            "13");

        Assert.Equal("13 °C", display);
    }

    [Fact]
    public void ResolveEventDisplayState_LeavesMatchingMappingCommandRawForRendererSubstitution()
    {
        var mappings = new[] { new SitemapMapping("5000", "Zbiornik pełny") };

        var display = SitemapSwitchStateResolver.ResolveEventDisplayState(
            "4870 ml",
            "%.0f ml",
            "5000",
            mappings);

        Assert.Equal("5000", display);
    }

    [Fact]
    public void ResolveEventDisplayState_MatchesWhitespacePaddedMappingCommand()
    {
        var mappings = new[] { new SitemapMapping(" 5000 ", "Zbiornik pełny") };

        var display = SitemapSwitchStateResolver.ResolveEventDisplayState(
            "4870 ml",
            "%.0f ml",
            "5000",
            mappings);

        Assert.Equal("5000", display);
    }

    [Fact]
    public void ResolveEventDisplayState_MatchesNumericEquivalentMappingCommand()
    {
        var mappings = new[] { new SitemapMapping("5000", "Zbiornik pełny") };

        var display = SitemapSwitchStateResolver.ResolveEventDisplayState(
            "4870 ml",
            "%.0f ml",
            "5000.0",
            mappings);

        Assert.Equal("5000.0", display);
    }

    [Theory]
    [InlineData("UNLOCKED", "ON", "LOCKED")]
    [InlineData("LOCKED", "OFF", "UNLOCKED")]
    public void ResolveEventDisplayState_KeepsFormattedLockStates(
        string currentDisplayState,
        string rawItemState,
        string expected)
    {
        var display = SitemapSwitchStateResolver.ResolveEventDisplayState(
            currentDisplayState,
            "%s",
            rawItemState);

        Assert.Equal(expected, display);
    }

    [Fact]
    public void ResolveEventDisplayState_NormalBinarySwitchReturnsRawState()
    {
        Assert.Equal("ON", SitemapSwitchStateResolver.ResolveEventDisplayState("OFF", "%s", "ON"));
    }

    [Theory]
    [InlineData(null, "4860.2", "4860.2")]
    [InlineData("%.2e", "4860.2", "4860.2")]
    [InlineData("%.0f", "not-a-number", "not-a-number")]
    public void ResolveEventDisplayState_FallsBackToRawWhenPatternUnavailableOrUnsupported(
        string? pattern,
        string rawItemState,
        string expected)
    {
        var display = SitemapSwitchStateResolver.ResolveEventDisplayState("4870 ml", pattern, rawItemState);

        Assert.Equal(expected, display);
    }
}
