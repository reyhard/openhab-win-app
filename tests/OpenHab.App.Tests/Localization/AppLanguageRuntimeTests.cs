using OpenHab.App.Settings;
using OpenHab.Windows.Tray.Localization;
using System.Globalization;
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

    [Fact]
    public void ToLanguageTagMapsUnknownLanguageToSystem()
    {
        Assert.Null(AppLanguageRuntime.ToLanguageTag((AppLanguage)999));
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
        Assert.Equal("pl-PL", Assert.Single(appliedTags));
    }

    [Theory]
    [InlineData(typeof(COMException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void ApplyLanguageFallsBackToSystemWhenOverrideFails(Type exceptionType)
    {
        var appliedLanguage = AppLanguageRuntime.ApplyLanguage(
            AppLanguage.Polish,
            _ => throw CreateException(exceptionType),
            _ => { });

        Assert.Equal(AppLanguage.System, appliedLanguage);
    }

    [Fact]
    public void ApplyLanguageAppliesDotNetCultureWhenNoTestOverrideIsProvided()
    {
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var appliedLanguage = AppLanguageRuntime.ApplyLanguage(AppLanguage.English, _ => { });

            Assert.Equal(AppLanguage.English, appliedLanguage);
            Assert.Equal("en-US", CultureInfo.DefaultThreadCurrentCulture?.Name);
            Assert.Equal("en-US", CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static Exception CreateException(Type exceptionType)
    {
        if (exceptionType == typeof(COMException))
        {
            return new COMException("Language override is unavailable.");
        }

        return (Exception)Activator.CreateInstance(exceptionType, "Language override is unavailable.")!;
    }
}
