namespace OpenHab.Core.Auth;

public interface ICredentialStore
{
    Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken);
    Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken);
    Task RemoveAsync(string resource, string key, CancellationToken cancellationToken);
}
