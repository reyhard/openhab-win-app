using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Windows.ApplicationModel.Resources;
using OpenHab.App.Localization;

namespace OpenHab.Windows.Tray.Localization;

internal sealed class WinUiTextLocalizer : ITextLocalizer
{
    private readonly Func<string, string?> resourceLookup;

    [ExcludeFromCodeCoverage(Justification = "WinUI ResourceLoader construction is framework glue; injected lookup behavior is unit tested.")]
    public WinUiTextLocalizer()
        : this(languageTag: null)
    {
    }

    [ExcludeFromCodeCoverage(Justification = "WinUI ResourceManager construction is framework glue; injected lookup behavior is unit tested.")]
    public WinUiTextLocalizer(string? languageTag)
        : this(CreateResourceLookup(languageTag))
    {
    }

    internal WinUiTextLocalizer(Func<string, string?> resourceLookup)
    {
        this.resourceLookup = resourceLookup;
    }

    public string Get(string key)
    {
        foreach (var resourceKey in ResourceKeyCandidates(key))
        {
            try
            {
                var value = resourceLookup(resourceKey);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
                // WinUI resource lookup is COM-backed and can fail in unpackaged or damaged runtime states.
            }
        }

        return DefaultEnglishTextLocalizer.Instance.Get(key);
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    [ExcludeFromCodeCoverage(Justification = "WinUI resource lookup is framework glue; fallback and key-candidate behavior use the injected constructor in tests.")]
    private static Func<string, string?> CreateResourceLookup(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            ResourceManager? resourceManager = null;
            ResourceContext? resourceContext = null;
            ResourceMap? resourceMap = null;
            return key =>
            {
                resourceManager ??= new ResourceManager();
                resourceContext ??= resourceManager.CreateResourceContext();
                resourceContext.QualifierValues["Language"] = languageTag;
                resourceMap ??= resourceManager.MainResourceMap.GetSubtree("Resources");
                return resourceMap.GetValue(key, resourceContext).ValueAsString;
            };
        }

        ResourceLoader? resourceLoader = null;
        return key =>
        {
            resourceLoader ??= new ResourceLoader();
            return resourceLoader.GetString(key);
        };
    }

    private static IEnumerable<string> ResourceKeyCandidates(string key)
    {
        yield return key;

        var slashKey = key.Replace('.', '/');
        if (!string.Equals(slashKey, key, StringComparison.Ordinal))
        {
            yield return slashKey;
        }
    }
}
