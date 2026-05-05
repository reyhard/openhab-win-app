using Windows.Security.Credentials;

namespace OpenHab.Core.Auth;

public sealed class WindowsCredentialStore : ICredentialStore
{
    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret must not be blank.", nameof(secret));
        }

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(resource, key, secret));
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception) when (true)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(resource, key);
            vault.Remove(credential);
        }
        catch (Exception) when (true)
        {
            // Already removed or never stored — no-op.
        }

        return Task.CompletedTask;
    }
}
