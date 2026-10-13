using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BimIt.Revit.Addin.Assistant;

internal sealed class AssistantPaneHost : UserControl
{
    private readonly AssistantShell _shell;
    private readonly Grid _root = new();
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
        FontSize = 14,
        Text = "Open BIM-IT Assistant from the ribbon to start the local assistant.",
    };

    private WebView2? _webView;
    private CoreWebView2? _core;
    private int _initializeOnce;
    private int _disposed;
    private int _contentReady;
    private bool _openRequested;

    public AssistantPaneHost(AssistantShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _root.Children.Add(_status);
        Content = _root;
        Loaded += OnLoaded;
    }

    public bool WebViewCreated => _webView is not null;

    public void NotifyOpened()
    {
        _openRequested = true;
        TryCreateWebView();
    }

    public void Post(string json)
    {
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
            // A failed post cannot reach Revit or the approval authority.
        }
    }

    public void DisposeSurface()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Loaded -= OnLoaded;
        Detach();
        if (_webView is not null)
        {
            _root.Children.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        TryCreateWebView();
    }

    private void TryCreateWebView()
    {
        if (!AssistantWebViewLifecycle.ShouldCreate(_openRequested, IsLoaded, _webView is not null || Volatile.Read(ref _initializeOnce) != 0))
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
            ReleaseFailed();
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
                ReleaseFailed();
                return;
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                ReleaseFailed();
                return;
            }

            var assemblyDirectory = Path.GetDirectoryName(typeof(AssistantPaneHost).Assembly.Location);
            var uiFolder = assemblyDirectory is null ? null : Path.Combine(assemblyDirectory, "assistant-ui");
            if (uiFolder is null || !File.Exists(Path.Combine(uiFolder, "index.html")))
            {
                ReleaseFailed();
                return;
            }

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitMCP",
                "AssistantUi",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userData);
            await webView.EnsureCoreWebView2Async(environment);
            if (Volatile.Read(ref _disposed) != 0 || webView.CoreWebView2 is null)
            {
                ReleaseFailed();
                return;
            }

            _core = webView.CoreWebView2;
            _core.Settings.AreHostObjectsAllowed = AssistantWebViewSecurity.HostObjectsAllowed;
            _core.Settings.IsWebMessageEnabled = AssistantWebViewSecurity.WebMessagesEnabled;
            _core.Settings.AreDefaultScriptDialogsEnabled = AssistantWebViewSecurity.ScriptDialogsEnabled;
            _core.Settings.AreDevToolsEnabled = AssistantWebViewSecurity.DevToolsEnabled;
            _core.Settings.IsStatusBarEnabled = false;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigationStarting += OnNavigationStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.SetVirtualHostNameToFolderMapping(
                AssistantContent.LocalHostName,
                uiFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.Navigate(AssistantContent.IndexUri);
        }
        catch (Exception)
        {
            ReleaseFailed();
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || _core is null || !args.IsSuccess || !AssistantOrigin.IsLocal(_core.Source))
        {
            ReleaseFailed();
            return;
        }

        Volatile.Write(ref _contentReady, 1);
        _status.Visibility = Visibility.Collapsed;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _contentReady) == 0 || !AssistantOrigin.IsLocal(args.Source))
        {
            return;
        }

        try
        {
            _shell.ReceivePageMessage(args.WebMessageAsJson);
        }
        catch (Exception)
        {
            // Malformed page events are ignored. They cannot approve a write.
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!AssistantNavigationPolicy.IsAllowed(args.Uri))
        {
            args.Cancel = true;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (AssistantWindowPolicy.BlockNewWindow)
        {
            args.Handled = true;
        }
    }

    private void ReleaseFailed()
    {
        Detach();
        if (_webView is not null)
        {
            _root.Children.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }

        Volatile.Write(ref _contentReady, 0);
        _status.Text = "BIM-IT Assistant could not open its local view.";
        _status.Visibility = Visibility.Visible;
    }

    private void Detach()
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
