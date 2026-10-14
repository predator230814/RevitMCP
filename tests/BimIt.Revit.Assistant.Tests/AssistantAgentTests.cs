using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using BimIt.Revit.Contracts;
using GitHub.Copilot;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantAgentTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Only_the_assistant_project_references_the_copilot_sdk()
    {
        var root = RepoRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        Assert.Contains("GitHub.Copilot.SDK", props, StringComparison.Ordinal);
        Assert.Contains("1.0.21", props, StringComparison.Ordinal);

        foreach (var project in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(project);
            if (Path.GetFileName(project) == "BimIt.Revit.Assistant.csproj")
            {
                Assert.Contains("GitHub.Copilot.SDK", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("GitHub.Copilot.SDK", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Copilot_session_is_empty_mode_with_only_the_three_read_tools()
    {
        var directory = Directory.CreateTempSubdirectory("bimit-copilot-config-").FullName;
        try
        {
            var client = GitHubCopilotSessionBlueprint.CreateClientOptions(directory);
            Assert.Equal(CopilotClientMode.Empty, client.Mode);
            Assert.Equal(directory, client.BaseDirectory);
            Assert.Equal(directory, client.WorkingDirectory);
            Assert.True(client.UseLoggedInUser);
            Assert.Null(client.GitHubToken);
            Assert.False(client.EnableRemoteSessions);
            Assert.Null(client.Connection);

            var tools = GitHubCopilotSessionBlueprint.CreateTools(new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId));
            Assert.Equal(BimItAgentReadTools.Names, tools.Select(tool => tool.Name).ToArray());
            foreach (var tool in tools)
            {
                var schema = tool.JsonSchema.GetRawText();
                Assert.DoesNotContain("instance_id", schema, StringComparison.Ordinal);
                Assert.DoesNotContain("document_id", schema, StringComparison.Ordinal);
            }

            var session = GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, tools);
            Assert.Equal("auto", session.Model);
            Assert.False(session.Streaming);
            Assert.False(session.EnableSessionStore);
            Assert.False(session.EnableSkills);
            Assert.False(session.Memory!.Enabled);
            Assert.False(session.InfiniteSessions!.Enabled);
            Assert.Empty(session.McpServers!);
            Assert.False(session.EnableHostGitOperations);
            Assert.False(session.EnableFileHooks);
#pragma warning disable GHCP001 // EnableMcpApps is experimental in SDK 1.0.21.
            Assert.False(session.EnableMcpApps);
#pragma warning restore GHCP001
            Assert.False(session.EnableConfigDiscovery);
            Assert.True(session.SkipCustomInstructions);
            Assert.Null(session.GitHubToken);
            Assert.Empty(session.SkillDirectories!);
            Assert.Empty(session.PluginDirectories!);
            Assert.NotSame(PermissionHandler.ApproveAll, session.OnPermissionRequest);

            var allowed = Assert.IsAssignableFrom<IList<string>>(session.AvailableTools);
            Assert.Equal(
                [
                    "custom:" + BimItAgentReadTools.ModelContext,
                    "custom:" + BimItAgentReadTools.Warnings,
                    "custom:" + BimItAgentReadTools.ActiveViewElements,
                ],
                allowed.ToArray());
            Assert.DoesNotContain(allowed, name => name.Contains('*') || name.StartsWith("builtin:", StringComparison.Ordinal) || name.StartsWith("mcp:", StringComparison.Ordinal));
            Assert.Equal(SystemMessageMode.Append, session.SystemMessage!.Mode);
            Assert.Contains("read-only", session.SystemMessage.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("instance_id", session.SystemMessage.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unexpected_permissions_fail_closed_and_the_three_tools_are_allowed()
    {
        Assert.True(GitHubCopilotPermissions.IsAllowed(new PermissionRequestCustomTool
        {
            ToolName = BimItAgentReadTools.Warnings,
            ToolDescription = "warnings",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestCustomTool
        {
            ToolName = "revit_apply_parameter_updates",
            ToolDescription = "write",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "dir",
            HasWriteFileRedirection = false,
            Intention = "list",
            PossiblePaths = [],
            PossibleUrls = [],
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestWrite
        {
            CanOfferSessionApproval = false,
            Diff = "",
            FileName = "model.rvt",
            Intention = "edit",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestRead
        {
            Intention = "read",
            Path = "model.rvt",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestMcp
        {
            ReadOnly = true,
            ServerName = "bimit",
            ToolName = "revit_get_context",
            ToolTitle = "context",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestMemory
        {
            Fact = "remember",
        }));

        var blueprint = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant", "Providers", "GitHubCopilot", "GitHubCopilotSessionBlueprint.cs"));
        Assert.Contains("CopilotClientMode.Empty", blueprint, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveAll", blueprint, StringComparison.Ordinal);
        Assert.Contains("SkipPermission = true", blueprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_tools_inject_identity_and_reject_writes()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson("doc-9"))
            : tool == AssistantTools.GetWarnings
                ? Success("""{"matched_count":0,"truncated":false}""")
                : Success("""{"matched_count":2,"truncated":true}"""));
        var tools = new BimItAgentReadTools(session, InstanceId);

        var context = await tools.InvokeAsync(BimItAgentReadTools.ModelContext, CancellationToken.None);
        Assert.True((bool)context["ok"]!);
        Assert.Equal(InstanceId, session.Calls[0].Arguments["instance_id"]);
        Assert.Single(session.Calls[0].Arguments);

        var warnings = await tools.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.True((bool)warnings["ok"]!);
        Assert.Equal(InstanceId, session.Calls[2].Arguments["instance_id"]);
        Assert.Equal("doc-9", session.Calls[2].Arguments["document_id"]);
        Assert.Equal(2, session.Calls[2].Arguments.Count);

        var view = await tools.InvokeAsync(BimItAgentReadTools.ActiveViewElements, CancellationToken.None);
        Assert.True((bool)view["ok"]!);
        var query = session.Calls[4];
        Assert.Equal(AssistantTools.QueryElements, query.Tool);
        Assert.Equal(InstanceId, query.Arguments["instance_id"]);
        Assert.Equal("active_view", query.Arguments["scope"]);
        Assert.Equal(25, query.Arguments["limit"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(query.Arguments["filters"]));
        Assert.Equal("doc-9", query.Arguments["document_id"]);

        var denied = await tools.InvokeAsync("revit_preview_parameter_updates", CancellationToken.None);
        var unknown = await tools.InvokeAsync("shell", CancellationToken.None);
        Assert.False((bool)denied["ok"]!);
        Assert.Equal("tool_not_allowed", denied["code"]);
        Assert.False((bool)unknown["ok"]!);
        Assert.Equal(5, session.Calls.Count);
    }

    [Fact]
    public async Task Warnings_do_not_invent_a_document_id_and_keep_mcp_errors_structured()
    {
        var missing = new BimItAgentReadTools(new RecordingSession(_ => Success(ContextJson(null))), InstanceId);
        var blocked = await missing.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal("bridge_v12_required", blocked["code"]);
        Assert.False((bool)blocked["ok"]!);

        var none = new BimItAgentReadTools(new RecordingSession(_ => Success("""{"document":null,"active_view":null}""")), InstanceId);
        var noDocument = await none.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, noDocument["code"]);

        var failed = new BimItAgentReadTools(
            new RecordingSession(tool => tool == AssistantTools.GetContext
                ? Success(ContextJson("doc-9"))
                : Error("NO_REVIT_INSTANCE", "No eligible Revit instance is available. Token ghp_secret and C:\\secret\\model.rvt")),
            InstanceId);
        var error = await failed.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal("NO_REVIT_INSTANCE", error["code"]);
        var message = Assert.IsType<string>(error["message"]);
        Assert.DoesNotContain("ghp_secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\secret", message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", message, StringComparison.Ordinal);
        Assert.Contains("[path]", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Natural_language_uses_the_runtime_and_slash_workflows_do_not()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime();
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime);

        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        var status = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("ready", status.Message!.McpState);
        Assert.True(status.Message.ProviderState is "starting" or "ready");
        await WaitFor(() => runtime.Initializations == 1);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: "Analyse les warnings.")),
            timeout.Token);
        var chat = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.ChatResult, chat.Message!.Kind);
        Assert.Equal("There are 7 warnings.", chat.Message.Body);
        Assert.Equal(["Analyse les warnings."], runtime.Prompts);
        Assert.Empty(session.Calls);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.WorkflowResult, workflow.Message!.Kind);
        Assert.Empty(runtime.Prompts.Skip(1));
        Assert.Equal(AssistantTools.GetContext, Assert.Single(session.Calls).Tool);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.ResetConversation, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var reset = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.Status, reset.Message!.Kind);
        Assert.Equal("ready", reset.Message.McpState);
        Assert.Equal(1, runtime.Resets);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
        Assert.True(runtime.Disposed);
    }

    [Fact]
    public async Task Provider_startup_failure_does_not_stop_the_assistant()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime { FailInitialization = true };
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(new AssistantHostOptions(pipe, InstanceId, null, null, null), session, runtime);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        var status = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("ready", status.Message!.McpState);
        await WaitFor(() => runtime.Initializations == 1);
        Assert.Equal("unavailable", runtime.Status.StateToken);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.False(workflow.Message!.IsError);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
    }

    [Fact]
    public void Ipc_chat_messages_are_bounded_and_reject_credentials()
    {
        var chat = AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"Analyse les warnings."}""");
        Assert.Equal("Analyse les warnings.", chat.Message!.Text);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","token":"ghp_secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","githubToken":"secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","apiKey":"secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"status","assistantState":"ready","mcpState":"ready","authorization":"bearer secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":""}""").Accepted);
        Assert.False(AssistantIpc.Parse("{\"type\":\"chat\",\"requestId\":\"abc\",\"text\":\"" + new string('a', 2_001) + "\"}").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"queryStatus","text":"hi"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"resetConversation","instanceId":"x"}""").Accepted);
        Assert.False(AssistantIpc.GrantsApprovalAuthority);

        var serialized = AssistantIpc.Serialize(new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
            null,
            null,
            null,
            false,
            "abc",
            null,
            null,
            null,
            null,
            null,
            null,
            "BIM-IT Assistant",
            "Seven warnings.",
            false,
            "GitHub Copilot",
            "ready",
            "auto",
            null,
            "GitHub Copilot · auto"));
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AssistantIpcKind.ChatResult, AssistantIpc.Parse(serialized).Message!.Kind);
    }

    [Fact]
    public void Provider_runtime_directory_is_per_process_and_removed()
    {
        var first = CopilotRuntimeDirectory.Create();
        var second = CopilotRuntimeDirectory.Create();
        Assert.NotEqual(first, second);
        Assert.Contains(Environment.ProcessId + "-", Path.GetFileName(first), StringComparison.Ordinal);
        Assert.Contains(Path.Combine("BIM-IT", "Assistant", "CopilotRuntime"), first, StringComparison.Ordinal);
        CopilotRuntimeDirectory.TryDelete(first);
        CopilotRuntimeDirectory.TryDelete(second);
        Assert.False(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
    }

    [Fact]
    public void Assistant_source_does_not_create_a_chat_transcript_store()
    {
        var root = Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant");
        var source = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnableSessionStore = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation.json", source, StringComparison.Ordinal);
        Assert.Contains("EnableSessionStore = false", source, StringComparison.Ordinal);
        Assert.Contains("ChildProcessLifetime.BindToThisProcess()", File.ReadAllText(Path.Combine(root, "AssistantHost.cs")), StringComparison.Ordinal);
    }

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private static string ContextJson(string? documentId)
    {
        var result = new GetContextResult
        {
            Instance = new GetContextInstance { InstanceId = InstanceId, RevitVersion = "2026", RevitBuild = "26.5.0.55" },
            Document = new GetContextDocument
            {
                Title = "Sample",
                Kind = GetContextDocumentKind.Project,
                IsWorkshared = false,
                IsModelInCloud = false,
                IsReadOnly = false,
                IsModified = false,
            },
            ActiveView = new GetContextActiveView { ElementId = "view-1", Name = "Level 1", ViewType = "FloorPlan" },
            Selection = new GetContextSelection { Count = 0 },
        };
        var node = JsonNode.Parse(JsonSerializer.Serialize(result, ContractJson.Options))!;
        if (documentId is not null)
        {
            node["document"]!["document_id"] = documentId;
        }

        return node.ToJsonString();
    }

    private static AssistantToolCall Success(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new AssistantToolCall(false, null, null, document.RootElement.Clone());
    }

    private static AssistantToolCall Error(string code, string message)
    {
        return new AssistantToolCall(true, code, message, null);
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

    private sealed class RecordingSession(Func<string, AssistantToolCall> responder) : IAssistantToolSession
    {
        public List<Call> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 3, AssistantTools.WorkflowTools, null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(toolName, new Dictionary<string, object?>(arguments)));
            return Task.FromResult(responder(toolName));
        }

        public sealed record Call(string Tool, Dictionary<string, object?> Arguments);
    }

    private sealed class FakeRuntime : IAssistantAgentRuntime
    {
        public bool FailInitialization { get; init; }

        public int Initializations { get; private set; }

        public int Resets { get; private set; }

        public bool Disposed { get; private set; }

        public List<string> Prompts { get; } = [];

        public AssistantProviderStatus Status { get; private set; } = new("GitHub Copilot", AssistantProviderState.Starting, null, null);

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Initializations++;
            Status = FailInitialization
                ? new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, "GitHub Copilot sign-in is required.")
                : new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Ready, "auto", null);
            return Task.CompletedTask;
        }

        public Task<AssistantAgentTurn> SendAsync(string userText, CancellationToken cancellationToken)
        {
            Prompts.Add(userText);
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "There are 7 warnings.", false));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken)
        {
            Resets++;
            Prompts.Clear();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
