namespace OpenHab.App.DeviceInfo;

public sealed record DeviceInfoSyncStatus(
    DateTimeOffset? LastAttemptedSync,
    DateTimeOffset? LastSuccessfulSync,
    string? LastResult,
    string? LastError)
{
    public static DeviceInfoSyncStatus Initial { get; } = new(null, null, null, null);
}
