using System.Text.Json;
using ModelContextProtocol.Protocol;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class QueryElementsMcpBoundaryTests
{
    [Fact]
    public void Query_tool_metadata_matches_cap_0002()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(QueryElementsToolMetadata.Name, tool.Name);
        Assert.Equal(QueryElementsToolMetadata.Title, tool.Title);
        Assert.Equal(QueryElementsToolMetadata.Description, tool.Description);
        Assert.NotNull(tool.Annotations);
        Assert.True(tool.Annotations.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
    }

    [Fact]
    public void Input_schema_is_strict_and_closed()
    {
        var schema = CreateProtocolTool().InputSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "scope", "filters" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var names = schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "instance_id", "document_id", "scope", "filters", "limit" }, names);

        Assert.Equal(
            new[] { "document", "active_view" },
            schema.GetProperty("properties").GetProperty("scope").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());

        var limit = schema.GetProperty("properties").GetProperty("limit");
        Assert.Equal(50, limit.GetProperty("default").GetInt32());
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());

        foreach (var idName in new[] { "instance_id", "document_id" })
        {
            var id = schema.GetProperty("properties").GetProperty(idName);
            Assert.Equal("string", id.GetProperty("type").GetString());
            Assert.False(id.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");
            Assert.False(id.TryGetProperty("minLength", out _));
        }
    }

    [Fact]
    public void Filter_schema_is_closed_and_bounded()
    {
        var filters = CreateProtocolTool().InputSchema.GetProperty("properties").GetProperty("filters");
        Assert.False(filters.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, filters.GetProperty("minProperties").GetInt32());

        var names = filters.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "category_names", "family_names", "type_names", "level_names", "text_contains" }, names);
        Assert.DoesNotContain("parameter", string.Join(',', names), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bounding", string.Join(',', names), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workset", string.Join(',', names), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phase", string.Join(',', names), StringComparison.OrdinalIgnoreCase);

        foreach (var arrayName in new[] { "category_names", "family_names", "type_names", "level_names" })
        {
            var array = filters.GetProperty("properties").GetProperty(arrayName);
            Assert.Equal(1, array.GetProperty("minItems").GetInt32());
            Assert.Equal(20, array.GetProperty("maxItems").GetInt32());
            Assert.Equal(1, array.GetProperty("items").GetProperty("minLength").GetInt32());
            Assert.Equal(256, array.GetProperty("items").GetProperty("maxLength").GetInt32());
        }

        var text = filters.GetProperty("properties").GetProperty("text_contains");
        Assert.Equal(1, text.GetProperty("minLength").GetInt32());
        Assert.Equal(256, text.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Output_schema_is_strict_and_closed()
    {
        var root = CreateProtocolTool().OutputSchema!.Value;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "matched_count", "truncated", "element_refs" },
            root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var context = root.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(context.GetProperty("properties").GetProperty("instance_id").TryGetProperty("format", out _));

        var refs = root.GetProperty("properties").GetProperty("element_refs");
        Assert.Equal(100, refs.GetProperty("maxItems").GetInt32());
        Assert.True(refs.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal("string", refs.GetProperty("items").GetProperty("type").GetString());
        Assert.False(refs.GetProperty("items").TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");

        var propertyNames = string.Join(',', root.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("name", propertyNames, StringComparison.Ordinal);
        Assert.DoesNotContain("category", propertyNames, StringComparison.Ordinal);
        Assert.DoesNotContain("parameter", propertyNames, StringComparison.Ordinal);
        Assert.DoesNotContain("geometry", propertyNames, StringComparison.Ordinal);
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateQueryResult("instance-1", "doc-1", 0, false),
            McpCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "matched_count", "truncated", "element_refs" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(0, structured.GetProperty("matched_count").GetInt32());
        Assert.False(structured.GetProperty("truncated").GetBoolean());
        Assert.Equal(0, structured.GetProperty("element_refs").GetArrayLength());
        Assert.False(structured.TryGetProperty("pipe_name", out _));
    }

    [Fact]
    public void Bounded_and_truncated_results_do_not_include_element_detail()
    {
        var bounded = McpCallResultFactory.Success(
            TestSupport.CreateQueryResult("i", "d", 2, false, "ref-a", "ref-b"),
            "2026-07-28");
        var truncated = McpCallResultFactory.Success(
            TestSupport.CreateQueryResult("i", "d", 37, true, "ref-a"),
            "2026-07-28");
        var max = McpCallResultFactory.Success(
            TestSupport.CreateQueryResult("i", "d", 150, true, Enumerable.Range(0, 100).Select(i => $"ref-{i:000}").ToArray()),
            "2026-07-28");

        foreach (var result in new[] { bounded, truncated, max })
        {
            Assert.False(result.IsError);
            Assert.Empty(result.Content);
            var structured = result.StructuredContent!.Value;
            Assert.False(structured.TryGetProperty("elements", out _));
            Assert.False(structured.TryGetProperty("parameters", out _));
            Assert.True(structured.GetProperty("element_refs").GetArrayLength() <= 100);
        }
    }

    [Fact]
    public void Legacy_protocol_falls_back_to_one_compact_json_text_block()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateQueryResult("instance-1", "doc-1", 0, false),
            "2025-03-26");

        Assert.False(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(0, document.RootElement.GetProperty("matched_count").GetInt32());
        Assert.DoesNotContain('\n', text.Text);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The active document no longer matches document_id.")]
    [InlineData(CapabilityErrorCodes.InvalidQuery, "The query request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active document is available.")]
    [InlineData(CapabilityErrorCodes.NoActiveView, "The active view cannot be queried.")]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout, "The Revit query timed out.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit query could not be executed.")]
    [InlineData(McpToolErrorCodes.NoRevitInstance, ToolErrorMessages.NoRevitInstance)]
    public void Error_result_is_compact_json_text_without_structured_content(string code, string message)
    {
        var result = McpCallResultFactory.Error(code, message);

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("context", out _));
    }

    [Fact]
    public void Instance_required_error_includes_minimal_candidates()
    {
        var result = McpCallResultFactory.Error(
            McpToolErrorCodes.InstanceRequired,
            ToolErrorMessages.InstanceRequired,
            [
                new InstanceCandidate { InstanceId = "b", RevitVersion = "2026", RevitBuild = "26.5.0.55" }
            ]);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        var candidate = document.RootElement.GetProperty("candidates")[0];
        Assert.Equal(
            new[] { "instance_id", "revit_version", "revit_build" },
            candidate.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static Tool CreateProtocolTool()
    {
        var discovery = new FakeDiscovery();
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new InvalidOperationException("unused")
        };
        var application = new QueryElementsApplicationService(discovery, factory, new ServerTimeouts());
        return QueryElementsToolRegistration.Create(new QueryElementsMcpTools(application)).ProtocolTool;
    }
}
