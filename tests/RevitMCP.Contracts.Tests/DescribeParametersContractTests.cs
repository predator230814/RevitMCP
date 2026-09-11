using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class DescribeParametersContractTests
{
    [Fact]
    public void Serialization_uses_exact_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateResult(), ContractJson.Options);

        Assert.Contains("\"document_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_refs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name_contains\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_type_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"forge_type_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"present_on_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"read_only_on_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"matched_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"built_in\"", json, StringComparison.Ordinal);
        Assert.Contains("\"measurable_spec\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PresentOnCount", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(DescribeParametersRequest.DocumentId),
                nameof(DescribeParametersRequest.ElementRefs),
                nameof(DescribeParametersRequest.Limit),
                nameof(DescribeParametersRequest.NameContains),
                nameof(DescribeParametersRequest.Source)
            ],
            typeof(DescribeParametersRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain(
            typeof(DescribeParametersRequest).GetProperties(),
            property => property.Name.Contains("Instance", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_and_descriptor_have_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(DescribeParametersResult.Context),
                nameof(DescribeParametersResult.Elements),
                nameof(DescribeParametersResult.MatchedCount),
                nameof(DescribeParametersResult.Parameters),
                nameof(DescribeParametersResult.Truncated)
            ],
            typeof(DescribeParametersResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [
                nameof(DescribeParameterDescriptor.DataType),
                nameof(DescribeParameterDescriptor.Identity),
                nameof(DescribeParameterDescriptor.Name),
                nameof(DescribeParameterDescriptor.ParameterRef),
                nameof(DescribeParameterDescriptor.PresentOnCount),
                nameof(DescribeParameterDescriptor.ReadOnlyOnCount),
                nameof(DescribeParameterDescriptor.Source)
            ],
            typeof(DescribeParameterDescriptor).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(DescribeParameterElementResult.ElementRef), nameof(DescribeParameterElementResult.Status)],
            typeof(DescribeParameterElementResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Transport_neutral_request_has_no_instance_id()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("instance_id", out _));
    }

    [Fact]
    public void Document_id_element_refs_and_parameter_refs_are_ordinary_opaque_strings()
    {
        var request = JsonSerializer.Deserialize<DescribeParametersRequest>(
            """
            {
              "document_id": "opaque-document-id",
              "element_refs": ["opaque-element-ref"]
            }
            """,
            ContractJson.Options);

        Assert.NotNull(request);
        Assert.Equal("opaque-document-id", request.DocumentId);
        Assert.Equal(["opaque-element-ref"], request.ElementRefs);
        Assert.Equal(typeof(string), typeof(DescribeParametersRequest).GetProperty(nameof(DescribeParametersRequest.DocumentId))!.PropertyType);
        Assert.Equal(typeof(string), typeof(DescribeParameterDescriptor).GetProperty(nameof(DescribeParameterDescriptor.ParameterRef))!.PropertyType);
        Assert.Equal(DescribeParameterSource.Both, request.Source);
        Assert.Equal(50, request.Limit);
        Assert.Null(request.NameContains);
    }

    [Fact]
    public void Local_identity_omits_portable_fields()
    {
        var json = JsonSerializer.Serialize(
            new DescribeParameterIdentity { Kind = DescribeParameterIdentityKind.Local },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["kind"], document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("local", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void Empty_data_type_omits_forge_type_id()
    {
        var json = JsonSerializer.Serialize(
            new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Unknown },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("forge_type_id", out _));
    }

    [Fact]
    public void Result_contains_no_parameter_values()
    {
        var json = JsonSerializer.Serialize(CreateResult(), ContractJson.Options);
        Assert.DoesNotContain("value_text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AsDouble", json, StringComparison.Ordinal);
        Assert.DoesNotContain("writable", json, StringComparison.Ordinal);
    }

    private static DescribeParametersRequest CreateRequest()
    {
        return new DescribeParametersRequest
        {
            DocumentId = "opaque-document-id",
            ElementRefs = ["opaque-element-ref"],
            Source = DescribeParameterSource.Both,
            NameContains = "Flow",
            Limit = 50
        };
    }

    private static DescribeParametersResult CreateResult()
    {
        return new DescribeParametersResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = "instance-1",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new DescribeParameterElementResult
                {
                    ElementRef = "opaque-element-ref",
                    Status = GetElementResultStatus.Ok
                }
            ],
            MatchedCount = 1,
            Truncated = false,
            Parameters =
            [
                new DescribeParameterDescriptor
                {
                    ParameterRef = "opaque-parameter-ref",
                    Name = "Flow",
                    Source = GetElementParameterSource.Instance,
                    Identity = new DescribeParameterIdentity
                    {
                        Kind = DescribeParameterIdentityKind.BuiltIn,
                        ParameterTypeId = "autodesk.revit.parameter:hvacAirflow"
                    },
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                        ForgeTypeId = "autodesk.spec.aec:airflow"
                    },
                    PresentOnCount = 1,
                    ReadOnlyOnCount = 0
                }
            ]
        };
    }
}
