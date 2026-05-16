using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using OpenHab.App.Localization;

namespace OpenHab.Windows.Tray.Localization;

internal sealed class WinUiTextLocalizer : ITextLocalizer
{
    private readonly ResourceLoader resourceLoader = new();

    public string Get(string key)
    {
        var value = resourceLoader.GetString(key);
        return string.IsNullOrEmpty(value) ? DefaultEnglishTextLocalizer.Instance.Get(key) : value;
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
