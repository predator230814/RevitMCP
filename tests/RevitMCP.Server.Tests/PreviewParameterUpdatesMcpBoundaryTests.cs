using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class PreviewParameterUpdatesMcpBoundaryTests
{
    [Fact]
    public void Preview_tool_metadata_matches_server_0007()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(PreviewParameterUpdatesToolMetadata.Name, tool.Name);
        Assert.Equal("Preview Revit Parameter Updates", tool.Title);
        Assert.Equal(
            "Validate and preview one bounded batch of proposed instance-parameter updates in the active project document, and create an immutable ephemeral intent when every update is eligible. Does not modify the Revit model.",
            tool.Description);
        Assert.NotNull(tool.Annotations);
        Assert.False(tool.Annotations.ReadOnlyHint);
        Assert.False(tool.Annotations.DestructiveHint);
        Assert.False(tool.Annotations.IdempotentHint);
        Assert.False(tool.Annotations.OpenWorldHint);
    }

    [Fact]
    public void Input_schema_is_closed()
    {
        var schema = CreateProtocolTool().InputSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "document_id", "updates" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance_id", "document_id", "updates" },
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        TestSupport.AssertOptionalNullableInstanceId(schema.GetProperty("properties").GetProperty("instance_id"));
        TestSupport.AssertRequiredOpaqueString(schema.GetProperty("properties").GetProperty("document_id"));

        var updates = schema.GetProperty("properties").GetProperty("updates");
        Assert.Equal(1, updates.GetProperty("minItems").GetInt32());
        Assert.Equal(20, updates.GetProperty("maxItems").GetInt32());
        Assert.False(updates.TryGetProperty("uniqueItems", out _));

        var item = updates.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "value" },
            item.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        TestSupport.AssertRequiredOpaqueString(item.GetProperty("properties").GetProperty("element_ref"));
        TestSupport.AssertRequiredOpaqueString(item.GetProperty("properties").GetProperty("parameter_ref"));

        var values = item.GetProperty("properties").GetProperty("value").GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(3, values.Length);
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "string"
            && shape.GetProperty("properties").GetProperty("value").GetProperty("maxLength").GetInt32() == 512);
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "integer"
            && shape.GetProperty("properties").GetProperty("value").GetProperty("minimum").GetInt32() == int.MinValue
            && shape.GetProperty("properties").GetProperty("value").GetProperty("maximum").GetInt32() == int.MaxValue);
        Assert.Contains(values, shape =>
            shape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() == "quantity"
            && shape.GetProperty("properties").GetProperty("unit_type_id").GetProperty("minLength").GetInt32() == 1);
        Assert.DoesNotContain("element_reference", schema.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("clear", schema.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Output_schema_has_explicit_ready_variants_and_closed_items()
    {
        var variants = CreateProtocolTool().OutputSchema!.Value.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, variants.Length);

        var notReady = Assert.Single(variants, shape =>
            shape.GetProperty("properties").GetProperty("ready").GetProperty("const").GetBoolean() == false);
        Assert.False(notReady.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "ready", "items" },
            notReady.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "context", "ready", "items" },
            notReady.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(notReady.GetProperty("properties").TryGetProperty("intent_ref", out _));
        Assert.False(notReady.GetProperty("properties").TryGetProperty("intent_fingerprint", out _));
        Assert.False(notReady.GetProperty("properties").TryGetProperty("expires_at", out _));

        var ready = Assert.Single(variants, shape =>
            shape.GetProperty("properties").GetProperty("ready").GetProperty("const").GetBoolean());
        Assert.False(ready.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "ready", "items", "intent_ref", "intent_fingerprint", "expires_at" },
            ready.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("string", ready.GetProperty("properties").GetProperty("intent_ref").GetProperty("type").GetString());
        Assert.Equal("string", ready.GetProperty("properties").GetProperty("intent_fingerprint").GetProperty("type").GetString());
        Assert.False(ready.GetProperty("properties").GetProperty("intent_ref").TryGetProperty("format", out _));
        Assert.False(ready.GetProperty("properties").GetProperty("intent_ref").TryGetProperty("pattern", out _));
        Assert.Equal("date-time", ready.GetProperty("properties").GetProperty("expires_at").GetProperty("format").GetString());

        var items = notReady.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
        var failure = items[0];
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status" },
            failure.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status" },
            failure.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(
            new[]
            {
                "parameter_ref_not_found",
                "element_not_found",
                "unsupported_parameter_source",
                "parameter_not_present",
                "parameter_not_writable",
                "value_type_mismatch",
                "invalid_unit",
                "unsupported_value"
            },
            failure.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());

        var eligible = items[1];
        Assert.Equal(
            new[] { "ok", "no_change" },
            eligible.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(512, eligible.GetProperty("properties").GetProperty("element_name").GetProperty("maxLength").GetInt32());
        Assert.Equal("ok", ready.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("status").GetProperty("const").GetString());
        AssertDataTypeVariants(ready.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("data_type"));

        var before = eligible.GetProperty("properties").GetProperty("before").GetProperty("oneOf").EnumerateArray().ToArray();
        var absent = Assert.Single(before, shape => shape.GetProperty("properties").GetProperty("has_value").GetProperty("const").GetBoolean() == false);
        Assert.False(absent.GetProperty("properties").TryGetProperty("value", out _));
        var present = Assert.Single(before, shape => shape.GetProperty("properties").GetProperty("has_value").GetProperty("const").GetBoolean());
        Assert.Contains("value", present.GetProperty("required").EnumerateArray().Select(value => value.GetString()));

        var outputText = CreateProtocolTool().OutputSchema!.Value.GetRawText();
        Assert.DoesNotContain("element_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementId", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("guid", outputText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uuid", outputText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("document_id", "null")]
    [InlineData("updates", "null")]
    [InlineData("updates", "[]")]
    public async Task Missing_shape_is_rejected_before_discovery(string name, string json)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments[name] = TestSupport.JsonValue(json));
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("updates")]
    public async Task Omitted_required_field_is_rejected_before_discovery(string requiredName)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments.Remove(requiredName));
    }

    [Fact]
    public async Task More_than_20_updates_are_rejected_before_discovery()
    {
        var updates = string.Join(
            ",",
            Enumerable.Range(0, 21).Select(index =>
                $$$"""{"element_ref":"e{{{index}}}","parameter_ref":"p","value":{"kind":"string","value":"a"}}"""));
        await AssertRejectedBeforeDiscoveryAsync(arguments =>
            arguments["updates"] = TestSupport.JsonValue("[" + updates + "]"));
    }

    [Theory]
    [InlineData("documentId", "\"doc-1\"")]
    [InlineData("elementRef", "\"ref-1\"")]
    [InlineData("parameterRef", "\"pref-1\"")]
    [InlineData("unitTypeId", "\"feet\"")]
    [InlineData("instanceId", "\"only\"")]
    public async Task Alias_is_rejected_before_discovery(string name, string json)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments[name] = TestSupport.JsonValue(json), readyInstance: true);
    }

    [Fact]
    public async Task Extra_top_level_update_and_value_properties_are_rejected_before_discovery()
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments["unexpected"] = TestSupport.JsonValue("true"), readyInstance: true);
        await AssertRejectedBeforeDiscoveryAsync(arguments =>
            arguments["updates"] = TestSupport.JsonValue(
                """[{"element_ref":"e","parameter_ref":"p","value":{"kind":"string","value":"a"},"extra":true}]"""),
            readyInstance: true);
        await AssertRejectedBeforeDiscoveryAsync(arguments =>
            arguments["updates"] = TestSupport.JsonValue(
                """[{"element_ref":"e","parameter_ref":"p","value":{"kind":"string","value":"a","extra":true}}]"""),
            readyInstance: true);
    }

    [Theory]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"element_reference","value":"x"}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"quantity","value":1,"unit_type_id":""}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"integer","value":1.5}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"integer","value":2147483648}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"integer","value":-2147483649}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"string","value":"a"}},{"element_ref":"e","parameter_ref":"p","value":{"kind":"string","value":"a"}}]""")]
    [InlineData("""[{"element_ref":"e","parameter_ref":"p","value":{"kind":"string","value":"a"}},{"element_ref":"e","parameter_ref":"p","value":{"kind":"integer","value":2}}]""")]
    public async Task Malformed_values_and_duplicate_pairs_are_rejected_before_discovery(string updates)
    {
        await AssertRejectedBeforeDiscoveryAsync(arguments => arguments["updates"] = TestSupport.JsonValue(updates), readyInstance: true);
    }

    [Fact]
    public async Task String_longer_than_512_is_rejected_before_discovery()
    {
        var value = new string('a', 513);
        var updates = "[{\"element_ref\":\"e\",\"parameter_ref\":\"p\",\"value\":{\"kind\":\"string\",\"value\":"
            + JsonSerializer.Serialize(value) + "}}]";
        await AssertRejectedBeforeDiscoveryAsync(
            arguments => arguments["updates"] = TestSupport.JsonValue(updates),
            readyInstance: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Empty_or_whitespace_document_id_and_refs_are_preserved(string token)
    {
        var (tool, _, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["document_id"] = TestSupport.JsonValue(JsonSerializer.Serialize(token));
        arguments["updates"] = TestSupport.JsonValue(
            "[{\"element_ref\":" + JsonSerializer.Serialize(token)
            + ",\"parameter_ref\":" + JsonSerializer.Serialize(token)
            + ",\"value\":{\"kind\":\"string\",\"value\":\"\"}}]");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        var request = factory.Clients[0].LastPreviewParameterUpdatesRequest;
        Assert.NotNull(request);
        Assert.Equal(token, request.DocumentId);
        Assert.Equal(token, request.Updates[0].ElementRef);
        Assert.Equal(token, request.Updates[0].ParameterRef);
        Assert.Equal("", Assert.IsType<PreviewParameterStringValue>(request.Updates[0].Value).Value);
    }

    [Fact]
    public async Task Null_instance_id_is_unspecified_and_explicit_blank_ids_do_not_auto_select()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var omitted = await McpToolInvoke.InvokeAsync(tool, ValidArguments());
        Assert.False(omitted.IsError);
        Assert.Equal(1, discovery.CallCount);
        Assert.Single(factory.RequestedPipes);

        foreach (var instanceId in new[] { "", " ", "\t" })
        {
            var arguments = ValidArguments();
            arguments["instance_id"] = TestSupport.JsonValue(JsonSerializer.Serialize(instanceId));
            var result = await McpToolInvoke.InvokeAsync(tool, arguments);
            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains(McpToolErrorCodes.InstanceNotFound, text, StringComparison.Ordinal);
        }

        Assert.Single(factory.RequestedPipes);
    }

    [Fact]
    public async Task Value_variants_preserve_order_and_omit_instance_id_from_the_contract_request()
    {
        var (tool, _, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["instance_id"] = TestSupport.JsonValue("\"only\"");
        arguments["updates"] = TestSupport.JsonValue(
            """
            [
              {"element_ref":"e2","parameter_ref":"p2","value":{"kind":"quantity","value":-0.0,"unit_type_id":"unit"}},
              {"element_ref":"e0","parameter_ref":"p0","value":{"kind":"string","value":"kept"}},
              {"element_ref":"e1","parameter_ref":"p1","value":{"kind":"integer","value":2147483647}}
            ]
            """);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        var request = factory.Clients[0].LastPreviewParameterUpdatesRequest!;
        Assert.Null(typeof(PreviewParameterUpdatesRequest).GetProperty("InstanceId"));
        Assert.Equal(new[] { "e2", "e0", "e1" }, request.Updates.Select(update => update.ElementRef));
        var quantity = Assert.IsType<PreviewParameterQuantityValue>(request.Updates[0].Value);
        Assert.Equal(-0.0, quantity.Value);
        Assert.Equal("unit", quantity.UnitTypeId);
        Assert.Equal("kept", Assert.IsType<PreviewParameterStringValue>(request.Updates[1].Value).Value);
        Assert.Equal(int.MaxValue, Assert.IsType<PreviewParameterIntegerValue>(request.Updates[2].Value).Value);
    }

    [Fact]
    public async Task Ready_false_is_success_without_intent_fields()
    {
        var (tool, _, _) = CreateInvocableTool(readyInstance: true, ready: false);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "items", "ready" },
            structured.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.False(structured.GetProperty("ready").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "parameter_ref", "status" },
            structured.GetProperty("items")[0].EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task Ready_true_requires_intent_fields_and_full_ok_items()
    {
        var (tool, _, _) = CreateInvocableTool(readyInstance: true, ready: true);
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        var structured = result.StructuredContent!.Value;
        Assert.True(structured.GetProperty("ready").GetBoolean());
        Assert.Equal("intent-1", structured.GetProperty("intent_ref").GetString());
        Assert.Equal("fingerprint-1", structured.GetProperty("intent_fingerprint").GetString());
        Assert.EndsWith("Z", structured.GetProperty("expires_at").GetString(), StringComparison.Ordinal);
        var item = structured.GetProperty("items")[0];
        Assert.Equal("ok", item.GetProperty("status").GetString());
        Assert.Equal("measurable_spec", item.GetProperty("data_type").GetProperty("kind").GetString());
        Assert.False(item.GetProperty("before").TryGetProperty("value", out _));
        Assert.False(item.GetProperty("before").GetProperty("has_value").GetBoolean());
        Assert.Equal("proposed", item.GetProperty("proposed").GetProperty("value").GetString());
        Assert.DoesNotContain("element_id", structured.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capability_errors_are_not_success_shaped()
    {
        var (tool, _, _) = CreateInvocableTool(
            readyInstance: true,
            error: new BridgeException(CapabilityErrorCodes.IntentCapacityReached, "The preview intent capacity has been reached."));
        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains(CapabilityErrorCodes.IntentCapacityReached, text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ready\"", text, StringComparison.Ordinal);
    }

    private static async Task AssertRejectedBeforeDiscoveryAsync(
        Action<Dictionary<string, JsonElement>> mutate,
        bool readyInstance = true)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance);
        var arguments = ValidArguments();
        mutate(arguments);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
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
    }

    private static Tool CreateProtocolTool()
    {
        return CreateInvocableTool().Tool.ProtocolTool;
    }

    private static (
        McpServerTool Tool,
        FakeDiscovery Discovery,
        RecordingBridgeClientFactory Factory) CreateInvocableTool(
        bool readyInstance = false,
        bool ready = false,
        BridgeException? error = null)
    {
        var discovery = new FakeDiscovery();
        RecordingBridgeClientFactory factory;
        if (readyInstance)
        {
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 8));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 8)),
                        PreviewParameterUpdates = (request, _, _) =>
                        {
                            if (error is not null)
                            {
                                return Task.FromException<PreviewParameterUpdatesResult>(error);
                            }

                            var items = ready
                                ? new[]
                                {
                                    TestSupport.CreatePreviewEligibleItem(
                                        request.Updates[0].ElementRef,
                                        request.Updates[0].ParameterRef,
                                        PreviewParameterUpdateStatus.Ok,
                                        request.Updates[0].Value,
                                        dataType: new DescribeParameterDataType
                                        {
                                            Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                                            ForgeTypeId = "autodesk.spec.aec:length-2.0.0"
                                        })
                                }
                                : new[]
                                {
                                    TestSupport.CreatePreviewFailureItem(
                                        request.Updates[0].ElementRef,
                                        request.Updates[0].ParameterRef,
                                        PreviewParameterUpdateStatus.ElementNotFound)
                                };
                            return Task.FromResult(TestSupport.CreatePreviewParameterUpdatesResult(
                                "only",
                                request.DocumentId,
                                ready,
                                items,
                                ready ? "intent-1" : null,
                                ready ? "fingerprint-1" : null,
                                ready ? new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero) : null));
                        }
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

        var application = new PreviewParameterUpdatesApplicationService(discovery, factory, new ServerTimeouts());
        var tool = PreviewParameterUpdatesToolRegistration.Create(new PreviewParameterUpdatesMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidArguments()
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = TestSupport.JsonValue("\"doc-1\""),
            ["updates"] = TestSupport.JsonValue(
                """[{"element_ref":"ref-1","parameter_ref":"pref-1","value":{"kind":"string","value":"proposed"}}]""")
        };
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
    }
}
