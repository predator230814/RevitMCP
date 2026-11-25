using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class RunCSharpQueryApplicationService
{
    private const string ProtocolIncompatibleMessage =
        "revit_run_csharp_query requires a negotiated bridge protocol that supports it.";

    private static readonly HashSet<string> AcceptedCapabilityErrors = new(StringComparer.Ordinal)
    {
        "CSHARP_QUERY_DISABLED",
        "INVALID_CSHARP_QUERY",
        "CSHARP_QUERY_API_UNSUPPORTED",
        "CSHARP_QUERY_POLICY_REJECTED",
        "CSHARP_QUERY_COMPILE_FAILED",
        "CSHARP_QUERY_COMPILER_FAILED",
        "CSHARP_QUERY_DOCUMENT_NOT_READY",
        "CSHARP_QUERY_BUDGET_EXCEEDED",
        "CSHARP_QUERY_EXECUTION_FAILED",
        "CSHARP_QUERY_INVALID_RESULT",
        "CSHARP_QUERY_RESULT_TOO_LARGE",
        BridgeErrorCodes.ProtocolIncompatible,
        CapabilityErrorCodes.ExecutionTimeout
    };

    private readonly IRevitInstanceDiscovery _discovery;
    private readonly IBridgeClientFactory _clients;
    private readonly ServerTimeouts _timeouts;

    public RunCSharpQueryApplicationService(
        IRevitInstanceDiscovery discovery,
        IBridgeClientFactory clients,
        ServerTimeouts timeouts)
    {
        _discovery = discovery;
        _clients = clients;
        _timeouts = timeouts;
    }

    public async Task<RunCSharpQueryOutcome> ExecuteAsync(
        string? instanceId,
        RunCSharpQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var discovered = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var resolution = InstanceTargetResolver.ResolveForRunCSharpQuery(discovered, instanceId);
        if (resolution.Target is null)
        {
            return RunCSharpQueryOutcome.Failure(resolution.ErrorCode!, resolution.ErrorMessage!, resolution.Candidates);
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
                return RunCSharpQueryOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
            }

            if (!BridgeProtocol.SupportsRunCSharpQuery(handshake.SelectedProtocolVersion))
            {
                return RunCSharpQueryOutcome.Failure(
                    BridgeErrorCodes.ProtocolIncompatible,
                    ProtocolIncompatibleMessage);
            }

            var bridgeRequest = new RunCSharpQueryRequest
            {
                DocumentId = request.DocumentId,
                Source = request.Source
            };
            var result = await client
                .RunCSharpQueryAsync(bridgeRequest, _timeouts.RunCSharpQueryTimeout, cancellationToken)
                .ConfigureAwait(false);
            return RunCSharpQueryOutcome.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BridgeException exception) when (AcceptedCapabilityErrors.Contains(exception.ErrorCode))
        {
            return RunCSharpQueryOutcome.Failure(
                exception.ErrorCode,
                exception.Message,
                details: RunCSharpQueryErrorDetailsSanitizer.Sanitize(exception.Details));
        }
        catch (Exception exception) when (exception is BridgeException or IOException or TimeoutException or OperationCanceledException)
        {
            return RunCSharpQueryOutcome.Failure(McpToolErrorCodes.InstanceUnavailable, ToolErrorMessages.InstanceUnavailable);
        }
    }
}
