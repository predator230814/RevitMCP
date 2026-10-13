using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal interface IRevitInstanceDiscovery
{
    Task<IReadOnlyList<DiscoveredInstance>> DiscoverAsync(CancellationToken cancellationToken);
}
