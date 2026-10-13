using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public sealed class BridgeInstanceMetadata
{
    public required string InstanceId { get; init; }

    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartTimeUtc { get; init; }

    public required int WindowsSessionId { get; init; }

    public required string RevitVersion { get; init; }

    public required string RevitBuild { get; init; }

    public required string AddinVersion { get; init; }

    public IReadOnlyList<int> SupportedProtocolVersions { get; init; } = BridgeProtocol.SupportedVersions;
}
