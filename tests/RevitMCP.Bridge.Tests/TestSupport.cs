using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal static class TestSupport
{
    public static BridgeInstanceMetadata CreateMetadata(
        string? instanceId = null,
        int processId = 4242,
        DateTimeOffset? startTime = null,
        int sessionId = 7,
        IReadOnlyList<int>? protocolVersions = null)
    {
        return new BridgeInstanceMetadata
        {
            InstanceId = instanceId ?? Guid.NewGuid().ToString("D"),
            ProcessId = processId,
            ProcessStartTimeUtc = startTime ?? new DateTimeOffset(2026, 9, 8, 20, 42, 15, TimeSpan.Zero),
            WindowsSessionId = sessionId,
            RevitVersion = "2026",
            RevitBuild = "26.5.0.0",
            AddinVersion = "0.1.0",
            SupportedProtocolVersions = protocolVersions ?? BridgeProtocol.SupportedVersions
        };
    }

    public static RevitInstanceRegistration CreateRegistration(BridgeInstanceMetadata metadata, string? pipeName = null)
    {
        return new RevitInstanceRegistration
        {
            InstanceId = metadata.InstanceId,
            ProcessId = metadata.ProcessId,
            ProcessStartTimeUtc = metadata.ProcessStartTimeUtc,
            WindowsSessionId = metadata.WindowsSessionId,
            RevitVersion = metadata.RevitVersion,
            RevitBuild = metadata.RevitBuild,
            AddinVersion = metadata.AddinVersion,
            BridgeProtocolVersion = metadata.SupportedProtocolVersions.Max(),
            PipeName = pipeName ?? BridgePipeNames.Create(metadata.WindowsSessionId, metadata.InstanceId),
            RegistrationCreatedUtc = DateTimeOffset.UtcNow
        };
    }
}
