using OpenHab.App.Settings;
using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class DwmWindowDecorationsTests
{
    [Fact]
    public void BuildRequestsForWindows11IncludesRoundedBorderlessDarkMicaChrome()
    {
        var requests = WindowDecorationPolicy.BuildRequests(isWindows11OrLater: true).ToList();

        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.WindowCornerPreference &&
            r.IntValue == (int)DwmWindowCornerPreference.Round);
        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.BorderColor &&
            r.UIntValue == 0x00000000);
        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.UseImmersiveDarkMode &&
            r.IntValue == 1);
        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.SystemBackdropType &&
            r.IntValue == (int)DwmSystemBackdropType.MainWindow);
    }

    [Fact]
    public void BuildRequestsForOlderWindowsOnlyUsesBestEffortDarkMode()
    {
        var requests = WindowDecorationPolicy.BuildRequests(isWindows11OrLater: false).ToList();

        var request = Assert.Single(requests);
        Assert.Equal(DwmWindowAttribute.UseImmersiveDarkMode, request.Attribute);
        Assert.Equal(1, request.IntValue);
    }

    [Fact]
    public void BuildRequestsForLightThemeDisablesImmersiveDarkMode()
    {
        var requests = WindowDecorationPolicy.BuildRequests(
            isWindows11OrLater: true,
            theme: FlyoutTheme.Light).ToList();

        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.BorderColor &&
            r.UIntValue == 0x00FFFFFF);
        Assert.Contains(requests, r =>
            r.Attribute == DwmWindowAttribute.UseImmersiveDarkMode &&
            r.IntValue == 0);
    }

    [Fact]
    public void ResolveFlyoutThemeUsesSelectedColorTheme()
    {
        Assert.Equal(FlyoutTheme.Dark, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.FollowSystemSettings, true));
        Assert.Equal(FlyoutTheme.Light, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.FollowSystemSettings, false));
        Assert.Equal(FlyoutTheme.Dark, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.Dark, true));
        Assert.Equal(FlyoutTheme.Dark, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.Dark, false));
        Assert.Equal(FlyoutTheme.Light, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.Bright, true));
        Assert.Equal(FlyoutTheme.Light, WindowDecorationPolicy.ResolveFlyoutTheme(AppColorTheme.Bright, false));
    }
}
