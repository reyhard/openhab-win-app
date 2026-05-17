using System.Diagnostics.CodeAnalysis;
using Windows.Security.Credentials;

namespace OpenHab.Core.Auth;

[ExcludeFromCodeCoverage(Justification = "Windows Credential Manager wrapper around OS APIs.")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret must not be blank.", nameof(secret));

        var vault = new PasswordVault();

        // Remove any existing credential first to allow updates.
        try
        {
            var existing = vault.Retrieve(resource, key);
            vault.Remove(existing);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            // Not found — that's fine, we're about to add.
        }

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
