using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class GetContextResult
{
    [JsonPropertyName("instance")]
    public required GetContextInstance Instance { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("document")]
    public GetContextDocument? Document { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("active_view")]
    public GetContextActiveView? ActiveView { get; init; }

    [JsonPropertyName("selection")]
    public required GetContextSelection Selection { get; init; }
}
