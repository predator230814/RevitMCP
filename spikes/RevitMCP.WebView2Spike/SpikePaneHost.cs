using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RevitMCP.WebView2Spike.Diagnostics;
using RevitMCP.WebView2Spike.Messaging;

namespace RevitMCP.WebView2Spike;

internal sealed class SpikePaneHost : UserControl
{
    private readonly Grid _root = new();
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
        FontSize = 14,
    };

    private readonly string _revitVersionNumber;
    private readonly string _revitBuild;
    private WebView2? _webView;
    private CoreWebView2? _core;
    private int _initializeOnce;
    private int _disposed;
    private string _lastHostToUi = "not-sent";
    private string _lastUiToHost = "not-received";

    public SpikePaneHost(string revitVersionNumber, string revitBuild)
    {
        _revitVersionNumber = revitVersionNumber;
        _revitBuild = revitBuild;
        _root.Children.Add(_status);
        Content = _root;
        _status.Text = "WebView2 is not created until this pane is shown.";
        Loaded += OnLoaded;
    }

    public void BeginAfterShow()
    {
        TryCreateWebView();
    }

    public void DisposeHost()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Loaded -= OnLoaded;
        DetachCore();
        _webView?.Dispose();
        _webView = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        TryCreateWebView();
    }

    private void TryCreateWebView()
    {
        if (!SpikeWebViewLifecycle.ShouldCreate(
                SpikePaneLifetime.ShowRequested,
                IsLoaded,
                alreadyCreated: _webView is not null || Volatile.Read(ref _initializeOnce) != 0))
        {
            return;
        }

        if (Interlocked.Exchange(ref _initializeOnce, 1) != 0)
        {
            return;
        }

        var webView = new WebView2();
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
                ShowFailure("The Evergreen WebView2 Runtime is not installed. This spike does not download or install it.");
                return;
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                ShowFailure("The Evergreen WebView2 Runtime reported an empty version. This spike does not download or install it.");
                return;
            }

            var assemblyDirectory = Path.GetDirectoryName(typeof(SpikePaneHost).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                ShowFailure("Local UI assets were not found next to the spike assembly.");
                return;
            }

            var uiFolder = Path.Combine(assemblyDirectory, "ui");
            if (!File.Exists(Path.Combine(uiFolder, "index.html")))
            {
                ShowFailure("Local UI assets were not found next to the spike assembly.");
                return;
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitMCP",
                "WebView2Spike",
                SpikeBuild.RevitYear);
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);
            if (Volatile.Read(ref _disposed) != 0 || webView.CoreWebView2 is null)
            {
                return;
            }

            _core = webView.CoreWebView2;
            _core.Settings.AreHostObjectsAllowed = false;
            _core.Settings.IsWebMessageEnabled = true;
            _core.Settings.AreDefaultScriptDialogsEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.AreDevToolsEnabled = true;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigationStarting += OnNavigationStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.SetVirtualHostNameToFolderMapping(
                SpikeContent.LocalHostName,
                uiFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.Navigate(SpikeContent.LocalOrigin + "index.html");
            _status.Text = "Loading local content from " + SpikeContent.LocalOrigin;
        }
        catch (Exception ex)
        {
            ShowFailure("WebView2 initialization failed: " + ex.GetType().Name + ". Revit was not crashed and the model was not modified.");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || _core is null)
        {
            return;
        }

        if (!args.IsSuccess || !SpikeOrigin.IsLocal(_core.Source))
        {
            ShowFailure("Local content navigation failed. The spike did not leave the expected origin.");
            return;
        }

        _status.Visibility = Visibility.Collapsed;
        PostToPage(SpikeOutbound.HostReady(CreateHostReady(initialized: true, runtimeVersion: SafeRuntimeVersion())));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (!SpikeOrigin.IsLocal(args.Source))
            {
                _lastUiToHost = "rejected:origin";
                return;
            }

            var result = SpikeMessageParser.Parse(args.WebMessageAsJson);
            if (!result.Accepted)
            {
                _lastUiToHost = "rejected:" + result.Rejection;
                PostToPage(SpikeOutbound.Rejected(result.Rejection));
                return;
            }

            _lastUiToHost = "ping";
            PostToPage(SpikeOutbound.Pong(_lastHostToUi, _lastUiToHost));
        }
        catch (Exception)
        {
            _lastUiToHost = "rejected:malformed";
            PostToPage(SpikeOutbound.Rejected("malformed"));
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

    private void PostToPage(string json)
    {
        if (Volatile.Read(ref _disposed) != 0 || _core is null)
        {
            _lastHostToUi = "failed";
            return;
        }

        try
        {
            _core.PostWebMessageAsJson(json);
            _lastHostToUi = "sent";
        }
        catch (Exception)
        {
            _lastHostToUi = "failed";
        }
    }

    private HostReadyMessage CreateHostReady(bool initialized, string runtimeVersion)
    {
        var assemblies = WebView2AssemblyReport.LoadedAssemblies();
        return new HostReadyMessage
        {
            RevitVersion = _revitVersionNumber,
            RevitBuild = _revitBuild,
            DotnetRuntime = RuntimeInformation.FrameworkDescription,
            WebView2SdkVersion = WebView2AssemblyReport.VersionOf("Microsoft.Web.WebView2.Core"),
            WebView2WpfVersion = WebView2AssemblyReport.VersionOf("Microsoft.Web.WebView2.Wpf"),
            WebView2RuntimeVersion = runtimeVersion,
            CoreWebView2Initialized = initialized,
            ContentOrigin = SpikeContent.LocalOrigin,
            LastHostToUi = _lastHostToUi,
            LastUiToHost = _lastUiToHost,
            Assemblies = assemblies,
        };
    }

    private static string SafeRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return "";
        }
    }

    private static bool IsAllowedNavigation(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SpikeOrigin.IsLocal(uri);
    }

    private void ShowFailure(string message)
    {
        if (_webView is not null)
        {
            _webView.Visibility = Visibility.Collapsed;
        }

        _status.Visibility = Visibility.Visible;
        _status.Text = message;
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
