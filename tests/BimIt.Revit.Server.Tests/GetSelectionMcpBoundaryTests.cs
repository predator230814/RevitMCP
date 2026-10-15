using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class GetSelectionMcpBoundaryTests
{
    [Fact]
    public void Input_and_output_schemas_are_closed()
    {
        var tool = CreateProtocolTool();
        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.False(input.TryGetProperty("required", out _));
        Assert.Equal(
            new[] { "instance_id", "document_id", "limit" },
            input.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        TestSupport.AssertOptionalNullableInstanceId(input.GetProperty("properties").GetProperty("instance_id"));
        TestSupport.AssertOptionalNullableInstanceId(input.GetProperty("properties").GetProperty("document_id"));
        var limit = input.GetProperty("properties").GetProperty("limit");
        Assert.Equal("integer", limit.GetProperty("type").GetString());
        Assert.Equal(25, limit.GetProperty("default").GetInt32());
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());

        var output = tool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "selected_count", "truncated", "element_refs" },
            output.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var context = output.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var refs = output.GetProperty("properties").GetProperty("element_refs");
        Assert.Equal(100, refs.GetProperty("maxItems").GetInt32());
        Assert.Equal("string", refs.GetProperty("items").GetProperty("type").GetString());
        Assert.False(refs.GetProperty("items").TryGetProperty("format", out _));
        Assert.False(refs.GetProperty("items").TryGetProperty("minLength", out _));
    }

    [Theory]
    [InlineData("unexpected", "true")]
    [InlineData("instanceId", "\"only\"")]
    [InlineData("documentId", "\"doc\"")]
    [InlineData("maxElements", "10")]
    [InlineData("instance_id", "1")]
    [InlineData("document_id", "1")]
    [InlineData("limit", "1.5")]
    [InlineData("limit", "0")]
    [InlineData("limit", "101")]
    public async Task Malformed_input_is_invalid_request_before_discovery(string name, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(ready: true);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [name] = TestSupport.JsonValue(json)
        };

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_SELECTION", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("""{ "instance_id": null, "document_id": null }""")]
    [InlineData("""{ "document_id": "" }""")]
    [InlineData("""{ "document_id": " " }""")]
    [InlineData("{}")]
    [InlineData("""{ "limit": 1 }""")]
    [InlineData("""{ "limit": 100 }""")]
    public async Task Accepted_input_reaches_the_capability_unchanged(string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(ready: true);
        using var document = JsonDocument.Parse(json);
        var arguments = document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Equal(1, discovery.CallCount);
        var request = factory.Clients[0].LastGetSelectionRequest!;
        if (document.RootElement.TryGetProperty("document_id", out var documentId) && documentId.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(documentId.GetString(), request.DocumentId);
        }
        else
        {
            Assert.Null(request.DocumentId);
        }

        if (document.RootElement.TryGetProperty("limit", out var limit))
        {
            Assert.Equal(limit.GetInt32(), request.Limit);
        }
        else
        {
            Assert.Null(request.Limit);
        }

        var structured = result.StructuredContent!.Value;
        Assert.Equal(["z-ref", "a-ref"], structured.GetProperty("element_refs").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(4, structured.GetProperty("selected_count").GetInt32());
        Assert.True(structured.GetProperty("truncated").GetBoolean());
    }

    private static Tool CreateProtocolTool()
    {
        return CreateInvocableTool(ready: false).Tool.ProtocolTool;
    }

    private static (McpServerTool Tool, FakeDiscovery Discovery, RecordingBridgeClientFactory Factory) CreateInvocableTool(bool ready)
    {
        var discovery = new FakeDiscovery();
        RecordingBridgeClientFactory factory;
        if (ready)
        {
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 13));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    var registration = TestSupport.CreateRegistration("only", pipe);
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 13)),
                        GetSelection = (_, _, _) => Task.FromResult(new GetSelectionResult
                        {
                            Context = new GetSelectionContext
                            {
                                InstanceId = "only",
                                DocumentId = "document-a"
                            },
                            SelectedCount = 4,
                            Truncated = true,
                            ElementRefs = ["z-ref", "a-ref"]
                        })
                    });
                }
            };
        }
        else
        {
            factory = new RecordingBridgeClientFactory
            {
                Connect = (_, _, _) => throw new InvalidOperationException("Bridge invocation should not occur.")
            };
        }

        var application = new GetSelectionApplicationService(discovery, factory, new ServerTimeouts());
        return (GetSelectionToolRegistration.Create(new GetSelectionMcpTools(application)), discovery, factory);
    }

    private static void AssertInvalidRequest(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(McpToolErrorCodes.InvalidRequest, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(ToolErrorMessages.InvalidRequest, document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("System.", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit.get_selection", text.Text, StringComparison.Ordinal);
    }
}
