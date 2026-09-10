using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class LocalRevitInstanceDiscovery : IRevitInstanceDiscovery
{
    private readonly IWindowsSession _session;
    private readonly LocalInstanceDiscovery _discovery;

    public LocalRevitInstanceDiscovery(
        IWindowsSession session,
        IRegistrationStore store,
        IProcessInspector processInspector,
        ServerTimeouts timeouts)
    {
        _session = session;
        _discovery = new LocalInstanceDiscovery(
            store,
            processInspector,
            handshakeTimeout: timeouts.BootstrapTimeout);
    }

    public Task<IReadOnlyList<DiscoveredInstance>> DiscoverAsync(CancellationToken cancellationToken)
    {
        return _discovery.DiscoverAsync(_session.CurrentSessionId, cancellationToken);
    }
}
