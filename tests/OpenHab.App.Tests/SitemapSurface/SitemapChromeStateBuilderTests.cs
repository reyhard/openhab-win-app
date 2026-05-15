using OpenHab.App.Runtime;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class SitemapChromeStateBuilderTests
{
    [Fact]
    public void BuildPinsTitleToRootBreadcrumbAndShowsBreadcrumbsWhenNested()
    {
        var snapshot = Snapshot(
            descriptorTitle: "Kitchen",
            breadcrumbs: ["Home", "Kitchen"],
            statusText: "Ready.");

        var state = SitemapChromeStateBuilder.Build(snapshot, configuredSitemapName: "default", isSearchChromeOpen: false);

        Assert.Equal("Home", state.Title);
        Assert.Equal("Ready.", state.StatusText);
        Assert.Equal(["Home", "Kitchen"], state.Breadcrumbs);
        Assert.True(state.ShowBreadcrumbs);
        Assert.False(state.ShowSearch);
        Assert.Equal(string.Empty, state.SearchText);
    }

    [Fact]
    public void BuildUsesConfiguredSitemapNameWhenBreadcrumbsAreMissing()
    {
        var snapshot = Snapshot(descriptorTitle: "Ignored", breadcrumbs: []);

        var state = SitemapChromeStateBuilder.Build(snapshot, configuredSitemapName: "main", isSearchChromeOpen: false);

        Assert.Equal("main", state.Title);
        Assert.Equal(["main"], state.Breadcrumbs);
        Assert.False(state.ShowBreadcrumbs);
    }

    [Fact]
    public void BuildShowsSearchInsteadOfBreadcrumbsWhenSearchChromeIsOpen()
    {
        var snapshot = Snapshot(
            descriptorTitle: "Kitchen",
            breadcrumbs: ["Home", "Kitchen"],
            isSearchActive: true,
            searchQuery: "lamp");

        var state = SitemapChromeStateBuilder.Build(snapshot, configuredSitemapName: "main", isSearchChromeOpen: false);

        Assert.Equal("Home", state.Title);
        Assert.False(state.ShowBreadcrumbs);
        Assert.True(state.ShowSearch);
        Assert.Equal("lamp", state.SearchText);
    }

    private static SitemapRuntimeSnapshot Snapshot(
        string? descriptorTitle = null,
        IReadOnlyList<string>? breadcrumbs = null,
        string statusText = "",
        bool isSearchActive = false,
        string searchQuery = "")
    {
        var descriptor = descriptorTitle is null
            ? null
            : new SitemapRenderDescriptor(SitemapSkinKind.Windows11, "page", descriptorTitle, []);

        return new SitemapRuntimeSnapshot(
            Descriptor: descriptor,
            ActiveTransport: TransportKind.Local,
            ConnectionState: ConnectionState.Online,
            Breadcrumbs: breadcrumbs ?? [],
            StatusText: statusText,
            IsBusy: false,
            HasError: false,
            ChangedRowIndices: [],
            IsSearchActive: isSearchActive,
            SearchQuery: searchQuery,
            SearchResultCount: isSearchActive ? 1 : 0);
    }
}
