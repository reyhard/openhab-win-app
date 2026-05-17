using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using OpenHab.App.Localization;

namespace OpenHab.Windows.Tray.Localization;

internal sealed class WinUiTextLocalizer : ITextLocalizer
{
    private readonly Func<string, string?> resourceLookup;

    public WinUiTextLocalizer()
        : this(CreateResourceLookup())
    {
    }

    internal WinUiTextLocalizer(Func<string, string?> resourceLookup)
    {
        this.resourceLookup = resourceLookup;
    }

    public string Get(string key)
    {
        try
        {
            var value = resourceLookup(key);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            value = resourceLookup(key.Replace('.', '/'));
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // WinUI resource lookup is COM-backed and can fail in unpackaged or damaged runtime states.
        }

        return DefaultEnglishTextLocalizer.Instance.Get(key);
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static Func<string, string?> CreateResourceLookup()
    {
        ResourceLoader? resourceLoader = null;
        return key =>
        {
            resourceLoader ??= new ResourceLoader();
            return resourceLoader.GetString(key);
        };
    }
}
