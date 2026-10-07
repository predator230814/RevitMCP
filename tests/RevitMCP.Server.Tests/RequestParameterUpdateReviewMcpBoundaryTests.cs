using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class RequestParameterUpdateReviewMcpBoundaryTests
{
    [Fact]
    public void Tool_metadata_and_annotations_match_the_review_request()
    {
        var tool = CreateProtocolTool();
        Assert.Equal(RequestParameterUpdateReviewToolMetadata.Name, tool.Name);
        Assert.Equal("Request Revit Parameter Update Review", tool.Title);
        Assert.Equal(RequestParameterUpdateReviewToolMetadata.Description, tool.Description);
        Assert.False(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.DestructiveHint);
        Assert.False(tool.Annotations.IdempotentHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.DoesNotContain("revit.request_parameter_update_review", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("revit.request_parameter_update_review", tool.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Input_and_output_schemas_are_closed()
    {
        var tool = CreateProtocolTool();
        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "intent_ref" },
            input.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance_id", "intent_ref" },
            input.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(1, input.GetProperty("properties").GetProperty("instance_id").GetProperty("minLength").GetInt32());
        Assert.Equal(128, input.GetProperty("properties").GetProperty("instance_id").GetProperty("maxLength").GetInt32());
        Assert.Equal(128, input.GetProperty("properties").GetProperty("intent_ref").GetProperty("maxLength").GetInt32());
        Assert.False(input.GetProperty("properties").TryGetProperty("document_id", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("confirm", out _));
        Assert.False(input.GetProperty("properties").TryGetProperty("session_ref", out _));

        var output = tool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "status" }, output.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "started", "already_active", "busy", "unavailable", "terminal" },
            output.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
        var outputText = output.GetRawText();
        Assert.DoesNotContain("session_ref", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("intent_ref", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", outputText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("instance_id")]
    [InlineData("intent_ref")]
    public async Task Required_arguments_are_rejected_when_omitted(string name)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments.Remove(name));
    }

    [Theory]
    [InlineData("instance_id", "")]
    [InlineData("instance_id", " ")]
    [InlineData("intent_ref", "")]
    [InlineData("intent_ref", "bad ref")]
    public async Task Blank_refs_are_rejected_before_discovery(string name, string value)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments[name] = TestSupport.JsonValue(JsonSerializer.Serialize(value)));
    }

    [Fact]
    public async Task Oversized_refs_and_extra_arguments_are_rejected_before_discovery()
    {
        var oversized = new string('a', 129);
        await AssertRejectedBeforeDiscoveryAsync(arguments =>
            arguments["intent_ref"] = TestSupport.JsonValue(JsonSerializer.Serialize(oversized)));
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments["confirm"] = TestSupport.JsonValue("true"));
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments["document_id"] = TestSupport.JsonValue("\"doc\""));
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments["session_ref"] = TestSupport.JsonValue("\"session\""));
    }

    [Fact]
    public async Task Structured_output_contains_only_status()
    {
        var (tool, _, factory) = CreateInvocableTool(ready: true);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.False(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.Equal("started", structured.GetProperty("status").GetString());
        Assert.Equal("status", Assert.Single(structured.EnumerateObject()).Name);
        Assert.Equal("intent-ref", factory.Clients[0].LastRequestParameterUpdateReviewRequest!.IntentRef);
        Assert.DoesNotContain("session_ref", structured.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("revit.request_parameter_update_review", structured.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capability_error_is_mapped_without_a_status_payload()
    {
        var (tool, _, _) = CreateInvocableTool(
            ready: true,
            error: new BridgeException(CapabilityErrorCodes.InvalidApprovalReviewRequest, "The approval review request is invalid."));
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(CapabilityErrorCodes.InvalidApprovalReviewRequest, text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit.request_parameter_update_review", text, StringComparison.Ordinal);
    }

    private static async Task AssertRejectedBeforeDiscoveryAsync(Action<Dictionary<string, JsonElement>> mutate)
    {
        var (tool, discovery, factory) = CreateInvocableTool(ready: true);
        var arguments = ValidArguments();
        mutate(arguments);
        var result = await McpToolInvoke.InvokeAsync(tool, arguments);
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    private static Tool CreateProtocolTool()
    {
        return CreateInvocableTool(ready: false).Tool.ProtocolTool;
    }

    private static (
        McpServerTool Tool,
        FakeDiscovery Discovery,
        RecordingBridgeClientFactory Factory) CreateInvocableTool(bool ready, BridgeException? error = null)
    {
        var discovery = new FakeDiscovery();
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new InvalidOperationException("Bridge invocation should not occur.")
        };
        if (ready)
        {
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 9));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    var registration = TestSupport.CreateRegistration("only", pipe);
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 9)),
                        RequestParameterUpdateReview = (_, _, _) => error is null
                            ? Task.FromResult(new RequestParameterUpdateReviewResult
                            {
                                Status = RequestParameterUpdateReviewStatus.Started
                            })
                            : Task.FromException<RequestParameterUpdateReviewResult>(error)
                    });
                }
            };
        }

        var application = new RequestParameterUpdateReviewApplicationService(discovery, factory, new ServerTimeouts());
        var tool = RequestParameterUpdateReviewToolRegistration.Create(new RequestParameterUpdateReviewMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidArguments()
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = TestSupport.JsonValue("\"only\""),
            ["intent_ref"] = TestSupport.JsonValue("\"intent-ref\"")
        };
    }
}
