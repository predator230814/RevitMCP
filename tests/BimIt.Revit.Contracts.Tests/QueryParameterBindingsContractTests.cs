using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class QueryParameterBindingsContractTests
{
    [Fact]
    public void Request_has_exactly_the_accepted_fields_and_no_instance_id()
    {
        Assert.Equal(
            [
                nameof(QueryParameterBindingsRequest.BindingKind),
                nameof(QueryParameterBindingsRequest.CategoryTokens),
                nameof(QueryParameterBindingsRequest.Cursor),
                nameof(QueryParameterBindingsRequest.DocumentId),
                nameof(QueryParameterBindingsRequest.IdentityKind),
                nameof(QueryParameterBindingsRequest.NameContains),
                nameof(QueryParameterBindingsRequest.PageSize)
            ],
            Names<QueryParameterBindingsRequest>());

        var json = JsonSerializer.Serialize(
            new QueryParameterBindingsRequest
            {
                DocumentId = "doc",
                NameContains = "Tt_",
                IdentityKind = "shared",
                BindingKind = "instance",
                CategoryTokens = ["OST_ElectricalFixtures"],
                PageSize = 50,
                Cursor = "c"
            },
            ContractJson.Options);
        foreach (var name in new[] { "document_id", "name_contains", "identity_kind", "binding_kind", "category_tokens", "page_size", "cursor" })
        {
            Assert.Contains("\"" + name + "\"", json, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("instance_id", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_row_category_and_summary_have_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(QueryParameterBindingsResult.Bindings),
                nameof(QueryParameterBindingsResult.Complete),
                nameof(QueryParameterBindingsResult.Context),
                nameof(QueryParameterBindingsResult.NextCursor),
                nameof(QueryParameterBindingsResult.ReturnedCount),
                nameof(QueryParameterBindingsResult.Summary)
            ],
            Names<QueryParameterBindingsResult>());
        Assert.Equal(
            [
                nameof(QueryParameterBindingRow.BindingKind),
                nameof(QueryParameterBindingRow.Categories),
                nameof(QueryParameterBindingRow.DataType),
                nameof(QueryParameterBindingRow.Identity),
                nameof(QueryParameterBindingRow.Name),
                nameof(QueryParameterBindingRow.ParameterRef)
            ],
            Names<QueryParameterBindingRow>());
        Assert.Equal(
            [nameof(QueryParameterBindingCategory.CategoryName), nameof(QueryParameterBindingCategory.CategoryToken)],
            Names<QueryParameterBindingCategory>());
        Assert.Equal(
            [
                nameof(QueryParameterBindingsSummary.BuiltInCount),
                nameof(QueryParameterBindingsSummary.DistinctCategoryCount),
                nameof(QueryParameterBindingsSummary.InstanceBoundCount),
                nameof(QueryParameterBindingsSummary.LocalCount),
                nameof(QueryParameterBindingsSummary.MatchedDefinitionCount),
                nameof(QueryParameterBindingsSummary.SharedCount),
                nameof(QueryParameterBindingsSummary.TypeBoundCount)
            ],
            Names<QueryParameterBindingsSummary>());
        Assert.Equal(
            [nameof(QueryParameterBindingsContext.DocumentId), nameof(QueryParameterBindingsContext.InstanceId)],
            Names<QueryParameterBindingsContext>());
    }

    [Fact]
    public void Rows_reuse_the_CAP_0004_identity_and_data_type_types()
    {
        Assert.Equal(typeof(DescribeParameterIdentity), typeof(QueryParameterBindingRow).GetProperty(nameof(QueryParameterBindingRow.Identity))!.PropertyType);
        Assert.Equal(typeof(DescribeParameterDataType), typeof(QueryParameterBindingRow).GetProperty(nameof(QueryParameterBindingRow.DataType))!.PropertyType);
    }

    [Fact]
    public void Serialization_uses_snake_case_explicit_null_cursor_and_keeps_unicode()
    {
        var json = ReadResultBudget.Serialize(Result(nextCursor: null));

        Assert.Contains("\"next_cursor\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"matched_definition_count\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"built_in_count\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"distinct_category_count\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"binding_kind\":\"instance\"", json, StringComparison.Ordinal);
        Assert.Contains("\"binding_kind\":\"type\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"shared\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"local\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category_token\":\"OST_ElectricalFixtures\"", json, StringComparison.Ordinal);
        Assert.Contains("\"forge_type_id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingKind", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unique_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("element_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stable_key", json, StringComparison.Ordinal);

        var roundTrip = JsonSerializer.Deserialize<QueryParameterBindingsResult>(json, ReadResultBudget.Options)!;
        Assert.Equal("Tt_Capacité_Sectionneur", roundTrip.Bindings[0].Name);
        Assert.Equal("CHARGE INTÉGRÉE", roundTrip.Bindings[1].Name);
        Assert.Equal(QueryParameterBindingKind.Type, roundTrip.Bindings[1].BindingKind);
        Assert.Null(roundTrip.NextCursor);
        Assert.Empty(roundTrip.Bindings[1].Categories);

        Assert.Contains("\"next_cursor\":\"abc\"", ReadResultBudget.Serialize(Result(nextCursor: "abc")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_error_code_is_stable()
    {
        Assert.Equal("INVALID_PARAMETER_BINDING_QUERY", CapabilityErrorCodes.InvalidParameterBindingQuery);
    }

    private static QueryParameterBindingsResult Result(string? nextCursor)
    {
        return new QueryParameterBindingsResult
        {
            Context = new QueryParameterBindingsContext { InstanceId = "instance", DocumentId = "doc" },
            Summary = new QueryParameterBindingsSummary
            {
                MatchedDefinitionCount = 2,
                SharedCount = 1,
                LocalCount = 1,
                BuiltInCount = 0,
                InstanceBoundCount = 1,
                TypeBoundCount = 1,
                DistinctCategoryCount = 1
            },
            ReturnedCount = 2,
            Complete = nextCursor is null,
            NextCursor = nextCursor,
            Bindings =
            [
                new QueryParameterBindingRow
                {
                    ParameterRef = "ref-1",
                    Name = "Tt_Capacité_Sectionneur",
                    Identity = new DescribeParameterIdentity { Kind = DescribeParameterIdentityKind.Shared, Guid = "857e1417-0000-0000-0000-000000000000" },
                    DataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.MeasurableSpec, ForgeTypeId = "autodesk.spec.aec.electrical:current-2.0.0" },
                    BindingKind = QueryParameterBindingKind.Instance,
                    Categories = [new QueryParameterBindingCategory { CategoryToken = "OST_ElectricalFixtures", CategoryName = "Electrical Fixtures" }]
                },
                new QueryParameterBindingRow
                {
                    ParameterRef = "ref-2",
                    Name = "CHARGE INTÉGRÉE",
                    Identity = new DescribeParameterIdentity { Kind = DescribeParameterIdentityKind.Local },
                    DataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Spec, ForgeTypeId = "autodesk.spec:spec.string-2.0.0" },
                    BindingKind = QueryParameterBindingKind.Type,
                    Categories = []
                }
            ]
        };
    }

    private static string[] Names<T>()
    {
        return typeof(T).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
