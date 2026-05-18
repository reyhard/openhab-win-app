using OpenHab.App.Localization;

namespace OpenHab.App.Tests.Localization;

public sealed class InvariantTextLocalizerTests
{
    [Fact]
    public void GetReturnsConfiguredValueAndFallsBackToKey()
    {
        var localizer = new InvariantTextLocalizer(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Greeting"] = "Hello"
        });

        Assert.Equal("Hello", localizer.Get("Greeting"));
        Assert.Equal("Missing", localizer.Get("Missing"));
    }

    [Fact]
    public void FormatUsesCurrentCultureAndConfiguredTemplate()
    {
        var localizer = new InvariantTextLocalizer(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elapsed"] = "{0}m ago"
        });

        Assert.Equal("5m ago", localizer.Format("Elapsed", 5));
    }

    [Fact]
    public void DefaultEnglishTextLocalizerContainsRuntimeFallbacks()
    {
        Assert.Equal("Loading...", DefaultEnglishTextLocalizer.Instance.Get("Runtime.Status.Loading"));
        Assert.Equal("Search results", DefaultEnglishTextLocalizer.Instance.Get("Sitemap.Search.ResultsTitle"));
    }
}
