using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetSelectionService
{
    Task<GetSelectionResult> GetSelectionAsync(
        GetSelectionRequest request,
        CancellationToken cancellationToken);
}
