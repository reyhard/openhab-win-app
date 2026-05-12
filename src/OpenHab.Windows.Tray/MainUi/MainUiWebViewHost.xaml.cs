using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OpenHab.App.MainUi;
using OpenHab.Core;

namespace OpenHab.Windows.Tray.MainUi;

public sealed partial class MainUiWebViewHost : UserControl
{
    private Uri? currentEndpoint;
    private Uri? currentBaseUri;
    private Uri? currentUri;
    private string pendingRoute = "/";
    private bool initialized;

    public MainUiWebViewHost()
    {
        InitializeComponent();
    }

    public bool CanGoBack => MainWebView.CanGoBack;

    public async Task NavigateAsync(Uri endpoint, string? route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        currentEndpoint = endpoint;
        currentUri = MainUiUrlBuilder.Build(endpoint, route);
        currentBaseUri = MainUiUrlBuilder.Build(endpoint, "/");
        pendingRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();

        ShowLoading();

        try
        {
            await EnsureInitializedAsync();
            cancellationToken.ThrowIfCancellationRequested();
            MainWebView.Source = currentUri;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main UI WebView initialization failed: {ex.GetType().Name}: {ex.Message}");
            ShowError("Main UI could not be loaded. WebView2 may be unavailable.");
        }
    }

    public void GoBack()
    {
        if (MainWebView.CanGoBack)
        {
            MainWebView.GoBack();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        await MainWebView.EnsureCoreWebView2Async();
        MainWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        MainWebView.CoreWebView2.Settings.IsScriptEnabled = true;
        MainWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        MainWebView.NavigationStarting += MainWebView_NavigationStarting;
        MainWebView.NavigationCompleted += MainWebView_NavigationCompleted;
        initialized = true;
    }

    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (currentBaseUri is not null && MainUiUrlBuilder.IsSameHost(currentBaseUri, uri))
        {
            currentUri = uri;
            MainWebView.Source = uri;
            return;
        }

        OpenExternal(uri);
    }

    private void MainWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || currentBaseUri is null)
        {
            return;
        }

        if (!MainUiUrlBuilder.IsSameHost(currentBaseUri, uri))
        {
            args.Cancel = true;
            OpenExternal(uri);
            return;
        }

        currentUri = uri;
    }

    private void MainWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            currentUri = MainWebView.Source;
            MainWebView.Visibility = Visibility.Visible;
            LoadingView.Visibility = Visibility.Collapsed;
            ErrorView.Visibility = Visibility.Collapsed;
            return;
        }

        DiagnosticLogger.Warn($"Main UI navigation failed: webError={args.WebErrorStatus}");
        ShowError("Check the configured endpoint and credentials, then retry.");
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentEndpoint is null)
        {
            return;
        }

        try
        {
            await NavigateAsync(currentEndpoint, pendingRoute);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Main UI retry failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentUri is not null)
        {
            OpenExternal(currentUri);
        }
    }

    private static void OpenExternal(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLogger.Warn("Open external URL blocked: unsupported URI scheme.");
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Open external URL failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowLoading()
    {
        LoadingView.Visibility = Visibility.Visible;
        ErrorView.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorMessageText.Text = message;
        MainWebView.Visibility = Visibility.Collapsed;
        LoadingView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Visible;
    }
}
