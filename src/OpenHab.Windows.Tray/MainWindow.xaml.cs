using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private bool isRefreshing;

    public MainWindow(AppSettingsController settingsController, SitemapRenderController renderController)
    {
        this.settingsController = settingsController;
        this.renderController = renderController;

        InitializeComponent();
        InitializeSettingsControls();
        Refresh();
    }

    public void Refresh()
    {
        isRefreshing = true;
        try
        {
            var descriptor = renderController.BuildCurrentDescriptor();
            TitleText.Text = descriptor.Title;
            SitemapRows.Children.Clear();

            foreach (var row in descriptor.Rows)
            {
                SitemapRows.Children.Add(SitemapControlFactory.Create(row));
            }

            SkinCombo.SelectedItem = settingsController.Current.Skin;
            EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
            LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
            CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
    }

    private void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || SkinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        Refresh();
    }

    private void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || EndpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
        Refresh();
    }

    private void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            || !Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            Refresh();
            return;
        }

        try
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
        }
        catch (ArgumentException)
        {
            Refresh();
        }
    }
}
