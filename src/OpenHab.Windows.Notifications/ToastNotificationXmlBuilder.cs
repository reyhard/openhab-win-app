using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Data.Xml.Dom;

namespace OpenHab.Windows.Notifications;

public sealed record ToastTagGroup(string? Tag, string? Group);

public static partial class ToastNotificationXmlBuilder
{
    private const int HashPrefixLength = 16;

    public static XmlDocument Build(ToastNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        builder.Append("<toast");
        if (request.Important)
        {
            builder.Append(" scenario=\"urgent\"");
        }

        if (!string.IsNullOrWhiteSpace(request.LaunchAction))
        {
            builder.Append(" launch=\"");
            builder.Append(XmlEscape(request.LaunchAction!.Trim()));
            builder.Append("\"");
        }

        builder.Append('>');

        if (!string.IsNullOrWhiteSpace(request.Header))
        {
            builder.Append("<header id=\"");
            builder.Append(XmlEscape(BuildHeaderId(request)));
            builder.Append("\" title=\"");
            builder.Append(XmlEscape(request.Header!.Trim()));
            builder.Append("\" arguments=\"openhab:open\" />");
        }

        builder.Append("<visual><binding template=\"ToastGeneric\">");

        if (request.AppLogoOverrideUri is not null)
        {
            builder.Append("<image placement=\"appLogoOverride\" src=\"");
            builder.Append(XmlEscape(request.AppLogoOverrideUri.ToString()));
            builder.Append("\" />");
        }

        if (request.HeroImageUri is not null)
        {
            builder.Append("<image placement=\"hero\" src=\"");
            builder.Append(XmlEscape(request.HeroImageUri.ToString()));
            builder.Append("\" />");
        }

        builder.Append("<text>");
        builder.Append(XmlEscape(request.Title));
        builder.Append("</text><text>");
        builder.Append(XmlEscape(request.Body));
        builder.Append("</text></binding></visual>");

        var buttons = request.Actions?.Take(3).ToList();
        if (buttons is { Count: > 0 })
        {
            builder.Append("<actions>");
            foreach (var button in buttons)
            {
                builder.Append("<action content=\"");
                builder.Append(XmlEscape(button.Title));
                builder.Append("\" activationType=\"foreground\" arguments=\"");
                builder.Append(XmlEscape($"{button.Type}:{button.Payload}"));
                builder.Append("\" />");
            }

            builder.Append("</actions>");
        }

        builder.Append("</toast>");

        var xml = new XmlDocument();
        xml.LoadXml(builder.ToString());
        return xml;
    }

    public static ToastTagGroup BuildTagAndGroup(ToastNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.ReferenceId))
        {
            return new ToastTagGroup(BuildStableId("ref", request.ReferenceId), "openhab-ref");
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            return new ToastTagGroup(BuildStableId("tag", request.Tag), "openhab-tag");
        }

        return new ToastTagGroup(null, null);
    }

    private static string BuildHeaderId(ToastNotificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ReferenceId))
        {
            return BuildStableId("hdr-ref", request.ReferenceId);
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            return BuildStableId("hdr-tag", request.Tag);
        }

        return "openhab";
    }

    private static string BuildStableId(string prefix, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{prefix}-{hex[..HashPrefixLength]}";
    }

    private static string XmlEscape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
