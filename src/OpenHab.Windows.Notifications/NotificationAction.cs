namespace OpenHab.Windows.Notifications;

public sealed record NotificationAction(string Type, string Payload);

public sealed record NotificationActionButton(string Title, string Type, string Payload);

public static class NotificationActionButtonExtensions
{
    public static string ToRawButton(this NotificationActionButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return $"{button.Title}={button.Type}:{button.Payload}";
    }
}

public static class NotificationActionParser
{
    public static NotificationAction? TryParse(string? rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            return null;

        ReadOnlySpan<char> span = rawAction.AsSpan().Trim();

        if (span.IsEmpty)
            return null;

        var trimmed = span.ToString();

        // Special case: http:// and https:// URLs — type is the scheme, payload is the full URL
        if (span.StartsWith("https://".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new NotificationAction("https", trimmed);

        if (span.StartsWith("http://".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new NotificationAction("http", trimmed);

        // General case: type is everything before the first colon, payload is everything after
        int colonIndex = span.IndexOf(':');
        if (colonIndex < 0)
        {
            // No colon — treat entire string as type with empty payload
            return new NotificationAction(span.ToString(), string.Empty);
        }

        string type = span[..colonIndex].ToString().Trim();
        string payload = span[(colonIndex + 1)..].ToString().Trim();

        if (type.Length == 0)
            return null;

        return new NotificationAction(type, payload);
    }

    public static NotificationActionButton? TryParseButton(string? rawButton)
    {
        if (string.IsNullOrWhiteSpace(rawButton))
            return null;

        ReadOnlySpan<char> span = rawButton.AsSpan().Trim();

        if (span.IsEmpty)
            return null;

        // Title is text before the first '=', the rest is the action
        int eqIndex = span.IndexOf('=');
        if (eqIndex < 0)
            return null;

        string title = span[..eqIndex].ToString().Trim();
        string actionPart = span[(eqIndex + 1)..].ToString().Trim();

        if (title.Length == 0 || actionPart.Length == 0)
            return null;

        NotificationAction? action = TryParse(actionPart);
        if (action is null)
            return null;

        return new NotificationActionButton(title, action.Type, action.Payload);
    }
}
