using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class GetSelectionContractTests
{
    [Fact]
    public void Request_and_result_use_snake_case_and_round_trip()
    {
        var request = new GetSelectionRequest
        {
            DocumentId = "opaque-document",
            Limit = 25
        };
        var result = CreateResult(["b-ref", "a-ref"]);
        var requestJson = JsonSerializer.Serialize(request, ContractJson.Options);
        var resultJson = JsonSerializer.Serialize(result, ContractJson.Options);

        Assert.Contains("\"document_id\": \"opaque-document\"", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"limit\": 25", requestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_count\": 2", resultJson, StringComparison.Ordinal);
        Assert.Contains("\"element_refs\"", resultJson, StringComparison.Ordinal);
        Assert.Contains("\"truncated\": false", resultJson, StringComparison.Ordinal);

        var restoredRequest = JsonSerializer.Deserialize<GetSelectionRequest>(requestJson, ContractJson.Options);
        var restoredResult = JsonSerializer.Deserialize<GetSelectionResult>(resultJson, ContractJson.Options);
        Assert.Equal("opaque-document", restoredRequest!.DocumentId);
        Assert.Equal(25, restoredRequest.Limit);
        Assert.Equal("instance-a", restoredResult!.Context.InstanceId);
        Assert.Equal(["b-ref", "a-ref"], restoredResult.ElementRefs);
        Assert.Equal(2, restoredResult.SelectedCount);
        Assert.False(restoredResult.Truncated);
    }

    [Fact]
    public void Omitted_document_id_and_limit_deserialize_as_null()
    {
        var request = JsonSerializer.Deserialize<GetSelectionRequest>("{}", ContractJson.Options);

        Assert.NotNull(request);
        Assert.Null(request.DocumentId);
        Assert.Null(request.Limit);
    }

    [Fact]
    public void Present_empty_document_id_stays_present()
    {
        var request = JsonSerializer.Deserialize<GetSelectionRequest>(
            """
            { "document_id": "  ", "limit": 1 }
            """,
            ContractJson.Options);

        Assert.Equal("  ", request!.DocumentId);
        Assert.Equal(1, request.Limit);
    }

    [Fact]
    public void Opaque_strings_have_no_guid_format_constraint()
    {
        foreach (var type in new[]
        {
            typeof(GetSelectionRequest),
            typeof(GetSelectionContext),
            typeof(GetSelectionResult)
        })
        {
            foreach (var property in type.GetProperties())
            {
                Assert.DoesNotContain(
                    property.GetCustomAttributes(inherit: true).Select(attribute => attribute.GetType().Name),
                    name => name.Contains("Guid", StringComparison.Ordinal) || name.Contains("Uuid", StringComparison.Ordinal));
            }
        }

        Assert.Null(typeof(GetSelectionRequest).GetProperty("InstanceId"));
        Assert.Equal(typeof(string), typeof(GetSelectionResult).GetProperty(nameof(GetSelectionResult.ElementRefs))!.PropertyType.GenericTypeArguments[0]);
    }

    [Fact]
    public void Result_does_not_declare_revit_or_mcp_types()
    {
        var names = typeof(GetSelectionResult).Assembly.GetTypes()
            .Where(type => type.Name.Contains("GetSelection", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties())
            .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name);

        Assert.DoesNotContain(names, name =>
            name.Contains("Autodesk", StringComparison.Ordinal)
            || name.Contains("ModelContextProtocol", StringComparison.Ordinal)
            || name.Contains("ElementId", StringComparison.Ordinal));
    }

    private static GetSelectionResult CreateResult(IReadOnlyList<string> refs)
    {
        return new GetSelectionResult
        {
            Context = new GetSelectionContext
            {
                InstanceId = "instance-a",
                DocumentId = "document-a"
            },
            SelectedCount = refs.Count,
            Truncated = false,
            ElementRefs = refs
        };
    }
}
