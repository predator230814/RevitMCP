using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class QueryElementsContractTests
{
    [Fact]
    public void Serialization_uses_exact_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateResult(["ref-a"]), ContractJson.Options);

        Assert.Contains("\"document_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scope\"", json, StringComparison.Ordinal);
        Assert.Contains("\"filters\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category_names\"", json, StringComparison.Ordinal);
        Assert.Contains("\"family_names\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type_names\"", json, StringComparison.Ordinal);
        Assert.Contains("\"level_names\"", json, StringComparison.Ordinal);
        Assert.Contains("\"text_contains\"", json, StringComparison.Ordinal);
        Assert.Contains("\"limit\"", json, StringComparison.Ordinal);
        Assert.Contains("\"instance_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"matched_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"truncated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_refs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"active_view\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementRefs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TextContains", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(QueryElementsRequest.DocumentId),
                nameof(QueryElementsRequest.Filters),
                nameof(QueryElementsRequest.Limit),
                nameof(QueryElementsRequest.Scope)
            ],
            typeof(QueryElementsRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Filter_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(QueryElementFilters.CategoryNames),
                nameof(QueryElementFilters.FamilyNames),
                nameof(QueryElementFilters.LevelNames),
                nameof(QueryElementFilters.TextContains),
                nameof(QueryElementFilters.TypeNames)
            ],
            typeof(QueryElementFilters).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Result_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(QueryElementsResult.Context),
                nameof(QueryElementsResult.ElementRefs),
                nameof(QueryElementsResult.MatchedCount),
                nameof(QueryElementsResult.Truncated)
            ],
            typeof(QueryElementsResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(QueryElementsContext.DocumentId), nameof(QueryElementsContext.InstanceId)],
            typeof(QueryElementsContext).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Document_id_is_an_ordinary_opaque_string()
    {
        var json = """
            {
              "document_id": "opaque-document-id",
              "scope": "document",
              "filters": { "category_names": ["Doors"] },
              "limit": 50
            }
            """;

        var request = JsonSerializer.Deserialize<QueryElementsRequest>(json, ContractJson.Options);
        Assert.NotNull(request);
        Assert.Equal("opaque-document-id", request.DocumentId);

        var property = typeof(QueryElementsRequest).GetProperty(nameof(QueryElementsRequest.DocumentId));
        Assert.Equal(typeof(string), property!.PropertyType);
        AssertNoGuidFormatConstraint(property);
        AssertNoGuidFormatConstraint(typeof(QueryElementsContext).GetProperty(nameof(QueryElementsContext.DocumentId))!);
    }

    [Fact]
    public void Element_ref_is_an_ordinary_opaque_string()
    {
        var result = JsonSerializer.Deserialize<QueryElementsResult>(
            JsonSerializer.Serialize(CreateResult(["opaque-element-ref"]), ContractJson.Options),
            ContractJson.Options);

        Assert.NotNull(result);
        Assert.Equal(["opaque-element-ref"], result.ElementRefs);
        Assert.Equal(typeof(IReadOnlyList<string>), typeof(QueryElementsResult).GetProperty(nameof(QueryElementsResult.ElementRefs))!.PropertyType);
        AssertNoGuidFormatConstraint(typeof(QueryElementsResult).GetProperty(nameof(QueryElementsResult.ElementRefs))!);
    }

    [Fact]
    public void Transport_neutral_contract_does_not_embed_guid_or_uuid_format()
    {
        foreach (var type in new[]
        {
            typeof(QueryElementsRequest),
            typeof(QueryElementFilters),
            typeof(QueryElementsResult),
            typeof(QueryElementsContext),
            typeof(QueryScope)
        })
        {
            foreach (var property in type.GetProperties())
            {
                AssertNoGuidFormatConstraint(property);
            }
        }

        var request = JsonSerializer.Deserialize<QueryElementsRequest>(
            """
            {
              "document_id": "not-a-guid",
              "scope": "document",
              "filters": { "text_contains": "VAV" },
              "limit": 10
            }
            """,
            ContractJson.Options);
        Assert.Equal("not-a-guid", request!.DocumentId);

        var result = JsonSerializer.Deserialize<QueryElementsResult>(
            JsonSerializer.Serialize(CreateResult(["also-not-a-uuid"]), ContractJson.Options),
            ContractJson.Options);
        Assert.Equal(["also-not-a-uuid"], result!.ElementRefs);
    }

    [Fact]
    public void Both_scope_values_round_trip()
    {
        foreach (var scope in new[] { QueryScope.Document, QueryScope.ActiveView })
        {
            var original = new QueryElementsRequest
            {
                DocumentId = null,
                Scope = scope,
                Filters = new QueryElementFilters { CategoryNames = ["Walls"] },
                Limit = 50
            };

            var restored = JsonSerializer.Deserialize<QueryElementsRequest>(
                JsonSerializer.Serialize(original, ContractJson.Options),
                ContractJson.Options);

            Assert.NotNull(restored);
            Assert.Equal(scope, restored.Scope);
        }

        using var documentScope = JsonDocument.Parse(JsonSerializer.Serialize(
            new QueryElementsRequest
            {
                Scope = QueryScope.Document,
                Filters = new QueryElementFilters { CategoryNames = ["Walls"] },
                Limit = 1
            },
            ContractJson.Options));
        Assert.Equal("document", documentScope.RootElement.GetProperty("scope").GetString());

        using var viewScope = JsonDocument.Parse(JsonSerializer.Serialize(
            new QueryElementsRequest
            {
                Scope = QueryScope.ActiveView,
                Filters = new QueryElementFilters { CategoryNames = ["Walls"] },
                Limit = 1
            },
            ContractJson.Options));
        Assert.Equal("active_view", viewScope.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public void Result_supports_zero_refs()
    {
        var json = JsonSerializer.Serialize(CreateResult([]), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var refs = document.RootElement.GetProperty("element_refs");
        Assert.Equal(JsonValueKind.Array, refs.ValueKind);
        Assert.Equal(0, refs.GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("matched_count").GetInt32());
        Assert.False(document.RootElement.GetProperty("truncated").GetBoolean());

        var restored = JsonSerializer.Deserialize<QueryElementsResult>(json, ContractJson.Options);
        Assert.NotNull(restored);
        Assert.Empty(restored.ElementRefs);
    }

    [Fact]
    public void Result_supports_bounded_refs()
    {
        var refs = Enumerable.Range(0, 100).Select(index => $"ref-{index:D3}").ToArray();
        var result = new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = "instance-1",
                DocumentId = "document-1"
            },
            MatchedCount = 140,
            Truncated = true,
            ElementRefs = refs
        };

        var restored = JsonSerializer.Deserialize<QueryElementsResult>(
            JsonSerializer.Serialize(result, ContractJson.Options),
            ContractJson.Options);

        Assert.NotNull(restored);
        Assert.Equal(100, restored.ElementRefs.Count);
        Assert.Equal(140, restored.MatchedCount);
        Assert.True(restored.Truncated);
        Assert.Equal(refs, restored.ElementRefs);
    }

    [Fact]
    public void Result_has_no_per_element_metadata_or_element_item_dto()
    {
        var names = typeof(QueryElementsResult).GetProperties()
            .Concat(typeof(QueryElementsContext).GetProperties())
            .Concat(typeof(QueryElementsRequest).GetProperties())
            .Concat(typeof(QueryElementFilters).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        string[] forbidden =
        [
            "ElementId",
            "Name",
            "Category",
            "Family",
            "Type",
            "Level",
            "Parameters",
            "Geometry",
            "BoundingBox",
            "Location"
        ];

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, names);
        }

        Assert.Null(typeof(QueryElementsResult).Assembly.GetType("RevitMCP.Contracts.QueryElement"));
        Assert.Null(typeof(QueryElementsResult).Assembly.GetType("RevitMCP.Contracts.QueryElementItem"));
        Assert.Equal(typeof(IReadOnlyList<string>), typeof(QueryElementsResult).GetProperty(nameof(QueryElementsResult.ElementRefs))!.PropertyType);
    }

    [Fact]
    public void Capability_error_codes_include_accepted_cap0002_codes()
    {
        Assert.Equal("NO_ACTIVE_DOCUMENT", CapabilityErrorCodes.NoActiveDocument);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", CapabilityErrorCodes.DocumentContextChanged);
        Assert.Equal("NO_ACTIVE_VIEW", CapabilityErrorCodes.NoActiveView);
        Assert.Equal("INVALID_QUERY", CapabilityErrorCodes.InvalidQuery);
        Assert.Equal("REVIT_EXECUTION_TIMEOUT", CapabilityErrorCodes.ExecutionTimeout);
        Assert.Equal("REVIT_EXECUTION_FAILED", CapabilityErrorCodes.ExecutionFailed);
    }

    [Fact]
    public void Omitted_document_id_is_absent_from_serialized_request()
    {
        var json = JsonSerializer.Serialize(
            new QueryElementsRequest
            {
                DocumentId = null,
                Scope = QueryScope.Document,
                Filters = new QueryElementFilters { CategoryNames = ["Doors"] },
                Limit = 50
            },
            ContractJson.Options);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("document_id", out _));
        Assert.Equal(["filters", "limit", "scope"], document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    private static QueryElementsRequest CreateRequest()
    {
        return new QueryElementsRequest
        {
            DocumentId = "opaque-document-id",
            Scope = QueryScope.ActiveView,
            Filters = new QueryElementFilters
            {
                CategoryNames = ["Mechanical Equipment"],
                FamilyNames = ["VAV Box"],
                TypeNames = ["VAV-6in"],
                LevelNames = ["Level 2"],
                TextContains = "VAV"
            },
            Limit = 50
        };
    }

    private static QueryElementsResult CreateResult(IReadOnlyList<string> elementRefs)
    {
        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            MatchedCount = elementRefs.Count,
            Truncated = false,
            ElementRefs = elementRefs
        };
    }

    private static void AssertNoGuidFormatConstraint(PropertyInfo property)
    {
        Assert.DoesNotContain(property.GetCustomAttributes(), attribute =>
        {
            var name = attribute.GetType().Name;
            return name.Contains("RegularExpression", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Uuid", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Format", StringComparison.OrdinalIgnoreCase);
        });

        var jsonProperty = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (jsonProperty is not null)
        {
            Assert.DoesNotContain("guid", jsonProperty.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("uuid", jsonProperty.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
