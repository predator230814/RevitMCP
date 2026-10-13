using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitRequestParameterUpdateReviewService
{
    Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken);
}
