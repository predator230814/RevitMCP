using System.IO.Pipes;
using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantActivityTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Labels_name_real_tools_and_do_not_copy_arguments()
    {
        Assert.Equal("Reading model context", AssistantActivityLabels.ForTool("revit_get_context", "instance_id=secret {\"x\":1}", false));
        Assert.Equal("Read model context", AssistantActivityLabels.ForTool("revit_get_context", null, true));
        Assert.Equal("Discovering Revit capabilities", AssistantActivityLabels.ForTool("revit_lookup", "document_id and a long description", false));
        Assert.Equal("Discovered Revit capabilities", AssistantActivityLabels.ForTool("revit_lookup", null, true));
        Assert.Equal("Reading Revit data", AssistantActivityLabels.ForTool("revit_read", "inputs: secret", false));
        Assert.Equal("Read Revit data", AssistantActivityLabels.ForTool("revit_read", null, true));
        Assert.Equal("Reading warnings", AssistantActivityLabels.ForTool("revit_get_warnings", "instance_id", false));
        Assert.Equal("Read warnings", AssistantActivityLabels.ForTool("revit_get_warnings", null, true));
        Assert.Equal("Querying parameter bindings", AssistantActivityLabels.ForTool("revit_query_parameter_bindings", null, false));
        var denied = AssistantActivityLabels.ForTool("revit_apply_parameter_updates", "intent_ref=secret", false);
        Assert.DoesNotContain("secret", denied, StringComparison.Ordinal);
        Assert.DoesNotContain("intent_ref", denied, StringComparison.Ordinal);
        Assert.All(
            new[] { "revit_get_context", "revit_lookup", "revit_read", "revit_get_warnings", "revit_query_parameter_bindings" },
            name =>
            {
                var label = AssistantActivityLabels.ForTool(name, "instance_id document_id {\"secret\":1}", false);
                Assert.DoesNotContain("instance_id", label, StringComparison.Ordinal);
                Assert.DoesNotContain("secret", label, StringComparison.Ordinal);
                Assert.DoesNotContain("{", label, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Activity_bus_does_not_leak_across_requests()
    {
        var bus = new AssistantActivityBus();
        var first = new List<string>();
        var second = new List<string>();
        bus.Bind(new ListSink(first));
        bus.Report(AssistantActivityLabels.Preparing());
        bus.Clear();
        bus.Report(AssistantActivityLabels.McpRead("revit_read", "secret", AssistantActivityState.Started));
        bus.Bind(new ListSink(second));
        bus.Report(AssistantActivityLabels.McpRead("revit_get_context", null, AssistantActivityState.Started));

        Assert.Equal(["Preparing"], first);
        Assert.Equal(["Reading model context"], second);
    }

    [Theory]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"preparing","activityState":"started","activityLabel":"Preparing"}""", true)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"mcp_read","activityState":"completed","activityLabel":"Read model context","activityTool":"revit_get_context"}""", true)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"reading_skill","activityState":"started","activityLabel":"Reading a Skill","activityTool":"bimit_read_skill"}""", true)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"thinking","activityState":"started","activityLabel":"Hidden reasoning"}""", false)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"mcp_read","activityState":"streaming","activityLabel":"Reading Revit data","activityTool":"revit_read"}""", false)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"mcp_read","activityState":"started","activityLabel":"Reading {\"secret\":1}","activityTool":"revit_read"}""", false)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"mcp_read","activityState":"started","activityLabel":"Reading Revit data","activityTool":"revit_apply_parameter_updates"}""", false)]
    [InlineData("""{"type":"chatActivity","requestId":"req-a","activityKind":"preparing","activityState":"started","activityLabel":"Preparing","title":"BIM-IT Assistant","body":"done"}""", false)]
    [InlineData("""{"type":"chatResult","requestId":"req-a","title":"BIM-IT Assistant","body":"done","activityKind":"preparing","activityState":"started","activityLabel":"Preparing"}""", false)]
    [InlineData("""{"type":"not-json""", false)]
    public void Activity_frames_parse_only_when_they_are_safe(string json, bool accepted)
    {
        var parsed = AssistantIpc.Parse(json);
        Assert.Equal(accepted, parsed.Accepted);
        if (accepted)
        {
            Assert.Equal(AssistantIpcKind.ChatActivity, parsed.Message!.Kind);
            Assert.NotEqual(AssistantIpcKind.ChatResult, parsed.Message.Kind);
            Assert.Null(parsed.Message.Body);
            Assert.Null(parsed.Message.Title);
        }
    }

    [Fact]
    public void Activity_round_trip_keeps_one_request_and_is_not_a_chat_result()
    {
        var message = new AssistantIpcMessage(
            AssistantIpcKind.ChatActivity,
            null,
            null,
            null,
            false,
            "req-a",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            ActivityKind: "mcp_read",
            ActivityState: "started",
            ActivityLabel: "Reading Revit data",
            ActivityTool: "revit_read");
        var parsed = AssistantIpc.Parse(AssistantIpc.Serialize(message));
        Assert.True(parsed.Accepted);
        Assert.Equal(AssistantIpcKind.ChatActivity, parsed.Message!.Kind);
        Assert.Equal("req-a", parsed.Message.RequestId);
        Assert.Equal("Reading Revit data", parsed.Message.ActivityLabel);
        Assert.Equal("revit_read", parsed.Message.ActivityTool);
        Assert.DoesNotContain("title", AssistantIpc.Serialize(message), StringComparison.Ordinal);
        Assert.False(AssistantIpc.IsModelToolName("revit_apply_mep_device_placements"));
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
    }

    [Fact]
    public async Task Exchange_reads_ordered_activities_then_one_result()
    {
        var seen = new List<string>();
        var result = await ReadExchangeAsync(
            "req-a",
            seen.Add,
            Frame(Activity("req-b", "preparing", "started", "Preparing", null)),
            Frame(Activity("req-a", "preparing", "started", "Preparing", null)),
            Frame(Activity("req-a", "mcp_read", "completed", "Read model context", "revit_get_context")),
            Frame(Activity("req-a", "mcp_read", "started", "Reading Revit data", "revit_read")),
            Frame("""{"type":"chatActivity","requestId":"req-a","activityKind":"thinking","activityState":"started","activityLabel":"Hidden reasoning"}"""),
            Frame("{"),
            Frame(Result("req-b", "Wrong request.", false)),
            Frame(Result("req-a", "The model is open.", false)));

        Assert.Equal(["Preparing", "Read model context", "Reading Revit data"], seen);
        Assert.Equal(AssistantIpcKind.ChatResult, result!.Kind);
        Assert.Equal("req-a", result.RequestId);
        Assert.Equal("The model is open.", result.Body);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Exchange_with_no_activities_returns_the_result()
    {
        var seen = new List<string>();
        var result = await ReadExchangeAsync("req-a", seen.Add, Frame(Result("req-a", "Done.", false)));
        Assert.Empty(seen);
        Assert.Equal(AssistantIpcKind.ChatResult, result!.Kind);
        Assert.Equal("Done.", result.Body);
    }

    [Fact]
    public async Task Exchange_returns_an_error_result_and_does_not_treat_an_activity_as_final()
    {
        var seen = new List<string>();
        var failed = await ReadExchangeAsync(
            "req-a",
            seen.Add,
            Frame(Activity("req-a", "preparing", "started", "Preparing", null)),
            Frame(Result("req-a", "The provider failed.", true)));
        Assert.Equal(["Preparing"], seen);
        Assert.True(failed!.IsError);
        Assert.Equal(AssistantIpcKind.ChatResult, failed.Kind);

        var onlyActivity = await ReadExchangeAsync("req-a", null, Frame(Activity("req-a", "mcp_read", "started", "Reading Revit data", "revit_read")));
        Assert.Null(onlyActivity);
    }

    [Fact]
    public async Task Exchange_cancellation_does_not_return_an_activity()
    {
        await using var stream = new MemoryStream(AssistantPipeFrames.Encode(AssistantIpc.Serialize(Activity("req-a", "preparing", "started", "Preparing", null))));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AssistantChatExchange.ReadUntilResultAsync(stream, "req-a", null, cancelled.Token));
    }

    [Fact]
    public async Task Host_streams_activities_for_one_request_and_keeps_the_next_request_separate()
    {
        var runtime = new ScriptedRuntime();
        runtime.Turns.Enqueue(async (request, _) =>
        {
            request.Activity?.Report(AssistantActivityLabels.Preparing());
            request.Activity?.Report(AssistantActivityLabels.McpRead("revit_get_context", "instance_id=secret", AssistantActivityState.Started));
            request.Activity?.Report(AssistantActivityLabels.McpRead("revit_get_context", null, AssistantActivityState.Completed));
            request.Activity?.Report(AssistantActivityLabels.PreparingResponse());
            return new AssistantAgentTurn("BIM-IT Assistant", "Context answer.", false, ["revit_get_context"]);
        });
        runtime.Turns.Enqueue((_, _) => throw new InvalidOperationException("provider failed closed"));
        runtime.Turns.Enqueue((request, _) =>
        {
            request.Activity?.Report(AssistantActivityLabels.McpRead("revit_read", "{\"inputs\":1}", AssistantActivityState.Started));
            throw new OperationCanceledException();
        });
        runtime.Turns.Enqueue((request, _) =>
        {
            request.Activity?.Report(AssistantActivityLabels.ReadingSkill(AssistantActivityState.Started));
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "Skill answer.", false, ["bimit_read_skill"]));
        });

        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"), new IdleSession(), runtime);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        Assert.Equal("ready", AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token)).Message!.McpState);

        var first = await ReadHostChatAsync(client, "req-1", "What is open?", timeout.Token);
        Assert.Equal(
            [AssistantIpcKind.ChatActivity, AssistantIpcKind.ChatActivity, AssistantIpcKind.ChatActivity, AssistantIpcKind.ChatActivity, AssistantIpcKind.ChatResult],
            first.Select(message => message.Kind).ToArray());
        Assert.Equal(
            new[] { "Preparing", "Reading model context", "Read model context", "Preparing the final response" },
            first.Take(4).Select(message => message.ActivityLabel).ToArray());
        Assert.All(first.Take(4), message => Assert.Equal("req-1", message.RequestId));
        Assert.Equal("Context answer.", first[^1].Body);
        Assert.DoesNotContain(first, message => (message.ActivityLabel ?? "").Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(AssistantIpc.Serialize(first[1]), "instance_id", StringComparison.Ordinal);

        var failed = await ReadHostChatAsync(client, "req-2", "Again.", timeout.Token);
        Assert.Equal(AssistantIpcKind.ChatResult, Assert.Single(failed).Kind);
        Assert.True(failed[0].IsError);
        Assert.Equal("req-2", failed[0].RequestId);
        Assert.DoesNotContain("Reading model context", failed[0].Body, StringComparison.Ordinal);

        var cancelled = await ReadHostChatAsync(client, "req-3", "Stop.", timeout.Token);
        Assert.Equal(AssistantIpcKind.ChatActivity, cancelled[0].Kind);
        Assert.Equal("req-3", cancelled[0].RequestId);
        Assert.Equal("Reading Revit data", cancelled[0].ActivityLabel);
        Assert.Equal(AssistantIpcKind.ChatResult, cancelled[^1].Kind);
        Assert.True(cancelled[^1].IsError);
        Assert.DoesNotContain(cancelled, message => message.ActivityLabel == "Reading model context");

        var skill = await ReadHostChatAsync(client, "req-4", "Use the skill.", timeout.Token);
        Assert.Equal("Reading a Skill", skill[0].ActivityLabel);
        Assert.Equal("req-4", skill[0].RequestId);
        Assert.Equal("bimit_read_skill", skill[0].ActivityTool);
        Assert.Equal(AssistantIpcKind.ChatResult, skill[^1].Kind);
        Assert.DoesNotContain(skill, message => message.ActivityLabel == "Reading Revit data");

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
    }

    private static async Task<AssistantIpcMessage?> ReadExchangeAsync(string requestId, Action<string>? onLabel, params byte[][] frames)
    {
        await using var stream = new MemoryStream();
        foreach (var frame in frames)
        {
            await stream.WriteAsync(frame);
        }

        stream.Position = 0;
        return await AssistantChatExchange.ReadUntilResultAsync(
            stream,
            requestId,
            message => onLabel?.Invoke(message.ActivityLabel ?? ""),
            CancellationToken.None);
    }

    private static async Task<List<AssistantIpcMessage>> ReadHostChatAsync(Stream client, string requestId, string text, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, requestId, null, null, null, null, null, null, null, null, false, Text: text)),
            cancellationToken);
        var messages = new List<AssistantIpcMessage>();
        var result = await AssistantChatExchange.ReadUntilResultAsync(
            client,
            requestId,
            messages.Add,
            cancellationToken);
        Assert.NotNull(result);
        messages.Add(result);
        return messages;
    }

    private static byte[] Frame(string json) => AssistantPipeFrames.Encode(json);

    private static byte[] Frame(AssistantIpcMessage message) => AssistantPipeFrames.Encode(AssistantIpc.Serialize(message));

    private static AssistantIpcMessage Activity(string requestId, string kind, string state, string label, string? tool)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.ChatActivity, null, null, null, false, requestId, null, null, null, null, null, null, null, null, false,
            ActivityKind: kind, ActivityState: state, ActivityLabel: label, ActivityTool: tool);
    }

    private static AssistantIpcMessage Result(string requestId, string body, bool isError)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.ChatResult, null, null, null, false, requestId, null, null, null, null, null, null, "BIM-IT Assistant", body, isError,
            UsedTools: []);
    }

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private sealed class ListSink(List<string> labels) : IAssistantActivitySink
    {
        public void Report(AssistantActivity activity) => labels.Add(activity.Label);
    }

    private sealed class IdleSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 1, ["revit_get_context"], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The activity test does not call MCP.");
        }
    }

    private sealed class ScriptedRuntime : IAssistantAgentRuntime
    {
        public Queue<Func<AssistantAgentRequest, CancellationToken, Task<AssistantAgentTurn>>> Turns { get; } = new();

        public AssistantProviderStatus Status { get; } = new("GitHub Copilot", AssistantProviderState.Ready, "auto", null);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
        {
            var turn = Turns.Dequeue();
            return turn(request, cancellationToken);
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
