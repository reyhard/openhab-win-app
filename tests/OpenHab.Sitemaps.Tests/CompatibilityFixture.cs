namespace OpenHab.Sitemaps.Tests;

internal static class CompatibilityFixture
{
    public static string ReadText(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenHab.Windows.sln")))
            {
                var fixturePath = Path.Combine([directory.FullName, "tests", "CompatibilityFixtures", .. segments]);
                if (!File.Exists(fixturePath))
                {
                    throw new FileNotFoundException(
                        $"Compatibility fixture was not found under repository root '{directory.FullName}': {fixturePath}",
                        fixturePath);
                }

                return File.ReadAllText(fixturePath);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate OpenHab.Windows.sln while resolving compatibility fixture '{Path.Combine(segments)}'.");
    }
}
