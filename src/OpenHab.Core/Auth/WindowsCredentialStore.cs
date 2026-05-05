using Windows.Security.Credentials;

namespace OpenHab.Core.Auth;

public sealed class WindowsCredentialStore : ICredentialStore
{
    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret must not be blank.", nameof(secret));

        cancellationToken.ThrowIfCancellationRequested();

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(resource, key, secret));
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            vault.Remove(credential);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            // Already removed or never stored — no-op.
        }

        return Task.CompletedTask;
    }
}
