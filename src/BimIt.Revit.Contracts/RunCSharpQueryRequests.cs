using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RunCSharpQueryRequest
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed class RunCSharpQueryApiIdentity
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

public sealed class RunCSharpQueryDiagnostic
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("column")]
    public required int Column { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public sealed class RunCSharpQueryResult
{
    [JsonPropertyName("result")]
    public required JsonElement Result { get; init; }

    [JsonPropertyName("source_sha256")]
    public required string SourceSha256 { get; init; }

    [JsonPropertyName("cache_key")]
    public required string CacheKey { get; init; }

    [JsonPropertyName("policy_version")]
    public required int PolicyVersion { get; init; }

    [JsonPropertyName("revit_api")]
    public required RunCSharpQueryApiIdentity RevitApi { get; init; }

    [JsonPropertyName("compile_outcome")]
    public required string CompileOutcome { get; init; }

    [JsonPropertyName("duration_ms")]
    public required int DurationMs { get; init; }

    [JsonPropertyName("result_characters")]
    public required int ResultCharacters { get; init; }
}
