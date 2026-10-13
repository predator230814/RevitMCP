using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetWarningsService
{
    Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        CancellationToken cancellationToken);
}
