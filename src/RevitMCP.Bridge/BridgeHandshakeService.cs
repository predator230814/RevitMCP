using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public sealed class BridgeHandshakeService : IRevitBridgeService
{
    private readonly BridgeInstanceMetadata _metadata;

    public BridgeHandshakeService(BridgeInstanceMetadata metadata)
    {
        _metadata = metadata;
    }

    public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ExpectedInstanceId))
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeFailed, "expected_instance_id is required.");
        }

        if (request.SupportedProtocolVersions is null || request.SupportedProtocolVersions.All(version => version <= 0))
        {
            throw new BridgeException(BridgeErrorCodes.HandshakeFailed, "supported_protocol_versions must contain at least one positive integer.");
        }

        if (!string.Equals(request.ExpectedInstanceId, _metadata.InstanceId, StringComparison.Ordinal))
        {
            throw new BridgeException(BridgeErrorCodes.IdentityMismatch, "The endpoint identity does not match the expected instance.");
        }

        var selected = ProtocolVersionSelector.SelectHighestCommon(request.SupportedProtocolVersions, _metadata.SupportedProtocolVersions);
        if (selected is null)
        {
            throw new BridgeException(BridgeErrorCodes.ProtocolIncompatible, "No common bridge protocol version exists.");
        }

        return Task.FromResult(new BridgeHandshakeResult
        {
            InstanceId = _metadata.InstanceId,
            ProcessId = _metadata.ProcessId,
            ProcessStartTimeUtc = _metadata.ProcessStartTimeUtc,
            WindowsSessionId = _metadata.WindowsSessionId,
            RevitVersion = _metadata.RevitVersion,
            RevitBuild = _metadata.RevitBuild,
            AddinVersion = _metadata.AddinVersion,
            SupportedProtocolVersions = _metadata.SupportedProtocolVersions,
            SelectedProtocolVersion = selected.Value
        });
    }
}
