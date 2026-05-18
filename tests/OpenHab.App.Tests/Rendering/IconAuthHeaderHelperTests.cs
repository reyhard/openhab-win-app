using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public sealed class IconAuthHeaderHelperTests
{
    [Fact]
    public void ApplyAuthHeadersUsesBearerTokenWhenPresent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://demo.local/icon/light");
        var context = new SitemapControlFactory.IconAuthContext(
            ApiToken: "token-123",
            BasicUserName: "user",
            BasicPassword: "secret",
            TransportKind: null);

        IconAuthHeaderHelper.ApplyAuthHeaders(request, context);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public void ApplyAuthHeadersUsesBasicCredentialsWhenNoTokenExists()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://demo.local/icon/light");
        var context = new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: "user",
            BasicPassword: "secret",
            TransportKind: null);

        IconAuthHeaderHelper.ApplyAuthHeaders(request, context);

        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal("dXNlcjpzZWNyZXQ=", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public void ApplyAuthHeadersLeavesRequestUnauthenticatedWhenContextHasNoCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://demo.local/icon/light");
        var context = new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: null,
            BasicPassword: null,
            TransportKind: null);

        IconAuthHeaderHelper.ApplyAuthHeaders(request, context);

        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData(null, null, "none")]
    [InlineData("token-123", "user", "bearer")]
    [InlineData(null, "user", "basic")]
    [InlineData(" ", " ", "none")]
    public void GetAuthModeDescribesEffectiveCredential(string? token, string? userName, string expected)
    {
        var context = new SitemapControlFactory.IconAuthContext(
            ApiToken: token,
            BasicUserName: userName,
            BasicPassword: "secret",
            TransportKind: null);

        Assert.Equal(expected, IconAuthHeaderHelper.GetAuthMode(context));
    }

    [Fact]
    public void GetAuthModeReturnsNoneForMissingContext()
    {
        Assert.Equal("none", IconAuthHeaderHelper.GetAuthMode(null));
    }
}
