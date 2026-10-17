using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitRequestMepDevicePlacementReviewService
{
    Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(
        RequestMepDevicePlacementReviewRequest request,
        CancellationToken cancellationToken);
}
