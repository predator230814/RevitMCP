using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public sealed class RevitInstanceRegistration
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

    [JsonPropertyName("bridge_protocol_version")]
    public required int BridgeProtocolVersion { get; init; }

    [JsonPropertyName("pipe_name")]
    public required string PipeName { get; init; }

    [JsonPropertyName("registration_created_utc")]
    public required DateTimeOffset RegistrationCreatedUtc { get; init; }
}
