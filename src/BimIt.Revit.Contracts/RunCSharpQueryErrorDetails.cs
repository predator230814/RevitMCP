using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class RunCSharpQueryErrorDetails
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("source_sha256")]
    public string? SourceSha256 { get; init; }

    [JsonPropertyName("policy_version")]
    public int? PolicyVersion { get; init; }

    [JsonPropertyName("revit_api")]
    public RunCSharpQueryApiIdentity? RevitApi { get; init; }

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<RunCSharpQueryDiagnostic>? Diagnostics { get; init; }

    [JsonPropertyName("compile_outcome")]
    public string? CompileOutcome { get; init; }
}
