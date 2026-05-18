using System.Net.Http.Headers;
using System.Text;

namespace OpenHab.Windows.Tray.Rendering;

internal static class IconAuthHeaderHelper
{
    internal static void ApplyAuthHeaders(HttpRequestMessage request, SitemapControlFactory.IconAuthContext authContext)
    {
        if (!string.IsNullOrWhiteSpace(authContext.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.ApiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(authContext.BasicUserName))
        {
            var raw = $"{authContext.BasicUserName}:{authContext.BasicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    internal static string GetAuthMode(SitemapControlFactory.IconAuthContext? authContext)
    {
        if (authContext is null) return "none";

        var context = authContext.Value;
        if (!string.IsNullOrWhiteSpace(context.ApiToken)) return "bearer";
        if (!string.IsNullOrWhiteSpace(context.BasicUserName)) return "basic";
        return "none";
    }
}
