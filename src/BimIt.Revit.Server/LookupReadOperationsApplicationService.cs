using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class LookupReadOperationsApplicationService
{
    private const string ProtocolIncompatibleMessage =
        "revit.lookup_read_operations and revit.read_operation require a negotiated bridge protocol version that explicitly supports them.";

    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        CapabilityErrorCodes.UnknownReadOperation,
        CapabilityErrorCodes.ReadOperationVersionUnsupported,
        CapabilityErrorCodes.InvalidReadOperationLookup,
        CapabilityErrorCodes.InvalidReadOperationInput,
        CapabilityErrorCodes.ReadOperationResultInvalid,
        CapabilityErrorCodes.NoActiveDocument,
        CapabilityErrorCodes.DocumentContextChanged,
        CapabilityErrorCodes.UnsupportedDocumentKind,
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

    public LookupReadOperationsApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<LookupReadOperationsOutcome> ExecuteAsync(
        string? instanceId,
        LookupReadOperationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForReadOperations(discovered, instanceId);
        if (resolution.Target is null)
        {
            return LookupReadOperationsOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!, resolution.Candidates);
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
                return LookupReadOperationsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            if (!BridgeProtocol.SupportsReadOperations(handshake.SelectedProtocolVersion))
            {
                return LookupReadOperationsOutcome.Failure(
                    BridgeErrorCodes.ProtocolIncompatible,
                    ProtocolIncompatibleMessage);
            }

            var result = await client.LookupReadOperationsAsync(request, _timeouts.CapabilityTimeout, cancellationToken).ConfigureAwait(false);
            return LookupReadOperationsOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return LookupReadOperationsOutcome.Failure(exception.ErrorCode, exception.Message);
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return LookupReadOperationsOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
