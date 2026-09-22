using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitGetMepTopologyService
{
    Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        CancellationToken cancellationToken);
}
