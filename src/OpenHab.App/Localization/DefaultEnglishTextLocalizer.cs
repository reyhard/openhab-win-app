namespace OpenHab.App.Localization;

public static class DefaultEnglishTextLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["App.Name"] = "openHAB",
        ["Common.Cancel"] = "Cancel",
        ["Common.Continue"] = "Continue",
        ["Common.Delete"] = "Delete",
        ["Common.Save"] = "Save",
        ["Runtime.Status.Loading"] = "Loading...",
        ["Runtime.Connection.Failed"] = "Connection failed.",
        ["Runtime.Connection.FallbackFailed"] = "Fallback failed.",
        ["Runtime.Connection.ConnectedViaCloudLocalFailed"] = "Connected via cloud (local failed).",
        ["Runtime.Connection.ConnectedViaCloud"] = "Connected via cloud ({0})",
        ["Runtime.Connection.ConnectedViaLocal"] = "Connected via local ({0})",
        ["Runtime.Connection.State"] = "Connection: {0}",
        ["Runtime.LiveUpdates.UnavailableRefreshManually"] = "Live updates unavailable. Refresh manually.",
        ["Runtime.MainUi.LoadError"] = "Error: Main UI could not be loaded.",
        ["Runtime.Error"] = "Error.",
        ["Sitemap.Search.ResultsTitle"] = "Search results",
        ["Sitemap.Action.Run"] = "Run",
        ["Sitemap.WebView.NoUrlConfigured"] = "No URL configured",
        ["Sitemap.WebView.OpenInBrowser"] = "Open in browser",
        ["Sitemap.Chart.RequiresItem"] = "Chart requires an item",
        ["Shortcuts.Validation.TargetItemRequired"] = "Target Item is required.",
        ["Shortcuts.Validation.CommandValueRequired"] = "Command value is required for this action type.",
        ["Shortcuts.Validation.BindingAlreadyUsed"] = "Shortcut is already used by {0}.",
        ["Shortcuts.CommandMenu.OwnerName"] = "openHAB Command Menu",
        ["Shortcuts.ActionOwner"] = "Action: {0}",
        ["Shortcuts.UnnamedAction"] = "Unnamed action"
    };

    public static ITextLocalizer Instance { get; } = new InvariantTextLocalizer(Strings);
}
