using System.Net;
using OpenHab.Core.Api;
using OpenHab.Sitemaps.Parsing;

namespace OpenHab.CompatibilityProbe;

public sealed record SitemapValidationResult(int WidgetCount, int WidgetIdsObserved);

public sealed record MainUiPagesValidationResult(int PageCount);

public static class ProductionPayloadValidator
{
    public static SitemapValidationResult ValidateSitemap(string json)
    {
        var homepage = OpenHabSitemapJsonParser.ParseHomepage(json);
        var widgetIdsObserved = homepage.Widgets.Count(widget => !string.IsNullOrWhiteSpace(widget.WidgetId));
        return new SitemapValidationResult(homepage.Widgets.Count, widgetIdsObserved);
    }

    public static MainUiPagesValidationResult ValidateMainUiPages(string json)
    {
        using var client = new HttpClient(new StaticJsonHandler(json));
        var openHabClient = new OpenHabHttpClient(client, new Uri("http://compatibility.invalid/"));
        var pages = openHabClient.GetMainUiPageComponentsAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new MainUiPagesValidationResult(pages.Count);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
