using OpenHab.Core;
using OpenHab.Core.Api;

namespace OpenHab.Core.Tests.Diagnostics;

public sealed class SafeDiagnosticTextTests
{
    [Theory]
    [InlineData("Authorization: Bearer oh.secret.token", "oh.secret.token", "Authorization:")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "dXNlcjpwYXNz", "Authorization:")]
    [InlineData("https://user:pass@example.com/rest/items", "user:pass", "example.com/rest/items")]
    [InlineData("request failed?token=abc123&item=Light", "abc123", "request failed?")]
    [InlineData("{\"password\":\"secret-value\"}", "secret-value", "\"password\"")]
    public void ForLog_RedactsSensitiveValues(string input, string forbidden, string expectedContext)
    {
        var safe = SafeDiagnosticText.ForLog(input);

        Assert.DoesNotContain(forbidden, safe, StringComparison.Ordinal);
        Assert.Contains(expectedContext, safe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForUserStatus_PreservesHttpStatusButDropsBody()
    {
        var ex = new OpenHabRequestException(
            System.Net.HttpStatusCode.Unauthorized,
            "openHAB request failed with 401 Unauthorized: {\"token\":\"secret\"}");

        var safe = SafeDiagnosticText.ForUserStatus(ex, "Connection failed.");

        Assert.Contains("Connection failed.", safe, StringComparison.Ordinal);
        Assert.Contains("401", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("token", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForUserStatus_MapsTimeout()
    {
        var safe = SafeDiagnosticText.ForUserStatus(new TimeoutException("server did not answer"), "Connection failed.");

        Assert.Equal("Connection failed. The request timed out.", safe);
    }

    [Fact]
    public void ForUserStatus_MapsGenericExceptionWithoutMessage()
    {
        var safe = SafeDiagnosticText.ForUserStatus(new InvalidOperationException("contains token=abc"), "Connection failed.");

        Assert.Equal("Connection failed. InvalidOperationException.", safe);
    }
}
