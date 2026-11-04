using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class QueryFamilyTypesApplicationService
{
    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.NoActiveDocument,
        CapabilityErrorCodes.DocumentContextChanged,
        CapabilityErrorCodes.UnsupportedDocumentKind,
        CapabilityErrorCodes.InvalidFamilyTypeQuery,
        CapabilityErrorCodes.InvalidCursor,
        CapabilityErrorCodes.StaleCursor,
        CapabilityErrorCodes.ResultTooLarge,
        CapabilityErrorCodes.ExecutionTimeout,
        CapabilityErrorCodes.ExecutionFailed,
        BridgeErrorCodes.ProtocolIncompatible
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public QueryFamilyTypesApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<QueryFamilyTypesOutcome> ExecuteAsync(
        string? instanceId,
        QueryFamilyTypesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForQueryFamilyTypes(discovered, instanceId);
        if (resolution.Target is null)
        {
            return QueryFamilyTypesOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!, resolution.Candidates);
        }

        var registration = resolution.Target.Registration!;
        try
        {
            await using var client = await _clients
                .ConnectAsync(registration.PipeName, _timeouts.BootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);
            var handshake = await client.HandshakeAsync(
                    new BridgeHandshakeRequest
                    {
                        ExpectedInstanceId = registration.InstanceId,
                        SupportedProtocolVersions = BridgeProtocol.SupportedVersions,
                        ClientName = "RevitMCP.Server",
                        ClientVersion = "0.1.0"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(handshake.InstanceId, registration.InstanceId, StringComparison.Ordinal))
            {
                return QueryFamilyTypesOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            if (!BridgeProtocol.SupportsQueryFamilyTypes(handshake.SelectedProtocolVersion))
            {
                return QueryFamilyTypesOutcome.Failure(
                    BridgeErrorCodes.ProtocolIncompatible,
                    "revit.query_family_types requires a negotiated bridge protocol version that explicitly supports it.");
            }

            var result = await client.QueryFamilyTypesAsync(request, _timeouts.CapabilityTimeout, cancellationToken).ConfigureAwait(false);
            return QueryFamilyTypesOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return QueryFamilyTypesOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return QueryFamilyTypesOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
