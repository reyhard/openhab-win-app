using OpenHab.Core.Auth;

namespace OpenHab.Core.Tests.Auth;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task StoreAndRetrieveRoundTrip()
    {
        ICredentialStore store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "local-token", "oh.token.abc123", CancellationToken.None);
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "local-token", CancellationToken.None);
        Assert.Equal("oh.token.abc123", retrieved);
    }

    [Fact]
    public async Task RetrieveMissingKeyReturnsNull()
    {
        ICredentialStore store = new FakeCredentialStore();
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "nonexistent", CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RemoveRemovesStoredKey()
    {
        ICredentialStore store = new FakeCredentialStore();
        await store.StoreAsync("OpenHabAuth", "cloud-token", "oh.token.xyz789", CancellationToken.None);
        await store.RemoveAsync("OpenHabAuth", "cloud-token", CancellationToken.None);
        var retrieved = await store.RetrieveAsync("OpenHabAuth", "cloud-token", CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task StoreRejectsBlankSecret(string secret)
    {
        ICredentialStore store = new FakeCredentialStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreAsync("OpenHabAuth", "local-token", secret, CancellationToken.None));
    }
}
