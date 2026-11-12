using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

/// <summary>
/// CAP-0016 transport-neutral lookup request. No <c>instance_id</c>: routing stays a Server concern.
/// Every field is optional. A JSON null is absence and is validated by later layers.
/// </summary>
public sealed class LookupReadOperationsRequest
{
    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    [JsonPropertyName("contract_version")]
    public int? ContractVersion { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("offset")]
    public int? Offset { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

public sealed class LookupReadOperationsResult
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("total")]
    public required int Total { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("next_offset")]
    public int? NextOffset { get; init; }

    [JsonPropertyName("operations")]
    public required IReadOnlyList<ReadOperationDescriptor> Operations { get; init; }
}

public sealed class ReadOperationDescriptor
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("contract_version")]
    public required int ContractVersion { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("context")]
    public required string Context { get; init; }

    [JsonPropertyName("effects")]
    public required string Effects { get; init; }

    [JsonPropertyName("pagination")]
    public required string Pagination { get; init; }

    [JsonConverter(typeof(RawJsonElementConverter))]
    [JsonPropertyName("input_schema")]
    public JsonElement? InputSchema { get; init; }

    [JsonConverter(typeof(RawJsonElementConverter))]
    [JsonPropertyName("output_schema")]
    public JsonElement? OutputSchema { get; init; }
}

/// <summary>
/// CAP-0016 transport-neutral read request. No <c>instance_id</c>: routing stays a Server concern.
/// Missing or null values are validated by later layers.
/// </summary>
public sealed class ReadOperationRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    [JsonPropertyName("contract_version")]
    public int? ContractVersion { get; init; }

    [JsonConverter(typeof(RawJsonElementConverter))]
    [JsonPropertyName("inputs")]
    public JsonElement? Inputs { get; init; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

public sealed class ReadOperationResult
{
    [JsonPropertyName("context")]
    public required ReadOperationContext Context { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("contract_version")]
    public required int ContractVersion { get; init; }

    [JsonConverter(typeof(RawJsonElementConverter))]
    [JsonPropertyName("result")]
    public required JsonElement Result { get; init; }

    [JsonPropertyName("complete")]
    public required bool Complete { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

public sealed class ReadOperationContext
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }
}

/// <summary>
/// Writes a <see cref="JsonElement"/> with its original JSON text.
/// The contract serializer otherwise re-encodes nested payloads.
/// </summary>
internal sealed class RawJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(value.GetRawText());
    }
}
