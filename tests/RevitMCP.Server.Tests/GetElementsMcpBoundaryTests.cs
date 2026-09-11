using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetElementsMcpBoundaryTests
{
    [Fact]
    public void Get_elements_tool_metadata_matches_server_0003()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(GetElementsToolMetadata.Name, tool.Name);
        Assert.Equal(GetElementsToolMetadata.Title, tool.Title);
        Assert.Equal(GetElementsToolMetadata.Description, tool.Description);
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
            new[] { "document_id", "element_refs", "projection" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var names = schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "instance_id", "document_id", "element_refs", "projection" }, names);

        foreach (var idName in new[] { "instance_id", "document_id" })
        {
            var id = schema.GetProperty("properties").GetProperty(idName);
            Assert.Equal("string", id.GetProperty("type").GetString());
            Assert.False(id.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "guid");
            Assert.False(id.TryGetProperty("minLength", out _));
        }

        var refs = schema.GetProperty("properties").GetProperty("element_refs");
        Assert.Equal(1, refs.GetProperty("minItems").GetInt32());
        Assert.Equal(10, refs.GetProperty("maxItems").GetInt32());
        Assert.True(refs.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal("string", refs.GetProperty("items").GetProperty("type").GetString());
        Assert.False(refs.GetProperty("items").TryGetProperty("format", out var refFormat) && refFormat.GetString() is "uuid" or "guid");
        Assert.False(refs.GetProperty("items").TryGetProperty("pattern", out _));
    }

    [Fact]
    public void Projection_schema_is_closed_and_requires_at_least_one_property()
    {
        var projection = CreateProtocolTool().InputSchema.GetProperty("properties").GetProperty("projection");
        Assert.False(projection.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "fields", "parameter_names" },
            projection.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        var anyOfRequired = projection.GetProperty("anyOf")
            .EnumerateArray()
            .SelectMany(clause => clause.GetProperty("required").EnumerateArray().Select(value => value.GetString()))
            .ToArray();
        Assert.Equal(new[] { "fields", "parameter_names" }, anyOfRequired);

        var fields = projection.GetProperty("properties").GetProperty("fields");
        Assert.Equal(1, fields.GetProperty("minItems").GetInt32());
        Assert.Equal(5, fields.GetProperty("maxItems").GetInt32());
        Assert.True(fields.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(
            new[] { "name", "category_name", "family_name", "type_name", "level_name" },
            fields.GetProperty("items").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());

        var names = projection.GetProperty("properties").GetProperty("parameter_names");
        Assert.Equal(1, names.GetProperty("minItems").GetInt32());
        Assert.Equal(10, names.GetProperty("maxItems").GetInt32());
        Assert.True(names.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(1, names.GetProperty("items").GetProperty("minLength").GetInt32());
        Assert.Equal(256, names.GetProperty("items").GetProperty("maxLength").GetInt32());

        var schemaText = projection.GetRawText();
        Assert.DoesNotContain("wildcard", schemaText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"all\"", schemaText, StringComparison.Ordinal);
        Assert.DoesNotContain("full", schemaText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Output_schema_is_strict_and_models_ok_and_not_found()
    {
        var root = CreateProtocolTool().OutputSchema!.Value;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "elements" },
            root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var context = root.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var elements = root.GetProperty("properties").GetProperty("elements");
        Assert.Equal(1, elements.GetProperty("minItems").GetInt32());
        Assert.Equal(10, elements.GetProperty("maxItems").GetInt32());

        var shapes = elements.GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, shapes.Length);

        var notFound = shapes.Single(shape =>
            shape.GetProperty("properties").GetProperty("status").GetProperty("const").GetString() == "not_found");
        Assert.False(notFound.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "status" },
            notFound.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "element_ref", "status" },
            notFound.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        var ok = shapes.Single(shape =>
            shape.GetProperty("properties").GetProperty("status").GetProperty("const").GetString() == "ok");
        Assert.False(ok.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "name", "category_name", "family_name", "type_name", "level_name", "parameters", "parameters_truncated" }
                .OrderBy(name => name, StringComparer.Ordinal),
            ok.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name is not ("element_ref" or "status"))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            new[] { "parameters_truncated" },
            ok.GetProperty("dependentRequired").GetProperty("parameters").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "parameters" },
            ok.GetProperty("dependentRequired").GetProperty("parameters_truncated").EnumerateArray().Select(value => value.GetString()).ToArray());

        var parameter = ok.GetProperty("properties").GetProperty("parameters");
        Assert.Equal(20, parameter.GetProperty("maxItems").GetInt32());
        var parameterObject = parameter.GetProperty("items");
        Assert.False(parameterObject.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "name", "source", "value_text", "value_truncated" },
            parameterObject.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance", "type" },
            parameterObject.GetProperty("properties").GetProperty("source").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(512, parameterObject.GetProperty("properties").GetProperty("value_text").GetProperty("maxLength").GetInt32());

        var outputText = root.GetRawText();
        Assert.DoesNotContain("element_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("storage", outputText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guid", outputText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("geometry", outputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetElementsResult(
                "instance-1",
                "doc-1",
                TestSupport.CreateOkElement("ref-1", ProjectedString.Of("HRU")),
                TestSupport.CreateNotFoundElement("missing")),
            McpCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "elements" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ref-1", structured.GetProperty("elements")[0].GetProperty("element_ref").GetString());
        Assert.Equal("ok", structured.GetProperty("elements")[0].GetProperty("status").GetString());
        Assert.Equal("HRU", structured.GetProperty("elements")[0].GetProperty("name").GetString());
        Assert.False(structured.GetProperty("elements")[0].TryGetProperty("category_name", out _));
        Assert.False(structured.GetProperty("elements")[0].TryGetProperty("parameters", out _));
        Assert.Equal("not_found", structured.GetProperty("elements")[1].GetProperty("status").GetString());
        Assert.Equal(
            new[] { "element_ref", "status" },
            structured.GetProperty("elements")[1].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(structured.TryGetProperty("pipe_name", out _));
    }

    [Fact]
    public void Parameter_entries_survive_structured_success()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetElementsResult(
                "i",
                "d",
                TestSupport.CreateOkElement(
                    "ref-1",
                    parameters:
                    [
                        new GetElementParameter
                        {
                            Name = "Mark",
                            Source = GetElementParameterSource.Instance,
                            ValueText = "HRU409",
                            ValueTruncated = false
                        },
                        new GetElementParameter
                        {
                            Name = "Type Mark",
                            Source = GetElementParameterSource.Type,
                            ValueText = null,
                            ValueTruncated = false
                        }
                    ],
                    parametersTruncated: false)),
            "2026-07-28");

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        var parameters = result.StructuredContent!.Value.GetProperty("elements")[0].GetProperty("parameters");
        Assert.Equal(2, parameters.GetArrayLength());
        Assert.Equal("Mark", parameters[0].GetProperty("name").GetString());
        Assert.Equal("instance", parameters[0].GetProperty("source").GetString());
        Assert.Equal("HRU409", parameters[0].GetProperty("value_text").GetString());
        Assert.Equal(JsonValueKind.Null, parameters[1].GetProperty("value_text").ValueKind);
        Assert.False(result.StructuredContent.Value.GetProperty("elements")[0].GetProperty("parameters_truncated").GetBoolean());
    }

    [Fact]
    public void Legacy_protocol_falls_back_to_one_compact_json_text_block()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetElementsResult("instance-1", "doc-1", TestSupport.CreateOkElement("ref-1")),
            "2025-03-26");

        Assert.False(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal("doc-1", document.RootElement.GetProperty("context").GetProperty("document_id").GetString());
        Assert.DoesNotContain('\n', text.Text);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidInspection, "The element inspection request is invalid.")]
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
        Assert.False(document.RootElement.TryGetProperty("elements", out _));
    }

    [Fact]
    public async Task Unexpected_top_level_property_is_rejected_without_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool();
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            ValidGetElementsArguments(extra: ("unexpected", TestSupport.JsonValue("true"))));

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Document_id_typo_is_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments["documentId"] = TestSupport.JsonValue("\"doc-1\"");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Element_refs_typo_is_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments["elementRefs"] = TestSupport.JsonValue("""["ref-1"]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Instance_id_typo_is_rejected_and_not_auto_selected()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments["instanceId"] = TestSupport.JsonValue("\"only\"");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Unexpected_nested_projection_property_is_rejected()
    {
        var (tool, discovery, factory) = CreateInvocableTool();
        var arguments = ValidGetElementsArguments();
        arguments["projection"] = TestSupport.JsonValue(
            """{"fields":["name"],"include_all_parameters":true}""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("element_refs")]
    [InlineData("projection")]
    public async Task Omitted_required_field_is_rejected_before_discovery(string requiredName)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments.Remove(requiredName);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_INSPECTION", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("element_refs")]
    [InlineData("projection")]
    public async Task Explicit_null_required_field_is_rejected_before_discovery(string requiredName)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments[requiredName] = TestSupport.JsonValue("null");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_INSPECTION", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Theory]
    [InlineData("document_id", "1")]
    [InlineData("element_refs", "\"ref-1\"")]
    [InlineData("projection", "\"name\"")]
    public async Task Uninterpretable_required_field_shape_is_rejected_before_discovery(string requiredName, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments[requiredName] = TestSupport.JsonValue(json);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Explicit_null_instance_id_means_unspecified_and_still_executes()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidGetElementsArguments();
        arguments["instance_id"] = TestSupport.JsonValue("null");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Valid_get_elements_arguments_still_execute()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidGetElementsArguments());

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
        Assert.Equal(1, factory.Clients[0].GetElementsCalls);
        var request = factory.Clients[0].LastGetElementsRequest;
        Assert.NotNull(request);
        Assert.Equal("doc-1", request.DocumentId);
        Assert.Equal(new[] { "ref-1" }, request.ElementRefs);
        Assert.Equal(new[] { GetElementField.Name }, request.Projection.Fields);
        Assert.Null(request.Projection.ParameterNames);
        Assert.Null(typeof(GetElementsRequest).GetProperty("InstanceId"));
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
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 4));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 4)),
                        GetElements = (request, _, _) => Task.FromResult(
                            TestSupport.CreateGetElementsResult(
                                "only",
                                request.DocumentId,
                                TestSupport.CreateOkElement(request.ElementRefs[0], ProjectedString.Of("HRU"))))
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

        var application = new GetElementsApplicationService(discovery, factory, new ServerTimeouts());
        var tool = GetElementsToolRegistration.Create(new GetElementsMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidGetElementsArguments(
        params (string Name, JsonElement Value)[] extra)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = TestSupport.JsonValue("\"doc-1\""),
            ["element_refs"] = TestSupport.JsonValue("""["ref-1"]"""),
            ["projection"] = TestSupport.JsonValue("""{"fields":["name"]}""")
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
        Assert.DoesNotContain("elementRefs", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("instanceId", text.Text, StringComparison.Ordinal);
    }
}
