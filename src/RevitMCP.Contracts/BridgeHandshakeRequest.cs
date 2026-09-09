using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class BridgeHandshakeRequest
{
    [JsonPropertyName("expected_instance_id")]
    public required string ExpectedInstanceId { get; init; }

    [JsonPropertyName("supported_protocol_versions")]
    public required IReadOnlyList<int> SupportedProtocolVersions { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("client_version")]
    public string? ClientVersion { get; init; }
}
