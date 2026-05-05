using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private bool isRefreshing;

    public MainWindow(AppSettingsController settingsController, SitemapRuntimeController runtimeController)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;

        InitializeComponent();
        InitializeSettingsControls();
        RefreshSettingsBindings();
        _ = LoadRuntimeAsync();
    }

    public async Task LoadRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.LoadAsync(ct));
    }

    public async Task RefreshRuntimeAsync()
    {
        await RunRuntimeOperationAsync(ct => runtimeController.RefreshAsync(ct));
    }

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
    }

    private async Task RunRuntimeOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            await operation(CancellationToken.None);
            RefreshRuntimeBindings();
            RefreshSettingsBindings();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void RefreshSettingsBindings()
    {
        SkinCombo.SelectedItem = settingsController.Current.Skin;
        EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
        LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
        CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();
        SitemapNameText.Text = settingsController.Current.SitemapName;
    }

    private void RefreshRuntimeBindings()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        SitemapRows.Children.Clear();

        var rows = snapshot.Descriptor?.Rows;
        if (rows is null)
        {
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var rowIndex = index;
            SitemapRows.Children.Add(SitemapControlFactory.Create(
                rows[index],
                () => OnRowActivatedAsync(rowIndex)));
        }
    }

    private async Task OnRowActivatedAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct =>
        {
            await runtimeController.ActivateRowAsync(rowIndex, ct);
        });
    }

    private async void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || SkinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        await RefreshRuntimeAsync();
    }

    private async void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || EndpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
        await RefreshRuntimeAsync();
    }

    private async void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            || !Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            RefreshSettingsBindings();
            return;
        }

        try
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
            await RefreshRuntimeAsync();
        }
        catch (ArgumentException)
        {
            RefreshSettingsBindings();
        }
    }

    private async void SitemapNameText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        try
        {
            settingsController.SetSitemapName(SitemapNameText.Text);
            await LoadRuntimeAsync();
        }
        catch (ArgumentException)
        {
            RefreshSettingsBindings();
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadRuntimeAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRuntimeAsync();
    }
}
