using System.Text.Json.Serialization;

namespace BimIt.Revit.Contracts;

public sealed class DiscoveredInstance
{
    [JsonPropertyName("state")]
    public required DiscoveryState State { get; init; }

    [JsonPropertyName("registration")]
    public RevitInstanceRegistration? Registration { get; init; }

    [JsonPropertyName("handshake")]
    public BridgeHandshakeResult? Handshake { get; init; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }
}
