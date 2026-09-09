using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class BridgeError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
