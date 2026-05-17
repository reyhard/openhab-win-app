using OpenHab.App.Settings;
using OpenHab.Core;
using System.Globalization;
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

    internal static AppLanguage ApplyLanguage(
        AppLanguage language,
        Action<string> applyLanguageTag,
        Action<string>? applyCulture = null)
    {
        ArgumentNullException.ThrowIfNull(applyLanguageTag);
        applyCulture ??= ApplyDotNetCulture;

        var languageTag = ToLanguageTag(language);
        if (languageTag is null)
        {
            return AppLanguage.System;
        }

        try
        {
            applyLanguageTag(languageTag);
            applyCulture(languageTag);
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

    private static void ApplyDotNetCulture(string languageTag)
    {
        var culture = CultureInfo.GetCultureInfo(languageTag);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
