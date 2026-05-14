# WinUI Sitemap Icon Rasterization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make openHAB server SVG icons render sharply in the WinUI sitemap renderer without downloading or caching one icon per visual size.

**Architecture:** Keep the network cache at the icon payload level: URI, tint color, and auth mode identify downloaded bytes. Do not cache WinUI `ImageSource` objects, because `SvgImageSource` decode state is tied to rasterization/layout. Each target `Image` gets a newly created source from cached bytes so SVG decoding can match that control.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, xUnit.

---

### Task 1: Add Regression Coverage For Cache Semantics

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`

- [ ] **Step 1: Add a pure helper for payload cache keys**

Add this method near the existing icon loading helpers:

```csharp
internal static string BuildIconPayloadCacheKey(Uri iconUri, string? iconColor, IconAuthContext? authContext)
{
    ArgumentNullException.ThrowIfNull(iconUri);
    return $"{iconUri.AbsoluteUri}|{iconColor ?? string.Empty}|{GetAuthMode(authContext)}";
}
```

- [ ] **Step 2: Write the failing test**

Add this test to `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`:

```csharp
[Fact]
public void BuildIconPayloadCacheKey_DoesNotIncludeVisualDimensions()
{
    var uri = new Uri("https://demo.local/icon/light?format=svg&state=ON");
    var key = SitemapControlFactory.BuildIconPayloadCacheKey(uri, "#ff0000", null);

    Assert.Equal("https://demo.local/icon/light?format=svg&state=ON|#ff0000|none", key);
    Assert.DoesNotContain("Width", key, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Height", key, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Run the targeted test to verify red**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter BuildIconPayloadCacheKey_DoesNotIncludeVisualDimensions
```

Expected before implementation: compile failure because `BuildIconPayloadCacheKey` does not exist.

### Task 2: Cache Payloads, Not WinUI Image Sources

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Replace the cache type**

Replace the current `ConcurrentDictionary<string, ImageSource>` field with:

```csharp
private static readonly ConcurrentDictionary<string, IconPayload> IconPayloadCache = new(StringComparer.Ordinal);
```

Add:

```csharp
private sealed record IconPayload(byte[] Bytes, string? MediaType);
```

- [ ] **Step 2: Change cache-hit handling**

In `TryLoadIconForFormatAsync`, use `BuildIconPayloadCacheKey` and decode a fresh source from cached bytes:

```csharp
var cacheKey = BuildIconPayloadCacheKey(iconUri, iconColor, authContext);
if (IconPayloadCache.TryGetValue(cacheKey, out var cachedPayload))
{
    var cachedSource = await CreateImageSourceFromBytesAsync(cachedPayload.Bytes, cachedPayload.MediaType);
    if (cachedSource is null)
    {
        return $"format={format}:decode-failed(media={cachedPayload.MediaType ?? "unknown"},source=cache)";
    }

    image.Source = cachedSource;
    return null;
}
```

- [ ] **Step 3: Store payloads after successful download**

After a successful decode from network bytes, store a copied payload:

```csharp
IconPayloadCache.TryAdd(cacheKey, new IconPayload(bytes.ToArray(), mediaType));
```

### Task 3: Let SVG Decode Match The Target Control

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Remove fixed 96x96 SVG rasterization**

Change `CreateSvgFromBytesAsync` to construct a plain `SvgImageSource`:

```csharp
private static async Task<SvgImageSource?> CreateSvgFromBytesAsync(byte[] bytes)
{
    var svg = new SvgImageSource();
    ...
}
```

Expected effect: per Microsoft documentation, `RasterizePixelWidth` and `RasterizePixelHeight` default to `NaN`, allowing the application layout to determine SVG decode size.

- [ ] **Step 2: Keep bitmap fallback unchanged**

Leave `CreateBitmapFromBytesAsync` behavior intact. PNG icons may still scale if openHAB only returns a small bitmap, but SVGs remain preferred and no longer reuse a fixed raster.

### Task 4: Verify

**Files:**
- No source edits.

- [ ] **Step 1: Run targeted App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SitemapControlFactoryTests"
```

Expected: all `SitemapControlFactoryTests` pass.

- [ ] **Step 2: Run the tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run the direct test gate if time permits**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.
