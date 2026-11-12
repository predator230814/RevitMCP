using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class QueryParameterBindingsMcpBoundaryTests
{
    private const string V23 = "id-v23";
    private const string P23 = "pipe-v23";
    private const string V22 = "id-v22";
    private const string P22 = "pipe-v22";

    // ---- Metadata and schema ----

    [Fact]
    public void Tool_identity_and_annotations_are_exact()
    {
        var tool = Tool().ProtocolTool;
        Assert.Equal("revit_query_parameter_bindings", tool.Name);
        Assert.Equal("Query Revit Parameter Bindings", tool.Title);
        Assert.True(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.Null(tool.Annotations.DestructiveHint);
    }

    [Fact]
    public void Tool_description_follows_the_accepted_order_and_makes_no_overclaim()
    {
        var description = Tool().ProtocolTool.Description!;
        var order = new[]
        {
            "project parameter bindings",
            "next_cursor",
            "document_id comes from revit_get_context",
            "revit_describe_parameters",
            "OST_",
            "case-insensitive substring"
        };
        var previous = -1;
        foreach (var fragment in order)
        {
            var index = description.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index > previous, $"'{fragment}' is missing or out of order.");
            previous = index;
        }

        Assert.Contains("revit_get_parameter_values", description, StringComparison.Ordinal);
        Assert.Contains("complete is true", description, StringComparison.Ordinal);
        Assert.Contains("not the current page", description, StringComparison.Ordinal);
        Assert.DoesNotContain("Tt_", description, StringComparison.Ordinal);
        Assert.DoesNotContain("approved", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compliant", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Input_schema_is_closed_and_exact()
    {
        var input = Tool().ProtocolTool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "document_id" }, Strings(input.GetProperty("required")));
        var properties = input.GetProperty("properties");
        Assert.Equal(
            new[] { "binding_kind", "category_tokens", "cursor", "document_id", "identity_kind", "instance_id", "name_contains", "page_size" },
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        var documentId = properties.GetProperty("document_id");
        Assert.Equal("string", documentId.GetProperty("type").GetString());
        Assert.False(documentId.TryGetProperty("minLength", out _));

        var name = properties.GetProperty("name_contains");
        Assert.Equal(1, name.GetProperty("minLength").GetInt32());
        Assert.Equal(256, name.GetProperty("maxLength").GetInt32());

        AssertEnum(properties.GetProperty("identity_kind"), "all", "shared", "local", "built_in");
        AssertEnum(properties.GetProperty("binding_kind"), "all", "instance", "type");

        var tokens = properties.GetProperty("category_tokens");
        Assert.Equal(1, tokens.GetProperty("minItems").GetInt32());
        Assert.Equal(20, tokens.GetProperty("maxItems").GetInt32());
        Assert.True(tokens.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(1, tokens.GetProperty("items").GetProperty("minLength").GetInt32());
        Assert.Equal(80, tokens.GetProperty("items").GetProperty("maxLength").GetInt32());

        var pageSize = properties.GetProperty("page_size");
        Assert.Equal(1, pageSize.GetProperty("minimum").GetInt32());
        Assert.Equal(100, pageSize.GetProperty("maximum").GetInt32());

        var cursor = properties.GetProperty("cursor");
        Assert.Equal(1, cursor.GetProperty("minLength").GetInt32());
        Assert.Equal(2048, cursor.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Output_schema_is_closed_and_exact_at_every_level()
    {
        var output = Tool().ProtocolTool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "summary", "returned_count", "complete", "next_cursor", "bindings" },
            Strings(output.GetProperty("required")));

        var context = output.GetProperty("properties").GetProperty("context");
        Assert.False(context.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "instance_id", "document_id" }, Strings(context.GetProperty("required")));

        var summary = output.GetProperty("properties").GetProperty("summary");
        Assert.False(summary.GetProperty("additionalProperties").GetBoolean());
        var summaryFields = new[]
        {
            "matched_definition_count",
            "shared_count",
            "local_count",
            "built_in_count",
            "instance_bound_count",
            "type_bound_count",
            "distinct_category_count"
        };
        Assert.Equal(summaryFields, Strings(summary.GetProperty("required")));
        foreach (var field in summaryFields)
        {
            var node = summary.GetProperty("properties").GetProperty(field);
            Assert.Equal("integer", node.GetProperty("type").GetString());
            Assert.Equal(0, node.GetProperty("minimum").GetInt32());
        }

        var description = summary.GetProperty("description").GetString()!;
        Assert.Contains("entire matched set", description, StringComparison.Ordinal);
        Assert.Contains("every page", description, StringComparison.Ordinal);
        Assert.Contains("not totals of the current page", description, StringComparison.Ordinal);

        Assert.Contains("authoritative", Describe(output, "complete"), StringComparison.Ordinal);

        var row = output.GetProperty("properties").GetProperty("bindings").GetProperty("items");
        Assert.False(row.GetProperty("additionalProperties").GetBoolean());
        var rowFields = new[] { "parameter_ref", "name", "identity", "data_type", "binding_kind", "categories" };
        Assert.Equal(rowFields, Strings(row.GetProperty("required")));
        Assert.Equal(
            rowFields.Order(StringComparer.Ordinal).ToArray(),
            row.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.False(row.GetProperty("properties").TryGetProperty("visible", out _));
        Assert.Equal(new[] { "instance", "type" }, Strings(row.GetProperty("properties").GetProperty("binding_kind").GetProperty("enum")));

        var categories = row.GetProperty("properties").GetProperty("categories");
        Assert.Contains("complete set", categories.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("never shortens", categories.GetProperty("description").GetString(), StringComparison.Ordinal);
        var category = categories.GetProperty("items");
        Assert.False(category.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "category_token", "category_name" }, Strings(category.GetProperty("required")));
    }

    [Fact]
    public void Next_cursor_is_required_and_nullable()
    {
        var output = Tool().ProtocolTool.OutputSchema!.Value;
        Assert.Contains("next_cursor", Strings(output.GetProperty("required")));
        var type = output.GetProperty("properties").GetProperty("next_cursor").GetProperty("type");
        Assert.Equal(new[] { "string", "null" }, type.EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void Identity_schema_equals_the_cap0004_identity_schema()
    {
        var cap0004 = Cap0004Row().GetProperty("properties").GetProperty("identity");
        var cap0015 = Cap0015Row().GetProperty("properties").GetProperty("identity");
        Assert.True(Equivalent(cap0004, cap0015), "identity schema drifted from CAP-0004");
        Assert.Equal(3, cap0015.GetProperty("oneOf").GetArrayLength());
    }

    [Fact]
    public void Data_type_schema_equals_the_cap0004_data_type_schema()
    {
        var cap0004 = Cap0004Row().GetProperty("properties").GetProperty("data_type");
        var cap0015 = Cap0015Row().GetProperty("properties").GetProperty("data_type");
        Assert.True(Equivalent(cap0004, cap0015), "data_type schema drifted from CAP-0004");
        Assert.Equal(4, cap0015.GetProperty("oneOf").GetArrayLength());
    }

    // ---- MCP boundary ----

    [Fact]
    public async Task Unknown_property_is_rejected_before_the_bridge()
    {
        var client = Client(V23, P23, 23);
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["projection"] = Json("[\"name\"]")
        });
        AssertInvalidRequest(result);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
    }

    [Theory]
    [InlineData("identity_kind", "\"other\"")]
    [InlineData("binding_kind", "\"other\"")]
    [InlineData("page_size", "0")]
    [InlineData("page_size", "101")]
    [InlineData("name_contains", "\"\"")]
    [InlineData("cursor", "\"\"")]
    [InlineData("category_tokens", "[]")]
    public async Task Malformed_values_are_rejected_before_the_bridge(string property, string json)
    {
        var client = Client(V23, P23, 23);
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            [property] = Json(json)
        });
        AssertInvalidRequest(result);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
    }

    [Fact]
    public async Task Too_many_or_duplicate_category_tokens_are_rejected_before_the_bridge()
    {
        var client = Client(V23, P23, 23);
        var tokens = Enumerable.Range(1, 21).Select(index => $"OST_Category{index}").ToArray();
        var tooMany = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["category_tokens"] = Json(JsonSerializer.Serialize(tokens))
        });
        AssertInvalidRequest(tooMany);

        var duplicate = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["category_tokens"] = Json("[\"OST_Walls\",\"OST_Walls\"]")
        });
        AssertInvalidRequest(duplicate);
        Assert.Equal(0, client.QueryParameterBindingsCalls);
    }

    [Fact]
    public async Task Null_optional_properties_are_equivalent_to_omission()
    {
        var omitted = Client(V23, P23, 23);
        var omittedResult = await Invoke(omitted, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\"")
        });
        Assert.False(omittedResult.IsError);

        var nulled = Client(V23, P23, 23);
        var nulledResult = await Invoke(nulled, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json("null"),
            ["document_id"] = Json("\"doc\""),
            ["name_contains"] = Json("null"),
            ["identity_kind"] = Json("null"),
            ["binding_kind"] = Json("null"),
            ["category_tokens"] = Json("null"),
            ["page_size"] = Json("null"),
            ["cursor"] = Json("null")
        });
        Assert.False(nulledResult.IsError);

        var request = nulled.LastQueryParameterBindingsRequest!;
        Assert.Equal("doc", request.DocumentId);
        Assert.Null(request.NameContains);
        Assert.Null(request.IdentityKind);
        Assert.Null(request.BindingKind);
        Assert.Null(request.CategoryTokens);
        Assert.Null(request.PageSize);
        Assert.Null(request.Cursor);
        Assert.Equal(omitted.LastQueryParameterBindingsRequest!.DocumentId, request.DocumentId);
    }

    [Fact]
    public async Task Whitespace_only_name_reaches_the_cap0015_validation()
    {
        var client = Client(V23, P23, 23);
        client.QueryParameterBindings = (_, _, _) => throw new BridgeException(
            CapabilityErrorCodes.InvalidParameterBindingQuery,
            "name_contains must not be blank.");
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["name_contains"] = Json("\"   \"")
        });

        Assert.True(result.IsError);
        Assert.Equal(CapabilityErrorCodes.InvalidParameterBindingQuery, ErrorCode(result));
        Assert.Equal(1, client.QueryParameterBindingsCalls);
        Assert.Equal("   ", client.LastQueryParameterBindingsRequest!.NameContains);
    }

    [Fact]
    public async Task Unicode_request_and_result_round_trip_with_complete_categories()
    {
        var client = Client(V23, P23, 23);
        client.QueryParameterBindings = (_, _, _) => Task.FromResult(Result(
            V23,
            complete: true,
            nextCursor: null,
            rows:
            [
                Row("ref-1", "CHARGE INTÉGRÉE", BuiltIn: false, shared: false, binding: QueryParameterBindingKind.Type,
                    ("OST_ElectricalEquipment", "Electrical Equipment"),
                    ("OST_SecurityDevices", "Security Devices")),
                Row("ref-2", "Tt_Capacité_Sectionneur", BuiltIn: false, shared: true, binding: QueryParameterBindingKind.Instance,
                    ("OST_ElectricalFixtures", "Electrical Fixtures"))
            ]));
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["name_contains"] = Json("\"Capacité\"")
        });

        Assert.False(result.IsError);
        Assert.Equal("Capacité", client.LastQueryParameterBindingsRequest!.NameContains);
        var structured = result.StructuredContent!.Value;
        var bindings = structured.GetProperty("bindings");
        Assert.Equal("CHARGE INTÉGRÉE", bindings[0].GetProperty("name").GetString());
        Assert.Equal("Tt_Capacité_Sectionneur", bindings[1].GetProperty("name").GetString());
        Assert.Equal(2, bindings[0].GetProperty("categories").GetArrayLength());
        Assert.Equal("OST_SecurityDevices", bindings[0].GetProperty("categories")[1].GetProperty("category_token").GetString());
        Assert.Equal("type", bindings[0].GetProperty("binding_kind").GetString());
        Assert.Equal("instance", bindings[1].GetProperty("binding_kind").GetString());
        Assert.Equal("local", bindings[0].GetProperty("identity").GetProperty("kind").GetString());
        Assert.Equal("shared", bindings[1].GetProperty("identity").GetProperty("kind").GetString());
        Assert.False(bindings[0].TryGetProperty("visible", out _));
    }

    [Fact]
    public async Task Final_page_preserves_a_null_next_cursor_and_the_full_summary()
    {
        var client = Client(V23, P23, 23);
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\"")
        });

        Assert.False(result.IsError);
        Assert.Empty(result.Content);
        var structured = result.StructuredContent!.Value;
        Assert.True(structured.TryGetProperty("next_cursor", out var next));
        Assert.Equal(JsonValueKind.Null, next.ValueKind);
        Assert.True(structured.GetProperty("complete").GetBoolean());
        Assert.Equal(
            new[]
            {
                "matched_definition_count",
                "shared_count",
                "local_count",
                "built_in_count",
                "instance_bound_count",
                "type_bound_count",
                "distinct_category_count"
            },
            structured.GetProperty("summary").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(144, structured.GetProperty("summary").GetProperty("matched_definition_count").GetInt32());
        Assert.Equal(109, structured.GetProperty("summary").GetProperty("distinct_category_count").GetInt32());
    }

    // ---- Routing and orchestration ----

    [Fact]
    public async Task Omitted_instance_selects_the_single_v23_host()
    {
        var client = Client(V23, P23, 23);
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.QueryParameterBindingsCalls);
    }

    [Fact]
    public async Task Omitted_instance_chooses_v23_when_a_v22_host_is_also_ready()
    {
        var v23 = Client(V23, P23, 23);
        var v22 = Client(V22, P22, 22);
        var outcome = await Service(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal)
        {
            [P23] = v23,
            [P22] = v22
        }, (V23, P23, 23), (V22, P22, 22)).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, v23.QueryParameterBindingsCalls);
        Assert.Equal(0, v22.QueryParameterBindingsCalls);
        Assert.Equal(0, v22.QueryFamilyTypesCalls);
    }

    [Fact]
    public async Task Two_v23_hosts_require_an_instance_id()
    {
        var outcome = await Service(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal), ("id-a", "pipe-a", 23), ("id-b", "pipe-b", 23))
            .ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
    }

    [Fact]
    public async Task No_v23_host_returns_no_instance_without_connecting_to_v22()
    {
        var client = Client(V22, P22, 22);
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(client)
        };
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(V22, P22, protocolVersion: 22));
        var outcome = await new QueryParameterBindingsApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
        AssertNoCapabilityCalls(client);
    }

    [Fact]
    public async Task Explicit_v22_is_incompatible_and_calls_no_capability()
    {
        var client = Client(V22, P22, 22);
        var outcome = await Service(client, V22, P22, 22).ExecuteAsync(V22, Request(), CancellationToken.None);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, outcome.ErrorCode);
        AssertNoCapabilityCalls(client);
    }

    [Fact]
    public async Task Explicit_v23_invokes_exactly_the_parameter_binding_rpc()
    {
        var client = Client(V23, P23, 23);
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(V23, Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.QueryParameterBindingsCalls);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
        Assert.Equal(new ServerTimeouts().CapabilityTimeout, client.LastQueryParameterBindingsTimeout);
    }

    [Fact]
    public async Task Handshake_offers_the_current_supported_versions_and_the_result_is_returned_unchanged()
    {
        IReadOnlyList<int>? offered = null;
        var registration = TestSupport.CreateRegistration(V23, P23);
        var expected = Result(V23, complete: false, nextCursor: "cursor-1", rows: [Row("ref", "A", false, true, QueryParameterBindingKind.Instance, ("OST_Walls", "Walls"))]);
        var client = new RecordingBridgeClient
        {
            Handshake = (request, _) =>
            {
                offered = request.SupportedProtocolVersions;
                return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 23));
            },
            QueryParameterBindings = (_, _, _) => Task.FromResult(expected)
        };
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.Equal(BridgeProtocol.SupportedVersions, offered);
        Assert.Equal(24, BridgeProtocol.CurrentVersion);
        Assert.Same(expected, outcome.Result);
    }

    [Fact]
    public async Task All_request_fields_arrive_unchanged_at_the_bridge_client()
    {
        var client = Client(V23, P23, 23);
        var request = new QueryParameterBindingsRequest
        {
            DocumentId = "doc-opaque",
            NameContains = "Tt_",
            IdentityKind = "shared",
            BindingKind = "type",
            CategoryTokens = ["OST_ElectricalFixtures", "OST_SecurityDevices"],
            PageSize = 50,
            Cursor = "opaque-cursor"
        };
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(null, request, CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Same(request, client.LastQueryParameterBindingsRequest);
    }

    [Fact]
    public async Task Mcp_arguments_become_exactly_one_request_without_instance_id()
    {
        var client = Client(V23, P23, 23);
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["instance_id"] = Json($"\"{V23}\""),
            ["document_id"] = Json("\"doc-opaque\""),
            ["name_contains"] = Json("\"Tt_\""),
            ["identity_kind"] = Json("\"shared\""),
            ["binding_kind"] = Json("\"instance\""),
            ["category_tokens"] = Json("[\"OST_ElectricalFixtures\"]"),
            ["page_size"] = Json("50"),
            ["cursor"] = Json("\"opaque-cursor\"")
        });

        Assert.False(result.IsError);
        Assert.Equal(1, client.QueryParameterBindingsCalls);
        var request = client.LastQueryParameterBindingsRequest!;
        Assert.Equal("doc-opaque", request.DocumentId);
        Assert.Equal("Tt_", request.NameContains);
        Assert.Equal("shared", request.IdentityKind);
        Assert.Equal("instance", request.BindingKind);
        Assert.Equal(["OST_ElectricalFixtures"], request.CategoryTokens);
        Assert.Equal(50, request.PageSize);
        Assert.Equal("opaque-cursor", request.Cursor);
        Assert.DoesNotContain("instance_id", JsonSerializer.Serialize(request, ContractJson.Options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CapabilityErrorCodes.NoActiveDocument)]
    [InlineData(CapabilityErrorCodes.DocumentContextChanged)]
    [InlineData(CapabilityErrorCodes.UnsupportedDocumentKind)]
    [InlineData(CapabilityErrorCodes.InvalidParameterBindingQuery)]
    [InlineData(CapabilityErrorCodes.InvalidCursor)]
    [InlineData(CapabilityErrorCodes.StaleCursor)]
    [InlineData(CapabilityErrorCodes.ResultTooLarge)]
    [InlineData(CapabilityErrorCodes.ExecutionTimeout)]
    [InlineData(CapabilityErrorCodes.ExecutionFailed)]
    [InlineData(BridgeErrorCodes.ProtocolIncompatible)]
    public async Task Accepted_capability_errors_propagate_with_their_exact_codes(string code)
    {
        var client = Client(V23, P23, 23);
        client.QueryParameterBindings = (_, _, _) => throw new BridgeException(code, "compact message");
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.Equal("compact message", outcome.ErrorMessage);
        Assert.Equal(1, client.QueryParameterBindingsCalls);
    }

    [Fact]
    public async Task Unexpected_transport_failure_is_reported_as_instance_unavailable()
    {
        var client = Client(V23, P23, 23);
        client.QueryParameterBindings = (_, _, _) => throw new IOException("C:\\secret\\pipe");
        var outcome = await Service(client, V23, P23, 23).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.DoesNotContain("secret", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_result_is_compact_and_flagged()
    {
        var client = Client(V23, P23, 23);
        client.QueryParameterBindings = (_, _, _) => throw new BridgeException(CapabilityErrorCodes.StaleCursor, "The parameter binding query cursor is stale. Restart the query without a cursor.");
        var result = await Invoke(client, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\""),
            ["cursor"] = Json("\"old\"")
        });

        Assert.True(result.IsError);
        Assert.False(result.StructuredContent.HasValue);
        Assert.Equal(CapabilityErrorCodes.StaleCursor, ErrorCode(result));
    }

    // ---- Helpers ----

    private static JsonElement Cap0004Row()
    {
        return Cap0004JsonSchemas.Output
            .GetProperty("properties").GetProperty("parameters").GetProperty("items");
    }

    private static JsonElement Cap0015Row()
    {
        return QueryParameterBindingsJsonSchemas.Output
            .GetProperty("properties").GetProperty("bindings").GetProperty("items");
    }

    private static bool Equivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = left.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                var rightProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                return leftProperties.Count == rightProperties.Count
                    && leftProperties.All(pair => rightProperties.TryGetValue(pair.Key, out var other) && Equivalent(pair.Value, other));
            case JsonValueKind.Array:
                var leftItems = left.EnumerateArray().ToArray();
                var rightItems = right.EnumerateArray().ToArray();
                return leftItems.Length == rightItems.Length
                    && leftItems.Zip(rightItems).All(pair => Equivalent(pair.First, pair.Second));
            default:
                return left.GetRawText() == right.GetRawText();
        }
    }

    private static string[] Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static void AssertEnum(JsonElement property, params string[] values)
    {
        var items = property.GetProperty("enum").EnumerateArray().ToArray();
        Assert.Equal(values, items.Take(values.Length).Select(value => value.GetString()).ToArray());
        Assert.Equal(values.Length + 1, items.Length);
        Assert.Equal(JsonValueKind.Null, items[^1].ValueKind);
    }

    private static string Describe(JsonElement schema, string property)
    {
        return schema.GetProperty("properties").GetProperty(property).GetProperty("description").GetString()!;
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

    private static void AssertNoCapabilityCalls(RecordingBridgeClient client)
    {
        Assert.Equal(0, client.QueryParameterBindingsCalls);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
    }

    private static async Task<CallToolResult> Invoke(RecordingBridgeClient client, IDictionary<string, JsonElement> arguments)
    {
        var tool = QueryParameterBindingsToolRegistration.Create(
            new QueryParameterBindingsMcpTools(Service(client, V23, P23, 23)));
        return await McpToolInvoke.InvokeAsync(tool, arguments);
    }

    private static ModelContextProtocol.Server.McpServerTool Tool()
    {
        return QueryParameterBindingsToolRegistration.Create(
            new QueryParameterBindingsMcpTools(Service(Client(V23, P23, 23), V23, P23, 23)));
    }

    private static QueryParameterBindingsApplicationService Service(RecordingBridgeClient client, string instanceId, string pipe, int protocolVersion)
    {
        return Service(
            new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [pipe] = client },
            (instanceId, pipe, protocolVersion));
    }

    private static QueryParameterBindingsApplicationService Service(
        IReadOnlyDictionary<string, RecordingBridgeClient> clients,
        params (string InstanceId, string Pipe, int ProtocolVersion)[] instances)
    {
        var discovery = new FakeDiscovery();
        foreach (var instance in instances)
        {
            discovery.Instances.Add(TestSupport.Ready(instance.InstanceId, instance.Pipe, protocolVersion: instance.ProtocolVersion));
        }

        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) => Task.FromResult(clients[pipe])
        };
        return new QueryParameterBindingsApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RecordingBridgeClient Client(string instanceId, string pipe, int version)
    {
        var registration = TestSupport.CreateRegistration(instanceId, pipe);
        return new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: version)),
            QueryParameterBindings = (_, _, _) => Task.FromResult(Result(instanceId, complete: true, nextCursor: null, rows: []))
        };
    }

    private static QueryParameterBindingsResult Result(
        string instanceId,
        bool complete,
        string? nextCursor,
        IReadOnlyList<QueryParameterBindingRow> rows)
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext { InstanceId = instanceId, DocumentId = "doc" },
            Summary = new QueryParameterBindingsSummary
            {
                MatchedDefinitionCount = 144,
                SharedCount = 73,
                LocalCount = 71,
                BuiltInCount = 0,
                InstanceBoundCount = 138,
                TypeBoundCount = 6,
                DistinctCategoryCount = 109
            },
            ReturnedCount = rows.Count,
            Complete = complete,
            NextCursor = nextCursor,
            Bindings = rows
        };
    }

    private static QueryParameterBindingRow Row(
        string reference,
        string name,
        bool BuiltIn,
        bool shared,
        QueryParameterBindingKind binding,
        params (string Token, string Name)[] categories)
    {
        return new QueryParameterBindingRow
        {
            ParameterRef = reference,
            Name = name,
            Identity = new DescribeParameterIdentity
            {
                Kind = BuiltIn ? DescribeParameterIdentityKind.BuiltIn : shared ? DescribeParameterIdentityKind.Shared : DescribeParameterIdentityKind.Local,
                Guid = shared ? "857e1417-5cec-423a-b077-34ce3b791d84" : null
            },
            DataType = new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Spec,
                ForgeTypeId = "autodesk.spec:spec.string-2.0.0"
            },
            BindingKind = binding,
            Categories = categories
                .Select(category => new QueryParameterBindingCategory { CategoryToken = category.Token, CategoryName = category.Name })
                .ToArray()
        };
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static QueryParameterBindingsRequest Request()
    {
        return new QueryParameterBindingsRequest { DocumentId = "doc" };
    }
}
