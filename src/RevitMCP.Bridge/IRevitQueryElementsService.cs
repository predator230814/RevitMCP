using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitQueryElementsService
{
    Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, CancellationToken cancellationToken);
}
