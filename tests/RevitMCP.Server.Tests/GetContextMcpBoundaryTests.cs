using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetContextMcpBoundaryTests
{
    [Fact]
    public void Tools_list_exposes_exactly_one_accepted_revit_tool()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(GetContextToolMetadata.Name, tool.Name);
        Assert.Equal(GetContextToolMetadata.Title, tool.Title);
        Assert.Equal(GetContextToolMetadata.Description, tool.Description);
        Assert.NotNull(tool.Annotations);
        Assert.True(tool.Annotations.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
    }

    [Fact]
    public void Input_schema_has_only_optional_opaque_instance_id()
    {
        var schema = CreateProtocolTool().InputSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
        Assert.True(schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False);

        using var properties = schema.GetProperty("properties").EnumerateObject();
        var names = properties.Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "instance_id" }, names);

        var instanceId = schema.GetProperty("properties").GetProperty("instance_id");
        Assert.Equal("string", instanceId.GetProperty("type").GetString());
        Assert.False(instanceId.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");
    }

    [Fact]
    public void Output_schema_matches_cap_0001()
    {
        var schema = CreateProtocolTool().OutputSchema;
        Assert.NotNull(schema);
        Assert.Equal(JsonValueKind.Object, schema.Value.ValueKind);

        var root = schema.Value;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        var required = root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "instance", "document", "active_view", "selection" }, required);

        var instanceRequired = root.GetProperty("properties").GetProperty("instance").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "instance_id", "revit_version", "revit_build" }, instanceRequired);
        Assert.False(root.GetProperty("properties").GetProperty("instance").GetProperty("additionalProperties").GetBoolean());

        AssertNullableObject(root.GetProperty("properties").GetProperty("document"));
        AssertNullableObject(root.GetProperty("properties").GetProperty("active_view"));

        var documentObject = root.GetProperty("properties").GetProperty("document").GetProperty("anyOf")[1];
        Assert.False(documentObject.GetProperty("additionalProperties").GetBoolean());
        var documentRequired = documentObject.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(
            new[] { "title", "kind", "is_workshared", "is_model_in_cloud", "is_read_only", "is_modified" },
            documentRequired);
        Assert.Equal(
            new[] { "project", "family" },
            documentObject.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());

        var viewObject = root.GetProperty("properties").GetProperty("active_view").GetProperty("anyOf")[1];
        Assert.False(viewObject.GetProperty("additionalProperties").GetBoolean());

        var selection = root.GetProperty("properties").GetProperty("selection");
        Assert.False(selection.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(0, selection.GetProperty("properties").GetProperty("count").GetProperty("minimum").GetInt32());
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = GetContextCallResultFactory.Success(
            TestSupport.ZeroDocument("instance-1"),
            GetContextCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("document").ValueKind);
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("active_view").ValueKind);
        Assert.Equal(0, structured.GetProperty("selection").GetProperty("count").GetInt32());
        Assert.False(structured.TryGetProperty("pipe_name", out _));
        Assert.Equal(
            new[] { "instance", "document", "active_view", "selection" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void July_2026_success_does_not_duplicate_json_in_text()
    {
        var result = GetContextCallResultFactory.Success(TestSupport.ZeroDocument("instance-1"), "2026-07-28");

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);
    }

    [Fact]
    public void Legacy_protocol_falls_back_to_one_compact_json_text_block()
    {
        var result = GetContextCallResultFactory.Success(TestSupport.ZeroDocument("instance-1"), "2025-03-26");

        Assert.False(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var block = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(block);
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("document").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("active_view").ValueKind);
        Assert.DoesNotContain('\n', text.Text);
    }

    [Fact]
    public void Error_result_is_compact_json_text_without_structured_content()
    {
        var result = GetContextCallResultFactory.Error(
            McpToolErrorCodes.NoRevitInstance,
            ToolErrorMessages.NoRevitInstance);

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(McpToolErrorCodes.NoRevitInstance, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(ToolErrorMessages.NoRevitInstance, document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("instance", out _));
        Assert.DoesNotContain("at RevitMCP", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pipe", text.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\\\.", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Instance_required_error_includes_minimal_sorted_candidates()
    {
        var result = GetContextCallResultFactory.Error(
            McpToolErrorCodes.InstanceRequired,
            ToolErrorMessages.InstanceRequired,
            [
                new InstanceCandidate { InstanceId = "b", RevitVersion = "2026", RevitBuild = "26.5.0.55" },
                new InstanceCandidate { InstanceId = "a", RevitVersion = "2026", RevitBuild = "26.5.0.55" }
            ]);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        var candidates = document.RootElement.GetProperty("candidates");
        Assert.Equal(2, candidates.GetArrayLength());
        Assert.Equal(
            new[] { "instance_id", "revit_version", "revit_build" },
            candidates[0].EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Outcome_adapter_preserves_success_and_error_shapes()
    {
        var success = GetContextCallResultFactory.FromOutcome(
            GetContextOutcome.Success(TestSupport.ZeroDocument("id")),
            "2026-07-28");
        Assert.False(success.IsError);
        Assert.True(success.StructuredContent.HasValue);

        var error = GetContextCallResultFactory.FromOutcome(
            GetContextOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable),
            "2026-07-28");
        Assert.True(error.IsError);
        Assert.False(error.StructuredContent.HasValue);
    }

    [Fact]
    public async Task Unexpected_or_typo_instance_id_property_is_rejected_without_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool();
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["instanceId"] = TestSupport.JsonValue("\"only\"")
            });

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Unexpected_top_level_property_is_rejected_without_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["instance_id"] = TestSupport.JsonValue("\"only\""),
                ["debug"] = TestSupport.JsonValue("true")
            });

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Valid_get_context_arguments_still_execute()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["instance_id"] = TestSupport.JsonValue("\"only\"")
            });

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
        Assert.Equal(1, factory.Clients[0].GetContextCalls);
    }

    private static Tool CreateProtocolTool()
    {
        return CreateInvocableTool().Tool.ProtocolTool;
    }

    private static (
        McpServerTool Tool,
        FakeDiscovery Discovery,
        RecordingBridgeClientFactory Factory) CreateInvocableTool(bool readyInstance = false)
    {
        var discovery = new FakeDiscovery();
        RecordingBridgeClientFactory factory;
        if (readyInstance)
        {
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only"));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration)),
                        GetContext = (_, _, _) => Task.FromResult(TestSupport.ZeroDocument("only"))
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

        var application = new GetContextApplicationService(discovery, factory, new ServerTimeouts());
        var tool = GetContextToolRegistration.Create(new GetContextMcpTools(application));
        return (tool, discovery, factory);
    }

    private static void AssertInvalidRequest(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(McpToolErrorCodes.InvalidRequest, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(ToolErrorMessages.InvalidRequest, document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("JsonException", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("at RevitMCP", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("instanceId", text.Text, StringComparison.Ordinal);
    }

    private static void AssertNullableObject(JsonElement property)
    {
        var kinds = property.GetProperty("anyOf").EnumerateArray().Select(option => option.GetProperty("type").GetString()).ToArray();
        Assert.Contains("null", kinds);
        Assert.Contains("object", kinds);
    }
}
