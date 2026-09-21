using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class DescribeParametersMcpBoundaryTests
{
    [Fact]
    public void Describe_parameters_tool_metadata_matches_server_0004()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(DescribeParametersToolMetadata.Name, tool.Name);
        Assert.Equal(DescribeParametersToolMetadata.Title, tool.Title);
        Assert.Equal(DescribeParametersToolMetadata.Description, tool.Description);
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
            new[] { "document_id", "element_refs" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var names = schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "instance_id", "document_id", "element_refs", "source", "name_contains", "limit" }, names);

        TestSupport.AssertOptionalNullableInstanceId(schema.GetProperty("properties").GetProperty("instance_id"));
        TestSupport.AssertRequiredOpaqueString(schema.GetProperty("properties").GetProperty("document_id"));

        var refs = schema.GetProperty("properties").GetProperty("element_refs");
        Assert.Equal(1, refs.GetProperty("minItems").GetInt32());
        Assert.Equal(10, refs.GetProperty("maxItems").GetInt32());
        Assert.True(refs.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal("string", refs.GetProperty("items").GetProperty("type").GetString());
        Assert.False(refs.GetProperty("items").TryGetProperty("format", out var refFormat) && refFormat.GetString() is "uuid" or "guid");
        Assert.False(refs.GetProperty("items").TryGetProperty("pattern", out _));

        var source = schema.GetProperty("properties").GetProperty("source");
        Assert.Equal(
            new[] { "instance", "type", "both" },
            source.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("both", source.GetProperty("default").GetString());

        var nameContains = schema.GetProperty("properties").GetProperty("name_contains");
        Assert.Equal(1, nameContains.GetProperty("minLength").GetInt32());
        Assert.Equal(256, nameContains.GetProperty("maxLength").GetInt32());

        var limit = schema.GetProperty("properties").GetProperty("limit");
        Assert.Equal("integer", limit.GetProperty("type").GetString());
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());
        Assert.Equal(50, limit.GetProperty("default").GetInt32());
    }

    [Fact]
    public void Output_schema_is_closed_with_exact_identity_and_data_type_variants()
    {
        var root = CreateProtocolTool().OutputSchema!.Value;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "elements", "matched_count", "truncated", "parameters" },
            root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "context", "elements", "matched_count", "truncated", "parameters" },
            root.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        var context = root.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var elements = root.GetProperty("properties").GetProperty("elements");
        Assert.Equal(1, elements.GetProperty("minItems").GetInt32());
        Assert.Equal(10, elements.GetProperty("maxItems").GetInt32());
        var element = elements.GetProperty("items");
        Assert.False(element.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "ok", "not_found" },
            element.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());

        Assert.Equal(0, root.GetProperty("properties").GetProperty("matched_count").GetProperty("minimum").GetInt32());
        Assert.Equal("boolean", root.GetProperty("properties").GetProperty("truncated").GetProperty("type").GetString());

        var parameters = root.GetProperty("properties").GetProperty("parameters");
        Assert.Equal(100, parameters.GetProperty("maxItems").GetInt32());
        var descriptor = parameters.GetProperty("items");
        Assert.False(descriptor.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "parameter_ref", "name", "source", "identity", "data_type", "present_on_count", "read_only_on_count" },
            descriptor.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance", "type" },
            descriptor.GetProperty("properties").GetProperty("source").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(1, descriptor.GetProperty("properties").GetProperty("present_on_count").GetProperty("minimum").GetInt32());
        Assert.Equal(10, descriptor.GetProperty("properties").GetProperty("present_on_count").GetProperty("maximum").GetInt32());
        Assert.Equal(0, descriptor.GetProperty("properties").GetProperty("read_only_on_count").GetProperty("minimum").GetInt32());
        Assert.Equal(10, descriptor.GetProperty("properties").GetProperty("read_only_on_count").GetProperty("maximum").GetInt32());

        var identities = descriptor.GetProperty("properties").GetProperty("identity").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(3, identities.Length);
        Assert.Contains(identities, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "built_in"
            && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "kind", "parameter_type_id" })
            && !shape.GetProperty("properties").TryGetProperty("guid", out _));
        Assert.Contains(identities, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "shared"
            && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "kind", "guid" })
            && !shape.GetProperty("properties").TryGetProperty("parameter_type_id", out _));
        Assert.Contains(identities, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "local"
            && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "kind" })
            && !shape.GetProperty("properties").TryGetProperty("guid", out _)
            && !shape.GetProperty("properties").TryGetProperty("parameter_type_id", out _));
        Assert.All(identities, shape =>
        {
            Assert.False(shape.GetProperty("additionalProperties").GetBoolean());
            Assert.False(shape.GetRawText().Contains("uuid", StringComparison.OrdinalIgnoreCase));
        });

        var dataTypes = descriptor.GetProperty("properties").GetProperty("data_type").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(4, dataTypes.Length);
        foreach (var kind in new[] { "measurable_spec", "spec", "category" })
        {
            Assert.Contains(dataTypes, shape =>
                shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == kind
                && shape.GetProperty("required").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "kind", "forge_type_id" }));
        }

        var unknown = Assert.Single(dataTypes, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "unknown");
        Assert.Equal(new[] { "kind" }, unknown.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.True(unknown.GetProperty("properties").TryGetProperty("forge_type_id", out _));

        var outputText = root.GetRawText();
        Assert.DoesNotContain("value_text", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("element_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("storage", outputText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("formula", outputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateDescribeParametersResult(
                "instance-1",
                "doc-1",
                [TestSupport.CreateDescribeOkElement("ref-1"), TestSupport.CreateDescribeNotFoundElement("missing")],
                matchedCount: 1,
                truncated: false,
                TestSupport.CreateBuiltInDescriptor()),
            McpCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "elements", "matched_count", "truncated", "parameters" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ok", structured.GetProperty("elements")[0].GetProperty("status").GetString());
        Assert.Equal("not_found", structured.GetProperty("elements")[1].GetProperty("status").GetString());
        Assert.Equal(
            new[] { "element_ref", "status" },
            structured.GetProperty("elements")[1].EnumerateObject().Select(property => property.Name).ToArray());

        var identity = structured.GetProperty("parameters")[0].GetProperty("identity");
        Assert.Equal(
            new[] { "kind", "parameter_type_id" },
            identity.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("built_in", identity.GetProperty("kind").GetString());
        Assert.False(identity.TryGetProperty("guid", out _));

        var dataType = structured.GetProperty("parameters")[0].GetProperty("data_type");
        Assert.Equal("measurable_spec", dataType.GetProperty("kind").GetString());
        Assert.False(structured.GetProperty("parameters")[0].TryGetProperty("value_text", out _));
        Assert.False(structured.TryGetProperty("pipe_name", out _));
    }

    [Fact]
    public void Shared_local_and_unknown_identity_shapes_omit_irrelevant_fields()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateDescribeParametersResult(
                "i",
                "d",
                [TestSupport.CreateDescribeOkElement("ref-1")],
                matchedCount: 3,
                truncated: false,
                new DescribeParameterDescriptor
                {
                    ParameterRef = "pref-shared",
                    Name = "Shared",
                    Source = GetElementParameterSource.Type,
                    Identity = new DescribeParameterIdentity
                    {
                        Kind = DescribeParameterIdentityKind.Shared,
                        Guid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
                    },
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.Spec,
                        ForgeTypeId = "autodesk.spec.aec:length"
                    },
                    PresentOnCount = 1,
                    ReadOnlyOnCount = 1
                },
                new DescribeParameterDescriptor
                {
                    ParameterRef = "pref-local",
                    Name = "Local",
                    Source = GetElementParameterSource.Instance,
                    Identity = new DescribeParameterIdentity
                    {
                        Kind = DescribeParameterIdentityKind.Local
                    },
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.Unknown
                    },
                    PresentOnCount = 1,
                    ReadOnlyOnCount = 0
                }),
            "2026-07-28");

        var parameters = result.StructuredContent!.Value.GetProperty("parameters");
        var shared = parameters[0].GetProperty("identity");
        Assert.Equal(new[] { "kind", "guid" }, shared.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(shared.TryGetProperty("parameter_type_id", out _));

        var local = parameters[1].GetProperty("identity");
        Assert.Equal(new[] { "kind" }, local.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("unknown", parameters[1].GetProperty("data_type").GetProperty("kind").GetString());
        Assert.False(parameters[1].GetProperty("data_type").TryGetProperty("forge_type_id", out _));
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidParameterDiscovery, "The parameter discovery request is invalid.")]
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
        Assert.False(document.RootElement.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task Unexpected_top_level_property_is_rejected_without_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool();
        var result = await McpToolInvoke.InvokeAsync(
            tool,
            ValidDescribeArguments(extra: ("unexpected", TestSupport.JsonValue("true"))));

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("documentId", "\"doc-1\"")]
    [InlineData("elementRefs", """["ref-1"]""")]
    [InlineData("instanceId", "\"only\"")]
    [InlineData("nameContains", "\"Flow\"")]
    public async Task Alias_or_casing_is_rejected_before_discovery(string name, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments[name] = TestSupport.JsonValue(json);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("element_refs")]
    public async Task Omitted_required_field_is_rejected_before_discovery(string requiredName)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments.Remove(requiredName);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_PARAMETER_DISCOVERY", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("document_id", "null")]
    [InlineData("document_id", "1")]
    [InlineData("element_refs", "null")]
    [InlineData("element_refs", "\"ref-1\"")]
    [InlineData("element_refs", "[]")]
    [InlineData("element_refs", """["ref-1","ref-2","ref-3","ref-4","ref-5","ref-6","ref-7","ref-8","ref-9","ref-10","ref-11"]""")]
    [InlineData("element_refs", """["ref-1","ref-1"]""")]
    [InlineData("element_refs", """[1]""")]
    [InlineData("source", "null")]
    [InlineData("source", "\"instances\"")]
    [InlineData("name_contains", "null")]
    [InlineData("name_contains", "\"\"")]
    [InlineData("limit", "null")]
    [InlineData("limit", "0")]
    [InlineData("limit", "101")]
    [InlineData("limit", "1.5")]
    [InlineData("instance_id", "1")]
    public async Task Malformed_input_is_rejected_before_discovery(string name, string json)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments[name] = TestSupport.JsonValue(json);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Name_contains_longer_than_256_is_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments["name_contains"] = TestSupport.JsonValue("\"" + new string('a', 257) + "\"");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_null_instance_id_means_unspecified_and_still_executes()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments["instance_id"] = TestSupport.JsonValue("null");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
        Assert.Null(factory.Clients[0].LastDescribeParametersRequest!.GetType().GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Omitted_source_and_limit_map_to_transport_neutral_defaults()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidDescribeArguments());

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(1, factory.Clients[0].DescribeParametersCalls);
        var request = factory.Clients[0].LastDescribeParametersRequest;
        Assert.NotNull(request);
        Assert.Equal("doc-1", request.DocumentId);
        Assert.Equal(new[] { "ref-1" }, request.ElementRefs);
        Assert.Equal(DescribeParameterSource.Both, request.Source);
        Assert.Null(request.NameContains);
        Assert.Equal(50, request.Limit);
        Assert.Null(typeof(DescribeParametersRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Explicit_source_and_limit_are_passed_unchanged()
    {
        var (tool, _, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidDescribeArguments();
        arguments["source"] = TestSupport.JsonValue("\"type\"");
        arguments["name_contains"] = TestSupport.JsonValue("\"Flow\"");
        arguments["limit"] = TestSupport.JsonValue("25");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        var request = factory.Clients[0].LastDescribeParametersRequest;
        Assert.Equal(DescribeParameterSource.Type, request!.Source);
        Assert.Equal("Flow", request.NameContains);
        Assert.Equal(25, request.Limit);
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
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 5));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 5)),
                        DescribeParameters = (request, _, _) => Task.FromResult(
                            TestSupport.CreateDescribeParametersResult(
                                "only",
                                request.DocumentId,
                                [TestSupport.CreateDescribeOkElement(request.ElementRefs[0])],
                                matchedCount: 1,
                                truncated: false,
                                TestSupport.CreateBuiltInDescriptor()))
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

        var application = new DescribeParametersApplicationService(discovery, factory, new ServerTimeouts());
        var tool = DescribeParametersToolRegistration.Create(new DescribeParametersMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidDescribeArguments(
        params (string Name, JsonElement Value)[] extra)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = TestSupport.JsonValue("\"doc-1\""),
            ["element_refs"] = TestSupport.JsonValue("""["ref-1"]""")
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
        Assert.DoesNotContain("INVALID_PARAMETER_DISCOVERY", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", text.Text, StringComparison.Ordinal);
    }
}
