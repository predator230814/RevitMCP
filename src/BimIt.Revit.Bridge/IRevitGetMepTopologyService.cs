using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetMepTopologyService
{
    Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        CancellationToken cancellationToken);
}
