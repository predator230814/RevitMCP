using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal interface IRevitInstanceDiscovery
{
    Task<IReadOnlyList<DiscoveredInstance>> DiscoverAsync(CancellationToken cancellationToken);
}
