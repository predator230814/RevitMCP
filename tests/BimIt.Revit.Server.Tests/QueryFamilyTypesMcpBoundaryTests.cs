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
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service())).ProtocolTool;
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
        var tool = QueryFamilyTypesToolRegistration.Create(new QueryFamilyTypesMcpTools(Service())).ProtocolTool;
        var input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "document_id" }, input.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var documentId = input.GetProperty("properties").GetProperty("document_id");
        Assert.Equal("string", documentId.GetProperty("type").GetString());
        Assert.False(documentId.TryGetProperty("minLength", out _));
        var tokens = input.GetProperty("properties").GetProperty("category_tokens");
        Assert.Equal(1, tokens.GetProperty("minItems").GetInt32());
        Assert.Equal(20, tokens.GetProperty("maxItems").GetInt32());
        Assert.Equal(new[] { "all", "placed", "unused" }, input.GetProperty("properties").GetProperty("usage").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
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
    public async Task Protocol_v21_is_incompatible_and_does_not_call_another_capability()
    {
        var client = Client(21);
        var outcome = await Service(client).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, outcome.ErrorCode);
        Assert.Equal(0, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
    }

    [Fact]
    public async Task Protocol_v22_invokes_the_family_type_rpc()
    {
        var client = Client(22);
        var outcome = await Service(client).ExecuteAsync(null, Request(), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, client.QueryFamilyTypesCalls);
        Assert.Equal(0, client.GetMepDeviceTypesCalls);
        Assert.Equal(0, client.QueryElementsCalls);
        Assert.Equal("doc", client.LastQueryFamilyTypesRequest!.DocumentId);
    }

    private static QueryFamilyTypesApplicationService Service(RecordingBridgeClient? client = null)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("id", "pipe", protocolVersion: client?.HandshakeRequest is null ? 22 : 22));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => Task.FromResult(client ?? Client(22))
        };
        if (client is not null)
        {
            discovery.Instances.Clear();
            discovery.Instances.Add(TestSupport.Ready("id", "pipe", protocolVersion: 21));
        }

        return new QueryFamilyTypesApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RecordingBridgeClient Client(int version)
    {
        var registration = TestSupport.CreateRegistration("id", "pipe");
        return new RecordingBridgeClient
        {
            Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: version)),
            QueryFamilyTypes = (_, _, _) => Task.FromResult(new QueryFamilyTypesResult
            {
                Context = new QueryFamilyTypesContext { InstanceId = "id", DocumentId = "doc" },
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

    private static QueryFamilyTypesRequest Request()
    {
        return new QueryFamilyTypesRequest { DocumentId = "doc" };
    }
}
