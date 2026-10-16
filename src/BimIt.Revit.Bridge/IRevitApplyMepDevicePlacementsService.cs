using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitApplyMepDevicePlacementsService
{
    Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(
        ApplyMepDevicePlacementsRequest request,
        CancellationToken cancellationToken);
}
