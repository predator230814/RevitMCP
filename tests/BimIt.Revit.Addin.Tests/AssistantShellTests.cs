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

    private static AssistantShell CreateShell(string instanceId, RecordingProcessFactory factory, ScriptedLink link)
    {
        return new AssistantShell(
            () => instanceId,
            () => true,
            () => ("2026", "26.5.0.55"),
            factory,
            link);
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

        public IAssistantProcessHandle Start(string fileName, string arguments)
        {
            if (Fail)
            {
                throw new InvalidOperationException("assistant failed to start");
            }

            Starts++;
            Arguments.Add(arguments);
            return new Handle();
        }

        private sealed class Handle : IAssistantProcessHandle
        {
            public bool HasExited => false;

            public void Kill()
            {
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

        public Task<AssistantIpcMessage?> ConnectAsync(
            string pipeName,
            string instanceId,
            string? revitVersion,
            string? revitBuild,
            bool bridgeReady,
            CancellationToken cancellationToken)
        {
            SeenInstance = instanceId;
            return Task.FromResult(Status);
        }

        public Task<AssistantIpcMessage?> RunAsync(string requestId, AssistantWorkflowName workflow, CancellationToken cancellationToken)
        {
            Workflows.Add(workflow);
            return Task.FromResult(Result);
        }

        public Task ShutdownAsync() => Task.CompletedTask;
    }
}
