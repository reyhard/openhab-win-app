using Microsoft.UI.Xaml;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class FlyoutWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRuntimeController runtimeController;
    private readonly Action requestOpenMainWindow;
    private bool isRefreshing;

    public FlyoutWindow(
        AppSettingsController settingsController,
        SitemapRuntimeController runtimeController,
        Action requestOpenMainWindow)
    {
        this.settingsController = settingsController;
        this.runtimeController = runtimeController;
        this.requestOpenMainWindow = requestOpenMainWindow;

        InitializeComponent();
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
            var row = rows[index];
            Func<Task>? activateRow = row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand
                ? () => OnRowActivatedAsync(rowIndex)
                : null;
            SitemapRows.Children.Add(SitemapControlFactory.Create(row, activateRow));
        }
    }

    private async Task OnRowActivatedAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct => await runtimeController.ActivateRowAsync(rowIndex, ct));
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRuntimeAsync();
    }

    private void OpenAppButton_Click(object sender, RoutedEventArgs e)
    {
        requestOpenMainWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        requestOpenMainWindow();
    }
}
