using OpenHab.Sitemaps.Runtime;

namespace OpenHab.Sitemaps.Tests;

public sealed class OpenHabStatePatternFormatterTests
{
    [Theory]
    [InlineData("%.0f ml", "4870.4", "4870 ml")]
    [InlineData("%.0f ml", "4860.2", "4860 ml")]
    [InlineData("%.0f", "4870.5", "4871")]
    [InlineData("%.1f", "21.46", "21.5")]
    [InlineData("%d", "42", "42")]
    [InlineData("%d", "42.0", "42")]
    [InlineData("%s", "AUTO", "AUTO")]
    [InlineData("%.0f %%", "87", "87 %")]
    [InlineData("temp %.1f °C", "21.46", "temp 21.5 °C")]
    [InlineData("%5.1f", "3.2", "  3.2")]
    [InlineData("%-5s|", "ab", "ab   |")]
    [InlineData("%+06.1f", "3.2", "+003.2")]
    [InlineData("%06.1f", "-3.2", "-003.2")]
    [InlineData("%06.1f", "3.2", "0003.2")]
    [InlineData("% 5d", "42", "   42")]
    [InlineData("%-5d", "42", "42   ")]
    [InlineData("%+d", "-42", "-42")]
    [InlineData("%f", "1.25", "1.250000")]
    [InlineData("%% %d %%", "42", "% 42 %")]
    [InlineData("%+5s", "ab", "   ab")]
    public void TryFormat_FormatsSupportedPatterns(string pattern, string rawState, string expected)
    {
        Assert.Equal(expected, OpenHabStatePatternFormatter.TryFormat(pattern, rawState));
    }

    [Theory]
    [InlineData("%.2e", "1.0")]
    [InlineData("%.0f", "not-a-number")]
    [InlineData("%d", "42.5")]
    [InlineData("no conversion", "1")]
    [InlineData("", "1")]
    [InlineData(null, "1")]
    [InlineData("%d", null)]
    [InlineData("%d", "")]
    [InlineData("%", "42")]
    [InlineData("%5", "42")]
    [InlineData("%.f", "42")]
    [InlineData("%q", "42")]
    [InlineData("%d %s", "42")]
    [InlineData("%d %", "42")]
    [InlineData("%d", "not-a-number")]
    [InlineData("%f", "not-a-number")]
    public void TryFormat_ReturnsNullForUnsupportedOrInvalidInput(string? pattern, string? rawState)
    {
        Assert.Null(OpenHabStatePatternFormatter.TryFormat(pattern, rawState));
    }
}
