using OpenHab.Core.Api;

namespace OpenHab.Core;

public static class SafeDiagnosticText
{
    public static string ForLog(string? value, int maxLength = 240)
    {
        return SensitiveTextRedactor.Redact(value, maxLength);
    }

    public static string ForLog(Exception exception, int maxLength = 240)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return ForLog($"{exception.GetType().Name}: {exception.Message}", maxLength);
    }

    public static string ForLog(Uri uri, int maxLength = 240)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        return ForLog(builder.Uri.AbsoluteUri, maxLength);
    }

    public static string ForUserStatus(Exception exception, string prefix)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "Operation failed.";
        }

        prefix = prefix.TrimEnd();

        return exception switch
        {
            OpenHabRequestException requestException =>
                $"{prefix} openHAB returned HTTP {(int)requestException.StatusCode} {requestException.StatusCode}.",
            TimeoutException =>
                $"{prefix} The request timed out.",
            OperationCanceledException =>
                $"{prefix} The operation was canceled.",
            HttpRequestException =>
                $"{prefix} The openHAB endpoint could not be reached.",
            _ =>
                $"{prefix} {exception.GetType().Name}."
        };
    }
}
