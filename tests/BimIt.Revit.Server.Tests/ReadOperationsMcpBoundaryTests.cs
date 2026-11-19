using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class ReadOperationsMcpBoundaryTests
{
    private const string V24 = "id-v24";
    private const string P24 = "pipe-v24";
    private const string V23 = "id-v23";
    private const string P23 = "pipe-v23";

    [Fact]
    public void Lookup_identity_description_and_hints_match_the_accepted_contract()
    {
        var tool = LookupTool().ProtocolTool;
        Assert.Equal("revit_lookup", tool.Name);
        Assert.Equal("Lookup Revit read operations", tool.Title);
        Assert.True(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.Null(tool.Annotations.DestructiveHint);
        Assert.NotNull(tool.OutputSchema);

        var description = tool.Description!;
        var order = new[]
        {
            "BIM-IT registered read operations",
            "does not describe the Revit API",
            "Omit operation to search",
            "query",
            "offset",
            "limit",
            "summaries and no schemas",
            "Pass operation",
            "input and output schemas",
            "contract_version is optional",
            "reachable Revit host is required",
            "active document is not"
        };
        AssertOrder(description, order);
    }

    [Fact]
    public void Read_identity_description_and_hints_match_the_accepted_contract()
    {
        var tool = ReadTool().ProtocolTool;
        Assert.Equal("revit_read", tool.Name);
        Assert.Equal("Read a registered Revit operation", tool.Title);
        Assert.True(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.Null(tool.Annotations.DestructiveHint);
        Assert.NotNull(tool.OutputSchema);

        var description = tool.Description!;
        AssertOrder(
            description,
            [
                "registered read operation",
                "active host project",
                "operation and contract_version come from revit_lookup",
                "no default version",
                "document_id comes from revit_get_context",
                "inputs must match the operation schema",
                "page_size and cursor",
                "pagination is cursor",
                "pagination none",
                "next_cursor",
                "complete is true"
            ]);
    }

    [Fact]
    public void Lookup_input_is_the_closed_envelope_and_output_leaves_schemas_open()
    {
        var input = LookupTool().ProtocolTool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.False(input.TryGetProperty("required", out _));
        Assert.Equal(
            ["contract_version", "instance_id", "limit", "offset", "operation", "query"],
            Names(input.GetProperty("properties")));

        var operation = input.GetProperty("properties").GetProperty("operation");
        Assert.Equal(1, operation.GetProperty("minLength").GetInt32());
        Assert.Equal(64, operation.GetProperty("maxLength").GetInt32());
        Assert.Equal(1, input.GetProperty("properties").GetProperty("contract_version").GetProperty("minimum").GetInt32());
        Assert.Equal(256, input.GetProperty("properties").GetProperty("query").GetProperty("maxLength").GetInt32());
        Assert.Equal(0, input.GetProperty("properties").GetProperty("offset").GetProperty("minimum").GetInt32());
        Assert.Equal(1, input.GetProperty("properties").GetProperty("limit").GetProperty("minimum").GetInt32());
        Assert.Equal(50, input.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32());

        var output = LookupTool().ProtocolTool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["mode", "total", "returned_count", "next_offset", "operations"], Strings(output.GetProperty("required")));
        var descriptor = output.GetProperty("properties").GetProperty("operations").GetProperty("items");
        Assert.False(descriptor.GetProperty("additionalProperties").GetBoolean());
        Assert.DoesNotContain("input_schema", Strings(descriptor.GetProperty("required")));
        Assert.DoesNotContain("output_schema", Strings(descriptor.GetProperty("required")));
        Assert.Equal("object", descriptor.GetProperty("properties").GetProperty("input_schema").GetProperty("type").GetString());
        Assert.False(descriptor.GetProperty("properties").GetProperty("input_schema").TryGetProperty("additionalProperties", out _));
        Assert.False(descriptor.GetProperty("properties").GetProperty("output_schema").TryGetProperty("additionalProperties", out _));
    }

    [Fact]
    public void Read_input_is_closed_except_for_open_inputs_and_result_is_not_closed()
    {
        var input = ReadTool().ProtocolTool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["document_id", "operation", "contract_version", "inputs"], Strings(input.GetProperty("required")));
        Assert.Equal(
            ["contract_version", "cursor", "document_id", "inputs", "instance_id", "operation", "page_size"],
            Names(input.GetProperty("properties")));
        var inputs = input.GetProperty("properties").GetProperty("inputs");
        Assert.Equal("object", inputs.GetProperty("type").GetString());
        Assert.False(inputs.TryGetProperty("additionalProperties", out _));
        Assert.False(inputs.TryGetProperty("properties", out _));
        Assert.Equal(1, input.GetProperty("properties").GetProperty("document_id").GetProperty("minLength").GetInt32());
        Assert.Equal(64, input.GetProperty("properties").GetProperty("operation").GetProperty("maxLength").GetInt32());
        Assert.Equal(1, input.GetProperty("properties").GetProperty("page_size").GetProperty("minimum").GetInt32());
        Assert.Equal(200, input.GetProperty("properties").GetProperty("page_size").GetProperty("maximum").GetInt32());
        Assert.Equal(2048, input.GetProperty("properties").GetProperty("cursor").GetProperty("maxLength").GetInt32());

        var output = ReadTool().ProtocolTool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["context", "operation", "contract_version", "result", "complete", "next_cursor"], Strings(output.GetProperty("required")));
        var result = output.GetProperty("properties").GetProperty("result");
        Assert.Equal("object", result.GetProperty("type").GetString());
        Assert.False(result.TryGetProperty("additionalProperties", out _));
        var context = output.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Unknown_lookup_envelope_property_fails_before_the_bridge()
    {
        var client = Client(V24, P24, 24);
        var result = await InvokeLookup(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = Json("\"rooms\""),
            ["catalogue"] = Json("true")
        });
        AssertInvalidRequest(result);
        Assert.Equal(0, client.LookupReadOperationsCalls);
        Assert.Equal(0, client.ReadOperationCalls);
    }

    [Theory]
    [InlineData("limit", "0")]
    [InlineData("limit", "51")]
    [InlineData("offset", "-1")]
    [InlineData("query", "\"\"")]
    [InlineData("contract_version", "0")]
    public async Task Lookup_values_outside_the_envelope_fail_before_the_bridge(string property, string json)
    {
        var client = Client(V24, P24, 24);
        var result = await InvokeLookup(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [property] = Json(json)
        });
        AssertInvalidRequest(result);
        Assert.Equal(0, client.LookupReadOperationsCalls);
    }

    [Fact]
    public async Task Unknown_read_envelope_property_fails_before_the_bridge()
    {
        var client = Client(V24, P24, 24);
        var result = await InvokeRead(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1"),
            ["inputs"] = Json("{}"),
            ["name_contains"] = Json("\"Shared\"")
        });
        AssertInvalidRequest(result);
        Assert.Equal(0, client.ReadOperationCalls);
        Assert.Equal(0, client.LookupReadOperationsCalls);
    }

    [Theory]
    [InlineData("document_id", "null")]
    [InlineData("operation", "null")]
    [InlineData("contract_version", "null")]
    [InlineData("inputs", "null")]
    [InlineData("page_size", "0")]
    [InlineData("page_size", "201")]
    [InlineData("cursor", "\"\"")]
    public async Task Read_envelope_rejections_happen_before_the_bridge(string property, string json)
    {
        var client = Client(V24, P24, 24);
        var arguments = ReadArguments();
        arguments[property] = Json(json);
        var result = await InvokeRead(client, arguments);
        AssertInvalidRequest(result);
        Assert.Equal(0, client.ReadOperationCalls);
    }

    [Fact]
    public async Task Unknown_property_inside_inputs_reaches_the_bridge()
    {
        var client = Client(V24, P24, 24);
        client.ReadOperation = (_, _, _) => throw new BridgeException(
            CapabilityErrorCodes.InvalidReadOperationInput,
            "inputs.not_a_field is not an accepted input.");
        var result = await InvokeRead(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc-1\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1"),
            ["inputs"] = Json("{\"name_contains\":\"Shared\",\"not_a_field\":true}")
        });

        Assert.True(result.IsError);
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, ErrorCode(result));
        Assert.Equal(1, client.ReadOperationCalls);
        Assert.Equal(0, client.LookupReadOperationsCalls);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
        var inputs = client.LastReadOperationRequest!.Inputs!.Value;
        Assert.Equal("Shared", inputs.GetProperty("name_contains").GetString());
        Assert.True(inputs.GetProperty("not_a_field").GetBoolean());
    }

    [Fact]
    public async Task Search_and_exact_lookup_arguments_are_transported_without_instance_id()
    {
        var searchClient = Client(V24, P24, 24);
        var search = await InvokeLookup(searchClient, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V24}\""),
            ["query"] = Json("\"Work\""),
            ["offset"] = Json("2"),
            ["limit"] = Json("10")
        });
        Assert.False(search.IsError);
        var searchRequest = searchClient.LastLookupReadOperationsRequest!;
        Assert.Null(searchRequest.Operation);
        Assert.Null(searchRequest.ContractVersion);
        Assert.Equal("Work", searchRequest.Query);
        Assert.Equal(2, searchRequest.Offset);
        Assert.Equal(10, searchRequest.Limit);
        Assert.DoesNotContain("instance_id", JsonSerializer.Serialize(searchRequest, ContractJson.Options), StringComparison.Ordinal);

        var exactClient = Client(V24, P24, 24);
        var exact = await InvokeLookup(exactClient, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V24}\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1")
        });
        Assert.False(exact.IsError);
        var exactRequest = exactClient.LastLookupReadOperationsRequest!;
        Assert.Equal("project.user_worksets", exactRequest.Operation);
        Assert.Equal(1, exactRequest.ContractVersion);
        Assert.Null(exactRequest.Query);
        Assert.Null(exactRequest.Offset);
        Assert.Null(exactRequest.Limit);
        Assert.DoesNotContain("instance_id", JsonSerializer.Serialize(exactRequest, ContractJson.Options), StringComparison.Ordinal);
        Assert.Equal(new ServerTimeouts().CapabilityTimeout, exactClient.LastLookupReadOperationsTimeout);
    }

    [Fact]
    public async Task Null_lookup_fields_are_absence_and_illegal_mode_combinations_reach_the_addin()
    {
        var nulled = Client(V24, P24, 24);
        var result = await InvokeLookup(nulled, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json("null"),
            ["operation"] = Json("null"),
            ["contract_version"] = Json("null"),
            ["query"] = Json("null"),
            ["offset"] = Json("null"),
            ["limit"] = Json("null")
        });
        Assert.False(result.IsError);
        var request = nulled.LastLookupReadOperationsRequest!;
        Assert.Null(request.Operation);
        Assert.Null(request.ContractVersion);
        Assert.Null(request.Query);
        Assert.Null(request.Offset);
        Assert.Null(request.Limit);

        var mixed = Client(V24, P24, 24);
        mixed.LookupReadOperations = (_, _, _) => throw new BridgeException(
            CapabilityErrorCodes.InvalidReadOperationLookup,
            "query is not legal when operation is set.");
        var mixedResult = await InvokeLookup(mixed, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["operation"] = Json("\"project.user_worksets\""),
            ["query"] = Json("\"work\"")
        });
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationLookup, ErrorCode(mixedResult));
        Assert.Equal(1, mixed.LookupReadOperationsCalls);
        Assert.Equal("project.user_worksets", mixed.LastLookupReadOperationsRequest!.Operation);
        Assert.Equal("work", mixed.LastLookupReadOperationsRequest.Query);
    }

    [Fact]
    public async Task Read_envelope_fields_are_transported_and_instance_id_is_not()
    {
        var client = Client(V24, P24, 24);
        var result = await InvokeRead(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V24}\""),
            ["document_id"] = Json("\"doc-opaque\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1"),
            ["inputs"] = Json("{\"name_contains\":\"Level\"}"),
            ["page_size"] = Json("25"),
            ["cursor"] = Json("\"opaque-cursor\"")
        });

        Assert.False(result.IsError);
        var request = client.LastReadOperationRequest!;
        Assert.Equal("doc-opaque", request.DocumentId);
        Assert.Equal("project.user_worksets", request.Operation);
        Assert.Equal(1, request.ContractVersion);
        Assert.Equal("Level", request.Inputs!.Value.GetProperty("name_contains").GetString());
        Assert.Equal(25, request.PageSize);
        Assert.Equal("opaque-cursor", request.Cursor);
        Assert.DoesNotContain("instance_id", JsonSerializer.Serialize(request, ContractJson.Options), StringComparison.Ordinal);
        Assert.Equal(new ServerTimeouts().CapabilityTimeout, client.LastReadOperationTimeout);
        Assert.Equal(1, client.ReadOperationCalls);
        Assert.Equal(0, client.LookupReadOperationsCalls);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
        Assert.Equal(0, client.QueryFamilyTypesCalls);

        var structured = result.StructuredContent!.Value;
        Assert.True(structured.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("next_cursor").ValueKind);
        Assert.Equal("project.user_worksets", structured.GetProperty("operation").GetString());
    }

    [Fact]
    public async Task Null_read_paging_fields_are_omitted()
    {
        var client = Client(V24, P24, 24);
        var arguments = ReadArguments();
        arguments["instance_id"] = Json("null");
        arguments["page_size"] = Json("null");
        arguments["cursor"] = Json("null");
        var result = await InvokeRead(client, arguments);
        Assert.False(result.IsError);
        Assert.Null(client.LastReadOperationRequest!.PageSize);
        Assert.Null(client.LastReadOperationRequest.Cursor);
    }

    [Fact]
    public async Task Omitted_instance_selects_only_a_protocol_24_host()
    {
        var v24 = Client(V24, P24, 24);
        var v23 = Client(V23, P23, 23);
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) => Task.FromResult(pipe == P24 ? v24 : v23)
        };
        var outcome = await LookupService(factory, (V24, P24, 24), (V23, P23, 23)).ExecuteAsync(null, new LookupReadOperationsRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([P24], factory.RequestedPipes);
        Assert.Equal(1, v24.LookupReadOperationsCalls);
        Assert.Equal(0, v23.LookupReadOperationsCalls);
        Assert.Equal(0, v23.ReadOperationCalls);
        Assert.Equal(0, v23.QueryParameterBindingsCalls);
        Assert.Equal(0, v24.QueryParameterBindingsCalls);
    }

    [Fact]
    public async Task No_protocol_24_host_returns_no_instance_without_connecting()
    {
        var client = Client(V23, P23, 23);
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(client)
        };
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(V23, P23, protocolVersion: 23));
        var outcome = await new LookupReadOperationsApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync(null, new LookupReadOperationsRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
        Assert.Equal(0, client.LookupReadOperationsCalls);
    }

    [Fact]
    public async Task Two_protocol_24_hosts_require_an_instance_id()
    {
        var outcome = await LookupService(new RecordingBridgeClientFactory(), ("id-a", "pipe-a", 24), ("id-b", "pipe-b", 24))
            .ExecuteAsync(null, new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
    }

    [Fact]
    public async Task Explicit_protocol_24_host_succeeds_and_calls_only_its_rpc()
    {
        var client = Client(V24, P24, 24);
        var lookup = await LookupService(client, V24, P24, 24).ExecuteAsync(V24, new LookupReadOperationsRequest { Operation = "project.user_worksets" }, CancellationToken.None);
        var read = await ReadService(client, V24, P24, 24).ExecuteAsync(V24, ReadRequest(), CancellationToken.None);

        Assert.True(lookup.IsSuccess);
        Assert.True(read.IsSuccess);
        Assert.Equal(1, client.LookupReadOperationsCalls);
        Assert.Equal(1, client.ReadOperationCalls);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
    }

    [Fact]
    public async Task Explicit_protocol_23_host_is_incompatible_and_does_not_fall_back()
    {
        var v24 = Client(V24, P24, 24);
        var v23 = Client(V23, P23, 23);
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) => Task.FromResult(pipe == P24 ? v24 : v23)
        };
        var instances = new[] { (V24, P24, 24), (V23, P23, 23) };
        var lookup = await LookupService(factory, instances).ExecuteAsync(V23, new LookupReadOperationsRequest(), CancellationToken.None);
        var read = await ReadService(factory, instances).ExecuteAsync(V23, ReadRequest(), CancellationToken.None);

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, lookup.ErrorCode);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, read.ErrorCode);
        Assert.Equal([P23, P23], factory.RequestedPipes);
        Assert.Equal(0, v23.LookupReadOperationsCalls);
        Assert.Equal(0, v23.ReadOperationCalls);
        Assert.Equal(0, v23.QueryParameterBindingsCalls);
        Assert.Equal(0, v23.QueryFamilyTypesCalls);
        Assert.Equal(0, v24.LookupReadOperationsCalls);
        Assert.Equal(0, v24.ReadOperationCalls);
        Assert.DoesNotContain(P24, factory.RequestedPipes);
    }

    [Fact]
    public async Task Protocol_23_still_serves_parameter_bindings()
    {
        var client = Client(V23, P23, 23);
        var lookup = await InvokeLookup(client, V23, P23, 23, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V23}\"")
        });
        var read = await InvokeRead(client, V23, P23, 23, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V23}\""),
            ["document_id"] = Json("\"doc\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1"),
            ["inputs"] = Json("{}")
        });
        var bindings = await InvokeBindings(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V23}\""),
            ["document_id"] = Json("\"doc\"")
        });

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(lookup));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(read));
        Assert.False(bindings.IsError);
        Assert.Equal(0, client.LookupReadOperationsCalls);
        Assert.Equal(0, client.ReadOperationCalls);
        Assert.Equal(1, client.QueryParameterBindingsCalls);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
    }

    [Fact]
    public async Task Missing_explicit_instance_is_not_found_and_an_unready_instance_is_unavailable()
    {
        var missing = await LookupService(Client(V24, P24, 24), V24, P24, 24)
            .ExecuteAsync("missing", new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceNotFound, missing.ErrorCode);

        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.WithState(DiscoveryState.Unavailable, V24, P24, protocolVersion: 24));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new InvalidOperationException("An unavailable instance must not be connected.")
        };
        var unavailable = await new ReadOperationApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync(V24, ReadRequest(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, unavailable.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.UnknownReadOperation)]
    [InlineData(CapabilityErrorCodes.ReadOperationVersionUnsupported)]
    [InlineData(CapabilityErrorCodes.InvalidReadOperationLookup)]
    [InlineData(CapabilityErrorCodes.InvalidReadOperationInput)]
    [InlineData(CapabilityErrorCodes.ReadOperationResultInvalid)]
    [InlineData(CapabilityErrorCodes.NoActiveDocument)]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged)]
    [InlineData(CapabilityErrorCodes.UnsupportedDocumentKind)]
    [InlineData(CapabilityErrorCodes.InvalidCursor)]
    [InlineData(CapabilityErrorCodes.StaleCursor)]
    [InlineData(CapabilityErrorCodes.ResultTooLarge)]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout)]
    [InlineData(CapabilityErrorCodes.ExecutionFailed)]
    [InlineData(BridgeErrorCodes.ProtocolIncompatible)]
    public async Task Accepted_capability_errors_pass_through_unchanged(string code)
    {
        var lookupClient = Client(V24, P24, 24);
        lookupClient.LookupReadOperations = (_, _, _) => throw new BridgeException(code, "compact lookup message");
        var lookup = await LookupService(lookupClient, V24, P24, 24).ExecuteAsync(null, new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal(code, lookup.ErrorCode);
        Assert.Equal("compact lookup message", lookup.ErrorMessage);

        var readClient = Client(V24, P24, 24);
        readClient.ReadOperation = (_, _, _) => throw new BridgeException(code, "compact read message");
        var read = await ReadService(readClient, V24, P24, 24).ExecuteAsync(null, ReadRequest(), CancellationToken.None);
        Assert.Equal(code, read.ErrorCode);
        Assert.Equal("compact read message", read.ErrorMessage);
        Assert.DoesNotContain("at ", read.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unexpected_transport_failure_stays_on_the_routing_convention()
    {
        var client = Client(V24, P24, 24);
        client.ReadOperation = (_, _, _) => throw new IOException("C:\\secret\\pipe");
        var outcome = await ReadService(client, V24, P24, 24).ExecuteAsync(null, ReadRequest(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("secret", outcome.ErrorMessage, StringComparison.Ordinal);

        client.LookupReadOperations = (_, _, _) => throw new BridgeException("BRIDGE_UNEXPECTED", "raw failure");
        var lookup = await LookupService(client, V24, P24, 24).ExecuteAsync(null, new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, lookup.ErrorCode);
        Assert.DoesNotContain("raw failure", lookup.ErrorMessage, StringComparison.Ordinal);
    }

    private static void AssertOrder(string description, IReadOnlyList<string> fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var index = description.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index > previous, $"'{fragment}' is missing or out of order.");
            previous = index;
        }
    }

    private static void AssertInvalidRequest(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Equal(McpToolErrorCodes.InvalidRequest, ErrorCode(result));
    }

    private static string ErrorCode(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static string[] Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static string[] Names(JsonElement properties)
    {
        return properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<CallToolResult> InvokeLookup(RecordingBridgeClient client, IDictionary<string, JsonElement> arguments)
    {
        return await InvokeLookup(client, V24, P24, 24, arguments);
    }

    private static async Task<CallToolResult> InvokeLookup(
        RecordingBridgeClient client,
        string instanceId,
        string pipe,
        int protocol,
        IDictionary<string, JsonElement> arguments)
    {
        var tool = LookupReadOperationsToolRegistration.Create(new LookupReadOperationsMcpTools(LookupService(client, instanceId, pipe, protocol)));
        return await McpToolInvoke.InvokeAsync(tool, arguments);
    }

    private static async Task<CallToolResult> InvokeRead(RecordingBridgeClient client, IDictionary<string, JsonElement> arguments)
    {
        return await InvokeRead(client, V24, P24, 24, arguments);
    }

    private static async Task<CallToolResult> InvokeRead(
        RecordingBridgeClient client,
        string instanceId,
        string pipe,
        int protocol,
        IDictionary<string, JsonElement> arguments)
    {
        var tool = ReadOperationToolRegistration.Create(new ReadOperationMcpTools(ReadService(client, instanceId, pipe, protocol)));
        return await McpToolInvoke.InvokeAsync(tool, arguments);
    }

    private static async Task<CallToolResult> InvokeBindings(RecordingBridgeClient client, IDictionary<string, JsonElement> arguments)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(V23, P23, protocolVersion: 23));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(client)
        };
        var tool = QueryParameterBindingsToolRegistration.Create(
            new QueryParameterBindingsMcpTools(new QueryParameterBindingsApplicationService(discovery, factory, new ServerTimeouts())));
        return await McpToolInvoke.InvokeAsync(tool, arguments);
    }

    private static ModelContextProtocol.Server.McpServerTool LookupTool()
    {
        return LookupReadOperationsToolRegistration.Create(new LookupReadOperationsMcpTools(LookupService(Client(V24, P24, 24), V24, P24, 24)));
    }

    private static ModelContextProtocol.Server.McpServerTool ReadTool()
    {
        return ReadOperationToolRegistration.Create(new ReadOperationMcpTools(ReadService(Client(V24, P24, 24), V24, P24, 24)));
    }

    private static LookupReadOperationsApplicationService LookupService(RecordingBridgeClient client, string instanceId, string pipe, int protocol)
    {
        return LookupService(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [pipe] = client }, (instanceId, pipe, protocol));
    }

    private static LookupReadOperationsApplicationService LookupService(
        IReadOnlyDictionary<string, RecordingBridgeClient> clients,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        return LookupService(Factory(clients), instances);
    }

    private static LookupReadOperationsApplicationService LookupService(
        RecordingBridgeClientFactory factory,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        var discovery = new FakeDiscovery();
        foreach (var instance in instances)
        {
            discovery.Instances.Add(TestSupport.Ready(instance.InstanceId, instance.Pipe, protocolVersion: instance.ProtocolVersion));
        }

        return new LookupReadOperationsApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static ReadOperationApplicationService ReadService(RecordingBridgeClient client, string instanceId, string pipe, int protocol)
    {
        return ReadService(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [pipe] = client }, (instanceId, pipe, protocol));
    }

    private static ReadOperationApplicationService ReadService(
        IReadOnlyDictionary<string, RecordingBridgeClient> clients,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        return ReadService(Factory(clients), instances);
    }

    private static ReadOperationApplicationService ReadService(
        RecordingBridgeClientFactory factory,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        var discovery = new FakeDiscovery();
        foreach (var instance in instances)
        {
            discovery.Instances.Add(TestSupport.Ready(instance.InstanceId, instance.Pipe, protocolVersion: instance.ProtocolVersion));
        }

        return new ReadOperationApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RecordingBridgeClientFactory Factory(IReadOnlyDictionary<string, RecordingBridgeClient> clients)
    {
        return new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) => Task.FromResult(clients[pipe])
        };
    }

    private static RecordingBridgeClient Client(string instanceId, string pipe, int version)
    {
        var registration = TestSupport.CreateRegistration(instanceId, pipe);
        return new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: version)),
            LookupReadOperations = (_, _, _) => Task.FromResult(LookupResult(instanceId)),
            ReadOperation = (_, _, _) => Task.FromResult(ReadResult(instanceId)),
            QueryParameterBindings = (_, _, _) => Task.FromResult(BindingsResult(instanceId))
        };
    }

    private static LookupReadOperationsResult LookupResult(string instanceId)
    {
        _ = instanceId;
        return new LookupReadOperationsResult
        {
            Mode = "search",
            Total = 1,
            ReturnedCount = 1,
            NextOffset = null,
            Operations =
            [
                new ReadOperationDescriptor
                {
                    Id = "project.user_worksets",
                    ContractVersion = 1,
                    Title = "User worksets",
                    Description = "User-created worksets of the active host project.",
                    Context = "document",
                    Effects = "read",
                    Pagination = "cursor"
                }
            ]
        };
    }

    private static ReadOperationResult ReadResult(string instanceId)
    {
        return new ReadOperationResult
        {
            Context = new ReadOperationContext { InstanceId = instanceId, DocumentId = "doc-opaque" },
            Operation = "project.user_worksets",
            ContractVersion = 1,
            Result = Json("""{"returned_count":0,"worksets":[]}"""),
            Complete = true,
            NextCursor = null
        };
    }

    private static QueryParameterBindingsResult BindingsResult(string instanceId)
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext { InstanceId = instanceId, DocumentId = "doc" },
            Summary = new QueryParameterBindingsSummary
            {
                MatchedDefinitionCount = 0,
                SharedCount = 0,
                LocalCount = 0,
                BuiltInCount = 0,
                InstanceBoundCount = 0,
                TypeBoundCount = 0,
                DistinctCategoryCount = 0
            },
            ReturnedCount = 0,
            Complete = true,
            NextCursor = null,
            Bindings = []
        };
    }

    private static ReadOperationRequest ReadRequest()
    {
        return new ReadOperationRequest
        {
            DocumentId = "doc",
            Operation = "project.user_worksets",
            ContractVersion = 1,
            Inputs = Json("{}")
        };
    }

    private static Dictionary<string, JsonElement> ReadArguments()
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["operation"] = Json("\"project.user_worksets\""),
            ["contract_version"] = Json("1"),
            ["inputs"] = Json("{}")
        };
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
