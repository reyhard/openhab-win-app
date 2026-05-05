using OpenHab.Core.Auth;

namespace OpenHab.Core.Tests.Auth;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> store = new();

    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret must not be blank.", nameof(secret));

        cancellationToken.ThrowIfCancellationRequested();

        store[$"{resource}:{key}"] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        store.TryGetValue($"{resource}:{key}", out var value);
        return Task.FromResult(value);
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be blank.", nameof(resource));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key must not be blank.", nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        store.Remove($"{resource}:{key}");
        return Task.CompletedTask;
    }
}
