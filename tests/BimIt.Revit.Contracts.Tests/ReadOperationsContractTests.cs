using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class ReadOperationsContractTests
{
    [Fact]
    public void Lookup_request_uses_the_accepted_snake_case_fields()
    {
        var json = JsonSerializer.Serialize(
            new LookupReadOperationsRequest
            {
                Operation = "project.user_worksets",
                ContractVersion = 1,
                Query = "workset",
                Offset = 0,
                Limit = 25
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(["operation", "contract_version", "query", "offset", "limit"], names);
        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_search_result_omits_schemas_and_writes_null_next_offset()
    {
        var json = JsonSerializer.Serialize(SearchResult(nextOffset: null), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var operation = root.GetProperty("operations")[0];

        Assert.Equal(JsonValueKind.Null, root.GetProperty("next_offset").ValueKind);
        Assert.False(operation.TryGetProperty("input_schema", out _));
        Assert.False(operation.TryGetProperty("output_schema", out _));
        Assert.Equal("project.user_worksets", operation.GetProperty("id").GetString());
        Assert.Equal(1, operation.GetProperty("contract_version").GetInt32());
    }

    [Fact]
    public void Lookup_exact_result_serializes_schemas_as_json_objects()
    {
        const string inputSchema = """{"z":1,"a":{"b":true},"note":"Tt_Capacité"}""";
        const string outputSchema = """{"type":"object","properties":{"kept":{"const":"  raw  "}}}""";
        var json = JsonSerializer.Serialize(
            SearchResult(nextOffset: null, inputSchema: inputSchema, outputSchema: outputSchema),
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var operation = document.RootElement.GetProperty("operations")[0];
        var input = operation.GetProperty("input_schema");
        var output = operation.GetProperty("output_schema");

        Assert.Equal(JsonValueKind.Object, input.ValueKind);
        Assert.Equal(JsonValueKind.Object, output.ValueKind);
        Assert.Equal(inputSchema, input.GetRawText());
        Assert.Equal(outputSchema, output.GetRawText());
        Assert.Equal("search", document.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void Read_request_round_trips_inputs_as_a_json_object()
    {
        const string inputs = """{"name_contains":"Tt_Capacité","flag":false,"n":1.50}""";
        var json = JsonSerializer.Serialize(
            new ReadOperationRequest
            {
                DocumentId = "doc",
                Operation = "project.user_worksets",
                ContractVersion = 1,
                Inputs = Parse(inputs),
                PageSize = 100,
                Cursor = "cursor"
            },
            ContractJson.Options);
        var restored = JsonSerializer.Deserialize<ReadOperationRequest>(json, ContractJson.Options);

        Assert.NotNull(restored);
        Assert.Equal("doc", restored.DocumentId);
        Assert.Equal("project.user_worksets", restored.Operation);
        Assert.Equal(1, restored.ContractVersion);
        Assert.Equal(100, restored.PageSize);
        Assert.Equal("cursor", restored.Cursor);
        Assert.Equal(JsonValueKind.Object, restored.Inputs!.Value.ValueKind);
        Assert.Equal(inputs, restored.Inputs.Value.GetRawText());
        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_result_round_trips_the_object_payload_null_cursor_and_context()
    {
        const string payload = """{"summary":{"worksharing_enabled":false,"matched_workset_count":0},"returned_count":0,"worksets":[]}""";
        var json = JsonSerializer.Serialize(
            new ReadOperationResult
            {
                Context = new ReadOperationContext { InstanceId = "host-1", DocumentId = "doc-9" },
                Operation = "project.user_worksets",
                ContractVersion = 1,
                Result = Parse(payload),
                Complete = true,
                NextCursor = null
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var restored = JsonSerializer.Deserialize<ReadOperationResult>(json, ContractJson.Options);

        Assert.Equal(JsonValueKind.Null, root.GetProperty("next_cursor").ValueKind);
        Assert.Equal("host-1", root.GetProperty("context").GetProperty("instance_id").GetString());
        Assert.Equal("doc-9", root.GetProperty("context").GetProperty("document_id").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("result").ValueKind);
        Assert.Equal(payload, root.GetProperty("result").GetRawText());
        Assert.NotNull(restored);
        Assert.Equal(payload, restored.Result.GetRawText());
        Assert.Null(restored.NextCursor);
        Assert.True(restored.Complete);
    }

    [Fact]
    public void Read_operation_error_codes_use_the_accepted_spellings()
    {
        Assert.Equal("UNKNOWN_READ_OPERATION", CapabilityErrorCodes.UnknownReadOperation);
        Assert.Equal("READ_OPERATION_VERSION_UNSUPPORTED", CapabilityErrorCodes.ReadOperationVersionUnsupported);
        Assert.Equal("INVALID_READ_OPERATION_LOOKUP", CapabilityErrorCodes.InvalidReadOperationLookup);
        Assert.Equal("INVALID_READ_OPERATION_INPUT", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Equal("READ_OPERATION_RESULT_INVALID", CapabilityErrorCodes.ReadOperationResultInvalid);
    }

    private static LookupReadOperationsResult SearchResult(
        int? nextOffset,
        string? inputSchema = null,
        string? outputSchema = null)
    {
        return new LookupReadOperationsResult
        {
            Mode = "search",
            Total = 1,
            ReturnedCount = 1,
            NextOffset = nextOffset,
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
                    Pagination = "cursor",
                    InputSchema = inputSchema is null ? null : Parse(inputSchema),
                    OutputSchema = outputSchema is null ? null : Parse(outputSchema)
                }
            ]
        };
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
