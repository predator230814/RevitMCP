using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApprovalPresentationTests
{
    [Fact]
    public void Production_addin_uses_the_spike_webview2_version_matrix()
    {
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMCP.Addin.csproj"));

        Assert.Equal(ApprovalWebViewPackages.Revit2025, "1.0.2045.28");
        Assert.Equal(ApprovalWebViewPackages.Revit2026, "1.0.2478.35");
        Assert.Equal(ApprovalWebViewPackages.Revit2027, "1.0.2478.35");
        Assert.Contains("<WebView2PackageVersion>1.0.2045.28</WebView2PackageVersion>", PropertyGroup(project, "2025"), StringComparison.Ordinal);
        Assert.Contains("<WebView2PackageVersion>1.0.2478.35</WebView2PackageVersion>", PropertyGroup(project, "2026"), StringComparison.Ordinal);
        Assert.Contains("<WebView2PackageVersion>1.0.2478.35</WebView2PackageVersion>", PropertyGroup(project, "2027"), StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.2478.35", PropertyGroup(project, "2025"), StringComparison.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2\" VersionOverride=\"$(WebView2PackageVersion)\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.4258.31", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_addin_enables_wpf_and_targets_x64()
    {
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMCP.Addin.csproj"));

        Assert.Contains("<UseWPF>true</UseWPF>", project, StringComparison.Ordinal);
        Assert.Contains("<PlatformTarget>x64</PlatformTarget>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Dockable_pane_registration_failure_does_not_fail_startup()
    {
        var startup = Slice(ApplicationSource(), "public Result OnStartup", "public Result OnShutdown");
        var unavailable = startup.IndexOf("_approvalUi.MarkUnavailable()", StringComparison.Ordinal);
        var bootstrap = startup.IndexOf("BeginStartup(new RevitIdlingScheduler(application))", StringComparison.Ordinal);
        var failed = startup.IndexOf("return Result.Failed", StringComparison.Ordinal);

        Assert.True(unavailable >= 0 && unavailable < bootstrap && bootstrap < failed);
        Assert.Contains("RegisterDockablePane", startup, StringComparison.Ordinal);
        Assert.Contains("VisibleByDefault = false", PaneProviderSource(), StringComparison.Ordinal);
        Assert.Contains("DockPosition.Right", PaneProviderSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void WebView2_is_not_created_during_startup()
    {
        var startup = Slice(ApplicationSource(), "public Result OnStartup", "public Result OnShutdown");

        Assert.DoesNotContain("new WebView2", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCoreWebView2", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("new WebView2", PaneProviderSource(), StringComparison.Ordinal);
        Assert.DoesNotContain("new WebView2", Slice(HostSource(), "public ApprovalPaneHost", "public void BeginAfterShow"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_unrequested_pane_does_not_create_WebView2()
    {
        Assert.False(ApprovalWebViewLifecycle.ShouldCreate(showRequested: false, paneLoaded: true, alreadyCreated: false));
        Assert.False(ApprovalWebViewLifecycle.ShouldCreate(showRequested: false, paneLoaded: true, alreadyCreated: true));
        Assert.DoesNotContain("BeginAfterShow", PaneProviderSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_presentation_on_a_loaded_host_permits_initialization()
    {
        Assert.True(ApprovalWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: false));
        Assert.False(ApprovalWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: false, alreadyCreated: false));
        Assert.False(ApprovalWebViewLifecycle.ShouldCreate(showRequested: true, paneLoaded: true, alreadyCreated: true));

        var create = Slice(HostSource(), "private void TryCreateWebView", "private async Task InitializeAsync");
        var gate = create.IndexOf("ApprovalWebViewLifecycle.ShouldCreate", StringComparison.Ordinal);
        var constructed = create.IndexOf("new WebView2", StringComparison.Ordinal);
        Assert.True(gate >= 0 && gate < constructed);
    }

    [Fact]
    public async Task Initialization_failure_resets_for_a_later_explicit_retry()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        Assert.Equal(ApprovalReviewStatus.Started, started.Status);

        harness.Ui.NotifyInitializationFailed();

        Assert.False(harness.Ui.ShowRequested);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Contains("Interlocked.Exchange(ref _initializeOnce, 0)", HostSource(), StringComparison.Ordinal);
        Assert.Contains("_runtime.NotifyInitializationFailed()", HostSource(), StringComparison.Ordinal);

        harness.Gate.ShowSucceeds = true;
        var retried = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        Assert.Equal(ApprovalReviewStatus.Started, retried.Status);
        Assert.NotEqual(started.SessionRef, retried.SessionRef);
    }

    [Theory]
    [InlineData("https://revitmcp-approval.local/index.html", true)]
    [InlineData("https://revitmcp-approval.local/", true)]
    [InlineData("https://revitmcp-spike.local/index.html", false)]
    [InlineData("https://revitmcp-approval.local.example/index.html", false)]
    [InlineData("https://evil.revitmcp-approval.local/index.html", false)]
    [InlineData("http://revitmcp-approval.local/index.html", false)]
    [InlineData("https://user@revitmcp-approval.local/index.html", false)]
    [InlineData("https://revitmcp-approval.local:444/index.html", false)]
    [InlineData("about:blank", false)]
    public void Local_origin_accepts_only_the_production_virtual_host(string uri, bool accepted)
    {
        Assert.Equal(accepted, ApprovalOrigin.IsLocal(uri));
        Assert.Equal("revitmcp-approval.local", ApprovalContent.LocalHostName);
    }

    [Fact]
    public void External_navigation_is_rejected()
    {
        Assert.False(ApprovalOrigin.IsLocal("https://example.com/"));
        Assert.Contains("args.Cancel = true", HostSource(), StringComparison.Ordinal);
        Assert.Contains("ApprovalOrigin.IsLocal", HostSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void New_windows_are_blocked()
    {
        Assert.Contains("args.Handled = true", HostSource(), StringComparison.Ordinal);
        Assert.Contains("AreDevToolsEnabled = false", HostSource(), StringComparison.Ordinal);
        Assert.Contains("AreHostObjectsAllowed = false", HostSource(), StringComparison.Ordinal);
        Assert.Contains("AreDefaultScriptDialogsEnabled = false", HostSource(), StringComparison.Ordinal);
        Assert.Contains("CoreWebView2HostResourceAccessKind.DenyCors", HostSource(), StringComparison.Ordinal);
        Assert.DoesNotContain("AreDevToolsEnabled = true", HostSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_parser_accepts_approve_current_for_a_bounded_session()
    {
        var parsed = ApprovalMessageParser.Parse("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\"}");

        Assert.True(parsed.Accepted);
        Assert.Equal(ApprovalInboundKind.ApproveCurrent, parsed.Kind);
        Assert.Equal("session~1", parsed.SessionRef);
    }

    [Fact]
    public void Strict_parser_accepts_reject_current_for_a_bounded_session()
    {
        var parsed = ApprovalMessageParser.Parse("{\"type\":\"rejectCurrent\",\"sessionRef\":\"session~1\"}");

        Assert.True(parsed.Accepted);
        Assert.Equal(ApprovalInboundKind.RejectCurrent, parsed.Kind);
    }

    [Fact]
    public void Strict_parser_accepts_dismiss_current_for_a_bounded_session()
    {
        var parsed = ApprovalMessageParser.Parse("{\"type\":\"dismissCurrent\",\"sessionRef\":\"session~1\"}");

        Assert.True(parsed.Accepted);
        Assert.Equal(ApprovalInboundKind.DismissCurrent, parsed.Kind);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"type\":")]
    public void Malformed_json_is_rejected(string json)
    {
        Assert.False(ApprovalMessageParser.Parse(json).Accepted);
    }

    [Fact]
    public void Unknown_message_is_rejected()
    {
        var parsed = ApprovalMessageParser.Parse("{\"type\":\"ping\",\"sessionRef\":\"session~1\"}");

        Assert.False(parsed.Accepted);
        Assert.Equal("unknown", parsed.Reason);
    }

    [Theory]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\",\"confirm\":true}")]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\",\"intentRef\":\"intent\"}")]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\",\"documentId\":\"doc-a\"}")]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\",\"createdAt\":\"2026-09-25T12:00:00Z\"}")]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"session~1\",\"expiresAt\":\"2026-09-25T12:10:00Z\"}")]
    public void Extra_members_are_rejected(string json)
    {
        var parsed = ApprovalMessageParser.Parse(json);

        Assert.False(parsed.Accepted);
        Assert.Equal("malformed", parsed.Reason);
    }

    [Theory]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"\"}")]
    [InlineData("{\"type\":\"approveCurrent\",\"sessionRef\":\"   \"}")]
    [InlineData("{\"type\":\"approveCurrent\"}")]
    public void Blank_or_oversized_session_refs_are_rejected(string json)
    {
        Assert.False(ApprovalMessageParser.Parse(json).Accepted);
        Assert.False(ApprovalMessageParser.Parse("{\"type\":\"approveCurrent\",\"sessionRef\":\"" + new string('a', ApprovalMessageParser.MaxSessionRefLength + 1) + "\"}").Accepted);
    }

    [Fact]
    public async Task Stale_session_ref_is_rejected_before_controller_dispatch()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        var callsBefore = harness.Gate.DispatchEntries;

        var stale = harness.Ui.Dispatch("{\"type\":\"approveCurrent\",\"sessionRef\":\"other-session\"}", "doc-a");

        Assert.Equal(ApprovalUiDispatchKind.Rejected, stale.Kind);
        Assert.Equal(callsBefore, harness.Gate.DispatchEntries);
        Assert.Equal(ApprovalReviewStatus.AlreadyActive, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(started.SessionRef, harness.Ui.Current!.SessionRef);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Outbound_review_json_contains_the_session_and_authoritative_items()
    {
        var harness = Harness();
        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");
        var json = ApprovalReviewJson.SerializeRender(review.RenderModel!);

        Assert.Contains("\"sessionRef\":\"" + review.RenderModel!.SessionRef + "\"", json, StringComparison.Ordinal);
        Assert.Contains("\"elementName\":\"Wall 1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"categoryName\":\"Walls\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameterName\":\"Comments\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"string\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"proposed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"renderReview\"", json, StringComparison.Ordinal);
        Assert.Contains("\"createdAt\":", json, StringComparison.Ordinal);
        Assert.Contains("\"expiresAt\":", json, StringComparison.Ordinal);
        Assert.Contains(review.RenderModel.CreatedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.Contains(review.RenderModel.ExpiresAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("intentRef")]
    [InlineData("intent_ref")]
    [InlineData("intentFingerprint")]
    [InlineData("intent_fingerprint")]
    [InlineData("instanceId")]
    [InlineData("instance_id")]
    [InlineData("documentId")]
    [InlineData("document_id")]
    public void Outbound_review_json_omits_authority_fields(string fieldName)
    {
        var harness = Harness();
        var review = harness.Controller.BeginReview(harness.IntentRef, "doc-a");
        var json = ApprovalReviewJson.SerializeRender(review.RenderModel!);

        Assert.DoesNotMatch(new Regex("\"" + fieldName + "\"\\s*:"), json);
    }

    [Fact]
    public void Presentation_uses_non_creating_active_document_lookup()
    {
        var source = AdapterSource();

        Assert.Contains("ActiveUIDocument", source, StringComparison.Ordinal);
        Assert.Contains(".TryGet(", source, StringComparison.Ordinal);
        Assert.Contains("ResolveActiveDocumentId", source, StringComparison.Ordinal);
        Assert.Equal(2, Count(source, "ResolveActiveDocumentId(application)"));
        Assert.Contains("EnqueueAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_does_not_call_GetId_or_GetOrAssign()
    {
        var source = AdapterSource();

        Assert.DoesNotContain("GetOrAssign", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetId(", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_and_reject_resolve_the_active_document_at_click_time()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        harness.Gate.DocumentId = "doc-b";

        var wrongDocument = await harness.Adapter.DispatchCurrentAsync(
            "{\"type\":\"approveCurrent\",\"sessionRef\":\"" + started.SessionRef + "\"}",
            CancellationToken.None);

        Assert.Equal(ApprovalUiDispatchKind.Unavailable, wrongDocument.Kind);
        Assert.False(wrongDocument.ReviewRemains);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(1, harness.Gate.DispatchEntries);
        Assert.Equal(ApprovalReviewStatus.Started, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);

        var rejection = Harness();
        var rejectedSession = await rejection.Adapter.PresentReviewAsync(rejection.IntentRef, CancellationToken.None);
        rejection.Gate.DocumentId = "doc-b";
        var rejected = await rejection.Adapter.DispatchCurrentAsync(
            "{\"type\":\"rejectCurrent\",\"sessionRef\":\"" + rejectedSession.SessionRef + "\"}",
            CancellationToken.None);

        Assert.Equal(ApprovalUiDispatchKind.Unavailable, rejected.Kind);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, rejection.Provider.TryConsumeApproved(rejection.IntentRef).Status);
        Assert.Equal(ApprovalReviewStatus.Started, rejection.Controller.BeginReview(rejection.IntentRef, "doc-a").Status);
        Assert.Contains("DispatchAsync", AdapterSource(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Presentation_failure_dismisses_the_pending_session_without_a_decision()
    {
        var harness = Harness();
        harness.Gate.ShowSucceeds = false;

        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        Assert.Equal(ApprovalReviewStatus.Unavailable, presented.Status);
        Assert.Null(harness.Ui.Current);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent("session~1", "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task WebView_failure_dismisses_the_pending_session_without_a_decision()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Ui.NotifyInitializationFailed();

        Assert.Null(harness.Ui.Current);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task Host_disposal_dismisses_the_current_pending_session()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Ui.NotifyHostDisposed();

        Assert.Null(harness.Ui.Current);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent(started.SessionRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        harness.Ui.RegisterSurface(new RecordingSurface());
        var retry = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        Assert.Equal(ApprovalReviewStatus.Started, retry.Status);
        Assert.NotEqual(started.SessionRef, retry.SessionRef);
    }

    [Fact]
    public async Task Duplicate_ui_action_cannot_create_two_terminal_decisions()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        var json = "{\"type\":\"approveCurrent\",\"sessionRef\":\"" + started.SessionRef + "\"}";

        var first = harness.Ui.Dispatch(json, "doc-a");
        var second = harness.Ui.Dispatch(json, "doc-a");

        Assert.Equal(ApprovalUiDispatchKind.Recorded, first.Kind);
        Assert.Equal(ApprovalUiDispatchKind.Rejected, second.Kind);
        Assert.Equal(ApprovalConsumeStatus.Consumed, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public void Ui_presentation_path_cannot_consume_approval()
    {
        foreach (var relative in new[]
        {
            Path.Combine("src", "RevitMCP.Addin", "Approval", "ApprovalUiRuntime.cs"),
            Path.Combine("src", "RevitMCP.Addin", "Approval", "RevitLocalApprovalPresentationAdapter.cs"),
            Path.Combine("src", "RevitMCP.Addin", "Approval", "ApprovalPaneHost.cs"),
            Path.Combine("src", "RevitMCP.Addin", "Approval", "ApprovalMessages.cs"),
            Path.Combine("src", "RevitMCP.Addin", "Approval", "Ui", "app.js"),
        })
        {
            Assert.DoesNotContain("TryConsumeApproved", File.ReadAllText(Path.Combine(RepoRoot(), relative)), StringComparison.Ordinal);
        }

        foreach (var type in new[] { typeof(ApprovalUiRuntime), typeof(RevitLocalApprovalPresentationAdapter) })
        {
            Assert.DoesNotContain(
                type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                method => method.Name.Contains("Consume", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Read_and_preview_construction_does_not_depend_on_webview_readiness()
    {
        var preview = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitPreviewParameterUpdatesService.cs"));
        var context = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitGetContextService.cs"));
        var startup = Slice(ApplicationSource(), "public Result OnStartup", "public Result OnShutdown");

        Assert.DoesNotContain("WebView2", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovalUiRuntime", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView2", context, StringComparison.Ordinal);
        Assert.Contains("BeginStartup(new RevitIdlingScheduler(application))", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView2", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_runtime_and_presentation_state_keep_no_revit_wrappers()
    {
        foreach (var type in new[]
        {
            typeof(ApprovalUiRuntime),
            typeof(RevitLocalApprovalPresentationAdapter),
            typeof(ApprovalInboundParse),
            typeof(ApprovalUiDispatchResult),
            typeof(ApprovalPresentationResult),
        })
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var name = field.FieldType.FullName ?? field.FieldType.Name;
                Assert.DoesNotContain("Autodesk.Revit.DB.Document", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Autodesk.Revit.DB.Element", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Autodesk.Revit.DB.Parameter", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Autodesk.Revit.UI.UIApplication", name, StringComparison.Ordinal);
            }
        }

        var gate = AdapterSource();
        Assert.DoesNotContain("UIApplication _", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Document _", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundled_page_is_local_and_has_no_network_or_storage_authority()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "Ui", "index.html"));
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "Ui", "app.js"));
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMCP.Addin.csproj"));

        Assert.Contains("Review proposed Revit changes", html, StringComparison.Ordinal);
        Assert.Contains("No approval request is currently active.", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.Ordinal);
        Assert.Contains("id=\"created-at\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"expires-at\"", html, StringComparison.Ordinal);
        Assert.Contains("message.createdAt", script, StringComparison.Ordinal);
        Assert.Contains("message.expiresAt", script, StringComparison.Ordinal);
        Assert.Contains("clearMetadata()", script, StringComparison.Ordinal);
        Assert.Contains("postMessage({ type: type, sessionRef: sessionRef })", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now", script, StringComparison.Ordinal);
        Assert.DoesNotContain("new Date", script, StringComparison.Ordinal);
        Assert.Contains("ui\\index.html", project, StringComparison.Ordinal);
        Assert.Contains("WebView2Loader.dll", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2.Core.dll", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Web.WebView2.Wpf.dll", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shutdown_rejects_a_later_ui_message_without_a_decision()
    {
        var harness = Harness();
        var started = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);
        harness.Ui.Shutdown();

        var stale = harness.Ui.Dispatch(
            "{\"type\":\"approveCurrent\",\"sessionRef\":\"" + started.SessionRef + "\"}",
            "doc-a");

        Assert.Equal(ApprovalUiDispatchKind.Unavailable, stale.Kind);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    private static HarnessState Harness()
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes(4));
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        var controller = new RevitLocalApprovalInteractionController(store, provider);
        var ui = new ApprovalUiRuntime();
        var gate = new ScriptedGate();
        var adapter = new RevitLocalApprovalPresentationAdapter(controller, ui, gate);
        ui.Attach(adapter, controller);
        ui.RegisterSurface(new RecordingSurface());
        var created = store.TryCreate(Draft());
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        return new HarnessState(store, provider, controller, ui, adapter, gate, created.IntentRef!);
    }

    private static IntentDraft Draft()
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items = new List<IntentItemDraft>
            {
                new()
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:42",
                    Status = "ok",
                    ElementName = "Wall 1",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    BeforeHasValue = false,
                    Proposed = new IntentTypedValue.StringValue("proposed"),
                },
            },
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private static string ApplicationSource() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMcpApplication.cs"));

    private static string HostSource() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "ApprovalPaneHost.cs"));

    private static string PaneProviderSource() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "ApprovalPaneProvider.cs"));

    private static string AdapterSource() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "RevitLocalApprovalPresentationAdapter.cs"));

    private static string PropertyGroup(string project, string year)
    {
        var marker = "<PropertyGroup Condition=\"'$(RevitVersion)' == '" + year + "'\">";
        var start = project.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = project.IndexOf("</PropertyGroup>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return project.Substring(start, end - start);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    [Fact]
    public async Task Show_that_invalidates_initialization_fails_closed_without_a_decision()
    {
        var harness = Harness();
        harness.Gate.DuringShow = harness.Ui.NotifyInitializationFailed;

        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        Assert.Equal(ApprovalReviewStatus.Unavailable, presented.Status);
        Assert.Null(harness.Ui.Current);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent("session~1", "doc-a").Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.RejectCurrent("session~1", "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    private sealed class RecordingSurface : IApprovalPaneSurface
    {
        public void BeginAfterShow()
        {
        }

        public void Post(string json)
        {
        }

        public void DisposeSurface()
        {
        }
    }

    private sealed class ScriptedGate : IApprovalRevitGate
    {
        public string? DocumentId { get; set; } = "doc-a";

        public bool ShowSucceeds { get; set; } = true;

        public Action? DuringShow { get; set; }

        public int PresentEntries { get; private set; }

        public int DispatchEntries { get; private set; }

        public Task<ApprovalPresentationResult> InvokeAsync(
            CancellationToken cancellationToken,
            Func<string?, Func<bool>, ApprovalPresentationResult> present)
        {
            PresentEntries++;
            return Task.FromResult(present(DocumentId, () =>
            {
                DuringShow?.Invoke();
                return ShowSucceeds;
            }));
        }

        public Task<ApprovalUiDispatchResult> DispatchAsync(
            CancellationToken cancellationToken,
            Func<string?, ApprovalUiDispatchResult> dispatch)
        {
            DispatchEntries++;
            return Task.FromResult(dispatch(DocumentId));
        }
    }

    private sealed record HarnessState(
        EphemeralWriteIntentStore Store,
        RevitLocalApprovalProviderStateMachine Provider,
        RevitLocalApprovalInteractionController Controller,
        ApprovalUiRuntime Ui,
        RevitLocalApprovalPresentationAdapter Adapter,
        ScriptedGate Gate,
        string IntentRef);
}
