using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class BridgeHandshakeResult
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("process_id")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("process_start_time_utc")]
    public required DateTimeOffset ProcessStartTimeUtc { get; init; }

    [JsonPropertyName("windows_session_id")]
    public required int WindowsSessionId { get; init; }

    [JsonPropertyName("revit_version")]
    public required string RevitVersion { get; init; }

    [JsonPropertyName("revit_build")]
    public required string RevitBuild { get; init; }

    [JsonPropertyName("addin_version")]
    public required string AddinVersion { get; init; }

    [JsonPropertyName("supported_protocol_versions")]
    public required IReadOnlyList<int> SupportedProtocolVersions { get; init; }

    [JsonPropertyName("selected_protocol_version")]
    public required int SelectedProtocolVersion { get; init; }
}
