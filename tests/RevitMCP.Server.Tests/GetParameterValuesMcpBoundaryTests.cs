using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetParameterValuesMcpBoundaryTests
{
    [Fact]
    public void Get_parameter_values_tool_metadata_matches_server_0005()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(GetParameterValuesToolMetadata.Name, tool.Name);
        Assert.Equal(GetParameterValuesToolMetadata.Title, tool.Title);
        Assert.Equal(GetParameterValuesToolMetadata.Description, tool.Description);
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
            new[] { "document_id", "reads" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance_id", "document_id", "reads" },
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        TestSupport.AssertOptionalNullableInstanceId(schema.GetProperty("properties").GetProperty("instance_id"));
        TestSupport.AssertRequiredOpaqueString(schema.GetProperty("properties").GetProperty("document_id"));

        var reads = schema.GetProperty("properties").GetProperty("reads");
        Assert.Equal(1, reads.GetProperty("minItems").GetInt32());
        Assert.Equal(50, reads.GetProperty("maxItems").GetInt32());
        Assert.True(reads.GetProperty("uniqueItems").GetBoolean());

        var item = reads.GetProperty("items");
        Assert.Equal("object", item.GetProperty("type").GetString());
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "parameter_ref" },
            item.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        TestSupport.AssertRequiredOpaqueString(item.GetProperty("properties").GetProperty("element_ref"));
        TestSupport.AssertRequiredOpaqueString(item.GetProperty("properties").GetProperty("parameter_ref"));
    }

    [Fact]
    public void Output_schema_is_closed_with_exact_item_and_value_variants()
    {
        var root = CreateProtocolTool().OutputSchema!.Value;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "items" },
            root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "context", "items" },
            root.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        var context = root.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var items = root.GetProperty("properties").GetProperty("items");
        Assert.Equal(1, items.GetProperty("minItems").GetInt32());
        Assert.Equal(50, items.GetProperty("maxItems").GetInt32());

        var variants = items.GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(3, variants.Length);

        var failure = variants[0];
        Assert.False(failure.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status" },
            failure.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "element_not_found", "parameter_ref_not_found", "parameter_not_present", "unsupported_value" },
            failure.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(failure.GetProperty("properties").TryGetProperty("data_type", out _));
        Assert.False(failure.GetProperty("properties").TryGetProperty("has_value", out _));
        Assert.False(failure.GetProperty("properties").TryGetProperty("value", out _));

        var noValue = variants[1];
        Assert.Equal("ok", noValue.GetProperty("properties").GetProperty("status").GetProperty("const").GetString());
        Assert.False(noValue.GetProperty("properties").GetProperty("has_value").GetProperty("const").GetBoolean());
        Assert.False(noValue.GetProperty("properties").TryGetProperty("value", out _));
        AssertDataTypeVariants(noValue.GetProperty("properties").GetProperty("data_type"));

        var valued = variants[2];
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status", "data_type", "has_value", "value" },
            valued.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.True(valued.GetProperty("properties").GetProperty("has_value").GetProperty("const").GetBoolean());
        AssertDataTypeVariants(valued.GetProperty("properties").GetProperty("data_type"));

        var values = valued.GetProperty("properties").GetProperty("value").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(5, values.Length);
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "string"
            && shape.GetProperty("properties").GetProperty("value").GetProperty("maxLength").GetInt32() == 512
            && shape.GetProperty("properties").GetProperty("truncated").GetProperty("type").GetString() == "boolean");
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "integer"
            && shape.GetProperty("properties").GetProperty("value").GetProperty("type").GetString() == "integer");
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "quantity"
            && shape.GetProperty("properties").GetProperty("value").GetProperty("type").GetString() == "number"
            && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "kind", "value", "unit_type_id" }));
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "element_reference"
            && shape.GetProperty("properties").GetProperty("resolved").GetProperty("const").GetBoolean() == false
            && !shape.GetProperty("properties").TryGetProperty("element_ref", out _));
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "element_reference"
            && shape.GetProperty("properties").GetProperty("resolved").GetProperty("const").GetBoolean()
            && shape.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32() == 512
            && shape.GetProperty("properties").TryGetProperty("element_ref", out _));

        var outputText = root.GetRawText();
        Assert.DoesNotContain("element_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementId", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("internal", outputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetParameterValuesResult(
                "instance-1",
                "doc-1",
                TestSupport.CreateParameterValueOk(
                    "ref-1",
                    "pref-1",
                    new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                        ForgeTypeId = "autodesk.spec.aec.hvac:airFlow-2.0.0"
                    },
                    hasValue: true,
                    new GetParameterQuantityValue
                    {
                        Value = 100,
                        UnitTypeId = "autodesk.unit.unit:cubicFeetPerMinute-1.0.1"
                    }),
                TestSupport.CreateParameterValueOk("ref-1", "pref-empty"),
                TestSupport.CreateParameterValueFailure("missing", "pref-1", GetParameterValueStatus.ElementNotFound)),
            McpCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "items" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ok", structured.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("quantity", structured.GetProperty("items")[0].GetProperty("value").GetProperty("kind").GetString());
        Assert.False(structured.GetProperty("items")[1].TryGetProperty("value", out _));
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status" },
            structured.GetProperty("items")[2].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(structured.TryGetProperty("pipe_name", out _));
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidParameterRead, "The parameter value request is invalid.")]
    [InlineData(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.")]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout, "The Revit inspection request timed out.")]
    [InlineData(CapabilityErrorCodes.ExecutionFailed, "The Revit inspection could not be executed.")]
    [InlineData(McpToolErrorCodes.NoRevitInstance, ToolErrorMessages.NoRevitInstance)]
    [InlineData(McpToolErrorCodes.InvalidRequest, ToolErrorMessages.InvalidRequest)]
    public void Error_result_is_compact_json_text_without_structured_content(string code, string message)
    {
        var result = McpCallResultFactory.Error(code, message);

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Unexpected_top_level_property_is_rejected_without_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool();
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            ValidArguments(extra: ("unexpected", TestSupport.JsonValue("true"))));

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("documentId", "\"doc-1\"")]
    [InlineData("elementRef", "\"ref-1\"")]
    [InlineData("parameterRef", "\"pref-1\"")]
    [InlineData("instanceId", "\"only\"")]
    public async Task Alias_or_casing_is_rejected_before_discovery(string name, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments[name] = TestSupport.JsonValue(json);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("reads")]
    public async Task Omitted_required_field_is_rejected_before_discovery(string requiredName)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments.Remove(requiredName);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_PARAMETER_READ", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("document_id", "null")]
    [InlineData("document_id", "1")]
    [InlineData("reads", "null")]
    [InlineData("reads", "\"ref-1\"")]
    [InlineData("reads", "[]")]
    [InlineData("reads", """[{"element_ref":null,"parameter_ref":"p1"}]""")]
    [InlineData("reads", """[{"element_ref":"e1","parameter_ref":null}]""")]
    [InlineData("reads", """[{"element_ref":1,"parameter_ref":"p1"}]""")]
    [InlineData("reads", """[{"parameter_ref":"p1"}]""")]
    [InlineData("reads", """[{"element_ref":"e1"}]""")]
    [InlineData("reads", """[{"element_ref":"e1","parameter_ref":"p1","extra":true}]""")]
    [InlineData("instance_id", "1")]
    public async Task Malformed_input_is_rejected_before_discovery(string name, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments[name] = TestSupport.JsonValue(json);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Reads_longer_than_50_are_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var pairs = string.Join(",", Enumerable.Range(1, 51).Select(index =>
            $$"""{"element_ref":"e{{index}}","parameter_ref":"p{{index}}"}"""));
        var arguments = ValidArguments();
        arguments["reads"] = TestSupport.JsonValue("[" + pairs + "]");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Duplicate_semantic_pairs_are_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["reads"] = TestSupport.JsonValue(
            """[{"element_ref":"e1","parameter_ref":"p1"},{"element_ref":"e1","parameter_ref":"p1"}]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Property_order_duplicate_pairs_are_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["reads"] = TestSupport.JsonValue(
            """[{"element_ref":"e1","parameter_ref":"p1"},{"parameter_ref":"p1","element_ref":"e1"}]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Empty_and_whitespace_opaque_strings_are_accepted_mcp_syntax()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["document_id"] = TestSupport.JsonValue("\" \"");
        arguments["reads"] = TestSupport.JsonValue(
            """[{"element_ref":"","parameter_ref":"\t"}]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        var request = factory.Clients[0].LastGetParameterValuesRequest;
        Assert.Equal(" ", request!.DocumentId);
        Assert.Equal("", request.Reads[0].ElementRef);
        Assert.Equal("\t", request.Reads[0].ParameterRef);
    }

    [Fact]
    public async Task Explicit_null_instance_id_means_unspecified_and_still_executes()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["instance_id"] = TestSupport.JsonValue("null");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
        Assert.Null(typeof(GetParameterValuesRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Valid_pair_order_is_preserved_and_instance_id_is_excluded()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["reads"] = TestSupport.JsonValue(
            """[{"element_ref":"ref-b","parameter_ref":"pref-2"},{"element_ref":"ref-a","parameter_ref":"pref-1"}]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(1, factory.Clients[0].GetParameterValuesCalls);
        var request = factory.Clients[0].LastGetParameterValuesRequest;
        Assert.Equal("doc-1", request!.DocumentId);
        Assert.Equal(new[] { "ref-b", "ref-a" }, request.Reads.Select(read => read.ElementRef));
        Assert.Equal(new[] { "pref-2", "pref-1" }, request.Reads.Select(read => read.ParameterRef));
        Assert.Null(typeof(GetParameterValuesRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public void All_four_typed_values_and_datatype_variants_serialize()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetParameterValuesResult(
                "i",
                "d",
                TestSupport.CreateParameterValueOk(
                    "e1",
                    "p1",
                    new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Spec, ForgeTypeId = "autodesk.spec:spec.string-2.0.0" },
                    hasValue: true,
                    new GetParameterStringValue { Value = "Mark", Truncated = false }),
                TestSupport.CreateParameterValueOk(
                    "e1",
                    "p2",
                    new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Unknown },
                    hasValue: true,
                    new GetParameterIntegerValue { Value = 0 }),
                TestSupport.CreateParameterValueOk(
                    "e1",
                    "p3",
                    new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                        ForgeTypeId = "autodesk.spec.aec.hvac:airFlow-2.0.0"
                    },
                    hasValue: true,
                    new GetParameterQuantityValue
                    {
                        Value = 100,
                        UnitTypeId = "autodesk.unit.unit:cubicFeetPerMinute-1.0.1"
                    }),
                TestSupport.CreateParameterValueOk(
                    "e1",
                    "p4",
                    new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Category, ForgeTypeId = "autodesk.revit.category:ost-phases" },
                    hasValue: true,
                    new GetParameterElementReferenceValue { Resolved = true, Name = "New Construction", ElementRef = "phase-1" }),
                TestSupport.CreateParameterValueOk(
                    "e1",
                    "p5",
                    hasValue: true,
                    value: new GetParameterElementReferenceValue { Resolved = false })),
            "2026-07-28");

        var items = result.StructuredContent!.Value.GetProperty("items");
        Assert.Equal("string", items[0].GetProperty("value").GetProperty("kind").GetString());
        Assert.Equal("integer", items[1].GetProperty("value").GetProperty("kind").GetString());
        Assert.Equal("quantity", items[2].GetProperty("value").GetProperty("kind").GetString());
        Assert.False(items[2].GetProperty("value").TryGetProperty("internal", out _));
        Assert.True(items[3].GetProperty("value").GetProperty("resolved").GetBoolean());
        Assert.False(items[4].GetProperty("value").GetProperty("resolved").GetBoolean());
        Assert.False(items[4].GetProperty("value").TryGetProperty("element_ref", out _));
        Assert.DoesNotContain("element_id", result.StructuredContent.Value.GetRawText(), StringComparison.Ordinal);
    }

    private static void AssertDataTypeVariants(JsonElement dataType)
    {
        var variants = dataType.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(4, variants.Length);
        foreach (var kind in new[] { "measurable_spec", "spec", "category" })
        {
            Assert.Contains(variants, shape =>
                shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind
                && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                    .SequenceEqual(new[] { "kind", "forge_type_id" }));
        }

        var unknown = Assert.Single(variants, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "unknown");
        Assert.Equal(new[] { "kind" }, unknown.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.True(unknown.GetProperty("properties").TryGetProperty("forge_type_id", out _));
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
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 6));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 6)),
                        GetParameterValues = (request, _, _) => Task.FromResult(
                            TestSupport.CreateGetParameterValuesResult(
                                "only",
                                request.DocumentId,
                                TestSupport.CreateParameterValueOk(
                                    request.Reads[0].ElementRef,
                                    request.Reads[0].ParameterRef)))
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

        var application = new GetParameterValuesApplicationService(discovery, factory, new ServerTimeouts());
        var tool = GetParameterValuesToolRegistration.Create(new GetParameterValuesMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidArguments(
        params (string Name, JsonElement Value)[] extra)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = TestSupport.JsonValue("\"doc-1\""),
            ["reads"] = TestSupport.JsonValue("""[{"element_ref":"ref-1","parameter_ref":"pref-1"}]""")
        };

        foreach (var (name, value) in extra)
        {
            arguments[name] = value;
        }

        return arguments;
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
        Assert.DoesNotContain("documentId", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("elementRef", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("parameterRef", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_PARAMETER_READ", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", text.Text, StringComparison.Ordinal);
    }
}
