using System.Diagnostics;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantProcessTests
{
    [Fact]
    public async Task Assistant_process_initializes_mcp_for_one_instance_and_exits()
    {
        var assistant = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Assistant.exe");
        var server = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Server.exe");
        Assert.True(File.Exists(assistant), assistant);
        Assert.True(File.Exists(server), server);

        var instanceId = "instance-" + Guid.NewGuid().ToString("N");
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        using var process = Start(assistant, $"--pipe \"{pipe}\" --instance-id \"{instanceId}\" --server \"{server}\"");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        await client.ConnectAsync(15_000, timeout.Token);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello(instanceId)), timeout.Token);
        var status = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.True(status.Accepted);
        Assert.Equal("ready", status.Message!.McpState);
        Assert.Equal(17, status.Message.ToolCount);
        Assert.Contains("revit_get_context", status.Message.ToolNames!);
        Assert.Contains("revit_get_warnings", status.Message.ToolNames!);
        Assert.Contains("revit_query_elements", status.Message.ToolNames!);
        Assert.Contains("revit_get_mep_topology", status.Message.ToolNames!);
        Assert.DoesNotContain("bimit_explore_selected_mep", status.Message.ToolNames!);
        Assert.Equal(17, status.Message.ToolNames!.Count);
        Assert.DoesNotContain(status.Message.ToolNames!, name => name.Contains("approve", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", status.Message.AssistantState);
        Assert.Contains(status.Message.ProviderState, new[] { "starting", "ready", "unavailable" });
        Assert.DoesNotContain("ghp_", status.Message.ProviderError ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("github_pat_", status.Message.ProviderLabel ?? "", StringComparison.Ordinal);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context,
                null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.WorkflowResult, workflow.Message!.Kind);
        Assert.True(workflow.Message.IsError);
        Assert.Contains("INSTANCE_NOT_FOUND", workflow.Message.Body, StringComparison.Ordinal);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task Mismatched_instance_hello_does_not_become_ready()
    {
        var assistant = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Assistant.exe");
        var server = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Server.exe");
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        using var process = Start(assistant, $"--pipe \"{pipe}\" --instance-id \"expected-instance\" --server \"{server}\"");
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        await client.ConnectAsync(15_000);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello("other-instance")), CancellationToken.None);

        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(3, process.ExitCode);
    }

    [Fact]
    public async Task Initialized_session_refuses_trusted_write_tools()
    {
        var server = Path.Combine(AppContext.BaseDirectory, "BimIt.Revit.Server.exe");
        await using var session = new McpAssistantToolSession(server);
        var readiness = await session.InitializeAsync(CancellationToken.None);
        Assert.True(readiness.Ready);
        Assert.Equal(17, readiness.ToolCount);

        foreach (var denied in new[]
        {
            "revit_preview_mep_device_placements",
            "revit_request_mep_device_placement_review",
            "revit_apply_mep_device_placements",
            "revit_preview_room_pattern_placements",
        })
        {
            var blocked = await session.CallAsync(denied, new Dictionary<string, object?> { ["instance_id"] = "x" }, CancellationToken.None);
            Assert.True(blocked.IsError);
            Assert.Equal("tool_not_allowed", blocked.ErrorCode);
        }

        var preview = await session.PreviewAsync(
            new PreviewMepDevicePlacementsRequest
            {
                DocumentId = "doc",
                TemplateElementRef = "template",
                LengthUnitId = "autodesk.unit.unit:millimeters-1.0.1",
                Placements = [new PreviewMepDevicePlacement { OffsetX = 1, OffsetY = 0, VerticalOffset = 0 }],
            },
            "instance",
            CancellationToken.None);
        Assert.NotEqual("tool_not_allowed", preview.ErrorCode);

        var review = await session.ReviewAsync("instance", "intent", CancellationToken.None);
        Assert.NotEqual("tool_not_allowed", review.ErrorCode);
        var apply = await session.ApplyAsync("instance", "intent", CancellationToken.None);
        Assert.NotEqual("tool_not_allowed", apply.ErrorCode);

        var refused = await session.CallAsync(
            "revit_apply_parameter_updates",
            new Dictionary<string, object?> { ["instance_id"] = "x", ["intent_ref"] = "y" },
            CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal("tool_not_allowed", refused.ErrorCode);

        var arbitrary = await session.CallAsync(
            "revit_describe_parameters",
            new Dictionary<string, object?> { ["instance_id"] = "x" },
            CancellationToken.None);
        Assert.Equal("tool_not_allowed", arbitrary.ErrorCode);

        var topology = await session.CallAsync(
            "revit_get_mep_topology",
            new Dictionary<string, object?> { ["instance_id"] = "x" },
            CancellationToken.None);
        Assert.NotEqual("tool_not_allowed", topology.ErrorCode);

        var selection = await session.CallAsync(
            "revit_get_selection",
            new Dictionary<string, object?> { ["instance_id"] = "x", ["limit"] = 10 },
            CancellationToken.None);
        Assert.NotEqual("tool_not_allowed", selection.ErrorCode);
    }

    [Fact]
    public async Task Ipc_pipe_is_current_user_and_length_framed()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant.Ipc", "AssistantPipe.cs"));
        Assert.Contains("PipeOptions.CurrentUserOnly", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TcpListener", source, StringComparison.Ordinal);
        Assert.StartsWith("bimit.revit.assistant.v1.p", AssistantPipeNames.Create(42, "abc"), StringComparison.Ordinal);

        using var stream = new MemoryStream();
        var payload = AssistantIpc.Serialize(Hello("instance-a"));
        await AssistantPipeFrames.WriteAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        var read = await AssistantPipeFrames.ReadAsync(stream, CancellationToken.None);
        Assert.Equal("instance-a", AssistantIpc.Parse(read).Message!.InstanceId);
    }

    private static AssistantIpcMessage Hello(string instanceId)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.Hello,
            instanceId,
            "2026",
            "26.5.0.55",
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);
    }

    private static Process Start(string fileName, string arguments)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Assistant process did not start.");
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
}
