using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Assistant;
using BimIt.Revit.Addin.Lifecycle;
using BimIt.Revit.Assistant.Ipc;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class AssistantShellTests
{
    [Fact]
    public void Ribbon_registration_does_not_start_the_assistant()
    {
        Assert.False(AssistantRibbon.StartsAssistantWhenRegistered);
        Assert.Equal("BIM-IT", AssistantRibbon.TabName);
        Assert.Equal("BIM-IT\nAssistant", AssistantRibbon.ButtonText);
        Assert.NotEqual(ApprovalPaneIds.PaneId, AssistantPaneIds.PaneId);
        Assert.Equal(new Guid("C3A91E74-5D28-4B6F-A0E1-9F4C2B7D18A5"), ApprovalPaneIds.PaneId);
        Assert.Equal("BIM-IT Revit Approval", ApprovalPaneIds.Title);
        Assert.Equal(new Guid("7B6E1D42-9A38-4C15-8F60-2D4A9E0C5B17"), AssistantPaneIds.PaneId);

        var ribbon = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "AssistantRibbonRegistration.cs"));
        Assert.Contains("AssistantRibbon.StartsAssistantWhenRegistered", ribbon, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", ribbon, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureStarted", ribbon, StringComparison.Ordinal);
    }

    [Fact]
    public void Revit_startup_does_not_start_the_assistant_process_or_webview()
    {
        var factory = new RecordingProcessFactory();
        var shell = CreateShell("instance-a", factory, new ScriptedLink());
        shell.OnRevitStartup();

        Assert.Equal(0, shell.Processes.StartCount);
        Assert.Equal(AssistantProcessPhase.NotStarted, shell.Processes.Phase);
        Assert.False(shell.OpenRequested);
        Assert.False(AssistantWebViewLifecycle.ShouldCreate(openRequested: false, paneLoaded: true, alreadyCreated: false));
    }

    [Fact]
    public void WebView_is_created_only_after_an_explicit_open()
    {
        Assert.False(AssistantWebViewLifecycle.ShouldCreate(false, true, false));
        Assert.False(AssistantWebViewLifecycle.ShouldCreate(true, false, false));
        Assert.False(AssistantWebViewLifecycle.ShouldCreate(true, true, true));
        Assert.True(AssistantWebViewLifecycle.ShouldCreate(true, true, false));

        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "AssistantPaneHost.cs"));
        var constructor = Slice(host, "public AssistantPaneHost", "public bool WebViewCreated");
        Assert.DoesNotContain("new WebView2", constructor, StringComparison.Ordinal);
        Assert.Contains("AssistantWebViewLifecycle.ShouldCreate", host, StringComparison.Ordinal);
        Assert.Contains("AssistantWebViewSecurity.DevToolsEnabled", host, StringComparison.Ordinal);
        Assert.Contains("AssistantWindowPolicy.BlockNewWindow", host, StringComparison.Ordinal);
        Assert.Contains("args.Cancel = true", host, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Assistant_origin_is_local_and_distinct_from_approval()
    {
        Assert.Equal("https://revitmcp-approval.local/", ApprovalContent.LocalOrigin);
        Assert.NotEqual(ApprovalContent.LocalHostName, AssistantContent.LocalHostName);
        Assert.True(AssistantOrigin.IsLocal("https://bimit-revit-assistant.local/index.html"));
        Assert.False(AssistantOrigin.IsLocal("https://revitmcp-approval.local/index.html"));
        Assert.False(AssistantOrigin.IsLocal("http://bimit-revit-assistant.local/"));
        Assert.False(AssistantOrigin.IsLocal("https://user@bimit-revit-assistant.local/"));
        Assert.False(AssistantOrigin.IsLocal("https://bimit-revit-assistant.local:444/"));
        Assert.False(AssistantOrigin.IsLocal("https://example.com/"));
        Assert.True(AssistantNavigationPolicy.IsAllowed("about:blank"));
        Assert.True(AssistantNavigationPolicy.IsAllowed(AssistantContent.IndexUri));
        Assert.False(AssistantNavigationPolicy.IsAllowed("https://example.com/"));
        Assert.True(AssistantWindowPolicy.BlockNewWindow);
        Assert.False(AssistantWebViewSecurity.HostObjectsAllowed);
        Assert.True(AssistantWebViewSecurity.WebMessagesEnabled);
        Assert.False(AssistantWebViewSecurity.ScriptDialogsEnabled);
        Assert.False(AssistantWebViewSecurity.DevToolsEnabled);
    }

    [Theory]
    [InlineData("""{"type":"approveIntent"}""")]
    [InlineData("""{"type":"callTool","name":"revit_apply_parameter_updates"}""")]
    [InlineData("""{"type":"executeRevitApi"}""")]
    [InlineData("""{"type":"invokeMethod"}""")]
    [InlineData("""{"type":"runCSharp"}""")]
    [InlineData("""{"type":"runPython"}""")]
    [InlineData("""{"type":"setParameter"}""")]
    [InlineData("""{"type":"executeShell"}""")]
    [InlineData("""{"type":"openFile"}""")]
    [InlineData("""{"type":"httpRequest"}""")]
    [InlineData("""{"type":"pageReady","extra":true}""")]
    [InlineData("""{"type":"runQuickAction","action":"inspectSelection"}""")]
    [InlineData("""{"type":"sendCommand"}""")]
    [InlineData("""{"type":"sendCommand","text":1}""")]
    [InlineData("")]
    [InlineData("{")]
    public void Page_messages_reject_unknown_or_approval_commands(string json)
    {
        var parsed = AssistantPageMessages.Parse(json);
        Assert.False(parsed.Accepted);
        Assert.False(AssistantPageMessages.GrantsApprovalAuthority);
    }

    [Fact]
    public void Page_messages_accept_only_the_closed_intents()
    {
        Assert.Equal(AssistantPageIntent.PageReady, AssistantPageMessages.Parse("""{"type":"pageReady"}""").Intent);
        Assert.Equal("context", AssistantPageMessages.Parse("""{"type":"runQuickAction","action":"context"}""").Action);
        Assert.Equal("warnings", AssistantPageMessages.Parse("""{"type":"runQuickAction","action":"warnings"}""").Action);
        Assert.Equal("activeView", AssistantPageMessages.Parse("""{"type":"runQuickAction","action":"activeView"}""").Action);
        Assert.Equal("/context", AssistantPageMessages.Parse("""{"type":"sendCommand","text":"/context"}""").Text);
        Assert.Equal(AssistantPageIntent.NewConversation, AssistantPageMessages.Parse("""{"type":"newConversation"}""").Intent);
        Assert.Equal(AssistantPageIntent.OpenDiagnostics, AssistantPageMessages.Parse("""{"type":"openDiagnostics"}""").Intent);
        Assert.Equal(AssistantPageIntent.RetryConnection, AssistantPageMessages.Parse("""{"type":"retryConnection"}""").Intent);
        Assert.False(AssistantPageMessages.Parse(new string('a', 8_001)).Accepted);
    }

    [Fact]
    public void Startup_failure_and_retry_do_not_fault_the_addin_lifecycle()
    {
        var events = new List<string>();
        var coordinator = new AddinLifecycleCoordinator(
            new RecordingDispatcherFactory(events),
            new RecordingBridgeFactory(events),
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
        coordinator.Prepare(new RecordingBootstrapSubscription());
        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.55"));
        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);

        var factory = new RecordingProcessFactory { Fail = true };
        var shell = CreateShell(coordinator.InstanceId, factory, new ScriptedLink());
        shell.OnRevitStartup();
        Assert.False(shell.Processes.EnsureStarted("BimIt.Revit.Assistant.exe", "--instance-id \"" + coordinator.InstanceId + "\""));
        Assert.Equal(AssistantProcessPhase.Failed, shell.Processes.Phase);
        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.Null(coordinator.FailureReason);

        factory.Fail = false;
        Assert.True(shell.Processes.EnsureStarted("BimIt.Revit.Assistant.exe", "--instance-id \"" + coordinator.InstanceId + "\""));
        Assert.Equal(1, shell.Processes.StartCount);
        Assert.True(shell.Processes.EnsureStarted("BimIt.Revit.Assistant.exe", "again"));
        Assert.Equal(1, shell.Processes.StartCount);
        shell.Processes.Shutdown();
        Assert.Equal(AssistantProcessPhase.Stopped, shell.Processes.Phase);
        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
    }

    [Fact]
    public async Task Commands_bind_one_process_to_the_exact_instance_and_reset_is_session_only()
    {
        var instanceId = "instance-" + Guid.NewGuid().ToString("N");
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink
        {
            Status = ReadyStatus(),
            Result = new AssistantIpcMessage(
                AssistantIpcKind.WorkflowResult,
                null,
                null,
                null,
                false,
                "req",
                null,
                null,
                null,
                null,
                null,
                null,
                "Model context",
                "Revit 2026.",
                false),
        };
        var shell = CreateShell(instanceId, factory, link);
        shell.OnRevitStartup();
        Assert.Equal(0, factory.Starts);

        shell.ReceivePageMessage("""{"type":"sendCommand","text":"/context"}""");
        shell.ReceivePageMessage("""{"type":"sendCommand","text":"/warnings"}""");
        shell.ReceivePageMessage("""{"type":"sendCommand","text":"/active-view"}""");
        await WaitFor(() => link.Workflows.Count == 3);

        Assert.Equal(1, factory.Starts);
        Assert.Contains("--instance-id \"" + instanceId + "\"", factory.Arguments[0], StringComparison.Ordinal);
        Assert.Equal(instanceId, link.SeenInstance);
        Assert.Equal(
            [AssistantWorkflowName.Context, AssistantWorkflowName.Warnings, AssistantWorkflowName.ActiveView],
            link.Workflows);
        Assert.Contains(shell.Conversation.Entries, entry => entry.Body == "Revit 2026.");

        var conversationId = shell.Conversation.ConversationId;
        shell.ReceivePageMessage("""{"type":"newConversation"}""");
        Assert.Empty(shell.Conversation.Entries);
        Assert.NotEqual(conversationId, shell.Conversation.ConversationId);

        shell.ReceivePageMessage("""{"type":"sendCommand","text":"show me the ducts"}""");
        Assert.Contains(shell.Conversation.Entries, entry => entry.Body.Contains("No AI provider is configured yet", StringComparison.Ordinal));
        Assert.Equal(1, factory.Starts);

        var conversation = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "AssistantConversation.cs"));
        Assert.DoesNotContain("File.", conversation, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", conversation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_startup_uses_one_process_and_one_pipe()
    {
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink
        {
            Status = ReadyStatus(),
            HoldConnect = true,
            Result = new AssistantIpcMessage(
                AssistantIpcKind.WorkflowResult,
                null,
                null,
                null,
                false,
                "req",
                null,
                null,
                null,
                null,
                null,
                null,
                "Model context",
                "Revit 2026.",
                false),
        };
        var shell = CreateShell("instance-race", factory, link, FastTimeouts());

        var first = shell.OpenSessionAsync();
        var second = shell.OpenSessionAsync();
        shell.ReceivePageMessage("""{"type":"runQuickAction","action":"context"}""");
        await WaitFor(() => link.Connects == 1);
        await Task.Delay(40);
        Assert.Equal(1, link.Connects);
        Assert.Equal(1, factory.Starts);
        Assert.Single(link.PipeNames);

        link.ReleaseConnect();
        await Task.WhenAll(first, second);
        await WaitFor(() => link.Workflows.Count == 1);
        Assert.Equal(AssistantProcessPhase.Ready, shell.Processes.Phase);
        Assert.Equal(0, factory.Handles[0].Kills);
        await shell.ShutdownAsync();
    }

    [Fact]
    public async Task Double_open_does_not_kill_a_ready_process()
    {
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus() };
        var shell = CreateShell("instance-ready", factory, link, FastTimeouts());

        await shell.OpenSessionAsync();
        await shell.OpenSessionAsync();

        Assert.Equal(1, factory.Starts);
        Assert.Equal(1, link.Connects);
        Assert.Equal(0, factory.Handles[0].Kills);
        Assert.Equal(AssistantProcessPhase.Ready, shell.Processes.Phase);
        await shell.ShutdownAsync();
    }

    [Fact]
    public async Task Unexpected_exit_becomes_unavailable_and_retry_starts_a_new_session()
    {
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus() };
        var shell = CreateShell("instance-retry", factory, link, FastTimeouts());
        await shell.OpenSessionAsync();

        factory.Handles[0].HasExited = true;
        shell.ObserveAssistantProcess();
        Assert.Equal(AssistantProcessPhase.Failed, shell.Processes.Phase);

        shell.ReceivePageMessage("""{"type":"retryConnection"}""");
        await WaitFor(() => factory.Starts == 2);
        Assert.Equal(2, link.Connects);
        Assert.Equal(2, link.PipeNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AssistantProcessPhase.Ready, shell.Processes.Phase);
        Assert.NotEqual(link.PipeNames[0], link.PipeNames[1]);
        await shell.ShutdownAsync();
    }

    [Fact]
    public async Task Workflow_timeout_does_not_send_the_tool_again()
    {
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus(), HoldRun = true };
        var shell = CreateShell("instance-timeout", factory, link, FastTimeouts());
        await shell.OpenSessionAsync();

        shell.ReceivePageMessage("""{"type":"sendCommand","text":"/context"}""");
        await WaitFor(() => shell.Conversation.Entries.Any(entry => entry.Body.Contains("timed out", StringComparison.Ordinal)));

        Assert.Single(link.Workflows);
        Assert.Contains(shell.Conversation.Entries, entry => entry.Body.Contains("was not sent again", StringComparison.Ordinal));
        Assert.Equal(AssistantProcessPhase.Failed, shell.Processes.Phase);
        await shell.ShutdownAsync();
    }

    [Fact]
    public async Task Shutdown_completes_when_the_pipe_does_not_close()
    {
        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus(), HoldShutdown = true };
        var shell = CreateShell("instance-shutdown", factory, link, FastTimeouts());
        await shell.OpenSessionAsync();

        await shell.ShutdownAsync();
        Assert.Equal(AssistantProcessPhase.Stopped, shell.Processes.Phase);
        Assert.True(factory.Handles[0].Killed);
    }

    [Fact]
    public async Task Hung_ipc_is_killed_before_revit_shutdown_returns()
    {
        var order = new List<string>();
        var events = new List<string>();
        var coordinator = new AddinLifecycleCoordinator(
            new RecordingDispatcherFactory(events),
            new RecordingBridgeFactory(events),
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
        coordinator.Prepare(new RecordingBootstrapSubscription());
        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.55"));

        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus(), HoldShutdown = true, ShutdownOrder = order };
        var shell = CreateShell(coordinator.InstanceId, factory, link, FastTimeouts());
        await shell.OpenSessionAsync();
        factory.Handles[0].OnKill = () => order.Add("process-kill");

        var approval = new ApprovalUiRuntime();
        approval.RegisterSurface(new RecordingApprovalSurface(() => order.Add("approval-shutdown")));

        var started = DateTime.UtcNow;
        AddinHostShutdown.Complete(coordinator, shell, approval);
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.Equal(AssistantProcessPhase.Stopped, shell.Processes.Phase);
        Assert.True(factory.Handles[0].Killed);
        Assert.True(approval.IsStopped);
        Assert.Equal(new[] { "ipc-shutdown", "process-kill", "approval-shutdown" }, order);

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "RevitMcpApplication.cs"));
        var shutdown = Slice(source, "public Result OnShutdown", "private void OnBootstrapSender");
        Assert.Contains("AddinHostShutdown.Complete(_lifecycle, _assistant, _approvalUi)", shutdown, StringComparison.Ordinal);
        Assert.Contains("return Result.Succeeded", shutdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Result.Failed", shutdown, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", shutdown, StringComparison.Ordinal);
        var hostShutdown = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "AddinHostShutdown.cs"));
        Assert.Contains("assistant?.ShutdownForHost()", hostShutdown, StringComparison.Ordinal);
        Assert.Contains("approvalUi?.Shutdown()", hostShutdown, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", hostShutdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assistant_shutdown_failure_still_kills_the_process_and_stops_approval()
    {
        var events = new List<string>();
        var coordinator = new AddinLifecycleCoordinator(
            new RecordingDispatcherFactory(events),
            new RecordingBridgeFactory(events),
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
        coordinator.Prepare(new RecordingBootstrapSubscription());
        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.55"));

        var factory = new RecordingProcessFactory();
        var link = new ScriptedLink { Status = ReadyStatus(), FailShutdown = true };
        var shell = CreateShell(coordinator.InstanceId, factory, link, FastTimeouts());
        await shell.OpenSessionAsync();
        var approval = new ApprovalUiRuntime();
        AddinHostShutdown.Complete(coordinator, shell, approval);

        Assert.Equal(AddinLifecycleState.Stopped, coordinator.State);
        Assert.True(factory.Handles[0].Killed);
        Assert.Equal(AssistantProcessPhase.Stopped, shell.Processes.Phase);
        Assert.True(approval.IsStopped);
    }

    [Fact]
    public void Retry_control_posts_the_bounded_retry_intent()
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "index.html"));
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "app.js"));
        Assert.Contains("id=\"retry\"", html, StringComparison.Ordinal);
        Assert.Contains("retryConnection", script, StringComparison.Ordinal);
    }

    private static AssistantIpcTimeouts FastTimeouts()
    {
        return new AssistantIpcTimeouts(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromSeconds(30));
    }

    private static AssistantShell CreateShell(
        string instanceId,
        RecordingProcessFactory factory,
        ScriptedLink link,
        AssistantIpcTimeouts? timeouts = null)
    {
        return new AssistantShell(
            () => instanceId,
            () => true,
            () => ("2026", "26.5.0.55"),
            factory,
            link,
            timeouts);
    }

    private static AssistantIpcMessage ReadyStatus()
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.Status,
            null,
            null,
            null,
            true,
            null,
            null,
            "ready",
            "ready",
            10,
            ["revit_get_context"],
            null,
            null,
            null,
            false);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        Assert.True(condition());
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return text[from..to];
    }

    private sealed class RecordingProcessFactory : IAssistantProcessFactory
    {
        public bool Fail { get; set; }

        public int Starts { get; private set; }

        public List<string> Arguments { get; } = [];

        public List<Handle> Handles { get; } = [];

        public IAssistantProcessHandle Start(string fileName, string arguments)
        {
            if (Fail)
            {
                throw new InvalidOperationException("assistant failed to start");
            }

            Starts++;
            Arguments.Add(arguments);
            var handle = new Handle();
            Handles.Add(handle);
            return handle;
        }

        internal sealed class Handle : IAssistantProcessHandle
        {
            public bool HasExited { get; set; }

            public int Kills { get; private set; }

            public bool Killed => Kills > 0;

            public Action? OnKill { get; set; }

            public void Kill()
            {
                Kills++;
                HasExited = true;
                OnKill?.Invoke();
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class ScriptedLink : IAssistantLink
    {
        public AssistantIpcMessage? Status { get; set; }

        public AssistantIpcMessage? Result { get; set; }

        public string? SeenInstance { get; private set; }

        public List<AssistantWorkflowName> Workflows { get; } = [];

        public List<string> PipeNames { get; } = [];

        public int Connects { get; private set; }

        public bool HoldConnect { get; set; }

        public bool HoldRun { get; set; }

        public bool HoldShutdown { get; set; }

        public bool FailShutdown { get; set; }

        public List<string>? ShutdownOrder { get; set; }

        private readonly TaskCompletionSource _connect = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseConnect() => _connect.TrySetResult();

        public async Task<AssistantIpcMessage?> ConnectAsync(
            string pipeName,
            string instanceId,
            string? revitVersion,
            string? revitBuild,
            bool bridgeReady,
            CancellationToken cancellationToken)
        {
            Connects++;
            PipeNames.Add(pipeName);
            SeenInstance = instanceId;
            if (HoldConnect)
            {
                await _connect.Task.WaitAsync(cancellationToken);
            }

            return Status;
        }

        public async Task<AssistantIpcMessage?> RunAsync(string requestId, AssistantWorkflowName workflow, CancellationToken cancellationToken)
        {
            Workflows.Add(workflow);
            if (HoldRun)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Result;
        }

        public Task ShutdownAsync()
        {
            ShutdownOrder?.Add("ipc-shutdown");
            if (FailShutdown)
            {
                return Task.FromException(new IOException("pipe occupied"));
            }

            return HoldShutdown ? Task.Delay(Timeout.Infinite) : Task.CompletedTask;
        }
    }

    private sealed class RecordingApprovalSurface : IApprovalPaneSurface
    {
        private readonly Action _onDispose;

        public RecordingApprovalSurface(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void BeginAfterShow()
        {
        }

        public void Post(string json)
        {
        }

        public void DisposeSurface() => _onDispose();
    }
}
