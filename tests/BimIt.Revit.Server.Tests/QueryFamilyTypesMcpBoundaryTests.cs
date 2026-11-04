using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class QueryFamilyTypesMcpBoundaryTests
{
    [Fact]
    public void Tool_metadata_is_read_only_and_closed_world()
    {
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(Client("id-v22", "pipe-v22", 22), "id-v22", "pipe-v22", 22))).ProtocolTool;
        Assert.Equal("revit_query_family_types", tool.Name);
        Assert.Equal(QueryFamilyTypesToolMetadata.Title, tool.Title);
        Assert.Contains("FamilySymbol", tool.Description, StringComparison.Ordinal);
        Assert.Contains("next_cursor", tool.Description, StringComparison.Ordinal);
        Assert.Contains("OST_", tool.Description, StringComparison.Ordinal);
        Assert.Contains("case-insensitive", tool.Description, StringComparison.Ordinal);
        Assert.True(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.OpenWorldHint);
        Assert.Null(tool.Annotations.DestructiveHint);
    }

    [Fact]
    public void Input_and_output_schemas_are_strict()
    {
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(Client("id-v22", "pipe-v22", 22), "id-v22", "pipe-v22", 22))).ProtocolTool;
        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "document_id" }, input.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var documentId = input.GetProperty("properties").GetProperty("document_id");
        Assert.Equal("string", documentId.GetProperty("type").GetString());
        Assert.False(documentId.TryGetProperty("minLength", out _));
        var tokens = input.GetProperty("properties").GetProperty("category_tokens");
        Assert.Equal(1, tokens.GetProperty("minItems").GetInt32());
        Assert.Equal(20, tokens.GetProperty("maxItems").GetInt32());
        var usage = input.GetProperty("properties").GetProperty("usage").GetProperty("enum").EnumerateArray().ToArray();
        Assert.Equal(new[] { "all", "placed", "unused" }, usage.Take(3).Select(value => value.GetString()).ToArray());
        Assert.Equal(JsonValueKind.Null, usage[3].ValueKind);
        Assert.Equal(1, input.GetProperty("properties").GetProperty("page_size").GetProperty("minimum").GetInt32());
        Assert.Equal(200, input.GetProperty("properties").GetProperty("page_size").GetProperty("maximum").GetInt32());
        Assert.Equal(2048, input.GetProperty("properties").GetProperty("cursor").GetProperty("maxLength").GetInt32());

        var output = tool.OutputSchema!.Value;
        Assert.False(output.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "context", "summary", "returned_count", "complete", "next_cursor", "types" },
            output.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var row = output.GetProperty("properties").GetProperty("types").GetProperty("items");
        Assert.Equal(
            new[] { "type_ref", "family_ref", "category_token", "category_name", "family_name", "type_name", "family_placement_type", "family_is_in_place", "placed_instance_count", "is_used" },
            row.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Contains("unknown", row.GetProperty("properties").GetProperty("family_placement_type").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Tool_and_schema_descriptions_state_the_summary_and_counting_semantics()
    {
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(Client("id-v22", "pipe-v22", 22), "id-v22", "pipe-v22", 22))).ProtocolTool;
        var description = tool.Description!;
        AssertMentions(description, "entire matched set", "not the current page", "usage=all", "no name filters", "category_tokens", "linked-document", "complete");

        var usage = Property(tool.InputSchema, "usage");
        AssertMentions(Describe(usage), "usage=all", "unfiltered");

        var output = tool.OutputSchema!.Value;
        var summary = Property(output, "summary");
        AssertMentions(Describe(summary), "entire matched set", "not totals of the current page", "relative to the matched set");

        var summaryFields = summary.GetProperty("properties");
        AssertMentions(Describe(summaryFields.GetProperty("unused_family_count")), "all unplaced", "usage=all", "no name filters");
        AssertMentions(Describe(summaryFields.GetProperty("placed_instance_count")), "host project", "linked-document", "excluded", "groups", "design options", "nested shared");

        var row = output.GetProperty("properties").GetProperty("types").GetProperty("items").GetProperty("properties");
        AssertMentions(Describe(row.GetProperty("placed_instance_count")), "host project", "linked-document", "groups", "design options", "nested shared");

        AssertMentions(Describe(Property(output, "complete")), "authoritative", "next_cursor");
        AssertMentions(Describe(Property(output, "next_cursor")), "opaque");
    }

    private static JsonElement Property(JsonElement schema, string name)
    {
        return schema.GetProperty("properties").GetProperty(name);
    }

    private static string Describe(JsonElement schemaNode)
    {
        Assert.True(schemaNode.TryGetProperty("description", out var description), "missing schema description");
        return description.GetString()!;
    }

    private static void AssertMentions(string text, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Mcp_serialization_matches_the_shared_result_budget()
    {
        var result = new QueryFamilyTypesResult
        {
            Context = new QueryFamilyTypesContext { InstanceId = "instance", DocumentId = "document" },
            Summary = new QueryFamilyTypesSummary
            {
                MatchedTypeCount = 1,
                DistinctFamilyCount = 1,
                PlacedTypeCount = 0,
                PlacedInstanceCount = 0,
                UsedFamilyCount = 0,
                UnusedFamilyCount = 1
            },
            ReturnedCount = 1,
            Complete = true,
            NextCursor = null,
            Types =
            [
                new QueryFamilyTypeRow
                {
                    TypeRef = "type",
                    FamilyRef = "family",
                    CategoryToken = "OST_Walls",
                    CategoryName = "Walls",
                    FamilyName = "Café",
                    TypeName = "200 mm",
                    FamilyPlacementType = "unknown",
                    FamilyIsInPlace = false,
                    PlacedInstanceCount = 0,
                    IsUsed = false
                }
            ]
        };

        var serialized = ReadResultBudget.Serialize(result);
        Assert.Equal(serialized, JsonSerializer.Serialize(result, McpJson.Options));
        Assert.Equal(120_000, ReadResultBudget.MaxCharacters);
        var roundTrip = JsonSerializer.Deserialize<QueryFamilyTypesResult>(serialized, ReadResultBudget.Options);
        Assert.Equal("Café", roundTrip!.Types[0].FamilyName);
        Assert.Contains("\"next_cursor\":null", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_usage_is_accepted_and_matches_omitted_usage()
    {
        var omitted = Client("id-v22", "pipe-v22", 22);
        var nulled = Client("id-v22", "pipe-v22", 22);
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(omitted, "id-v22", "pipe-v22", 22)));
        var omittedResult = await McpToolInvoke.InvokeAsync(tool, Arguments(includeUsage: false, usage: null));
        Assert.False(omittedResult.IsError);
        Assert.Null(omitted.LastQueryFamilyTypesRequest!.Usage);

        tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(nulled, "id-v22", "pipe-v22", 22)));
        var nullResult = await McpToolInvoke.InvokeAsync(tool, Arguments(includeUsage: true, usage: null));
        Assert.False(nullResult.IsError);
        Assert.Null(nulled.LastQueryFamilyTypesRequest!.Usage);
        Assert.Equal(omitted.LastQueryFamilyTypesRequest.DocumentId, nulled.LastQueryFamilyTypesRequest.DocumentId);

        var rejected = Client("id-v22", "pipe-v22", 22);
        tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service(rejected, "id-v22", "pipe-v22", 22)));
        var invalid = await McpToolInvoke.InvokeAsync(tool, Arguments(includeUsage: true, usage: "other"));
        Assert.True(invalid.IsError);
        Assert.Equal(0, rejected.QueryFamilyTypesCalls);
    }

    [Fact]
    public async Task Omitted_instance_id_selects_the_single_v22_host()
    {
        var client = Client("id-v22", "pipe-v22", 22);
        var outcome = await Service(client, "id-v22", "pipe-v22", 22).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.QueryFamilyTypesCalls);
    }

    [Fact]
    public async Task Omitted_instance_id_selects_v22_when_a_v21_host_is_also_ready()
    {
        var v22 = Client("id-v22", "pipe-v22", 22);
        var v21 = Client("id-v21", "pipe-v21", 21);
        var outcome = await Service(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal)
        {
            ["pipe-v22"] = v22,
            ["pipe-v21"] = v21
        }, ("id-v22", "pipe-v22", 22), ("id-v21", "pipe-v21", 21)).ExecuteAsync(null, Request(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, v22.QueryFamilyTypesCalls);
        Assert.Equal(0, v21.QueryFamilyTypesCalls);
        Assert.Equal(0, v21.GetMepDeviceTypesCalls);
        Assert.Equal(0, v21.QueryElementsCalls);
    }

    [Fact]
    public async Task Two_v22_hosts_require_an_instance_id()
    {
        var outcome = await Service(new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal), ("id-a", "pipe-a", 22), ("id-b", "pipe-b", 22))
            .ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
    }

    [Fact]
    public async Task Explicit_v21_is_incompatible_and_does_not_call_another_capability()
    {
        var client = Client("id-v21", "pipe-v21", 21);
        var outcome = await Service(client, "id-v21", "pipe-v21", 21).ExecuteAsync("id-v21", Request(), CancellationToken.None);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, outcome.ErrorCode);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
    }

    [Fact]
    public async Task Explicit_v22_invokes_the_family_type_rpc()
    {
        var client = Client("id-v22", "pipe-v22", 22);
        var outcome = await Service(client, "id-v22", "pipe-v22", 22).ExecuteAsync("id-v22", Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.QueryFamilyTypesCalls);
        Assert.Equal("doc", client.LastQueryFamilyTypesRequest!.DocumentId);
    }

    [Fact]
    public async Task No_v22_host_does_not_substitute_another_capability()
    {
        var client = Client("id-v21", "pipe-v21", 21);
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(client)
        };
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id-v21", "pipe-v21", protocolVersion: 21));
        var outcome = await new QueryFamilyTypesApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
    }

    private static QueryFamilyTypesApplicationService Service(RecordingBridgeClient client, string instanceId, string pipe, int protocolVersion)
    {
        return Service(
            new Dictionary<string, RecordingBridgeClient>(StringComparer.Ordinal) { [pipe] = client },
            (instanceId, pipe, protocolVersion));
    }

    private static QueryFamilyTypesApplicationService Service(
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
        return new QueryFamilyTypesApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RecordingBridgeClient Client(string instanceId, string pipe, int version)
    {
        var registration = TestSupport.CreateRegistration(instanceId, pipe);
        return new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: version)),
            QueryFamilyTypes = (_, _, _) => Task.FromResult(new QueryFamilyTypesResult
            {
                Context = new QueryFamilyTypesContext { InstanceId = instanceId, DocumentId = "doc" },
                Summary = new QueryFamilyTypesSummary
                {
                    MatchedTypeCount = 0,
                    DistinctFamilyCount = 0,
                    PlacedTypeCount = 0,
                    PlacedInstanceCount = 0,
                    UsedFamilyCount = 0,
                    UnusedFamilyCount = 0
                },
                ReturnedCount = 0,
                Complete = true,
                NextCursor = null,
                Types = []
            })
        };
    }

    private static Dictionary<string, JsonElement> Arguments(bool includeUsage, string? usage)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["document_id"] = Json("\"doc\"")
        };
        if (includeUsage)
        {
            arguments["usage"] = usage is null ? Json("null") : Json(JsonSerializer.Serialize(usage));
        }

        return arguments;
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static QueryFamilyTypesRequest Request()
    {
        return new QueryFamilyTypesRequest { DocumentId = "doc" };
    }
}
