using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class PreviewParameterUpdatesContractTests
{
    [Fact]
    public void Serialization_uses_exact_snake_case_names()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateReadyResult(), ContractJson.Options);

        Assert.Contains("\"document_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"updates\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unit_type_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_name_truncated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category_name_truncated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"parameter_name_truncated\"", json, StringComparison.Ordinal);
        Assert.Contains("\"data_type\"", json, StringComparison.Ordinal);
        Assert.Contains("\"has_value\"", json, StringComparison.Ordinal);
        Assert.Contains("\"intent_ref\"", json, StringComparison.Ordinal);
        Assert.Contains("\"intent_fingerprint\"", json, StringComparison.Ordinal);
        Assert.Contains("\"expires_at\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitTypeId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("HasValue", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IntentRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiresAt", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_result_and_item_have_exactly_the_accepted_fields()
    {
        Assert.Equal(
            [nameof(PreviewParameterUpdatesRequest.DocumentId), nameof(PreviewParameterUpdatesRequest.Updates)],
            PropertyNames(typeof(PreviewParameterUpdatesRequest)));
        Assert.Equal(
            [
                nameof(PreviewParameterUpdate.ElementRef),
                nameof(PreviewParameterUpdate.ParameterRef),
                nameof(PreviewParameterUpdate.Value)
            ],
            PropertyNames(typeof(PreviewParameterUpdate)));
        Assert.Equal(
            [
                nameof(PreviewParameterUpdatesResult.Context),
                nameof(PreviewParameterUpdatesResult.ExpiresAt),
                nameof(PreviewParameterUpdatesResult.IntentFingerprint),
                nameof(PreviewParameterUpdatesResult.IntentRef),
                nameof(PreviewParameterUpdatesResult.Items),
                nameof(PreviewParameterUpdatesResult.Ready)
            ],
            PropertyNames(typeof(PreviewParameterUpdatesResult)));
        Assert.Equal(
            [
                nameof(PreviewParameterUpdateItem.Before),
                nameof(PreviewParameterUpdateItem.CategoryName),
                nameof(PreviewParameterUpdateItem.CategoryNameTruncated),
                nameof(PreviewParameterUpdateItem.DataType),
                nameof(PreviewParameterUpdateItem.ElementName),
                nameof(PreviewParameterUpdateItem.ElementNameTruncated),
                nameof(PreviewParameterUpdateItem.ElementRef),
                nameof(PreviewParameterUpdateItem.ParameterName),
                nameof(PreviewParameterUpdateItem.ParameterNameTruncated),
                nameof(PreviewParameterUpdateItem.ParameterRef),
                nameof(PreviewParameterUpdateItem.Proposed),
                nameof(PreviewParameterUpdateItem.Status)
            ],
            PropertyNames(typeof(PreviewParameterUpdateItem)));
        Assert.Equal(
            [nameof(PreviewParameterBefore.HasValue), nameof(PreviewParameterBefore.Value)],
            PropertyNames(typeof(PreviewParameterBefore)));
        Assert.DoesNotContain(
            typeof(PreviewParameterUpdatesRequest).GetProperties(),
            property => property.Name.Contains("Instance", StringComparison.Ordinal));
        Assert.DoesNotContain(
            PropertyNames(typeof(PreviewParameterUpdateItem)),
            name => name.Contains("Order", StringComparison.Ordinal)
                || name.Contains("Position", StringComparison.Ordinal)
                || name.Contains("Index", StringComparison.Ordinal));
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
        var request = JsonSerializer.Deserialize<PreviewParameterUpdatesRequest>(
            """
            {
              "document_id": " ",
              "updates": [
                {
                  "element_ref": "",
                  "parameter_ref": "\t",
                  "value": { "kind": "quantity", "value": 1, "unit_type_id": " " }
                }
              ]
            }
            """,
            ContractJson.Options);

        Assert.NotNull(request);
        Assert.Equal(" ", request.DocumentId);
        Assert.Equal("", request.Updates[0].ElementRef);
        Assert.Equal("\t", request.Updates[0].ParameterRef);
        var quantity = Assert.IsType<PreviewParameterQuantityValue>(request.Updates[0].Value);
        Assert.Equal(" ", quantity.UnitTypeId);
    }

    [Fact]
    public void Value_union_serializes_string_integer_and_quantity_without_cap0005_fields()
    {
        var json = JsonSerializer.Serialize(CreateRequest(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("updates").EnumerateArray()
            .Select(update => update.GetProperty("value"))
            .ToArray();

        Assert.Equal("string", values[0].GetProperty("kind").GetString());
        Assert.Equal("", values[0].GetProperty("value").GetString());
        Assert.Equal(["kind", "value"], values[0].EnumerateObject().Select(property => property.Name).ToArray());

        Assert.Equal("integer", values[1].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Number, values[1].GetProperty("value").ValueKind);
        Assert.Equal(int.MinValue, values[1].GetProperty("value").GetInt32());
        Assert.Equal(["kind", "value"], values[1].EnumerateObject().Select(property => property.Name).ToArray());

        Assert.Equal("quantity", values[2].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Number, values[2].GetProperty("value").ValueKind);
        Assert.Equal(1.5, values[2].GetProperty("value").GetDouble());
        Assert.Equal("autodesk.unit.unit:feet-1.0.1", values[2].GetProperty("unit_type_id").GetString());
        Assert.Equal(["kind", "unit_type_id", "value"], values[2].EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.DoesNotContain("truncated", json, StringComparison.Ordinal);
        Assert.DoesNotContain("element_reference", json, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved", json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(PreviewParameterStringValue).GetProperties(),
            property => property.Name == "Truncated");
        Assert.DoesNotContain(
            typeof(PreviewParameterValue).Assembly.GetTypes(),
            type => type.IsSubclassOf(typeof(PreviewParameterValue)) && type.Name.Contains("ElementReference", StringComparison.Ordinal));

        var roundTrip = JsonSerializer.Deserialize<PreviewParameterUpdatesRequest>(json, ContractJson.Options);
        Assert.NotNull(roundTrip);
        Assert.IsType<PreviewParameterStringValue>(roundTrip.Updates[0].Value);
        Assert.IsType<PreviewParameterIntegerValue>(roundTrip.Updates[1].Value);
        Assert.IsType<PreviewParameterQuantityValue>(roundTrip.Updates[2].Value);
        Assert.Equal(typeof(int), typeof(PreviewParameterIntegerValue).GetProperty(nameof(PreviewParameterIntegerValue.Value))!.PropertyType);
    }

    [Fact]
    public void Element_reference_kind_is_not_a_preview_value()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PreviewParameterValue>(
            """{"kind":"element_reference","resolved":true}""",
            ContractJson.Options));
    }

    [Fact]
    public void Every_status_serializes_as_the_accepted_snake_case_token()
    {
        var expected = new Dictionary<PreviewParameterUpdateStatus, string>
        {
            [PreviewParameterUpdateStatus.Ok] = "ok",
            [PreviewParameterUpdateStatus.ElementNotFound] = "element_not_found",
            [PreviewParameterUpdateStatus.ParameterRefNotFound] = "parameter_ref_not_found",
            [PreviewParameterUpdateStatus.ParameterNotPresent] = "parameter_not_present",
            [PreviewParameterUpdateStatus.UnsupportedParameterSource] = "unsupported_parameter_source",
            [PreviewParameterUpdateStatus.ParameterNotWritable] = "parameter_not_writable",
            [PreviewParameterUpdateStatus.ValueTypeMismatch] = "value_type_mismatch",
            [PreviewParameterUpdateStatus.InvalidUnit] = "invalid_unit",
            [PreviewParameterUpdateStatus.UnsupportedValue] = "unsupported_value",
            [PreviewParameterUpdateStatus.NoChange] = "no_change"
        };

        Assert.Equal(expected.Keys.OrderBy(status => status), Enum.GetValues<PreviewParameterUpdateStatus>().OrderBy(status => status));
        foreach (var (status, token) in expected)
        {
            Assert.Equal($"\"{token}\"", JsonSerializer.Serialize(status, ContractJson.Options));
        }
    }

    [Fact]
    public void Failure_item_is_exactly_three_fields()
    {
        foreach (var status in Enum.GetValues<PreviewParameterUpdateStatus>()
            .Where(status => status is not PreviewParameterUpdateStatus.Ok and not PreviewParameterUpdateStatus.NoChange))
        {
            var json = JsonSerializer.Serialize(
                new PreviewParameterUpdateItem
                {
                    ElementRef = "el",
                    ParameterRef = "pr",
                    Status = status
                },
                ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                ["element_ref", "parameter_ref", "status"],
                document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        }
    }

    [Fact]
    public void Before_without_value_omits_value()
    {
        var json = JsonSerializer.Serialize(
            new PreviewParameterBefore { HasValue = false },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("has_value").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("value", out _));
        Assert.Equal(["has_value"], document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Ok_item_nests_cap0004_data_type_without_a_sibling_forge_type_id()
    {
        var json = JsonSerializer.Serialize(CreateReadyResult().Items[0], ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("forge_type_id", out _));
        Assert.False(document.RootElement.TryGetProperty("order", out _));
        Assert.False(document.RootElement.TryGetProperty("request_position", out _));
        var dataType = document.RootElement.GetProperty("data_type");
        Assert.Equal("measurable_spec", dataType.GetProperty("kind").GetString());
        Assert.Equal("autodesk.spec.aec:length-2.0.0", dataType.GetProperty("forge_type_id").GetString());
        Assert.Equal(typeof(DescribeParameterDataType), typeof(PreviewParameterUpdateItem).GetProperty(nameof(PreviewParameterUpdateItem.DataType))!.PropertyType);
    }

    [Fact]
    public void Ready_false_omits_intent_metadata()
    {
        var json = JsonSerializer.Serialize(
            new PreviewParameterUpdatesResult
            {
                Context = new DescribeParametersContext { InstanceId = "instance", DocumentId = "document" },
                Ready = false,
                Items =
                [
                    new PreviewParameterUpdateItem
                    {
                        ElementRef = "el",
                        ParameterRef = "pr",
                        Status = PreviewParameterUpdateStatus.ElementNotFound
                    }
                ]
            },
            ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("intent_ref", out _));
        Assert.False(document.RootElement.TryGetProperty("intent_fingerprint", out _));
        Assert.False(document.RootElement.TryGetProperty("expires_at", out _));
        Assert.Equal("instance", document.RootElement.GetProperty("context").GetProperty("instance_id").GetString());
    }

    [Fact]
    public void Ready_true_includes_intent_metadata_and_utc_expires_at()
    {
        var expiresAt = new DateTimeOffset(2026, 9, 29, 10, 5, 6, 123, TimeSpan.FromHours(-4)).AddTicks(4567);
        var result = CreateReadyResult();
        result = new PreviewParameterUpdatesResult
        {
            Context = result.Context,
            Ready = true,
            Items = result.Items,
            IntentRef = result.IntentRef,
            IntentFingerprint = result.IntentFingerprint,
            ExpiresAt = expiresAt
        };

        var json = JsonSerializer.Serialize(result, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.Equal("intent", document.RootElement.GetProperty("intent_ref").GetString());
        Assert.Equal("fingerprint", document.RootElement.GetProperty("intent_fingerprint").GetString());
        Assert.Equal("2026-09-29T14:05:06.1234567Z", document.RootElement.GetProperty("expires_at").GetString());
        Assert.DoesNotContain("+00:00", json, StringComparison.Ordinal);

        var roundTrip = JsonSerializer.Deserialize<PreviewParameterUpdatesResult>(json, ContractJson.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(expiresAt.UtcDateTime, roundTrip.ExpiresAt!.Value.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, roundTrip.ExpiresAt.Value.Offset);
    }

    [Fact]
    public void Expires_at_whole_second_uses_a_z_offset()
    {
        var json = JsonSerializer.Serialize(
            new DateTimeOffset(2026, 9, 29, 14, 5, 6, TimeSpan.Zero),
            new JsonSerializerOptions(ContractJson.Options)
            {
                Converters = { new PreviewExpiresAtJsonConverter() }
            });

        Assert.Equal("\"2026-09-29T14:05:06Z\"", json);
    }

    [Fact]
    public void New_preview_error_codes_are_exact_and_existing_codes_are_unchanged()
    {
        Assert.Equal("UNSUPPORTED_DOCUMENT_KIND", CapabilityErrorCodes.UnsupportedDocumentKind);
        Assert.Equal("DOCUMENT_NOT_WRITABLE", CapabilityErrorCodes.DocumentNotWritable);
        Assert.Equal("INVALID_PARAMETER_UPDATE_PREVIEW", CapabilityErrorCodes.InvalidParameterUpdatePreview);
        Assert.Equal("INTENT_CAPACITY_REACHED", CapabilityErrorCodes.IntentCapacityReached);
        Assert.Equal("NO_ACTIVE_DOCUMENT", CapabilityErrorCodes.NoActiveDocument);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", CapabilityErrorCodes.DocumentContextChanged);
        Assert.Equal("REVIT_EXECUTION_TIMEOUT", CapabilityErrorCodes.ExecutionTimeout);
        Assert.Equal("REVIT_EXECUTION_FAILED", CapabilityErrorCodes.ExecutionFailed);
    }

    private static string[] PropertyNames(Type type)
        => type.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private static PreviewParameterUpdatesRequest CreateRequest()
        => new()
        {
            DocumentId = "document",
            Updates =
            [
                new PreviewParameterUpdate
                {
                    ElementRef = "el-1",
                    ParameterRef = "pr-1",
                    Value = new PreviewParameterStringValue { Value = "" }
                },
                new PreviewParameterUpdate
                {
                    ElementRef = "el-2",
                    ParameterRef = "pr-2",
                    Value = new PreviewParameterIntegerValue { Value = int.MinValue }
                },
                new PreviewParameterUpdate
                {
                    ElementRef = "el-3",
                    ParameterRef = "pr-3",
                    Value = new PreviewParameterQuantityValue
                    {
                        Value = 1.5,
                        UnitTypeId = "autodesk.unit.unit:feet-1.0.1"
                    }
                }
            ]
        };

    private static PreviewParameterUpdatesResult CreateReadyResult()
        => new()
        {
            Context = new DescribeParametersContext { InstanceId = "instance", DocumentId = "document" },
            Ready = true,
            IntentRef = "intent",
            IntentFingerprint = "fingerprint",
            ExpiresAt = new DateTimeOffset(2026, 9, 29, 14, 5, 6, TimeSpan.Zero),
            Items =
            [
                new PreviewParameterUpdateItem
                {
                    ElementRef = "el-1",
                    ParameterRef = "pr-1",
                    Status = PreviewParameterUpdateStatus.Ok,
                    ElementName = "Door",
                    ElementNameTruncated = false,
                    CategoryName = "Doors",
                    CategoryNameTruncated = false,
                    ParameterName = "Comments",
                    ParameterNameTruncated = false,
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                        ForgeTypeId = "autodesk.spec.aec:length-2.0.0"
                    },
                    Before = new PreviewParameterBefore
                    {
                        HasValue = true,
                        Value = new PreviewParameterStringValue { Value = "old" }
                    },
                    Proposed = new PreviewParameterStringValue { Value = "new" }
                }
            ]
        };
}
