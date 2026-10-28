using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class ReadSpatialRoomsApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.NoActiveDocument,
        CapabilityErrorCodes.DocumentContextChanged,
        CapabilityErrorCodes.InvalidSpatialRooms,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public ReadSpatialRoomsApplicationService(IRevitInstanceDiscovery discovery, IBridgeClientFactory clients, ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<ReadSpatialRoomsOutcome> ExecuteAsync(string? instanceId, ReadSpatialRoomsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForReadSpatialRooms(discovered, instanceId);
        if (resolution.Target is null)
        {
            return ReadSpatialRoomsOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!, resolution.Candidates);
        }

        var registration = resolution.Target.Registration!;
        try
        {
            await using var client = await _clients.ConnectAsync(registration.PipeName, _timeouts.BootstrapTimeout, cancellationToken).ConfigureAwait(false);
            var handshake = await client.HandshakeAsync(
                new BridgeHandshakeRequest
                {
                    ExpectedInstanceId = registration.InstanceId,
                    SupportedProtocolVersions = BridgeProtocol.SupportedVersions,
                    ClientName = "RevitMCP.Server",
                    ClientVersion = "0.1.0"
                },
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(handshake.InstanceId, registration.InstanceId, StringComparison.Ordinal)
                || !BridgeProtocol.SupportsReadSpatialRooms(handshake.SelectedProtocolVersion))
            {
                return ReadSpatialRoomsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            return ReadSpatialRoomsOutcome.Success(await client.ReadSpatialRoomsAsync(request, _timeouts.CapabilityTimeout, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return ReadSpatialRoomsOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return ReadSpatialRoomsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
