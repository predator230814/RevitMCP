using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitGetElementsService
{
    Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, CancellationToken cancellationToken);
}
