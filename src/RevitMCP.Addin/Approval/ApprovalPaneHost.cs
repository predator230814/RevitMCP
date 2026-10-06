using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RevitMCP.Addin.Approval;

internal sealed class ApprovalPaneHost : UserControl, IApprovalPaneSurface
{
    private readonly ApprovalUiRuntime _runtime;
    private readonly Grid _root = new();
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
        FontSize = 14,
        Text = "No approval request is currently active.",
    };

    private WebView2? _webView;
    private CoreWebView2? _core;
    private int _initializeOnce;
    private int _disposed;
    private int _contentReady;

    public ApprovalPaneHost(ApprovalUiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _root.Children.Add(_status);
        Content = _root;
        Loaded += OnLoaded;
    }

    public void BeginAfterShow()
    {
        TryCreateWebView();
    }

    public void Post(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Post(json)));
            return;
        }

        if (Volatile.Read(ref _disposed) != 0 || _core is null || Volatile.Read(ref _contentReady) == 0)
        {
            return;
        }

        try
        {
            _core.PostWebMessageAsJson(json);
        }
        catch (Exception)
        {
            // A failed post cannot record an approval. The native session stays authoritative.
        }
    }

    public void DisposeSurface()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Loaded -= OnLoaded;
        DetachCore();
        if (_webView is not null)
        {
            _root.Children.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }

        _runtime.NotifyHostDisposed();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        TryCreateWebView();
    }

    private void TryCreateWebView()
    {
        if (!ApprovalWebViewLifecycle.ShouldCreate(
                _runtime.ShowRequested,
                IsLoaded,
                alreadyCreated: _webView is not null || Volatile.Read(ref _initializeOnce) != 0))
        {
            return;
        }

        if (Interlocked.Exchange(ref _initializeOnce, 1) != 0)
        {
            return;
        }

        WebView2 webView;
        try
        {
            webView = new WebView2();
        }
        catch (Exception)
        {
            ReleaseFailedWebView();
            return;
        }

        _webView = webView;
        _root.Children.Insert(0, webView);
        _ = InitializeAsync(webView);
    }

    private async Task InitializeAsync(WebView2 webView)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            string runtimeVersion;
            try
            {
                runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ReleaseFailedWebView();
                return;
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                ReleaseFailedWebView();
                return;
            }

            var assemblyDirectory = Path.GetDirectoryName(typeof(ApprovalPaneHost).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                ReleaseFailedWebView();
                return;
            }

            var uiFolder = Path.Combine(assemblyDirectory, "ui");
            if (!File.Exists(Path.Combine(uiFolder, "index.html")))
            {
                ReleaseFailedWebView();
                return;
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitMCP",
                "ApprovalUi",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (webView.CoreWebView2 is null)
            {
                ReleaseFailedWebView();
                return;
            }

            _core = webView.CoreWebView2;
            _core.Settings.AreHostObjectsAllowed = false;
            _core.Settings.IsWebMessageEnabled = true;
            _core.Settings.AreDefaultScriptDialogsEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.AreDevToolsEnabled = false;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigationStarting += OnNavigationStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.SetVirtualHostNameToFolderMapping(
                ApprovalContent.LocalHostName,
                uiFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.Navigate(ApprovalContent.LocalOrigin + "index.html");
        }
        catch (Exception)
        {
            ReleaseFailedWebView();
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || _core is null)
        {
            return;
        }

        if (!args.IsSuccess || !ApprovalOrigin.IsLocal(_core.Source))
        {
            ReleaseFailedWebView();
            return;
        }

        Volatile.Write(ref _contentReady, 1);
        _status.Visibility = Visibility.Collapsed;
        var current = _runtime.CurrentRenderJson;
        if (current is not null)
        {
            Post(current);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _contentReady) == 0)
        {
            return;
        }

        try
        {
            if (!ApprovalOrigin.IsLocal(args.Source))
            {
                return;
            }

            _runtime.ReceivePageMessage(args.WebMessageAsJson);
        }
        catch (Exception)
        {
            // Malformed host events are ignored. They cannot record a decision.
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!IsAllowedNavigation(args.Uri))
        {
            args.Cancel = true;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private static bool IsAllowedNavigation(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ApprovalOrigin.IsLocal(uri);
    }

    private void ReleaseFailedWebView()
    {
        DetachCore();
        if (_webView is not null)
        {
            _root.Children.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }

        Volatile.Write(ref _contentReady, 0);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _initializeOnce, 0);
        _status.Visibility = Visibility.Visible;
        _status.Text = "Approval review could not be opened.";
        _runtime.NotifyInitializationFailed();
    }

    private void DetachCore()
    {
        if (_core is null)
        {
            return;
        }

        _core.WebMessageReceived -= OnWebMessageReceived;
        _core.NavigationStarting -= OnNavigationStarting;
        _core.NewWindowRequested -= OnNewWindowRequested;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core = null;
    }
}
