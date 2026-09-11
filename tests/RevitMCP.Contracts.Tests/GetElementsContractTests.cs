using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class GetElementsContractTests
{
    [Fact]
    public void Serialization_uses_exact_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateOkResult(), ContractJson.Options);

        Assert.Contains("\"document_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_refs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"projection\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_names\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"family_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"level_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameters_truncated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value_text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value_truncated\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementRefs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterNames", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueText", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(GetElementsRequest.DocumentId),
                nameof(GetElementsRequest.ElementRefs),
                nameof(GetElementsRequest.Projection)
            ],
            typeof(GetElementsRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetElementsProjection.Fields), nameof(GetElementsProjection.ParameterNames)],
            typeof(GetElementsProjection).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain(
            typeof(GetElementsRequest).GetProperties(),
            property => property.Name.Contains("Instance", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_contract_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [nameof(GetElementsResult.Context), nameof(GetElementsResult.Elements)],
            typeof(GetElementsResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetElementsContext.DocumentId), nameof(GetElementsContext.InstanceId)],
            typeof(GetElementsContext).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [
                nameof(GetElementResult.CategoryName),
                nameof(GetElementResult.ElementRef),
                nameof(GetElementResult.FamilyName),
                nameof(GetElementResult.LevelName),
                nameof(GetElementResult.Name),
                nameof(GetElementResult.Parameters),
                nameof(GetElementResult.ParametersTruncated),
                nameof(GetElementResult.Status),
                nameof(GetElementResult.TypeName)
            ],
            typeof(GetElementResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain("MatchedCount", typeof(GetElementsResult).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Truncated", typeof(GetElementsResult).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void Parameter_entry_has_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [
                nameof(GetElementParameter.Name),
                nameof(GetElementParameter.Source),
                nameof(GetElementParameter.ValueText),
                nameof(GetElementParameter.ValueTruncated)
            ],
            typeof(GetElementParameter).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Transport_neutral_request_has_no_instance_id()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("instance_id", out _));
        Assert.Equal(
            ["document_id", "element_refs", "projection"],
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Document_id_and_element_refs_are_ordinary_opaque_strings()
    {
        var request = JsonSerializer.Deserialize<GetElementsRequest>(
            """
            {
              "document_id": "opaque-document-id",
              "element_refs": ["opaque-element-ref"],
              "projection": { "fields": ["name"] }
            }
            """,
            ContractJson.Options);

        Assert.NotNull(request);
        Assert.Equal("opaque-document-id", request.DocumentId);
        Assert.Equal(["opaque-element-ref"], request.ElementRefs);
        Assert.Equal(typeof(string), typeof(GetElementsRequest).GetProperty(nameof(GetElementsRequest.DocumentId))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<string>), typeof(GetElementsRequest).GetProperty(nameof(GetElementsRequest.ElementRefs))!.PropertyType);
        AssertNoGuidFormatConstraint(typeof(GetElementsRequest).GetProperty(nameof(GetElementsRequest.DocumentId))!);
        AssertNoGuidFormatConstraint(typeof(GetElementsRequest).GetProperty(nameof(GetElementsRequest.ElementRefs))!);
        AssertNoGuidFormatConstraint(typeof(GetElementResult).GetProperty(nameof(GetElementResult.ElementRef))!);
    }

    [Fact]
    public void Transport_neutral_contract_does_not_embed_guid_or_uuid_format()
    {
        foreach (var type in new[]
        {
            typeof(GetElementsRequest),
            typeof(GetElementsProjection),
            typeof(GetElementsResult),
            typeof(GetElementsContext),
            typeof(GetElementResult),
            typeof(GetElementParameter),
            typeof(GetElementField),
            typeof(GetElementResultStatus),
            typeof(GetElementParameterSource)
        })
        {
            foreach (var property in type.GetProperties())
            {
                AssertNoGuidFormatConstraint(property);
            }
        }
    }

    [Fact]
    public void Field_enum_serializes_the_five_accepted_values()
    {
        Assert.Equal(
            ["name", "category_name", "family_name", "type_name", "level_name"],
            Enum.GetValues<GetElementField>().Select(SerializeEnum).ToArray());
    }

    [Fact]
    public void Status_enum_serializes_ok_and_not_found()
    {
        Assert.Equal("ok", SerializeEnum(GetElementResultStatus.Ok));
        Assert.Equal("not_found", SerializeEnum(GetElementResultStatus.NotFound));
        Assert.Equal(2, Enum.GetValues<GetElementResultStatus>().Length);
    }

    [Fact]
    public void Parameter_source_enum_serializes_instance_and_type()
    {
        Assert.Equal("instance", SerializeEnum(GetElementParameterSource.Instance));
        Assert.Equal("type", SerializeEnum(GetElementParameterSource.Type));
        Assert.Equal(2, Enum.GetValues<GetElementParameterSource>().Length);
    }

    [Fact]
    public void Result_order_is_preserved()
    {
        var result = new GetElementsResult
        {
            Context = Context(),
            Elements =
            [
                NotFound("ref-b"),
                OkName("ref-a", "A"),
                NotFound("ref-c")
            ]
        };

        var restored = RoundTrip(result);
        Assert.Equal(["ref-b", "ref-a", "ref-c"], restored.Elements.Select(item => item.ElementRef).ToArray());
        Assert.Equal(
            [GetElementResultStatus.NotFound, GetElementResultStatus.Ok, GetElementResultStatus.NotFound],
            restored.Elements.Select(item => item.Status).ToArray());
    }

    [Fact]
    public void Not_found_serializes_exactly_two_properties()
    {
        var json = JsonSerializer.Serialize(NotFound("opaque-element-ref"), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["element_ref", "status"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("opaque-element-ref", document.RootElement.GetProperty("element_ref").GetString());
        Assert.Equal("not_found", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.TryGetProperty("name", out _));
        Assert.False(document.RootElement.TryGetProperty("parameters", out _));
        Assert.False(document.RootElement.TryGetProperty("parameters_truncated", out _));
    }

    [Fact]
    public void Unrequested_basic_field_is_absent()
    {
        var json = JsonSerializer.Serialize(
            new GetElementResult
            {
                ElementRef = "ref-1",
                Status = GetElementResultStatus.Ok,
                Name = ProjectedString.Of("VAV Box 12")
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("VAV Box 12", document.RootElement.GetProperty("name").GetString());
        Assert.False(document.RootElement.TryGetProperty("category_name", out _));
        Assert.False(document.RootElement.TryGetProperty("family_name", out _));
        Assert.False(document.RootElement.TryGetProperty("type_name", out _));
        Assert.False(document.RootElement.TryGetProperty("level_name", out _));
    }

    [Fact]
    public void Requested_available_basic_field_is_a_string()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new GetElementResult
            {
                ElementRef = "ref-1",
                Status = GetElementResultStatus.Ok,
                FamilyName = ProjectedString.Of("VAV Box")
            },
            ContractJson.Options));
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("family_name").ValueKind);
        Assert.Equal("VAV Box", document.RootElement.GetProperty("family_name").GetString());
    }

    [Fact]
    public void Requested_unavailable_basic_field_is_explicit_json_null()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new GetElementResult
            {
                ElementRef = "ref-1",
                Status = GetElementResultStatus.Ok,
                LevelName = ProjectedString.Unavailable
            },
            ContractJson.Options));
        Assert.True(document.RootElement.TryGetProperty("level_name", out var level));
        Assert.Equal(JsonValueKind.Null, level.ValueKind);
    }

    [Fact]
    public void Three_state_projection_survives_round_trip()
    {
        var original = new GetElementResult
        {
            ElementRef = "ref-1",
            Status = GetElementResultStatus.Ok,
            Name = ProjectedString.Of("VAV Box 12"),
            CategoryName = ProjectedString.Unavailable,
            FamilyName = ProjectedString.Omitted
        };

        var restored = JsonSerializer.Deserialize<GetElementResult>(
            JsonSerializer.Serialize(original, ContractJson.Options),
            ContractJson.Options);

        Assert.NotNull(restored);
        Assert.True(restored.Name.IsRequested);
        Assert.Equal("VAV Box 12", restored.Name.Value);
        Assert.True(restored.CategoryName.IsRequested);
        Assert.Null(restored.CategoryName.Value);
        Assert.False(restored.FamilyName.IsRequested);
        Assert.Null(restored.FamilyName.Value);
        Assert.False(restored.TypeName.IsRequested);
        Assert.False(restored.LevelName.IsRequested);
    }

    [Fact]
    public void Parameters_are_absent_when_not_requested()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new GetElementResult
            {
                ElementRef = "ref-1",
                Status = GetElementResultStatus.Ok,
                Name = ProjectedString.Of("A")
            },
            ContractJson.Options));
        Assert.False(document.RootElement.TryGetProperty("parameters", out _));
        Assert.False(document.RootElement.TryGetProperty("parameters_truncated", out _));
    }

    [Fact]
    public void Requested_zero_match_parameters_serialize_as_empty_array_and_false()
    {
        var json = JsonSerializer.Serialize(
            new GetElementResult
            {
                ElementRef = "ref-1",
                Status = GetElementResultStatus.Ok,
                Parameters = [],
                ParametersTruncated = false
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("parameters").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("parameters").GetArrayLength());
        Assert.False(document.RootElement.GetProperty("parameters_truncated").GetBoolean());
    }

    [Fact]
    public void Parameter_entry_serializes_exact_fields_only()
    {
        var json = JsonSerializer.Serialize(
            new GetElementParameter
            {
                Name = "Flow",
                Source = GetElementParameterSource.Instance,
                ValueText = "850 CFM",
                ValueTruncated = false
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["name", "source", "value_text", "value_truncated"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Parameter_value_text_null_is_explicitly_present()
    {
        var json = JsonSerializer.Serialize(
            new GetElementParameter
            {
                Name = "Mark",
                Source = GetElementParameterSource.Type,
                ValueText = null,
                ValueTruncated = false
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("value_text", out var value));
        Assert.Equal(JsonValueKind.Null, value.ValueKind);
        Assert.False(document.RootElement.GetProperty("value_truncated").GetBoolean());
    }

    [Fact]
    public void Capability_error_codes_include_invalid_inspection_and_preserve_existing_codes()
    {
        Assert.Equal("INVALID_INSPECTION", CapabilityErrorCodes.InvalidInspection);
        Assert.Equal("INVALID_QUERY", CapabilityErrorCodes.InvalidQuery);
        Assert.Equal("NO_ACTIVE_DOCUMENT", CapabilityErrorCodes.NoActiveDocument);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", CapabilityErrorCodes.DocumentContextChanged);
        Assert.Equal("NO_ACTIVE_VIEW", CapabilityErrorCodes.NoActiveView);
        Assert.Equal("REVIT_EXECUTION_TIMEOUT", CapabilityErrorCodes.ExecutionTimeout);
        Assert.Equal("REVIT_EXECUTION_FAILED", CapabilityErrorCodes.ExecutionFailed);
    }

    [Fact]
    public void Contracts_do_not_leak_revit_or_mcp_implementation_types()
    {
        var assembly = typeof(GetElementsRequest).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            Assert.DoesNotContain("Autodesk", type.FullName, StringComparison.Ordinal);
            Assert.DoesNotContain("RevitAPI", type.FullName, StringComparison.Ordinal);
            Assert.DoesNotContain("ModelContextProtocol", type.FullName, StringComparison.Ordinal);
            Assert.DoesNotContain("StreamJsonRpc", type.FullName, StringComparison.Ordinal);
        }

        DependencyBoundaryTests.AssertNoForbiddenReferences(
            assembly,
            ["RevitAPI", "RevitAPIUI", "ModelContextProtocol", "StreamJsonRpc"]);
    }

    private static GetElementsRequest CreateRequest()
    {
        return new GetElementsRequest
        {
            DocumentId = "opaque-document-id",
            ElementRefs = ["opaque-element-ref-1", "opaque-element-ref-2"],
            Projection = new GetElementsProjection
            {
                Fields =
                [
                    GetElementField.Name,
                    GetElementField.CategoryName,
                    GetElementField.FamilyName,
                    GetElementField.TypeName,
                    GetElementField.LevelName
                ],
                ParameterNames = ["Mark", "Flow"]
            }
        };
    }

    private static GetElementsResult CreateOkResult()
    {
        return new GetElementsResult
        {
            Context = Context(),
            Elements =
            [
                new GetElementResult
                {
                    ElementRef = "opaque-element-ref-1",
                    Status = GetElementResultStatus.Ok,
                    Name = ProjectedString.Of("VAV Box 12"),
                    CategoryName = ProjectedString.Of("Mechanical Equipment"),
                    FamilyName = ProjectedString.Of("VAV Box"),
                    TypeName = ProjectedString.Of("VAV-6in"),
                    LevelName = ProjectedString.Unavailable,
                    Parameters =
                    [
                        new GetElementParameter
                        {
                            Name = "Flow",
                            Source = GetElementParameterSource.Instance,
                            ValueText = "850 CFM",
                            ValueTruncated = false
                        }
                    ],
                    ParametersTruncated = false
                }
            ]
        };
    }

    private static GetElementsContext Context()
    {
        return new GetElementsContext
        {
            InstanceId = "opaque-instance-id",
            DocumentId = "opaque-document-id"
        };
    }

    private static GetElementResult OkName(string elementRef, string name)
    {
        return new GetElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.Ok,
            Name = ProjectedString.Of(name)
        };
    }

    private static GetElementResult NotFound(string elementRef)
    {
        return new GetElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.NotFound
        };
    }

    private static GetElementsResult RoundTrip(GetElementsResult result)
    {
        return JsonSerializer.Deserialize<GetElementsResult>(
            JsonSerializer.Serialize(result, ContractJson.Options),
            ContractJson.Options)!;
    }

    private static string SerializeEnum<T>(T value)
    {
        return JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(value, ContractJson.Options), ContractJson.Options)!;
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
