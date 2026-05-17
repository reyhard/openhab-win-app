using OpenHab.App.Settings;
using OpenHab.Windows.Tray.Localization;
using System.Runtime.InteropServices;

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

    [Fact]
    public void ApplyLanguageDoesNotTouchWinRtOverrideForSystemLanguage()
    {
        var appliedTags = new List<string>();

        var appliedLanguage = AppLanguageRuntime.ApplyLanguage(AppLanguage.System, appliedTags.Add, _ => { });

        Assert.Equal(AppLanguage.System, appliedLanguage);
        Assert.Empty(appliedTags);
    }

    [Fact]
    public void ApplyLanguageAppliesExplicitLanguageTag()
    {
        var appliedTags = new List<string>();

        var appliedLanguage = AppLanguageRuntime.ApplyLanguage(AppLanguage.Polish, appliedTags.Add, _ => { });

        Assert.Equal(AppLanguage.Polish, appliedLanguage);
        Assert.Collection(appliedTags, tag => Assert.Equal("pl-PL", tag));
    }

    [Fact]
    public void ApplyLanguageFallsBackToSystemWhenOverrideFails()
    {
        var appliedLanguage = AppLanguageRuntime.ApplyLanguage(
            AppLanguage.Polish,
            _ => throw new COMException("Language override is unavailable."),
            _ => { });

        Assert.Equal(AppLanguage.System, appliedLanguage);
    }
}
