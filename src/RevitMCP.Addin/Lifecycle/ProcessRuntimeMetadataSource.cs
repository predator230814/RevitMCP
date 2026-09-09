using System.Diagnostics;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Lifecycle;

internal sealed class ProcessRuntimeMetadataSource : IRuntimeMetadataSource
{
    public BridgeInstanceMetadata Capture(string instanceId, LifecycleRevitRuntime revit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var process = Process.GetCurrentProcess();
        return new BridgeInstanceMetadata
        {
            InstanceId = instanceId,
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime).ToUniversalTime(),
            WindowsSessionId = process.SessionId,
            RevitVersion = revit.VersionNumber,
            RevitBuild = revit.VersionBuild,
            AddinVersion = AddinVersionInfo.Current,
            SupportedProtocolVersions = BridgeProtocol.SupportedVersions
        };
    }
}

internal static class AddinVersionInfo
{
    public static string Current { get; } =
        typeof(AddinVersionInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
