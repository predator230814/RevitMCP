using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetMepTopologyMcpBoundaryTests
{
    [Fact]
    public void Get_mep_topology_tool_metadata_matches_server_0006()
    {
        var tool = CreateProtocolTool();

        Assert.Equal(GetMepTopologyToolMetadata.Name, tool.Name);
        Assert.Equal(GetMepTopologyToolMetadata.Title, tool.Title);
        Assert.Equal(GetMepTopologyToolMetadata.Description, tool.Description);
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
            new[] { "document_id", "seed_element_refs" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "instance_id", "document_id", "seed_element_refs", "domain", "max_depth", "max_elements", "max_edges" },
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        TestSupport.AssertOptionalNullableInstanceId(schema.GetProperty("properties").GetProperty("instance_id"));
        TestSupport.AssertRequiredOpaqueString(schema.GetProperty("properties").GetProperty("document_id"));

        var seeds = schema.GetProperty("properties").GetProperty("seed_element_refs");
        Assert.Equal(1, seeds.GetProperty("minItems").GetInt32());
        Assert.Equal(10, seeds.GetProperty("maxItems").GetInt32());
        Assert.True(seeds.GetProperty("uniqueItems").GetBoolean());
        TestSupport.AssertRequiredOpaqueString(seeds.GetProperty("items"));

        Assert.Equal(
            new[] { "hvac", "piping", "electrical", "cable_tray_conduit" },
            schema.GetProperty("properties").GetProperty("domain").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());

        AssertIntegerRange(schema.GetProperty("properties").GetProperty("max_depth"), defaultValue: 3, minimum: 1, maximum: 10);
        AssertIntegerRange(schema.GetProperty("properties").GetProperty("max_elements"), defaultValue: 100, minimum: 10, maximum: 250);
        AssertIntegerRange(schema.GetProperty("properties").GetProperty("max_edges"), defaultValue: 200, minimum: 10, maximum: 500);
    }

    [Fact]
    public void Output_schema_is_closed_cap_0006_graph()
    {
        var root = CreateProtocolTool().OutputSchema!.Value;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "seeds", "nodes", "edges", "truncated", "truncation_reasons" },
            root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "context", "seeds", "nodes", "edges", "truncated", "truncation_reasons" },
            root.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());

        var context = root.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "instance_id", "document_id" },
            context.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        var seeds = root.GetProperty("properties").GetProperty("seeds");
        Assert.Equal(1, seeds.GetProperty("minItems").GetInt32());
        Assert.Equal(10, seeds.GetProperty("maxItems").GetInt32());
        var seed = seeds.GetProperty("items");
        Assert.False(seed.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "status" },
            seed.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(
            new[] { "ok", "not_found", "no_connectors" },
            seed.GetProperty("properties").GetProperty("status").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray());

        var node = root.GetProperty("properties").GetProperty("nodes").GetProperty("items");
        Assert.False(node.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref", "depth" },
            node.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("integer", node.GetProperty("properties").GetProperty("depth").GetProperty("type").GetString());
        Assert.Equal(0, node.GetProperty("properties").GetProperty("depth").GetProperty("minimum").GetInt32());

        var edge = root.GetProperty("properties").GetProperty("edges").GetProperty("items");
        Assert.False(edge.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "element_ref_a", "element_ref_b", "domains" },
            edge.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var domains = edge.GetProperty("properties").GetProperty("domains");
        Assert.True(domains.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(
            new[] { "hvac", "piping", "electrical", "cable_tray_conduit" },
            domains.GetProperty("items").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());

        Assert.Equal("boolean", root.GetProperty("properties").GetProperty("truncated").GetProperty("type").GetString());
        var reasons = root.GetProperty("properties").GetProperty("truncation_reasons");
        Assert.True(reasons.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(
            new[] { "depth", "elements", "edges" },
            reasons.GetProperty("items").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());

        var outputText = root.GetRawText();
        Assert.DoesNotContain("element_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementId", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("connector_id", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("connector_ref", outputText, StringComparison.Ordinal);
        Assert.DoesNotContain("geometry", outputText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("system_id", outputText, StringComparison.Ordinal);
    }

    [Fact]
    public void Modern_success_uses_structured_content_without_text_duplicate()
    {
        var result = McpCallResultFactory.Success(
            TestSupport.CreateGetMepTopologyResult(
                "instance-1",
                "doc-1",
                [
                    new GetMepTopologySeed { ElementRef = "seed-a", Status = MepTopologySeedStatus.Ok },
                    new GetMepTopologySeed { ElementRef = "seed-b", Status = MepTopologySeedStatus.NotFound },
                    new GetMepTopologySeed { ElementRef = "seed-c", Status = MepTopologySeedStatus.NoConnectors }
                ],
                [
                    new GetMepTopologyNode { ElementRef = "seed-a", Depth = 0 },
                    new GetMepTopologyNode { ElementRef = "neighbor", Depth = 1 }
                ],
                [
                    new GetMepTopologyEdge
                    {
                        ElementRefA = "neighbor",
                        ElementRefB = "seed-a",
                        Domains = [MepTopologyDomain.Hvac, MepTopologyDomain.CableTrayConduit]
                    }
                ],
                truncated: true,
                truncationReasons: [MepTopologyTruncationReason.Depth, MepTopologyTruncationReason.Edges]),
            McpCallResultFactory.StructuredOutputSinceProtocolVersion);

        Assert.False(result.IsError);
        Assert.True(result.StructuredContent.HasValue);
        Assert.Empty(result.Content);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(
            new[] { "context", "seeds", "nodes", "edges", "truncated", "truncation_reasons" },
            structured.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ok", structured.GetProperty("seeds")[0].GetProperty("status").GetString());
        Assert.Equal("not_found", structured.GetProperty("seeds")[1].GetProperty("status").GetString());
        Assert.Equal("no_connectors", structured.GetProperty("seeds")[2].GetProperty("status").GetString());
        Assert.Equal(0, structured.GetProperty("nodes")[0].GetProperty("depth").GetInt32());
        Assert.Equal(
            new[] { "hvac", "cable_tray_conduit" },
            structured.GetProperty("edges")[0].GetProperty("domains").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.True(structured.GetProperty("truncated").GetBoolean());
        Assert.Equal(
            new[] { "depth", "edges" },
            structured.GetProperty("truncation_reasons").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(structured.TryGetProperty("pipe_name", out _));
        Assert.DoesNotContain("connector_id", structured.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("element_id", structured.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.")]
    [InlineData(CapabilityErrorCodes.InvalidMepTopology, "The MEP topology request is invalid.")]
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
        Assert.False(document.RootElement.TryGetProperty("seeds", out _));
        Assert.False(document.RootElement.TryGetProperty("nodes", out _));
        Assert.False(document.RootElement.TryGetProperty("edges", out _));
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
    [InlineData("seedElementRefs", "[\"ref-1\"]")]
    [InlineData("instanceId", "\"only\"")]
    [InlineData("maxDepth", "3")]
    [InlineData("maxElements", "100")]
    [InlineData("maxEdges", "200")]
    [InlineData("Domain", "\"hvac\"")]
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
    [InlineData("seed_element_refs")]
    public async Task Omitted_required_field_is_rejected_before_discovery(string requiredName)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments.Remove(requiredName);

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.DoesNotContain("INVALID_MEP_TOPOLOGY", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("document_id", "null")]
    [InlineData("document_id", "1")]
    [InlineData("seed_element_refs", "null")]
    [InlineData("seed_element_refs", "\"ref-1\"")]
    [InlineData("seed_element_refs", "[]")]
    [InlineData("seed_element_refs", "[null]")]
    [InlineData("seed_element_refs", "[1]")]
    [InlineData("seed_element_refs", "[{\"element_ref\":\"ref-1\"}]")]
    [InlineData("instance_id", "1")]
    [InlineData("domain", "null")]
    [InlineData("domain", "\"HVAC\"")]
    [InlineData("domain", "\"mechanical\"")]
    [InlineData("max_depth", "0")]
    [InlineData("max_depth", "11")]
    [InlineData("max_depth", "1.5")]
    [InlineData("max_depth", "\"3\"")]
    [InlineData("max_elements", "9")]
    [InlineData("max_elements", "251")]
    [InlineData("max_edges", "9")]
    [InlineData("max_edges", "501")]
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
    public async Task Seeds_longer_than_10_are_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var seeds = string.Join(",", Enumerable.Range(1, 11).Select(index => $"\"seed-{index}\""));
        var arguments = ValidArguments();
        arguments["seed_element_refs"] = TestSupport.JsonValue("[" + seeds + "]");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        AssertInvalidRequest(result);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Duplicate_seed_strings_are_rejected_before_discovery()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["seed_element_refs"] = TestSupport.JsonValue("""["e1","e1"]""");

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
        arguments["seed_element_refs"] = TestSupport.JsonValue("""["","\t","  "]""");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Equal(1, discovery.CallCount);
        var request = factory.Clients[0].LastGetMepTopologyRequest;
        Assert.Equal(" ", request!.DocumentId);
        Assert.Equal(new[] { "", "\t", "  " }, request.SeedElementRefs);
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
        Assert.Null(typeof(GetMepTopologyRequest).GetProperty("InstanceId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Explicit_empty_or_whitespace_instance_id_never_auto_selects(string instanceId)
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["instance_id"] = TestSupport.JsonValue(JsonSerializer.Serialize(instanceId));

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.True(result.IsError);
        Assert.Contains(McpToolErrorCodes.InstanceNotFound, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(1, discovery.CallCount);
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public async Task Valid_seed_order_is_preserved_and_omitted_bounds_stay_omitted()
    {
        var (tool, discovery, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["seed_element_refs"] = TestSupport.JsonValue("""["ref-b"," ","ref-a"]""");
        arguments["domain"] = TestSupport.JsonValue("\"piping\"");
        arguments["max_depth"] = TestSupport.JsonValue("4");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        Assert.Equal(1, discovery.CallCount);
        Assert.Equal(1, factory.Clients[0].GetMepTopologyCalls);
        var request = factory.Clients[0].LastGetMepTopologyRequest;
        Assert.Equal("doc-1", request!.DocumentId);
        Assert.Equal(new[] { "ref-b", " ", "ref-a" }, request.SeedElementRefs);
        Assert.Equal(MepTopologyDomain.Piping, request.Domain);
        Assert.Equal(4, request.MaxDepth);
        Assert.Null(request.MaxElements);
        Assert.Null(request.MaxEdges);
        Assert.Null(typeof(GetMepTopologyRequest).GetProperty("InstanceId"));
    }

    [Fact]
    public async Task Omitted_domain_and_bounds_are_not_defaulted_by_the_server()
    {
        var (tool, _, factory) = CreateInvocableTool(readyInstance: true);

        var result = await McpToolInvoke.InvokeAsync(tool, ValidArguments());

        Assert.False(result.IsError);
        var request = factory.Clients[0].LastGetMepTopologyRequest;
        Assert.Null(request!.Domain);
        Assert.Null(request.MaxDepth);
        Assert.Null(request.MaxElements);
        Assert.Null(request.MaxEdges);
    }

    [Fact]
    public async Task Boundary_integer_values_are_mapped_exactly()
    {
        var (tool, _, factory) = CreateInvocableTool(readyInstance: true);
        var arguments = ValidArguments();
        arguments["domain"] = TestSupport.JsonValue("\"cable_tray_conduit\"");
        arguments["max_depth"] = TestSupport.JsonValue("1");
        arguments["max_elements"] = TestSupport.JsonValue("250");
        arguments["max_edges"] = TestSupport.JsonValue("10");

        var result = await McpToolInvoke.InvokeAsync(tool, arguments);

        Assert.False(result.IsError);
        var request = factory.Clients[0].LastGetMepTopologyRequest;
        Assert.Equal(MepTopologyDomain.CableTrayConduit, request!.Domain);
        Assert.Equal(1, request.MaxDepth);
        Assert.Equal(250, request.MaxElements);
        Assert.Equal(10, request.MaxEdges);
    }

    private static void AssertIntegerRange(JsonElement property, int defaultValue, int minimum, int maximum)
    {
        Assert.Equal("integer", property.GetProperty("type").GetString());
        Assert.Equal(defaultValue, property.GetProperty("default").GetInt32());
        Assert.Equal(minimum, property.GetProperty("minimum").GetInt32());
        Assert.Equal(maximum, property.GetProperty("maximum").GetInt32());
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
            discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 7));
            factory = new RecordingBridgeClientFactory
            {
                Connect = (pipe, _, _) =>
                {
                    Assert.Equal("pipe-only", pipe);
                    var registration = TestSupport.CreateRegistration("only", "pipe-only");
                    return Task.FromResult(new RecordingBridgeClient
                    {
                        Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, 7)),
                        GetMepTopology = (request, _, _) => Task.FromResult(
                            TestSupport.CreateGetMepTopologyResult(
                                "only",
                                request.DocumentId,
                                request.SeedElementRefs.Select(seed => new GetMepTopologySeed
                                {
                                    ElementRef = seed,
                                    Status = MepTopologySeedStatus.Ok
                                }).ToArray()))
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

        var application = new GetMepTopologyApplicationService(discovery, factory, new ServerTimeouts());
        var tool = GetMepTopologyToolRegistration.Create(new GetMepTopologyMcpTools(application));
        return (tool, discovery, factory);
    }

    private static Dictionary<string, JsonElement> ValidArguments(
        params (string Name, JsonElement Value)[] extra)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = TestSupport.JsonValue("\"doc-1\""),
            ["seed_element_refs"] = TestSupport.JsonValue("""["ref-1"]""")
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
        Assert.DoesNotContain("seedElementRefs", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_MEP_TOPOLOGY", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", text.Text, StringComparison.Ordinal);
    }
}
