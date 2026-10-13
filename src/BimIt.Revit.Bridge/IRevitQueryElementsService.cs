using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitQueryElementsService
{
    Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, CancellationToken cancellationToken);
}
