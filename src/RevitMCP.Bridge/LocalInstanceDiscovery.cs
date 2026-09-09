using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public sealed class LocalInstanceDiscovery
{
    private readonly IRegistrationStore _store;
    private readonly IProcessInspector _processInspector;
    private readonly IBridgeClientFactory _clientFactory;
    private readonly TimeSpan _handshakeTimeout;
    private readonly IReadOnlyList<int> _supportedProtocolVersions;

    public LocalInstanceDiscovery(
        IRegistrationStore store,
        IProcessInspector processInspector,
        IBridgeClientFactory? clientFactory = null,
        TimeSpan? handshakeTimeout = null,
        IReadOnlyList<int>? supportedProtocolVersions = null)
    {
        _store = store;
        _processInspector = processInspector;
        _clientFactory = clientFactory ?? new NamedPipeBridgeClientFactory();
        _handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(2);
        _supportedProtocolVersions = supportedProtocolVersions ?? BridgeProtocol.SupportedVersions;
    }

    public async Task<IReadOnlyList<DiscoveredInstance>> DiscoverAsync(int windowsSessionId, CancellationToken cancellationToken)
    {
        var candidates = await _store.ReadSessionAsync(windowsSessionId, cancellationToken).ConfigureAwait(false);
        var results = new List<DiscoveredInstance>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.IsMalformed || candidate.Registration is null)
            {
                continue;
            }

            results.Add(await EvaluateAsync(candidate, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<DiscoveredInstance> EvaluateAsync(RegistrationReadResult candidate, CancellationToken cancellationToken)
    {
        var registration = candidate.Registration!;
        var process = _processInspector.GetProcess(registration.ProcessId);
        if (process is null || !ProcessIdentity.StartTimesMatch(registration.ProcessStartTimeUtc, process.StartTimeUtc))
        {
            FileRegistrationStore.TryDelete(candidate.FilePath);
            return new DiscoveredInstance
            {
                State = DiscoveryState.Stale,
                Registration = registration
            };
        }

        try
        {
            await using var client = await _clientFactory.ConnectAsync(registration.PipeName, _handshakeTimeout, cancellationToken).ConfigureAwait(false);
            var handshake = await client.HandshakeAsync(
                    new BridgeHandshakeRequest
                    {
                        ExpectedInstanceId = registration.InstanceId,
                        SupportedProtocolVersions = _supportedProtocolVersions,
                        ClientName = "RevitMCP.Discovery",
                        ClientVersion = "0.1.0"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!IdentitiesMatch(registration, handshake))
            {
                return new DiscoveredInstance
                {
                    State = DiscoveryState.Unavailable,
                    Registration = registration,
                    Handshake = handshake,
                    ErrorCode = BridgeErrorCodes.IdentityMismatch,
                    ErrorMessage = "The handshake identity does not match the registration candidate."
                };
            }

            return new DiscoveredInstance
            {
                State = DiscoveryState.Ready,
                Registration = registration,
                Handshake = handshake
            };
        }
        catch (BridgeException exception) when (exception.ErrorCode == BridgeErrorCodes.ProtocolIncompatible)
        {
            return new DiscoveredInstance
            {
                State = DiscoveryState.Incompatible,
                Registration = registration,
                ErrorCode = exception.ErrorCode,
                ErrorMessage = exception.Message
            };
        }
        catch (BridgeException exception) when (exception.ErrorCode == BridgeErrorCodes.IdentityMismatch)
        {
            return new DiscoveredInstance
            {
                State = DiscoveryState.Unavailable,
                Registration = registration,
                ErrorCode = exception.ErrorCode,
                ErrorMessage = exception.Message
            };
        }
        catch (BridgeException exception) when (exception.ErrorCode is BridgeErrorCodes.HandshakeTimeout or BridgeErrorCodes.HandshakeFailed)
        {
            return new DiscoveredInstance
            {
                State = DiscoveryState.Unavailable,
                Registration = registration,
                ErrorCode = exception.ErrorCode,
                ErrorMessage = exception.Message
            };
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new DiscoveredInstance
            {
                State = DiscoveryState.Unavailable,
                Registration = registration,
                ErrorCode = BridgeErrorCodes.HandshakeTimeout,
                ErrorMessage = "The bridge endpoint could not be reached."
            };
        }
    }

    private static bool IdentitiesMatch(RevitInstanceRegistration registration, BridgeHandshakeResult handshake)
    {
        return string.Equals(registration.InstanceId, handshake.InstanceId, StringComparison.Ordinal)
            && registration.ProcessId == handshake.ProcessId
            && ProcessIdentity.StartTimesMatch(registration.ProcessStartTimeUtc, handshake.ProcessStartTimeUtc)
            && registration.WindowsSessionId == handshake.WindowsSessionId;
    }
}
