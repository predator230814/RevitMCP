using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetElementsService
{
    Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, CancellationToken cancellationToken);
}
