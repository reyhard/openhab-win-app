using OpenHab.Core.Auth;

namespace OpenHab.Core.Tests.Auth;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> store = new();

    public Task StoreAsync(string resource, string key, string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret must not be blank.", nameof(secret));
        store[$"{resource}:{key}"] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.TryGetValue($"{resource}:{key}", out var value);
        return Task.FromResult(value);
    }

    public Task RemoveAsync(string resource, string key, CancellationToken cancellationToken)
    {
        store.Remove($"{resource}:{key}");
        return Task.CompletedTask;
    }
}
