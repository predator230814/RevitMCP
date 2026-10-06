using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitRequestParameterUpdateReviewService
{
    Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken);
}
