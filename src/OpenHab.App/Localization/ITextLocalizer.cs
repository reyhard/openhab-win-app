namespace OpenHab.App.Localization;

public interface ITextLocalizer
{
    string Get(string key);

    string Format(string key, params object[] args);
}
