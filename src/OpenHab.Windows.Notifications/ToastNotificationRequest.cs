namespace OpenHab.Windows.Notifications;

public sealed record ToastNotificationRequest(
    string Title,
    string Body,
    IReadOnlyList<NotificationActionButton>? Actions = null,
    string? LaunchAction = null,
    bool Important = false,
    string? Header = null,
    string? Tag = null,
    string? ReferenceId = null,
    Uri? AppLogoOverrideUri = null,
    Uri? HeroImageUri = null);
