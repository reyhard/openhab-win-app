using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Api;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using Windows.Graphics;

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
        this.Activated += OnFlyoutActivated;
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

    public void PopulateSitemaps(IReadOnlyList<SitemapInfo> sitemaps)
    {
        SitemapMenuFlyout.Items.Clear();
        var current = settingsController.Current.SitemapName;
        foreach (var s in sitemaps)
        {
            var item = new MenuFlyoutItem { Text = s.Label, Tag = s.Name };
            item.Click += SitemapMenuItem_Click;
            SitemapMenuFlyout.Items.Add(item);
        }
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
        // Sitemap selection is now reflected via the title; no ComboBox to update.
    }

    private void RefreshRuntimeBindings()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        BackButton.Visibility = runtimeController.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
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
            Func<Task>? activateRow = null;
            if (row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand)
                activateRow = () => OnRowActivatedAsync(rowIndex);
            else if (row.Action == RenderActionKind.Navigate)
                activateRow = () => OnRowNavigateAsync(rowIndex);
            Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
                ? cmd => runtimeController.SendCommandForRowAsync(rowIndex, cmd)
                : null;
            SitemapRows.Children.Add(SitemapControlFactory.Create(
                row,
                activateRow,
                sendCommand,
                settingsController.Current.LocalEndpoint,
                settingsController.Current.UseWindows11Icons));
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

    private async Task OnRowNavigateAsync(int rowIndex)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(async ct => await runtimeController.NavigateToChildAsync(rowIndex, ct));
    }

    private async void SitemapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            if (isRefreshing) return;
            settingsController.SetSitemapName(name);
            await LoadRuntimeAsync();
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

    private void NavigateBack_Click(object sender, RoutedEventArgs e)
    {
        if (!runtimeController.CanGoBack || isRefreshing) return;
        isRefreshing = true;
        try
        {
            runtimeController.NavigateBack();
            RefreshRuntimeBindings();
        }
        finally { isRefreshing = false; }
    }

    private void TitleText_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
    }

    private void OnFlyoutActivated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= OnFlyoutActivated;
        var appWindow = this.AppWindow;
        var currentPos = appWindow.Position;
        var startY = currentPos.Y + 60;
        appWindow.Move(new PointInt32(currentPos.X, startY));
        _ = AnimateSlideUpAsync(appWindow, currentPos.Y);
    }

    private static async Task AnimateSlideUpAsync(AppWindow window, int targetY)
    {
        var pos = window.Position;
        var startY = pos.Y;
        var steps = 8;
        var delay = TimeSpan.FromMilliseconds(12);
        for (int i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var eased = 1.0 - Math.Pow(1.0 - t, 3.0); // ease-out cubic
            var y = (int)(startY + (targetY - startY) * eased);
            window.Move(new PointInt32(pos.X, y));
            await Task.Delay(delay);
        }
    }
}
