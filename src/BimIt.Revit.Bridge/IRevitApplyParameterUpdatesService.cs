using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitApplyParameterUpdatesService
{
    Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        CancellationToken cancellationToken);
}
