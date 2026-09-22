using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class GetParameterValuesContractTests
{
    [Fact]
    public void Serialization_uses_exact_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateOkResult(), ContractJson.Options)
            + JsonSerializer.Serialize(
                new GetParameterQuantityValue { Value = 1.5, UnitTypeId = "autodesk.unit.unit:cubicFeet-1.0.1" },
                ContractJson.Options);

        Assert.Contains("\"document_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"data_type\"", json, StringComparison.Ordinal);
        Assert.Contains("\"has_value\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unit_type_id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("HasValue", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitTypeId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_and_result_have_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [nameof(GetParameterValuesRequest.DocumentId), nameof(GetParameterValuesRequest.Reads)],
            typeof(GetParameterValuesRequest).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetParameterValueRead.ElementRef), nameof(GetParameterValueRead.ParameterRef)],
            typeof(GetParameterValueRead).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetParameterValuesResult.Context), nameof(GetParameterValuesResult.Items)],
            typeof(GetParameterValuesResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain(
            typeof(GetParameterValuesRequest).GetProperties(),
            property => property.Name.Contains("Instance", StringComparison.Ordinal));
    }

    [Fact]
    public void Transport_neutral_request_has_no_instance_id()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("instance_id", out _));
    }

    [Fact]
    public void Request_round_trips_opaque_empty_and_whitespace_refs()
    {
        var request = JsonSerializer.Deserialize<GetParameterValuesRequest>(
            """
            {
              "document_id": " ",
              "reads": [
                { "element_ref": "", "parameter_ref": "\t" }
              ]
            }
            """,
            ContractJson.Options);

        Assert.NotNull(request);
        Assert.Equal(" ", request.DocumentId);
        Assert.Equal("", request.Reads[0].ElementRef);
        Assert.Equal("\t", request.Reads[0].ParameterRef);
    }

    [Fact]
    public void Ok_without_value_omits_value()
    {
        var json = JsonSerializer.Serialize(
            new GetParameterValueItem
            {
                ElementRef = "el",
                ParameterRef = "pr",
                Status = GetParameterValueStatus.Ok,
                DataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Unknown },
                HasValue = false
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("value", out _));
        Assert.False(document.RootElement.GetProperty("has_value").GetBoolean());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Failure_item_omits_data_type_has_value_and_value()
    {
        foreach (var status in new[]
        {
            GetParameterValueStatus.ElementNotFound,
            GetParameterValueStatus.ParameterRefNotFound,
            GetParameterValueStatus.ParameterNotPresent,
            GetParameterValueStatus.UnsupportedValue
        })
        {
            var json = JsonSerializer.Serialize(
                new GetParameterValueItem
                {
                    ElementRef = "el",
                    ParameterRef = "pr",
                    Status = status
                },
                ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(["element_ref", "parameter_ref", "status"],
                document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        }
    }

    [Fact]
    public void String_value_round_trips_kind_and_truncation()
    {
        AssertValueRoundTrip(
            new GetParameterStringValue { Value = "hello", Truncated = true },
            json =>
            {
                Assert.Equal("string", json.GetProperty("kind").GetString());
                Assert.Equal("hello", json.GetProperty("value").GetString());
                Assert.True(json.GetProperty("truncated").GetBoolean());
            },
            value =>
            {
                var typed = Assert.IsType<GetParameterStringValue>(value);
                Assert.Equal("hello", typed.Value);
                Assert.True(typed.Truncated);
            });
    }

    [Fact]
    public void Integer_value_round_trips_exact_integer()
    {
        AssertValueRoundTrip(
            new GetParameterIntegerValue { Value = -7 },
            json =>
            {
                Assert.Equal("integer", json.GetProperty("kind").GetString());
                Assert.Equal(-7, json.GetProperty("value").GetInt32());
            },
            value => Assert.Equal(-7, Assert.IsType<GetParameterIntegerValue>(value).Value));
    }

    [Fact]
    public void Quantity_value_round_trips_converted_number_and_unit()
    {
        AssertValueRoundTrip(
            new GetParameterQuantityValue { Value = 12.5, UnitTypeId = "autodesk.unit.unit:cubicFeet-1.0.1" },
            json =>
            {
                Assert.Equal("quantity", json.GetProperty("kind").GetString());
                Assert.Equal(12.5, json.GetProperty("value").GetDouble());
                Assert.Equal("autodesk.unit.unit:cubicFeet-1.0.1", json.GetProperty("unit_type_id").GetString());
            },
            value =>
            {
                var typed = Assert.IsType<GetParameterQuantityValue>(value);
                Assert.Equal(12.5, typed.Value);
                Assert.Equal("autodesk.unit.unit:cubicFeet-1.0.1", typed.UnitTypeId);
            });
    }

    [Fact]
    public void Element_reference_omits_optional_fields_when_unresolved()
    {
        AssertValueRoundTrip(
            new GetParameterElementReferenceValue { Resolved = false },
            json =>
            {
                Assert.Equal("element_reference", json.GetProperty("kind").GetString());
                Assert.False(json.GetProperty("resolved").GetBoolean());
                Assert.False(json.TryGetProperty("name", out _));
                Assert.False(json.TryGetProperty("element_ref", out _));
            },
            value =>
            {
                var typed = Assert.IsType<GetParameterElementReferenceValue>(value);
                Assert.False(typed.Resolved);
                Assert.Null(typed.Name);
                Assert.Null(typed.ElementRef);
            });
    }

    [Fact]
    public void Element_reference_type_target_may_omit_element_ref()
    {
        AssertValueRoundTrip(
            new GetParameterElementReferenceValue { Resolved = true, Name = "Generic" },
            json =>
            {
                Assert.True(json.GetProperty("resolved").GetBoolean());
                Assert.Equal("Generic", json.GetProperty("name").GetString());
                Assert.False(json.TryGetProperty("element_ref", out _));
            },
            value =>
            {
                var typed = Assert.IsType<GetParameterElementReferenceValue>(value);
                Assert.True(typed.Resolved);
                Assert.Equal("Generic", typed.Name);
                Assert.Null(typed.ElementRef);
            });
    }

    [Fact]
    public void Result_round_trips_ok_and_failure_items()
    {
        var json = JsonSerializer.Serialize(CreateOkResult(), ContractJson.Options);
        var result = JsonSerializer.Deserialize<GetParameterValuesResult>(json, ContractJson.Options);

        Assert.NotNull(result);
        Assert.Equal("instance-1", result.Context.InstanceId);
        Assert.Equal("opaque-document-id", result.Context.DocumentId);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(GetParameterValueStatus.Ok, result.Items[0].Status);
        Assert.True(result.Items[0].HasValue);
        Assert.IsType<GetParameterIntegerValue>(result.Items[0].Value);
        Assert.Equal(GetParameterValueStatus.ParameterRefNotFound, result.Items[1].Status);
        Assert.Null(result.Items[1].DataType);
        Assert.Null(result.Items[1].HasValue);
        Assert.Null(result.Items[1].Value);
    }

    [Fact]
    public void Invalid_parameter_read_error_code_is_exact()
    {
        Assert.Equal("INVALID_PARAMETER_READ", CapabilityErrorCodes.InvalidParameterRead);
    }

    [Fact]
    public void Status_enum_serializes_to_accepted_snake_case()
    {
        Assert.Equal("\"ok\"", JsonSerializer.Serialize(GetParameterValueStatus.Ok, ContractJson.Options));
        Assert.Equal("\"element_not_found\"", JsonSerializer.Serialize(GetParameterValueStatus.ElementNotFound, ContractJson.Options));
        Assert.Equal("\"parameter_ref_not_found\"", JsonSerializer.Serialize(GetParameterValueStatus.ParameterRefNotFound, ContractJson.Options));
        Assert.Equal("\"parameter_not_present\"", JsonSerializer.Serialize(GetParameterValueStatus.ParameterNotPresent, ContractJson.Options));
        Assert.Equal("\"unsupported_value\"", JsonSerializer.Serialize(GetParameterValueStatus.UnsupportedValue, ContractJson.Options));
    }

    private static void AssertValueRoundTrip(
        GetParameterValue value,
        Action<JsonElement> assertJson,
        Action<GetParameterValue> assertValue)
    {
        var item = new GetParameterValueItem
        {
            ElementRef = "el",
            ParameterRef = "pr",
            Status = GetParameterValueStatus.Ok,
            DataType = new DescribeParameterDataType { Kind = DescribeParameterDataTypeKind.Unknown },
            HasValue = true,
            Value = value
        };

        var json = JsonSerializer.Serialize(item, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        assertJson(document.RootElement.GetProperty("value"));

        var roundTrip = JsonSerializer.Deserialize<GetParameterValueItem>(json, ContractJson.Options);
        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip.Value);
        assertValue(roundTrip.Value);
    }

    private static GetParameterValuesRequest CreateRequest()
    {
        return new GetParameterValuesRequest
        {
            DocumentId = "opaque-document-id",
            Reads =
            [
                new GetParameterValueRead
                {
                    ElementRef = "opaque-element-ref",
                    ParameterRef = "opaque-parameter-ref"
                }
            ]
        };
    }

    private static GetParameterValuesResult CreateOkResult()
    {
        return new GetParameterValuesResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = "instance-1",
                DocumentId = "opaque-document-id"
            },
            Items =
            [
                new GetParameterValueItem
                {
                    ElementRef = "opaque-element-ref",
                    ParameterRef = "opaque-parameter-ref",
                    Status = GetParameterValueStatus.Ok,
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.Spec
                    },
                    HasValue = true,
                    Value = new GetParameterIntegerValue { Value = 3 }
                },
                new GetParameterValueItem
                {
                    ElementRef = "missing-el",
                    ParameterRef = "unknown-pr",
                    Status = GetParameterValueStatus.ParameterRefNotFound
                }
            ]
        };
    }
}
