using OpenHab.Sitemaps.Models;

namespace OpenHab.Sitemaps.Runtime;

public sealed class SitemapNavigator
{
    private readonly Stack<SitemapPage> _backStack = new();

    public SitemapNavigator(SitemapPage rootPage)
    {
        ArgumentNullException.ThrowIfNull(rootPage);
        CurrentPage = rootPage;
    }

    public SitemapPage CurrentPage { get; private set; }

    public SitemapIntent ActivateWidget(int widgetIndex)
    {
        if (CurrentPage.Widgets is null)
        {
            throw new InvalidOperationException("Current page widgets cannot be null.");
        }

        if (widgetIndex < 0 || widgetIndex >= CurrentPage.Widgets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(widgetIndex));
        }

        var widget = CurrentPage.Widgets[widgetIndex];
        if (widget.Children.Count > 0)
        {
            _backStack.Push(CurrentPage);
            CurrentPage = widget.Children[0];
            return new NavigateIntent(CurrentPage.Id);
        }

        if (widget.Type == SitemapWidgetType.Switch && widget.ItemName is not null)
        {
            var command = SitemapSwitchStateResolver.ResolveToggleCommand(widget.State, widget.RawItemState);
            return new SendCommandIntent(widget.ItemName, command);
        }

        if (IsFallbackWidget())
        {
            return new OpenFallbackIntent(widget.Label);
        }

        return new NoOpIntent();
    }

    public bool Back()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        CurrentPage = _backStack.Pop();
        return true;
    }

    private static bool IsFallbackWidget()
    {
        return false;
    }
}
