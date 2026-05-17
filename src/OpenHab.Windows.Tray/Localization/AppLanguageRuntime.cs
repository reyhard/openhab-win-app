using OpenHab.App.Settings;
using Windows.Globalization;

namespace OpenHab.Windows.Tray.Localization;

internal static class AppLanguageRuntime
{
    public static string? ToLanguageTag(AppLanguage language) =>
        language switch
        {
            AppLanguage.System => null,
            AppLanguage.English => "en-US",
            AppLanguage.Polish => "pl-PL",
            _ => null
        };

    public static bool ShouldShowRestartNotice(AppLanguage savedLanguage, AppLanguage appliedLanguage) =>
        savedLanguage != appliedLanguage;

    public static void ApplyLanguage(AppLanguage language)
    {
        ApplicationLanguages.PrimaryLanguageOverride = ToLanguageTag(language) ?? string.Empty;
    }
}
