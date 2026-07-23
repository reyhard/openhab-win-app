using System.Text.Json;

namespace OpenHab.CompatibilityProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        var mode = args.Length == 1 ? args[0] : string.Empty;
        if (mode is not ("sitemap" or "main-ui-pages"))
        {
            Console.Error.WriteLine("Usage: OpenHab.CompatibilityProbe <sitemap|main-ui-pages>");
            return 2;
        }

        try
        {
            var payload = Console.In.ReadToEnd();
            object result = mode == "sitemap"
                ? ProductionPayloadValidator.ValidateSitemap(payload)
                : ProductionPayloadValidator.ValidateMainUiPages(payload);
            Console.Out.Write(JsonSerializer.Serialize(result));
            return 0;
        }
        catch
        {
            // Never echo server payloads or exception details: the caller may be probing a private server.
            Console.Error.WriteLine("The production parser rejected the response.");
            return 1;
        }
    }
}
