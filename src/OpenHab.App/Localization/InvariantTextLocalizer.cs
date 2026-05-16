using System.Globalization;

namespace OpenHab.App.Localization;

public sealed class InvariantTextLocalizer(IReadOnlyDictionary<string, string> strings) : ITextLocalizer
{
    public string Get(string key) =>
        strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
