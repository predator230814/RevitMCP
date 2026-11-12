using System.Text.Json.Serialization;

namespace BimIt.Revit.Addin.ReadOperations;

internal readonly record struct UserWorksetCandidate(string Name, bool IsDefault, bool IsVisibleByDefault);

internal readonly record struct UserWorksetInventory(bool WorksharingEnabled, IReadOnlyList<UserWorksetCandidate> Worksets);

internal sealed class UserWorksetsRequest
{
    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }
}

internal sealed class UserWorksetsSummary
{
    [JsonPropertyName("worksharing_enabled")]
    public required bool WorksharingEnabled { get; init; }

    [JsonPropertyName("matched_workset_count")]
    public required int MatchedWorksetCount { get; init; }
}

internal sealed class UserWorksetRow
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }

    [JsonPropertyName("is_visible_by_default")]
    public required bool IsVisibleByDefault { get; init; }
}

internal sealed class UserWorksetsResult
{
    [JsonPropertyName("summary")]
    public required UserWorksetsSummary Summary { get; init; }

    [JsonPropertyName("returned_count")]
    public required int ReturnedCount { get; init; }

    [JsonPropertyName("worksets")]
    public required IReadOnlyList<UserWorksetRow> Worksets { get; init; }
}
