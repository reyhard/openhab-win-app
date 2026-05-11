using System.Text.RegularExpressions;

namespace OpenHab.Core;

public static partial class SensitiveTextRedactor
{
    public static string Redact(string? value, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = value;
        redacted = AuthorizationHeaderPattern().Replace(redacted, "$1 [redacted]");
        redacted = BasicCredentialPattern().Replace(redacted, "$1 [redacted]");
        redacted = JsonSecretPattern().Replace(redacted, "$1[redacted]");
        redacted = QuerySecretPattern().Replace(redacted, "$1=[redacted]");
        redacted = UrlCredentialPattern().Replace(redacted, "$1[redacted]@");

        if (redacted.Length > maxLength)
        {
            redacted = redacted[..maxLength];
        }

        return redacted;
    }

    [GeneratedRegex(@"(?i)\b(authorization\s*:\s*(?:bearer|basic))\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"(?i)\b(basic)\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BasicCredentialPattern();

    [GeneratedRegex(@"(?i)(""?(?:password|passwd|token|secret|authorization|apikey|api_key)""?\s*[:=]\s*)(?:""[^""]*""|[^"",\s}]+)")]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(@"(?i)\b(password|passwd|token|secret|authorization|apikey|api_key)=([^&\s]+)")]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex(@"(?i)(https?://)[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentialPattern();
}
