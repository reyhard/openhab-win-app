using OpenHab.App.Settings;
using OpenHab.Core;
using System.Runtime.InteropServices;
using ApplicationLanguages = Microsoft.Windows.Globalization.ApplicationLanguages;

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

    public static AppLanguage ApplyLanguage(AppLanguage language) =>
        ApplyLanguage(language, static tag => ApplicationLanguages.PrimaryLanguageOverride = tag);

    internal static AppLanguage ApplyLanguage(AppLanguage language, Action<string> applyLanguageTag)
    {
        ArgumentNullException.ThrowIfNull(applyLanguageTag);

        var languageTag = ToLanguageTag(language);
        if (languageTag is null)
        {
            return AppLanguage.System;
        }

        try
        {
            applyLanguageTag(languageTag);
            return language;
        }
        catch (Exception ex) when (IsLanguageOverrideUnavailable(ex))
        {
            DiagnosticLogger.Warn($"App language override failed: {ex.GetType().Name}");
            return AppLanguage.System;
        }
    }

    private static bool IsLanguageOverrideUnavailable(Exception ex) =>
        ex is COMException or InvalidOperationException or UnauthorizedAccessException;
}
