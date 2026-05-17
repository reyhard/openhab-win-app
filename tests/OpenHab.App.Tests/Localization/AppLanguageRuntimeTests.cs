using OpenHab.App.Settings;
using OpenHab.Windows.Tray.Localization;

namespace OpenHab.App.Tests.Localization;

public sealed class AppLanguageRuntimeTests
{
    [Fact]
    public void ToLanguageTagMapsSystemToNoOverride()
    {
        Assert.Null(AppLanguageRuntime.ToLanguageTag(AppLanguage.System));
    }

    [Theory]
    [InlineData(AppLanguage.English, "en-US")]
    [InlineData(AppLanguage.Polish, "pl-PL")]
    public void ToLanguageTagMapsOverrides(AppLanguage language, string expectedTag)
    {
        Assert.Equal(expectedTag, AppLanguageRuntime.ToLanguageTag(language));
    }

    [Theory]
    [InlineData(AppLanguage.System, AppLanguage.System, false)]
    [InlineData(AppLanguage.Polish, AppLanguage.System, true)]
    [InlineData(AppLanguage.Polish, AppLanguage.Polish, false)]
    [InlineData(AppLanguage.English, AppLanguage.Polish, true)]
    public void ShouldShowRestartNoticeOnlyWhenSavedLanguageDiffersFromAppliedLanguage(
        AppLanguage savedLanguage,
        AppLanguage appliedLanguage,
        bool expected)
    {
        Assert.Equal(expected, AppLanguageRuntime.ShouldShowRestartNotice(savedLanguage, appliedLanguage));
    }
}
