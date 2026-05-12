using OpenHab.Windows.Tray;
using OpenHab.App.Settings;

namespace OpenHab.App.Tests.Tray;

public sealed class DwmWindowDecorationsTests
{
    [Fact]
    public void BuildRequestsForWindows11IncludesRoundedBorderlessDarkMicaChrome()
    {
        var requests = DwmWindowDecorations.BuildRequests(isWindows11OrLater: true).ToList();

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
        var requests = DwmWindowDecorations.BuildRequests(isWindows11OrLater: false).ToList();

        var request = Assert.Single(requests);
        Assert.Equal(DwmWindowAttribute.UseImmersiveDarkMode, request.Attribute);
        Assert.Equal(1, request.IntValue);
    }

    [Fact]
    public void BuildRequestsForLightThemeDisablesImmersiveDarkMode()
    {
        var requests = DwmWindowDecorations.BuildRequests(
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
        Assert.Equal(FlyoutTheme.Dark, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.FollowSystemSettings, true));
        Assert.Equal(FlyoutTheme.Light, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.FollowSystemSettings, false));
        Assert.Equal(FlyoutTheme.Dark, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.Dark, true));
        Assert.Equal(FlyoutTheme.Dark, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.Dark, false));
        Assert.Equal(FlyoutTheme.Light, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.Bright, true));
        Assert.Equal(FlyoutTheme.Light, DwmWindowDecorations.ResolveFlyoutTheme(AppColorTheme.Bright, false));
    }
}
